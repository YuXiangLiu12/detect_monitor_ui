using System.Text.RegularExpressions;

namespace LeakMonitor;

// ================================================================
// ★ 二次开发：如果要适配新的数据协议，从这里开始修改
// ================================================================
//
// 步骤：
//   1. 新建一个继承 BaseProtocolParser 的类
//   2. 实现 Parse() 方法，返回 LeakData 或 null
//   3. 在 PARSER_REGISTRY 字典里注册你的解析器
//   4. 修改 config.json 中的 "protocol_type" 为你的注册键名
//
// 示例（二进制协议）:
//   class MyBinaryParser : BaseProtocolParser { ... }
//   PARSER_REGISTRY["my_binary"] = typeof(MyBinaryParser);
//   然后 config.json: { "protocol_type": "my_binary" }
// ================================================================

/// <summary>
/// 协议解析器抽象基类 —— 所有协议解析器都继承这个类
/// </summary>
public abstract class BaseProtocolParser
{
    /// <summary>
    /// 解析一行原始数据，返回 LeakData；解析失败返回 null
    /// </summary>
    /// <param name="rawLine">从串口读取的一行原始字符串（已去除首尾空白）</param>
    public abstract LeakData? Parse(string rawLine);

    /// <summary>
    /// 返回此协议的帧分隔符
    /// </summary>
    public virtual string GetNewline() => "\r\n";

    /// <summary>
    /// 返回此协议的人类可读名称
    /// </summary>
    public abstract string ProtocolName { get; }

    /// <summary>
    /// 是否为二进制协议（默认 false，即 ASCII 行协议）
    /// 二进制协议使用 SerialPort.Read() 逐字节读取并进行帧同步
    /// </summary>
    public virtual bool IsBinary => false;

    /// <summary>
    /// 解析二进制数据包，返回 LeakData；解析失败返回 null
    /// 仅当 IsBinary = true 时被调用
    /// </summary>
    /// <param name="packet">完整的二进制数据包</param>
    public virtual LeakData? Parse(byte[] packet) => null;
}

/// <summary>
/// Demo ASCII 协议解析器
///
/// 帧格式: $LEAK,TIME=YYYY-MM-DD HH:MM:SS,DIST=xxx.x,FULL=0/1*
/// 每帧以 \r\n 结尾（SerialPort.ReadLine() 自动处理）
///
/// 字段说明:
///   $          - 帧起始标记
///   LEAK       - 帧类型标识
///   TIME=...   - 时间戳
///   DIST=...   - 漏水距离（米），浮点数
///   FULL=0/1   - 水槽是否满（0=未满, 1=已满）
///   *          - 帧结束标记
/// </summary>
public class DemoAsciiParser : BaseProtocolParser
{
    public override string ProtocolName => "Demo ASCII ($LEAK,...)";

    // 正则表达式：匹配 $LEAK,TIME=...,DIST=...,FULL=...*
    // 时间格式: YYYY-MM-DD HH:MM:SS
    // 距离: 数字.数字
    // 满状态: 0 或 1
    private static readonly Regex _regex = new(
        @"^\$LEAK,TIME=(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}),DIST=([\d.]+),FULL=([01])\*$",
        RegexOptions.Compiled);

    public override LeakData? Parse(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
            return null;

        var match = _regex.Match(rawLine.Trim());
        if (!match.Success)
            return null;

        // match.Groups[1] = 时间戳
        // match.Groups[2] = 距离
        // match.Groups[3] = 满状态
        if (!double.TryParse(match.Groups[2].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double distance))
        {
            return null; // 距离解析失败
        }

        return new LeakData
        {
            Timestamp = match.Groups[1].Value,
            Distance = distance,
            AlarmCode = match.Groups[3].Value == "1" ? 2u : 0u,  // FULL=1 → 储液槽已满(alarm=2)
            RawFrame = rawLine.Trim(),
            ArrivalTime = DateTime.Now
        };
    }
}

/// <summary>
/// 二进制协议解析器
///
/// 帧格式（16 字节定长包，STM32 大端序）：
///   Byte 0-3:   帧头同步字 0x55 0xAA 0x55 0xAA
///   Byte 4-7:   时间戳 (uint32_t, big-endian, 按位压缩北京时间)
///                位布局: [31:26]年-2000 [25:22]月 [21:17]日
///                        [16:12]时 [11:6]分 [5:0]秒
///                值为 0 时回退到本地接收时间
///   Byte 8-11:  报警信息 (uint32_t, big-endian, 0=正常 1=管道泄漏 2=储液槽已满)
///   Byte 12-15: 漏水距离 (uint32_t, big-endian, 单位: cm)
///
/// 帧同步由 SerialReader 的 ReadLoopBinary 完成，
/// 本解析器接收已同步的完整 16 字节包。
/// </summary>
public class BinaryParser : BaseProtocolParser
{
    public override string ProtocolName => "Binary Protocol (16-byte packet, BE)";

    public override bool IsBinary => true;

    /// <summary>
    /// 解析二进制数据包（大端序）
    /// </summary>
    public override LeakData? Parse(byte[] packet)
    {
        if (packet == null || packet.Length < 16)
            return null;

        // ---- 大端序解析 uint32 字段 ----
        // 与 STM32 发送端打包方式一致:
        //   usart2_tx_packet[4]  = (uint8_t)(timestamp >> 24);
        //   usart2_tx_packet[5]  = (uint8_t)(timestamp >> 16);
        //   usart2_tx_packet[6]  = (uint8_t)(timestamp >> 8);
        //   usart2_tx_packet[7]  = (uint8_t)(timestamp >> 0);
        uint timestampRaw = (uint)(
            (packet[4] << 24) |
            (packet[5] << 16) |
            (packet[6] << 8) |
            (packet[7] << 0));

        uint alarmInfo = (uint)(
            (packet[8] << 24) |
            (packet[9] << 16) |
            (packet[10] << 8) |
            (packet[11] << 0));

        uint distanceCm = (uint)(
            (packet[12] << 24) |
            (packet[13] << 16) |
            (packet[14] << 8) |
            (packet[15] << 0));

        // ---- 时间戳解码: 按位压缩北京时间 ----
        // 与 STM32 发送端打包方式一致:
        //   uint32_t timestamp = ((year-2000)<<26) | (month<<22) | (day<<17)
        //                      | (hour<<12) | (minute<<6) | second;
        string timestampStr;
        if (timestampRaw > 0)
        {
            int year   = (int)((timestampRaw >> 26) & 0x3F) + 2000;   // [31:26] 6bit
            int month  = (int)((timestampRaw >> 22) & 0x0F);           // [25:22] 4bit
            int day    = (int)((timestampRaw >> 17) & 0x1F);           // [21:17] 5bit
            int hour   = (int)((timestampRaw >> 12) & 0x1F);           // [16:12] 5bit
            int minute = (int)((timestampRaw >> 6)  & 0x3F);           // [11:6]  6bit
            int second = (int)((timestampRaw >> 0)  & 0x3F);           // [5:0]   6bit

            // 合法性校验, 无效字段回退到本地时间
            if (year >= 2000 && year <= 2099 &&
                month >= 1 && month <= 12 &&
                day >= 1 && day <= 31 &&
                hour >= 0 && hour <= 23 &&
                minute >= 0 && minute <= 59 &&
                second >= 0 && second <= 59)
            {
                timestampStr = $"{year:D4}-{month:D2}-{day:D2} {hour:D2}:{minute:D2}:{second:D2}";
            }
            else
            {
                timestampStr = $"raw:{timestampRaw:X8}";
            }
        }
        else
        {
            // 时间戳为 0（设备未设置 RTC），回退到本地接收时间
            timestampStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        return new LeakData
        {
            Timestamp = timestampStr,
            Distance = distanceCm / 100.0,   // cm → meters
            AlarmCode = alarmInfo,             // 0=正常 1=管道泄漏 2=储液槽已满 3=双重报警
            RawFrame = BitConverter.ToString(packet),  // 十六进制显示，如 "55-AA-55-AA-..."
            ArrivalTime = DateTime.Now
        };
    }

    /// <summary>
    /// ASCII 行解析不适用于二进制协议
    /// </summary>
    public override LeakData? Parse(string rawLine) => null;
}

/// <summary>
/// 协议解析器注册表
///
/// ★ 添加新协议时，在这里注册你的解析器类
/// key = config.json 中 protocol_type 的值
/// value = 解析器类的 Type
/// </summary>
public static class ProtocolRegistry
{
    /// <summary>协议类型名 → 解析器 Type 的映射</summary>
    public static readonly Dictionary<string, Type> Parsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["demo_ascii"] = typeof(DemoAsciiParser),
        ["binary"] = typeof(BinaryParser),
        // ★ 在这里添加更多解析器
    };

    /// <summary>
    /// 根据协议名创建解析器实例
    /// </summary>
    public static BaseProtocolParser CreateParser(string protocolType)
    {
        if (Parsers.TryGetValue(protocolType, out var type))
        {
            var instance = Activator.CreateInstance(type);
            if (instance is BaseProtocolParser parser)
                return parser;
        }

        // 默认回退到 demo_ascii
        return new DemoAsciiParser();
    }
}
