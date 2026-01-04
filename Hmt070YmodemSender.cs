using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WPFYmodem
{
    public class Hmt070YmodemSender
    {
        private readonly SerialPort _serialPort;
        private readonly IProgress<string> _logProgress;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isTransferring;

        // CRC16-1021查找表
        private static readonly ushort[] CRC16_1021tab = new ushort[]
        {
            0x0000,0x1021,0x2042,0x3063,0x4084,0x50A5,0x60C6,0x70E7,
            0x8108,0x9129,0xA14A,0xB16B,0xC18C,0xD1AD,0xE1CE,0xF1EF,
            0x1231,0x0210,0x3273,0x2252,0x52B5,0x4294,0x72F7,0x62D6,
            0x9339,0x8318,0xB37B,0xA35A,0xD3BD,0xC39C,0xF3FF,0xE3DE,
            0x2462,0x3443,0x0420,0x1401,0x64E6,0x74C7,0x44A4,0x5485,
            0xA56A,0xB54B,0x8528,0x9509,0xE5EE,0xF5CF,0xC5AC,0xD58D,
            0x3653,0x2672,0x1611,0x0630,0x76D7,0x66F6,0x5695,0x46B4,
            0xB75B,0xA77A,0x9719,0x8738,0xF7DF,0xE7FE,0xD79D,0xC7BC,
            0x48C4,0x58E5,0x6886,0x78A7,0x0840,0x1861,0x2802,0x3823,
            0xC9CC,0xD9ED,0xE98E,0xF9AF,0x8948,0x9969,0xA90A,0xB92B,
            0x5AF5,0x4AD4,0x7AB7,0x6A96,0x1A71,0x0A50,0x3A33,0x2A12,
            0xDBFD,0xCBDC,0xFBBF,0xEB9E,0x9B79,0x8B58,0xBB3B,0xAB1A,
            0x6CA6,0x7C87,0x4CE4,0x5CC5,0x2C22,0x3C03,0x0C60,0x1C41,
            0xEDAE,0xFD8F,0xCDEC,0xDDCD,0xAD2A,0xBD0B,0x8D68,0x9D49,
            0x7E97,0x6EB6,0x5ED5,0x4EF4,0x3E13,0x2E32,0x1E51,0x0E70,
            0xFF9F,0xEFBE,0xDFDD,0xCFFC,0xBF1B,0xAF3A,0x9F59,0x8F78,
            0x9188,0x81A9,0xB1CA,0xA1EB,0xD10C,0xC12D,0xF14E,0xE16F,
            0x1080,0x00A1,0x30C2,0x20E3,0x5004,0x4025,0x7046,0x6067,
            0x83B9,0x9398,0xA3FB,0xB3DA,0xC33D,0xD31C,0xE37F,0xF35E,
            0x02B1,0x1290,0x22F3,0x32D2,0x4235,0x5214,0x6277,0x7256,
            0xB5EA,0xA5CB,0x95A8,0x8589,0xF56E,0xE54F,0xD52C,0xC50D,
            0x34E2,0x24C3,0x14A0,0x0481,0x7466,0x6447,0x5424,0x4405,
            0xA7DB,0xB7FA,0x8799,0x97B8,0xE75F,0xF77E,0xC71D,0xD73C,
            0x26D3,0x36F2,0x0691,0x16B0,0x6657,0x7676,0x4615,0x5634,
            0xD94C,0xC96D,0xF90E,0xE92F,0x99C8,0x89E9,0xB98A,0xA9AB,
            0x5844,0x4865,0x7806,0x6827,0x18C0,0x08E1,0x3882,0x28A3,
            0xCB7D,0xDB5C,0xEB3F,0xFB1E,0x8BF9,0x9BD8,0xABBB,0xBB9A,
            0x4A75,0x5A54,0x6A37,0x7A16,0x0AF1,0x1AD0,0x2AB3,0x3A92,
            0xFD2E,0xED0F,0xDD6C,0xCD4D,0xBDAA,0xAD8B,0x9DE8,0x8DC9,
            0x7C26,0x6C07,0x5C64,0x4C45,0x3CA2,0x2C83,0x1CE0,0x0CC1,
            0xEF1F,0xFF3E,0xCF5D,0xDF7C,0xAF9B,0xBFBA,0x8FD9,0x9FF8,
            0x6E17,0x7E36,0x4E55,0x5E74,0x2E93,0x3EB2,0x0ED1,0x1EF0
        };

        public Hmt070YmodemSender(SerialPort serialPort, IProgress<string> logProgress)
        {
            _serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
            _logProgress = logProgress;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        #region 公共方法

        /// <summary>
        /// 取消当前传输
        /// </summary>
        public void CancelTransfer()
        {
            if (_isTransferring && _cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                LogWithTimestamp("用户请求取消传输...");
                _cancellationTokenSource.Cancel();
            }
        }

        #endregion

        #region CRC计算方法

        // CRC16计算方法 - 根据协议文档
        private ushort CalculateCRC16(byte[] data, int start, int length)
        {
            ushort crc = 0;
            for (int i = 0; i < length; i++)
            {
                byte index = (byte)((crc >> 8) ^ data[start + i]);
                crc = (ushort)((crc << 8) ^ CRC16_1021tab[index]);
            }
            return crc;
        }

        // 获取CRC字节数组（高字节在前，低字节在后）
        private byte[] GetCRCBytes(ushort crc)
        {
            byte[] crcBytes = new byte[2];
            crcBytes[0] = (byte)((crc >> 8) & 0xFF);  // 高字节
            crcBytes[1] = (byte)(crc & 0xFF);          // 低字节
            return crcBytes;
        }

        #endregion

        #region 数据包构建方法

        // 构建文件头数据包
        private byte[] BuildFileHeader(FileInfo fileInfo)
        {
            // 创建133字节的数据包
            byte[] packet = new byte[133];

            // 1. 包头 (3字节)
            packet[0] = YmodemConstants.SOH;  // 0x01
            packet[1] = 0x00;                 // 块号0
            packet[2] = 0xFF;                 // 块号补码

            // 2. 文件名 (以0x00结束)
            string fileName = fileInfo.Name;
            byte[] fileNameBytes = Encoding.ASCII.GetBytes(fileName);
            int fileNameLength = Math.Min(fileNameBytes.Length, 100);
            Buffer.BlockCopy(fileNameBytes, 0, packet, 3, fileNameLength);
            packet[3 + fileNameLength] = 0x00; // 文件名结束符

            // 3. 文件大小 (ASCII字符串，以0x20结束)
            string fileSizeStr = fileInfo.Length.ToString();
            byte[] fileSizeBytes = Encoding.ASCII.GetBytes(fileSizeStr);
            int fileSizeStart = 3 + fileNameLength + 1;
            Buffer.BlockCopy(fileSizeBytes, 0, packet, fileSizeStart, fileSizeBytes.Length);
            packet[fileSizeStart + fileSizeBytes.Length] = 0x20; // 文件大小结束符(空格)

            // 4. 剩余部分填充0x00
            int fillStart = fileSizeStart + fileSizeBytes.Length + 1;
            for (int i = fillStart; i < 131; i++)
            {
                packet[i] = 0x00;
            }

            // 5. 计算CRC16（只计算128字节数据部分，从位置3开始）
            ushort crc = CalculateCRC16(packet, 3, 128);

            // 高字节在前，低字节在后
            byte[] crcBytes = GetCRCBytes(crc);
            packet[131] = crcBytes[0]; // CRC高字节
            packet[132] = crcBytes[1]; // CRC低字节

            DebugFileHeader(fileInfo, packet, crc);

            return packet;
        }

        // 构建数据包
        private byte[] BuildDataPacket(byte blockNumber, byte[] data, int dataLength, bool use128BytePacket)
        {
            int packetSize = use128BytePacket ? YmodemConstants.PACKET_SIZE_128 : YmodemConstants.PACKET_SIZE_1024;
            byte header = use128BytePacket ? YmodemConstants.SOH : YmodemConstants.STX;

            byte[] packet = new byte[3 + packetSize + 2]; // 头(3) + 数据 + CRC(2)

            // 包头
            packet[0] = header;
            packet[1] = blockNumber;
            packet[2] = (byte)(~blockNumber); // 包号补码

            // 数据部分
            if (dataLength > 0)
            {
                Buffer.BlockCopy(data, 0, packet, 3, Math.Min(dataLength, packetSize));
            }

            // 填充剩余部分为0x1A（标准Ymodem填充字符）
            // 注意：对于最后一个包，有些实现要求填充0x00
            byte fillByte = 0x1A;

            // 如果是最后一个包且数据不足，可以填充0x00
            if (use128BytePacket && dataLength < packetSize)
            {
                // 可选：填充0x00而不是0x1A
                // fillByte = 0x00;
            }

            for (int i = 3 + dataLength; i < 3 + packetSize; i++)
            {
                packet[i] = fillByte;
            }

            // 计算CRC（只计算数据部分！）
            ushort crc = CalculateCRC16(packet, 3, packetSize);

            // 高字节在前，低字节在后
            byte[] crcBytes = GetCRCBytes(crc);
            packet[3 + packetSize] = crcBytes[0];     // CRC高字节
            packet[3 + packetSize + 1] = crcBytes[1]; // CRC低字节

            DebugDataPacket(blockNumber, packet, dataLength, packetSize, crc);

            return packet;
        }

        // 构建结束帧数据包
        private byte[] BuildEndOfTransmissionPacket()
        {
            byte[] packet = new byte[133];

            packet[0] = YmodemConstants.SOH;
            packet[1] = 0x00;
            packet[2] = 0xFF;

            // 128字节数据全部填充0x00
            for (int i = 3; i < 131; i++)
            {
                packet[i] = 0x00;
            }

            // 计算CRC（全0x00的数据）
            ushort crc = CalculateCRC16(packet, 3, 128);

            // 高字节在前，低字节在后
            byte[] crcBytes = GetCRCBytes(crc);
            packet[131] = crcBytes[0]; // CRC高字节
            packet[132] = crcBytes[1]; // CRC低字节

            return packet;
        }

        #endregion

        #region 调试方法

        // 调试文件头
        private void DebugFileHeader(FileInfo fileInfo, byte[] packet, ushort crc)
        {
            StringBuilder debugInfo = new StringBuilder();
            debugInfo.AppendLine($"=== 文件头调试信息 ===");
            debugInfo.AppendLine($"文件名: {fileInfo.Name}");
            debugInfo.AppendLine($"文件大小: {fileInfo.Length} 字节");
            debugInfo.AppendLine($"CRC16计算值: 0x{crc:X4} (0x{(crc >> 8) & 0xFF:X2} 0x{crc & 0xFF:X2})");
            debugInfo.AppendLine($"CRC字节: 0x{packet[131]:X2} (高) 0x{packet[132]:X2} (低)");

            // 检查CRC字节顺序
            if (packet[131] != ((crc >> 8) & 0xFF) || packet[132] != (crc & 0xFF))
            {
                debugInfo.AppendLine("⚠️ CRC字节顺序可能错误！");
                debugInfo.AppendLine($"预期: 0x{((crc >> 8) & 0xFF):X2} 0x{(crc & 0xFF):X2}");
                debugInfo.AppendLine($"实际: 0x{packet[131]:X2} 0x{packet[132]:X2}");
            }

            // 显示文件头前20字节
            debugInfo.Append("文件头前20字节: ");
            for (int i = 0; i < Math.Min(20, packet.Length); i++)
            {
                debugInfo.Append($"{packet[i]:X2} ");
            }
            debugInfo.AppendLine();

            Log(debugInfo.ToString());
        }

        // 调试数据包
        private void DebugDataPacket(byte blockNumber, byte[] packet, int dataLength, int packetSize, ushort crc)
        {
            StringBuilder debugInfo = new StringBuilder();
            debugInfo.AppendLine($"=== 数据包{blockNumber}调试信息 ===");
            debugInfo.AppendLine($"包类型: {(packet[0] == YmodemConstants.SOH ? "SOH(128)" : "STX(1024)")}");
            debugInfo.AppendLine($"包号: 0x{packet[1]:X2}, 补码: 0x{packet[2]:X2}");
            debugInfo.AppendLine($"实际数据长度: {dataLength} 字节");
            debugInfo.AppendLine($"包数据长度: {packetSize} 字节");
            debugInfo.AppendLine($"CRC16计算值: 0x{crc:X4} (0x{(crc >> 8) & 0xFF:X2} 0x{crc & 0xFF:X2})");
            debugInfo.AppendLine($"CRC字节: 0x{packet[3 + packetSize]:X2} (高) 0x{packet[3 + packetSize + 1]:X2} (低)");

            // 检查CRC字节顺序
            if (packet[3 + packetSize] != ((crc >> 8) & 0xFF) || packet[3 + packetSize + 1] != (crc & 0xFF))
            {
                debugInfo.AppendLine("⚠️ CRC字节顺序可能错误！");
                debugInfo.AppendLine($"预期: 0x{((crc >> 8) & 0xFF):X2} 0x{(crc & 0xFF):X2}");
                debugInfo.AppendLine($"实际: 0x{packet[3 + packetSize]:X2} 0x{packet[3 + packetSize + 1]:X2}");
            }

            // 显示包头和数据前几个字节
            debugInfo.Append("包前20字节: ");
            for (int i = 0; i < Math.Min(20, packet.Length); i++)
            {
                debugInfo.Append($"{packet[i]:X2} ");
            }
            debugInfo.AppendLine();

            // 新增：如果包号在边界附近，显示更多信息
            if (blockNumber > 60 && blockNumber < 65) // 假设0x3c7f在包61-64之间
            {
                debugInfo.Append("包数据内容(完整): ");
                for (int i = 3; i < Math.Min(20, 3 + dataLength); i++)
                {
                    debugInfo.Append($"{packet[i]:X2} ");
                }
                debugInfo.AppendLine();
            }

            Log(debugInfo.ToString());
        }

        // 新增：边界调试方法
        private void DebugBoundaryData(byte blockNumber, long startOffset, long endOffset, byte[] data, int dataLength)
        {
            // 检查0x3c7f是否在这个数据块中
            if (startOffset <= 0x3C7F && endOffset > 0x3C7F)
            {
                int relativePos = (int)(0x3C7F - startOffset);

                StringBuilder debug = new StringBuilder();
                debug.AppendLine($"=== 边界0x3C7F调试信息 ===");
                debug.AppendLine($"数据包: {blockNumber}");
                debug.AppendLine($"数据范围: 0x{startOffset:X6} - 0x{endOffset:X6}");
                debug.AppendLine($"0x3C7F在数据块中的位置: {relativePos}");
                debug.AppendLine($"实际数据长度: {dataLength} 字节");
                debug.Append($"0x3C7F附近数据(16字节): ");

                int startIdx = Math.Max(relativePos - 8, 0);
                int endIdx = Math.Min(relativePos + 8, dataLength - 1);

                for (int i = startIdx; i <= endIdx; i++)
                {
                    debug.Append($"{data[i]:X2} ");
                }
                debug.AppendLine();

                // 显示具体的0x3c7f位置
                if (relativePos >= 0 && relativePos < dataLength)
                {
                    debug.AppendLine($"0x3C7F位置数据: 0x{data[relativePos]:X2}");
                }

                Log(debug.ToString());
            }
        }

        #endregion

        #region 文件传输核心方法

        public async Task<bool> SendSingleFileAsync(string filePath,
            IProgress<Tuple<long, long, int>> progress = null)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    LogWithTimestamp($"文件不存在: {filePath}");
                    return false;
                }

                var fileInfo = new FileInfo(filePath);
                long fileSize = fileInfo.Length;

                LogWithTimestamp($"开始发送文件: {Path.GetFileName(filePath)}");
                LogWithTimestamp($"文件大小: {fileSize:N0} 字节 ({FormatFileSize(fileSize)})");
                LogWithTimestamp($"文件偏移量0x3C7F: 在第 {0x3C7F} 字节处");

                // 1. 发送文件头
                if (!await SendFileHeader(fileInfo))
                {
                    LogWithTimestamp("发送文件头失败");
                    return false;
                }

                // 2. 发送文件数据
                if (!await SendFileData(filePath, fileSize, progress))
                {
                    LogWithTimestamp("发送文件数据失败");
                    return false;
                }

                // 3. 发送EOT
                if (!await SendEOT())
                {
                    LogWithTimestamp("发送EOT失败");
                    return false;
                }

                // 4. 发送结束帧（完整传输流程）
                if (!await SendEndFrame())
                {
                    LogWithTimestamp("发送结束帧失败");
                    return false;
                }

                LogWithTimestamp($"✓ 文件 {Path.GetFileName(filePath)} 发送完成");
                return true;
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"文件发送失败: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SendFileHeader(FileInfo fileInfo)
        {
            try
            {
                LogWithTimestamp("等待设备发送'C'字符...");

                // 等待字符'C'
                byte response = await WaitForCharacter(YmodemConstants.C, YmodemConstants.CONNECTION_TIMEOUT_MS);

                if (response != YmodemConstants.C)
                {
                    LogWithTimestamp($"等待'C'超时，收到: 0x{response:X2}");
                    return false;
                }

                LogWithTimestamp("收到'C'，开始发送文件头");

                // 构建并发送文件头
                byte[] headerPacket = BuildFileHeader(fileInfo);
                string headerHex = BytesToHex(headerPacket);
                LogWithTimestamp($"发→◇{headerHex} □");

                _serialPort.Write(headerPacket, 0, headerPacket.Length);

                // 等待ACK
                response = await WaitForCharacter(YmodemConstants.ACK, YmodemConstants.RESPONSE_TIMEOUT_MS);

                if (response == YmodemConstants.ACK)
                {
                    LogWithTimestamp("✓ 文件头发送成功");
                    return true;
                }
                else
                {
                    LogWithTimestamp($"文件头响应失败: 0x{response:X2}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"发送文件头异常: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SendFileData(string filePath, long fileSize, IProgress<Tuple<long, long, int>> progress)
        {
            try
            {
                using (FileStream fs = File.OpenRead(filePath))
                {
                    byte blockNumber = 1;
                    byte[] buffer1024 = new byte[YmodemConstants.PACKET_SIZE_1024];
                    byte[] buffer128 = new byte[YmodemConstants.PACKET_SIZE_128];
                    long totalBytes = 0;
                    int retryCount = 0;

                    while (totalBytes < fileSize)
                    {
                        // 计算剩余字节数
                        long remainingBytes = fileSize - totalBytes;

                        // 决定使用哪种数据包大小
                        bool use128BytePacket = remainingBytes <= YmodemConstants.PACKET_SIZE_128;
                        int readSize = use128BytePacket ?
                            Math.Min(YmodemConstants.PACKET_SIZE_128, (int)remainingBytes) :
                            Math.Min(YmodemConstants.PACKET_SIZE_1024, (int)remainingBytes);

                        // 记录开始偏移量
                        long startOffset = totalBytes;

                        // 读取数据
                        int bytesRead;
                        if (use128BytePacket)
                        {
                            bytesRead = await fs.ReadAsync(buffer128, 0, readSize);
                        }
                        else
                        {
                            bytesRead = await fs.ReadAsync(buffer1024, 0, readSize);
                        }

                        if (bytesRead == 0)
                            break;

                        long endOffset = totalBytes + bytesRead;

                        // 边界调试
                        DebugBoundaryData(blockNumber, startOffset, endOffset,
                            use128BytePacket ? buffer128 : buffer1024, bytesRead);

                        LogWithTimestamp($"发送数据包{blockNumber}: {bytesRead}字节 (0x{startOffset:X6}-0x{endOffset:X6})");
                        LogWithTimestamp($"使用包类型: {(use128BytePacket ? "128字节" : "1024字节")}, 剩余: {remainingBytes}字节");

                        // 构建数据包
                        byte[] packet = use128BytePacket ?
                            BuildDataPacket(blockNumber, buffer128, bytesRead, true) :
                            BuildDataPacket(blockNumber, buffer1024, bytesRead, false);

                        // 发送数据包（带重试机制）
                        bool packetSent = false;
                        for (retryCount = 0; retryCount < YmodemConstants.MAX_RETRY_COUNT; retryCount++)
                        {
                            string packetHex = BytesToHex(packet);
                            LogWithTimestamp($"发→◇{packetHex} □");

                            _serialPort.Write(packet, 0, packet.Length);

                            // 等待ACK
                            byte response = await WaitForCharacter(YmodemConstants.ACK, 3000);

                            if (response == YmodemConstants.ACK)
                            {
                                packetSent = true;
                                break;
                            }
                            else if (response == YmodemConstants.NAK)
                            {
                                LogWithTimestamp($"数据包{blockNumber}收到NAK，重试 {retryCount + 1}/{YmodemConstants.MAX_RETRY_COUNT}");
                                await Task.Delay(100 * (retryCount + 1)); // 延迟递增
                            }
                            else
                            {
                                LogWithTimestamp($"数据包{blockNumber}响应异常: 0x{response:X2}，重试 {retryCount + 1}/{YmodemConstants.MAX_RETRY_COUNT}");
                                await Task.Delay(100 * (retryCount + 1));
                            }
                        }

                        if (!packetSent)
                        {
                            LogWithTimestamp($"数据包{blockNumber}发送失败，达到最大重试次数");
                            return false;
                        }

                        totalBytes += bytesRead;

                        // 更新进度
                        if (progress != null)
                        {
                            int percent = fileSize > 0 ? (int)((totalBytes * 100) / fileSize) : 0;
                            progress.Report(Tuple.Create(totalBytes, fileSize, percent));
                        }

                        LogWithTimestamp($"✓ 数据包{blockNumber}发送成功 ({FormatFileSize(totalBytes)}/{FormatFileSize(fileSize)})");

                        blockNumber++;

                        // 如果不是最后一个包，等待设备准备好接收下一个包
                        if (totalBytes < fileSize)
                        {
                            await Task.Delay(10);
                        }
                    }

                    LogWithTimestamp($"所有数据包发送完成，总共 {blockNumber - 1} 个数据包");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"发送文件数据异常: {ex.Message}");
                return false;
            }
        }

        // EOT发送方法
        // 修改EOT发送方法，适配HMT070ATA-6模块
        private async Task<bool> SendEOT()
        {
            try
            {
                LogWithTimestamp("发送EOT...");

                // 第一次发送EOT
                _serialPort.Write(new byte[] { YmodemConstants.EOT }, 0, 1);

                // 等待NAK
                byte response = await WaitForCharacter(YmodemConstants.NAK, 2000);

                if (response == YmodemConstants.NAK)
                {
                    LogWithTimestamp("收到NAK，发送第二个EOT...");

                    // 第二次发送EOT
                    _serialPort.Write(new byte[] { YmodemConstants.EOT }, 0, 1);

                    // 等待响应（可能是ACK或NAK）
                    response = await WaitForCharacter(2000);

                    if (response == YmodemConstants.ACK)
                    {
                        LogWithTimestamp("✓ EOT发送成功（收到ACK）");
                        return true;
                    }
                    else if (response == YmodemConstants.NAK)
                    {
                        // HMT070ATA-6特殊处理：如果收到第二个NAK，再发第三个EOT
                        LogWithTimestamp("收到第二个NAK，发送第三个EOT...");
                        _serialPort.Write(new byte[] { YmodemConstants.EOT }, 0, 1);

                        // 等待ACK
                        response = await WaitForCharacter(2000);

                        if (response == YmodemConstants.ACK)
                        {
                            LogWithTimestamp("✓ EOT发送成功（第三个EOT收到ACK）");
                            return true;
                        }
                        else if (response == YmodemConstants.C)
                        {
                            LogWithTimestamp("✓ EOT发送成功（收到C，设备准备接收下一个文件）");
                            return true;
                        }
                        else if (response == 0xFF) // 超时
                        {
                            // 有些设备在收到EOT后可能不响应
                            LogWithTimestamp("EOT响应超时，假设设备已接受");
                            return true;
                        }
                        else
                        {
                            LogWithTimestamp($"第三个EOT响应异常: 0x{response:X2}");
                            return false;
                        }
                    }
                    else if (response == YmodemConstants.C)
                    {
                        LogWithTimestamp("✓ EOT发送成功（收到C，设备准备接收下一个文件）");
                        return true;
                    }
                    else if (response == 0xFF) // 超时
                    {
                        // 有些设备在收到EOT后可能不响应
                        LogWithTimestamp("EOT响应超时，假设设备已接受");
                        return true;
                    }
                    else
                    {
                        LogWithTimestamp($"第二个EOT响应异常: 0x{response:X2}");
                        return false;
                    }
                }
                else if (response == YmodemConstants.ACK)
                {
                    LogWithTimestamp("✓ EOT发送成功（直接收到ACK）");
                    return true;
                }
                else if (response == YmodemConstants.C)
                {
                    LogWithTimestamp("✓ EOT发送成功（直接收到C）");
                    return true;
                }
                else if (response == 0xFF) // 超时
                {
                    // 有些设备在收到EOT后可能不响应
                    LogWithTimestamp("EOT响应超时，假设设备已接受");
                    return true;
                }
                else
                {
                    LogWithTimestamp($"EOT响应异常: 0x{response:X2}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"发送EOT异常: {ex.Message}");
                return false;
            }
        }

        // 修改WaitForCharacter方法，增加超时返回
        private async Task<byte> WaitForCharacter(int timeoutMs, byte? expectedChar = null)
        {
            DateTime startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                // 检查取消请求
                if (_cancellationTokenSource != null && _cancellationTokenSource.Token.IsCancellationRequested)
                {
                    throw new OperationCanceledException("操作被用户取消");
                }

                if (_serialPort.BytesToRead > 0)
                {
                    byte[] buffer = new byte[1];
                    int bytesRead = _serialPort.Read(buffer, 0, 1);

                    if (bytesRead == 1)
                    {
                        string receivedHex = buffer[0].ToString("X2");

                        if (expectedChar.HasValue && buffer[0] == expectedChar.Value)
                        {
                            LogWithTimestamp($"收←◆{receivedHex} ✓");
                        }
                        else if (buffer[0] == YmodemConstants.ACK)
                        {
                            LogWithTimestamp($"收←◆{receivedHex} (ACK)");
                        }
                        else if (buffer[0] == YmodemConstants.NAK)
                        {
                            LogWithTimestamp($"收←◆{receivedHex} (NAK)");
                        }
                        else if (buffer[0] == YmodemConstants.C)
                        {
                            LogWithTimestamp($"收←◆{receivedHex} (C)");
                        }
                        else
                        {
                            LogWithTimestamp($"收←◆{receivedHex}");
                        }

                        return buffer[0];
                    }
                }

                await Task.Delay(10);
            }

            LogWithTimestamp("等待响应超时");
            return 0xFF; // 超时
        }
        // 发送结束帧（END帧）
        private async Task<bool> SendEndFrame()
        {
            try
            {
                LogWithTimestamp("等待设备发送'C'接收结束帧...");

                // 等待C字符
                byte response = await WaitForCharacter(YmodemConstants.C, 10000);

                if (response != YmodemConstants.C)
                {
                    LogWithTimestamp("等待'C'超时");
                    return false;
                }

                LogWithTimestamp("发送结束帧...");

                // 构建结束帧
                byte[] endPacket = BuildEndOfTransmissionPacket();
                string endPacketHex = BytesToHex(endPacket);
                LogWithTimestamp($"发→◇{endPacketHex} □");

                _serialPort.Write(endPacket, 0, endPacket.Length);

                // 等待ACK
                response = await WaitForCharacter(YmodemConstants.ACK, 5000);

                if (response == YmodemConstants.ACK)
                {
                    LogWithTimestamp("✓ 结束帧发送成功");
                    return true;
                }
                else
                {
                    LogWithTimestamp($"结束帧响应失败: 0x{response:X2}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"发送结束帧异常: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 多文件传输（修正版）

        public async Task<bool> SendMultipleFilesAsync(List<string> filePaths,
            IProgress<Tuple<int, long, long, int>> progress = null)
        {
            try
            {
                _isTransferring = true;
                _cancellationTokenSource = new CancellationTokenSource();

                int fileCount = filePaths.Count;
                LogWithTimestamp($"开始批量发送 {fileCount} 个文件");

                // 特殊情况：如果只有一个文件，当作单文件处理
                if (fileCount == 1)
                {
                    LogWithTimestamp("只有一个文件，按单文件传输流程处理...");
                    string filePath = filePaths[0];

                    bool success = await SendSingleFileAsync(filePath,
                        new Progress<Tuple<long, long, int>>(fileProgress =>
                        {
                            if (progress != null)
                            {
                                long transferred = fileProgress.Item1;
                                long total = fileProgress.Item2;
                                int percent = fileProgress.Item3;
                                progress.Report(Tuple.Create(0, transferred, total, percent));
                            }
                        }));

                    _isTransferring = false;
                    return success;
                }

                bool allSuccess = true;
                long totalTransferredBytes = 0;
                long totalFilesSize = CalculateTotalFileSize(filePaths);

                for (int i = 0; i < fileCount; i++)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        LogWithTimestamp("传输被用户取消");
                        return false;
                    }

                    string filePath = filePaths[i];
                    string fileName = Path.GetFileName(filePath);

                    LogWithTimestamp($"\n=== 开始传输文件[{i + 1}/{fileCount}]: {fileName} ===");

                    // 检查文件是否存在
                    if (!File.Exists(filePath))
                    {
                        LogWithTimestamp($"文件不存在: {filePath}");
                        allSuccess = false;
                        continue;
                    }

                    var fileInfo = new FileInfo(filePath);
                    long fileSize = fileInfo.Length;
                    LogWithTimestamp($"文件大小: {FormatFileSize(fileSize)}");

                    try
                    {
                        // 1. 等待设备发送'C'（每个文件传输前都需要）
                        LogWithTimestamp("等待设备发送'C'字符...");
                        byte response = await WaitForCharacter(YmodemConstants.C, YmodemConstants.CONNECTION_TIMEOUT_MS);

                        if (response != YmodemConstants.C)
                        {
                            LogWithTimestamp($"等待'C'超时，收到: 0x{response:X2}");
                            allSuccess = false;
                            continue;
                        }

                        // 2. 发送文件头
                        if (!await SendFileHeader(fileInfo))
                        {
                            LogWithTimestamp($"文件头发送失败: {fileName}");
                            allSuccess = false;
                            continue;
                        }

                        // 3. 发送文件数据
                        bool fileDataSuccess = false;
                        long currentFileTransferred = 0;

                        fileDataSuccess = await SendFileData(filePath, fileSize,
                            new Progress<Tuple<long, long, int>>(fileProgress =>
                            {
                                if (progress != null)
                                {
                                    long transferred = fileProgress.Item1;
                                    long total = fileProgress.Item2;
                                    int percent = fileProgress.Item3;
                                    currentFileTransferred = transferred;

                                    // 计算总体进度
                                    long currentTotalTransferred = totalTransferredBytes + transferred;
                                    int overallPercent = totalFilesSize > 0 ?
                                        (int)((currentTotalTransferred * 100) / totalFilesSize) : 0;

                                    progress.Report(Tuple.Create(i, transferred, total, percent));
                                }
                            }));

                        if (!fileDataSuccess)
                        {
                            LogWithTimestamp($"文件数据发送失败: {fileName}");
                            allSuccess = false;
                            continue;
                        }

                        // 4. 发送EOT
                        if (!await SendEOT())
                        {
                            LogWithTimestamp($"EOT发送失败: {fileName}");
                            allSuccess = false;
                            continue;
                        }

                        totalTransferredBytes += fileSize;
                        LogWithTimestamp($"✓ 文件传输完成: {fileName}");

                        // 检查是否是最后一个文件
                        bool isLastFile = (i == fileCount - 1);

                        if (isLastFile)
                        {
                            // 最后一个文件需要发送结束帧
                            LogWithTimestamp("这是最后一个文件，发送结束帧...");
                            if (!await SendEndFrame())
                            {
                                LogWithTimestamp($"结束帧发送失败");
                                allSuccess = false;
                            }
                            else
                            {
                                LogWithTimestamp("✓ 所有文件传输完成");
                            }
                        }
                        else
                        {
                            // 不是最后一个文件，等待下一个'C'继续传输下一个文件
                            LogWithTimestamp($"文件传输完成，等待下一个'C'传输下一个文件...");
                            await Task.Delay(100);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        LogWithTimestamp("传输被用户取消");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        LogWithTimestamp($"文件传输异常 {fileName}: {ex.Message}");
                        allSuccess = false;
                    }
                }

                if (allSuccess)
                {
                    LogWithTimestamp("✓ 所有文件传输完成");
                }
                else
                {
                    LogWithTimestamp("⚠️ 部分文件传输失败");
                }

                return allSuccess;
            }
            catch (OperationCanceledException)
            {
                LogWithTimestamp("传输被用户取消");
                return false;
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"批量发送文件失败: {ex.Message}");
                return false;
            }
            finally
            {
                _isTransferring = false;
            }
        }

        // 计算所有文件总大小
        private long CalculateTotalFileSize(List<string> filePaths)
        {
            long totalSize = 0;
            foreach (string filePath in filePaths)
            {
                if (File.Exists(filePath))
                {
                    totalSize += new FileInfo(filePath).Length;
                }
            }
            return totalSize;
        }

        // 格式化文件大小显示
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }

        #endregion

        #region Ymodem模式控制

        public async Task<bool> EnterYmodemModeWithRetryAsync(int maxRetries = 3)
        {
            for (int retry = 1; retry <= maxRetries; retry++)
            {
                LogWithTimestamp($"进入Ymodem模式尝试 #{retry}/{maxRetries}");

                bool success = await EnterYmodemModeAsync();
                if (success)
                {
                    return true;
                }

                if (retry < maxRetries)
                {
                    LogWithTimestamp($"尝试失败，等待2秒后重试...");
                    await Task.Delay(2000);

                    // 清空缓冲区
                    ClearSerialBuffers();
                }
            }

            LogWithTimestamp($"× 所有尝试均失败");
            return false;
        }

        public async Task<bool> EnterYmodemModeAsync()
        {
            try
            {
                LogWithTimestamp("发送Ymodem模式进入指令...");

                // 清空缓冲区
                ClearSerialBuffers();
                await Task.Delay(100);

                // 发送指令
                string cmdHex = BytesToHex(YmodemConstants.ENTER_YMODEM_CMD);
                LogWithTimestamp($"发→◇{cmdHex} □");

                _serialPort.Write(YmodemConstants.ENTER_YMODEM_CMD, 0, YmodemConstants.ENTER_YMODEM_CMD.Length);

                LogWithTimestamp("等待模块响应（最多等待15秒）...");

                DateTime startTime = DateTime.Now;
                while ((DateTime.Now - startTime).TotalSeconds < 15)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        LogWithTimestamp("操作被用户取消");
                        return false;
                    }

                    if (_serialPort.BytesToRead > 0)
                    {
                        byte[] buffer = new byte[_serialPort.BytesToRead];
                        int bytesRead = _serialPort.Read(buffer, 0, buffer.Length);

                        string receivedHex = BytesToHex(buffer, bytesRead);
                        LogWithTimestamp($"收←◆{receivedHex}");

                        if (buffer.Any(b => b == YmodemConstants.C))
                        {
                            LogWithTimestamp("✓ 检测到字符'C'，模块已进入Ymodem模式");
                            return true;
                        }
                    }

                    await Task.Delay(100);
                }

                LogWithTimestamp("× 等待超时，未收到模块响应");
                return false;
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"进入Ymodem模式失败: {ex.Message}");
                return false;
            }
        }

        public bool SendResetCommand()
        {
            try
            {
                LogWithTimestamp("发送复位指令...");

                string cmdHex = BytesToHex(YmodemConstants.RESET_CMD);
                LogWithTimestamp($"发→◇{cmdHex} □");

                _serialPort.Write(YmodemConstants.RESET_CMD, 0, YmodemConstants.RESET_CMD.Length);
                _serialPort.BaseStream.Flush();

                LogWithTimestamp("✓ 复位指令已发送");
                return true;
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"发送复位指令失败: {ex.Message}");
                return false;
            }
        }

        private void ClearSerialBuffers()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                }
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"清空串口缓冲区失败: {ex.Message}");
            }
        }

        #endregion

        #region 辅助方法

        private async Task<byte> WaitForCharacter(byte expectedChar, int timeoutMs)
        {
            DateTime startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                // 检查取消请求
                if (_cancellationTokenSource != null && _cancellationTokenSource.Token.IsCancellationRequested)
                {
                    throw new OperationCanceledException("操作被用户取消");
                }

                if (_serialPort.BytesToRead > 0)
                {
                    byte[] buffer = new byte[1];
                    int bytesRead = _serialPort.Read(buffer, 0, 1);

                    if (bytesRead == 1)
                    {
                        string receivedHex = buffer[0].ToString("X2");
                        if (buffer[0] == expectedChar)
                        {
                            LogWithTimestamp($"收←◆{receivedHex} ✓");
                        }
                        else
                        {
                            LogWithTimestamp($"收←◆{receivedHex}");
                        }
                        return buffer[0];
                    }
                }

                await Task.Delay(10);
            }

            LogWithTimestamp($"× 等待字符0x{expectedChar:X2}超时({timeoutMs}ms)");
            return 0xFF;
        }

        private string BytesToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        private string BytesToHex(byte[] bytes, int length)
        {
            return BitConverter.ToString(bytes, 0, length).Replace("-", " ");
        }

        private void Log(string message)
        {
            _logProgress?.Report(message);
        }

        private void LogWithTimestamp(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Log($"[{timestamp}] {message}");
        }

        #endregion
    }

    
}