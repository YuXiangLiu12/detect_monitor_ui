"""
虚拟串口数据模拟器
===================
用途: 在没有 STM32 硬件的情况下，模拟 TTL-USB 设备发送数据，方便测试 PC 端软件。

支持协议:
  - binary:  16字节二进制协议 (默认)
  - ascii:   Demo ASCII 协议 ($LEAK,...)

使用方法:
  1. 安装 com0com 虚拟串口工具 (https://com0com.sourceforge.net/)
  2. 创建一对虚拟串口 (例如 COM10 <-> COM11)
  3. 将 COM10 分配给此模拟器，COM11 分配给 LeakMonitor.exe
  4. 运行二进制协议: python simulator.py COM10
     运行 ASCII 协议:  python simulator.py COM10 115200 ascii

  如果没有 com0com:
  - 准备两根 USB-TTL 模块，TXD/RXD 交叉连接
  - 或使用物理跳线将单个 USB-TTL 的 TX 和 RX 短接（自发自收）
"""

import serial
import time
import random
import sys
import struct
from datetime import datetime


def generate_binary_packet():
    """
    生成一条 16 字节二进制协议数据包 (STM32 大端序)

    帧格式:
      Byte 0-3:   帧头同步字 0x55 0xAA 0x55 0xAA
      Byte 4-7:   时间戳 (uint32_t, big-endian, Unix timestamp)
      Byte 8-11:  报警信息 (uint32_t, big-endian, 0=正常 1=管道泄漏 2=储液槽已满 3=双重报警)
      Byte 12-15: 漏水距离 (uint32_t, big-endian, 单位: cm)
    """
    # 时间戳: 当前 Unix timestamp
    timestamp = int(time.time())

    # 报警信息:
    #   10% 概率: 随机选择 1=管道泄漏, 2=储液槽已满, 3=双重报警 (各1/3)
    #   90% 概率: 0=正常
    r = random.random()
    if r < 0.1:
        alarm = random.choice([1, 2, 3])
    else:
        alarm = 0

    # 漏水距离: 5000~15000 cm (50~150 米) 之间随机波动
    distance_cm = round(random.uniform(5000.0, 15000.0))

    # 大端序打包: '>' = big-endian, 'I' = uint32_t
    # 帧头手动拼接，数据字段用 struct.pack
    header = bytes([0x55, 0xAA, 0x55, 0xAA])
    payload = struct.pack('>III', timestamp, alarm, distance_cm)

    packet = header + payload
    return packet, timestamp, alarm, distance_cm


def generate_ascii_frame():
    """生成一条 Demo ASCII 协议帧"""
    now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    distance = round(random.uniform(100.0, 200.0), 1)
    is_full = 1 if random.random() < 0.1 else 0
    frame = f"$LEAK,TIME={now},DIST={distance},FULL={is_full}*\r\n"
    return frame.encode("ascii"), now, distance, is_full


def main():
    port = sys.argv[1] if len(sys.argv) > 1 else "COM10"
    baudrate = int(sys.argv[2]) if len(sys.argv) > 2 else 115200
    protocol = sys.argv[3].lower() if len(sys.argv) > 3 else "binary"

    if protocol not in ("binary", "ascii"):
        print(f"错误: 未知协议 '{protocol}'，可选: binary, ascii")
        return

    print(f"虚拟串口模拟器")
    print(f"  目标串口: {port}")
    print(f"  波特率:   {baudrate}")
    if protocol == "binary":
        print(f"  协议:     Binary (16-byte packet, big-endian)")
        print(f"  帧格式:   [55 AA 55 AA] + timestamp(I BE) + alarm(I BE) + distance_cm(I BE)")
    else:
        print(f"  协议:     Demo ASCII ($LEAK,...)")
    print(f"  帧间隔:   1 秒")
    print(f"  按 Ctrl+C 停止")
    print()

    try:
        ser = serial.Serial(port, baudrate, timeout=1)
        print(f"✓ 串口 {port} 已打开\n")
    except Exception as e:
        print(f"✗ 无法打开串口 {port}: {e}")
        print(f"\n提示: 请确认 {port} 存在且未被其他程序占用。")
        print(f"如果没有物理串口，请安装 com0com 虚拟串口工具。")
        return

    frame_count = 0
    try:
        while True:
            if protocol == "binary":
                packet, timestamp, alarm, distance_cm = generate_binary_packet()
                ser.write(packet)
                frame_count += 1
                ts_str = datetime.fromtimestamp(timestamp).strftime("%Y-%m-%d %H:%M:%S")
                print(f"[{frame_count:05d}] TS={ts_str} ALARM={alarm} DIST={distance_cm}cm "
                      f"HEX={packet.hex(' ').upper()}")
            else:
                frame, now, distance, is_full = generate_ascii_frame()
                ser.write(frame)
                frame_count += 1
                print(f"[{frame_count:05d}] {frame.decode('ascii').strip()}")

            time.sleep(1.0)
    except KeyboardInterrupt:
        print(f"\n\n已停止。共发送 {frame_count} 帧。")
    finally:
        ser.close()
        print(f"串口 {port} 已关闭。")


if __name__ == "__main__":
    main()
