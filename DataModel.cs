namespace LeakMonitor;

/// <summary>
/// 解析后的泄漏监测数据
/// </summary>
public class LeakData
{
    /// <summary>设备时间戳，格式 "YYYY-MM-DD HH:MM:SS"</summary>
    public string Timestamp { get; set; } = "";

    /// <summary>漏水距离（米）</summary>
    public double Distance { get; set; }

    /// <summary>报警状态码: 0=正常, 1=管道泄漏, 2=储液槽已满, 3=双重报警</summary>
    public uint AlarmCode { get; set; }

    /// <summary>原始帧字符串（用于调试）</summary>
    public string RawFrame { get; set; } = "";

    /// <summary>本地接收时间</summary>
    public DateTime ArrivalTime { get; set; } = DateTime.Now;
}

/// <summary>
/// 串口接收的数据包 —— 在 SerialReader 和 UI 之间传递
/// </summary>
public class DataEnvelope
{
    /// <summary>解析成功时不为 null</summary>
    public LeakData? Data { get; set; }

    /// <summary>原始字节串</summary>
    public string RawLine { get; set; } = "";

    /// <summary>解析失败时的错误信息</summary>
    public string? Error { get; set; }

    /// <summary>到达时间</summary>
    public DateTime ArrivalTime { get; set; } = DateTime.Now;

    /// <summary>是否解析成功</summary>
    public bool IsValid => Data != null && Error == null;
}

/// <summary>
/// 环形缓冲区 —— 存储最近 N 条数据，用于数据导出
/// </summary>
public class DataHistory
{
    private readonly LeakData[] _buffer;
    private int _writeIndex;
    private int _count;
    private readonly int _capacity;

    public DataHistory(int capacity = 300)
    {
        _capacity = capacity;
        _buffer = new LeakData[capacity];
        _writeIndex = 0;
        _count = 0;
    }

    public void Add(LeakData data)
    {
        _buffer[_writeIndex] = data;
        _writeIndex = (_writeIndex + 1) % _capacity;
        if (_count < _capacity) _count++;
    }

    /// <summary>获取最近 count 条数据（按时间先后排序）</summary>
    public List<LeakData> GetRecent(int count = 60)
    {
        var result = new List<LeakData>();
        int n = Math.Min(count, _count);
        if (n == 0) return result;

        int start = (_writeIndex - n + _capacity) % _capacity;
        for (int i = 0; i < n; i++)
        {
            var item = _buffer[(start + i) % _capacity];
            if (item != null!)
                result.Add(item);
        }
        return result;
    }

    public int Count => _count;
    public void Clear()
    {
        _count = 0;
        _writeIndex = 0;
        Array.Clear(_buffer, 0, _capacity);
    }
}
