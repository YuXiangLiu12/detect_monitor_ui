using System.Collections.Concurrent;
using System.IO.Ports;

namespace LeakMonitor;

/// <summary>
/// 串口读取器 —— 在后台线程中持续读取串口数据
///
/// 工作流程:
///   1. 打开指定 COM 口
///   2. 后台线程循环调用 ReadLine()（阻塞等待）
///   3. 读到一行后交给 ProtocolParser 解析
///   4. 解析结果（DataEnvelope）放入线程安全队列
///   5. UI 线程定时从队列取出数据更新界面
/// </summary>
public class SerialReader : IDisposable
{
    private SerialPort? _serialPort;
    private Thread? _readThread;
    private volatile bool _running;
    private readonly ConcurrentQueue<DataEnvelope> _queue;
    private readonly BaseProtocolParser _parser;

    // ---- 统计信息 ----
    private long _totalFrames;
    private long _errorFrames;
    public long TotalFrames => Interlocked.Read(ref _totalFrames);
    public long ErrorFrames => Interlocked.Read(ref _errorFrames);
    public bool IsConnected => _serialPort?.IsOpen ?? false;

    // ---- 事件（在后台线程中触发，订阅者需注意线程安全）----
    public event Action<string>? OnStatusChanged;
    public event Action<Exception>? OnError;

    public SerialReader(BaseProtocolParser parser)
    {
        _parser = parser;
        _queue = new ConcurrentQueue<DataEnvelope>();
    }

    /// <summary>
    /// 连接到串口
    /// </summary>
    public void Connect(string portName, int baudRate, int dataBits = 8, Parity parity = Parity.None, StopBits stopBits = StopBits.One)
    {
        if (_running)
            Disconnect();

        try
        {
            _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = 1000,  // 1秒读超时（用于正常退出）
                WriteTimeout = 1000,
                NewLine = _parser.GetNewline(),
                Encoding = System.Text.Encoding.ASCII
            };
            _serialPort.Open();
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            _running = true;
            _totalFrames = 0;
            _errorFrames = 0;

            _readThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "SerialReader"
            };
            _readThread.Start();

            OnStatusChanged?.Invoke($"已连接到 {portName} @ {baudRate} bps");
        }
        catch (Exception ex)
        {
            _serialPort?.Dispose();
            _serialPort = null;
            OnError?.Invoke(new Exception($"串口打开失败: {portName}", ex));
            throw;
        }
    }

    /// <summary>
    /// 断开串口连接
    /// </summary>
    public void Disconnect()
    {
        _running = false;

        // 等待读线程结束
        if (_readThread != null && _readThread.IsAlive)
        {
            _ = _readThread.Join(3000);
        }

        try
        {
            if (_serialPort?.IsOpen == true)
                _serialPort.Close();
        }
        catch { /* 忽略关闭时的异常 */ }

        _serialPort?.Dispose();
        _serialPort = null;
        _readThread = null;

        OnStatusChanged?.Invoke("已断开连接");
    }

    /// <summary>
    /// 后台读取循环 —— 根据协议类型选择 ASCII 或二进制模式
    /// </summary>
    private void ReadLoop()
    {
        if (_parser.IsBinary)
            ReadLoopBinary();
        else
            ReadLoopAscii();
    }

    /// <summary>
    /// ASCII 行协议读取循环（原有逻辑）
    /// </summary>
    private void ReadLoopAscii()
    {
        while (_running && _serialPort?.IsOpen == true)
        {
            try
            {
                // ReadLine() 阻塞直到收到 NewLine（\r\n）或超时
                string? line = _serialPort.ReadLine();
                if (line == null) continue;

                // 解析
                LeakData? data = _parser.Parse(line);

                var envelope = new DataEnvelope
                {
                    RawLine = line,
                    ArrivalTime = DateTime.Now
                };

                if (data != null)
                {
                    envelope.Data = data;
                    Interlocked.Increment(ref _totalFrames);
                }
                else
                {
                    envelope.Error = "协议解析失败";
                    Interlocked.Increment(ref _errorFrames);
                }

                _queue.Enqueue(envelope);
            }
            catch (TimeoutException)
            {
                // 读超时 → 继续循环（用于检查 _running 标志）
                continue;
            }
            catch (IOException)
            {
                // 串口被拔出或其他 IO 错误
                _queue.Enqueue(new DataEnvelope
                {
                    Error = "串口 IO 错误（设备可能被拔出）"
                });
                break;
            }
            catch (InvalidOperationException)
            {
                // 串口已关闭
                break;
            }
            catch (Exception ex)
            {
                _queue.Enqueue(new DataEnvelope
                {
                    Error = $"读取异常: {ex.Message}"
                });
            }
        }

        // 读取循环退出后，如果仍然标记为 running（说明是非预期退出），通知断开
        if (_running)
        {
            _running = false;
            OnStatusChanged?.Invoke("串口连接意外断开");
        }
    }

    /// <summary>
    /// 二进制协议读取循环
    ///
    /// 帧同步策略：
    ///   1. 从串口逐字节读取，进入字节缓冲区
    ///   2. 在缓冲区中扫描同步字 0x55 0xAA 0x55 0xAA
    ///   3. 找到同步字后，等待凑齐 16 字节 → 提取一个完整包
    ///   4. 丢弃同步字之前的字节（处理字节流失步）
    ///   5. 如果找不到同步字，仅保留最后 3 字节（防止同步字跨读取边界）
    /// </summary>
    private void ReadLoopBinary()
    {
        // 字节缓冲区 —— 用于拼接不完整的帧
        var byteBuffer = new List<byte>(256);
        var readBuf = new byte[256];
        int syncLostCount = 0;  // 失步计数器

        while (_running && _serialPort?.IsOpen == true)
        {
            try
            {
                // 先检查是否有可用字节（BytesToRead），避免不必要的阻塞
                // 但如果缓冲区为空，Read() 会阻塞等待至少 1 字节或超时
                int bytesRead = _serialPort.Read(readBuf, 0, readBuf.Length);
                if (bytesRead <= 0) continue;

                // 追加到字节缓冲区
                for (int i = 0; i < bytesRead; i++)
                    byteBuffer.Add(readBuf[i]);

                // 防止缓冲区无限增长（> 4096 字节时裁剪，保留最后 2048 字节）
                if (byteBuffer.Count > 4096)
                {
                    byteBuffer.RemoveRange(0, byteBuffer.Count - 2048);
                    syncLostCount++;
                }

                // 循环提取完整的数据包
                while (TryExtractBinaryPacket(byteBuffer, out byte[]? packet))
                {
                    if (packet != null)
                    {
                        // 调用二进制解析器
                        LeakData? data = _parser.Parse(packet);

                        var envelope = new DataEnvelope
                        {
                            RawLine = BitConverter.ToString(packet),
                            ArrivalTime = DateTime.Now
                        };

                        if (data != null)
                        {
                            envelope.Data = data;
                            Interlocked.Increment(ref _totalFrames);
                        }
                        else
                        {
                            envelope.Error = "二进制协议解析失败";
                            Interlocked.Increment(ref _errorFrames);
                        }

                        _queue.Enqueue(envelope);
                    }
                }
            }
            catch (TimeoutException)
            {
                // 读超时 → 继续循环（用于检查 _running 标志）
                continue;
            }
            catch (IOException)
            {
                _queue.Enqueue(new DataEnvelope
                {
                    Error = "串口 IO 错误（设备可能被拔出）"
                });
                break;
            }
            catch (InvalidOperationException)
            {
                // 串口已关闭
                break;
            }
            catch (Exception ex)
            {
                _queue.Enqueue(new DataEnvelope
                {
                    Error = $"读取异常: {ex.Message}"
                });
            }
        }

        if (syncLostCount > 0)
            OnStatusChanged?.Invoke($"警告: 二进制帧同步丢失 {syncLostCount} 次");

        // 读取循环退出后，如果仍然标记为 running，通知断开
        if (_running)
        {
            _running = false;
            OnStatusChanged?.Invoke("串口连接意外断开");
        }
    }

    /// <summary>
    /// 尝试从字节缓冲区中提取一个完整的二进制数据包
    /// </summary>
    /// <param name="buffer">字节缓冲区（会被修改：丢弃已提取/无效数据）</param>
    /// <param name="packet">提取出的 16 字节完整数据包，失败时为 null</param>
    /// <returns>true 表示成功提取一个包，false 表示需要更多数据</returns>
    private static bool TryExtractBinaryPacket(List<byte> buffer, out byte[]? packet)
    {
        packet = null;

        // 至少需要 16 字节才能组成一个完整包
        if (buffer.Count < 16)
            return false;

        // ---- 扫描同步字 0x55 0xAA 0x55 0xAA ----
        int syncPos = -1;
        for (int i = 0; i <= buffer.Count - 4; i++)
        {
            if (buffer[i] == 0x55 && buffer[i + 1] == 0xAA &&
                buffer[i + 2] == 0x55 && buffer[i + 3] == 0xAA)
            {
                syncPos = i;
                break;
            }
        }

        if (syncPos < 0)
        {
            // 找不到同步字 → 丢弃前面大部分字节，仅保留最后 3 字节
            // （防止同步字正好跨在本次和下次读取的边界上）
            int discard = buffer.Count - 3;
            if (discard > 0)
                buffer.RemoveRange(0, discard);
            return false;
        }

        // 丢弃同步字之前的垃圾字节
        if (syncPos > 0)
            buffer.RemoveRange(0, syncPos);

        // 此时 buffer[0..3] = 同步字，检查是否有完整 16 字节
        if (buffer.Count < 16)
            return false;

        // 提取完整的 16 字节数据包
        packet = new byte[16];
        buffer.CopyTo(0, packet, 0, 16);
        buffer.RemoveRange(0, 16);
        return true;
    }

    /// <summary>
    /// 尝试从队列中取出一条数据（非阻塞）
    /// </summary>
    public bool TryDequeue(out DataEnvelope envelope)
    {
        return _queue.TryDequeue(out envelope!);
    }

    /// <summary>
    /// 获取当前队列中的待处理数据条数
    /// </summary>
    public int QueueCount => _queue.Count;

    /// <summary>
    /// 清空队列
    /// </summary>
    public void ClearQueue()
    {
        while (_queue.TryDequeue(out _)) { }
    }

    /// <summary>
    /// 获取可用的串口列表
    /// </summary>
    public static string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }

    public void Dispose()
    {
        Disconnect();
    }
}
