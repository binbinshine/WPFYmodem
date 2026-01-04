using System;

namespace WPFYmodem
{
    public static class YmodemConstants
    {
        // 控制字符
        public const byte SOH = 0x01;    // 128字节数据包起始
        public const byte STX = 0x02;    // 1024字节数据包起始
        public const byte EOT = 0x04;    // 传输结束
        public const byte ACK = 0x06;    // 确认
        public const byte NAK = 0x15;    // 否认，请求重发
        public const byte CAN = 0x18;    // 取消传输
        public const byte C = 0x43;      // CRC16模式请求

        // 模块特定指令
        public static readonly byte[] ENTER_YMODEM_CMD =
            { 0xAA, 0x96, 0x55, 0xAA, 0x5A, 0xA5, 0xCC, 0x33, 0xC3, 0x3C };

        public static readonly byte[] RESET_CMD =
            { 0xAA, 0xEE, 0xAA, 0x55, 0xA5, 0x5A, 0xCC, 0x33, 0xC3, 0x3C };

        // 传输参数
        public const int PACKET_SIZE_128 = 128;
        public const int PACKET_SIZE_1024 = 1024;
        public const int MAX_RETRY_COUNT = 10;
        public const int RESPONSE_TIMEOUT_MS = 5000;
        public const int FILE_TRANSFER_TIMEOUT_MS = 200000;
        public const int CONNECTION_TIMEOUT_MS = 40000;
    }
}