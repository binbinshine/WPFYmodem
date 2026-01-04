using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace WPFYmodem
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region 私有字段
        private Hmt070YmodemSender _ymodemSender;
        private SerialPort _serialPort;
        private CancellationTokenSource _transferCts;
        private DispatcherTimer _refreshTimer;
        private DispatcherTimer _timeTimer;
        private NaturalStringComparer _naturalComparer = new NaturalStringComparer();

        private long _totalFileSize;
        private int _currentFileIndex;
        private int _totalFiles;
        private long _transferredBytes;
        private long _totalTransferSize;
        #endregion

        #region 公共属性
        private ObservableCollection<FileItemViewModel> _fileList = new ObservableCollection<FileItemViewModel>();
        public ObservableCollection<FileItemViewModel> FileList
        {
            get => _fileList;
            set
            {
                _fileList = value;
                OnPropertyChanged(nameof(FileList));
            }
        }
        private void ComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 只有当当前 ComboBox 是具有焦点的控件时才处理滚轮事件
            if (sender is ComboBox comboBox && comboBox == _currentComboBoxWithFocus)
            {
                // 防止事件冒泡
                e.Handled = true;

                if (comboBox.Items.Count == 0) return;

                int currentIndex = comboBox.SelectedIndex;
                int newIndex;

                // 根据滚轮方向计算新索引
                if (e.Delta > 0) // 向上滚动
                {
                    newIndex = currentIndex - 1;
                    if (newIndex < 0) newIndex = comboBox.Items.Count - 1;
                }
                else // 向下滚动
                {
                    newIndex = currentIndex + 1;
                    if (newIndex >= comboBox.Items.Count) newIndex = 0;
                }

                // 设置新选中的项
                comboBox.SelectedIndex = newIndex;
            }
        }
        // 存储当前具有焦点的 ComboBox
        private static ComboBox _currentComboBoxWithFocus;
        private void ComboBox_MouseEnter(object sender, MouseEventArgs e)
        {
            // 当鼠标进入 ComboBox 时，设置为当前具有焦点的控件
            if (sender is ComboBox comboBox)
            {
                _currentComboBoxWithFocus = comboBox;
            }
        }

        private void ComboBox_MouseLeave(object sender, MouseEventArgs e)
        {
            // 当鼠标离开 ComboBox 时，清除当前具有焦点的控件
            if (sender is ComboBox comboBox && _currentComboBoxWithFocus == comboBox)
            {
                _currentComboBoxWithFocus = null;
            }
        }
        private bool _isTransferring;
        public bool IsTransferring
        {
            get => _isTransferring;
            set
            {
                _isTransferring = value;
                OnPropertyChanged(nameof(IsTransferring));
                UpdateUIState();
            }
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
                UpdateUIState();
                UpdateConnectionIndicator();
            }
        }

        private bool _showResetPrompt;
        public bool ShowResetPrompt
        {
            get => _showResetPrompt;
            set
            {
                _showResetPrompt = value;
                OnPropertyChanged(nameof(ShowResetPrompt));
            }
        }

        private bool _needReset = false;
        #endregion

        #region 构造函数和初始化
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            InitializeSerialPorts();
            SetupRefreshTimer();
            SetupTimeUpdateTimer();

            // 设置默认选择
            cbBaudRate.SelectedIndex = 3; // 115200
            cbDataBits.SelectedIndex = 3; // 8
            cbStopBits.SelectedIndex = 0; // 1
            cbParity.SelectedIndex = 0;  // None
            cbFlowControl.SelectedIndex = 0; // None
        }

        private void InitializeSerialPorts()
        {
            GetSerialPort();
        }

        private void SetupRefreshTimer()
        {
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(3);
            _refreshTimer.Tick += RefreshTimer_Tick;

            if (chkAutoRefresh.IsChecked == true)
                _refreshTimer.Start();
        }

        private void SetupTimeUpdateTimer()
        {
            _timeTimer = new DispatcherTimer();
            _timeTimer.Interval = TimeSpan.FromSeconds(1);
            _timeTimer.Tick += TimeTimer_Tick;
            _timeTimer.Start();
        }
        #endregion

        #region 串口管理
        public void GetSerialPort()
        {
            try
            {
                string[] serialPortList = SerialPort.GetPortNames();

                if (serialPortList.Length == 0)
                {
                    cbComPort.Items.Clear();
                    cbComPort.Items.Add("无可用串口");
                    cbComPort.SelectedIndex = 0;
                    cbComPort.IsEnabled = false;

                    AddLog("未发现可用串口", LogLevel.Warning);
                    return;
                }

                // 自然排序
                serialPortList = serialPortList.OrderBy(p => p, _naturalComparer).ToArray();

                // 保存当前选中的端口
                string currentSelection = cbComPort.SelectedItem?.ToString();
                bool selectionChanged = false;

                cbComPort.Items.Clear();

                foreach (string port in serialPortList)
                {
                    cbComPort.Items.Add(port);
                }

                // 恢复之前的选中项
                if (!string.IsNullOrEmpty(currentSelection) && cbComPort.Items.Contains(currentSelection))
                {
                    cbComPort.SelectedItem = currentSelection;
                }
                else if (cbComPort.Items.Count > 0)
                {
                    cbComPort.SelectedIndex = 0;
                    selectionChanged = true;
                }

                cbComPort.IsEnabled = true;

                if (selectionChanged && cbComPort.SelectedItem != null)
                {
                    string selectedPort = cbComPort.SelectedItem.ToString();
                    AddLog($"发现 {serialPortList.Length} 个串口，已选择: {selectedPort}", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                AddLog($"获取串口列表失败: {ex.Message}", LogLevel.Error);

                cbComPort.Items.Clear();
                cbComPort.Items.Add("获取串口失败");
                cbComPort.SelectedIndex = 0;
                cbComPort.IsEnabled = false;
            }
        }

        private void RefreshSerialPorts()
        {
            GetSerialPort();
        }

        private async void ConnectSerialPort()
        {
            if (cbComPort.SelectedItem == null ||
                cbComPort.SelectedItem.ToString() == "无可用串口" ||
                cbComPort.SelectedItem.ToString() == "获取串口失败")
            {
                AddLog("请选择有效的串口端口", LogLevel.Warning);
                return;
            }

            string portName = cbComPort.SelectedItem.ToString();

            try
            {
                int baudRate = 115200;
                if (cbBaudRate.SelectedItem != null && cbBaudRate.SelectedItem is ComboBoxItem item)
                {
                    string baudRateText = item.Content.ToString();
                    int.TryParse(baudRateText, out baudRate);
                }

                int dataBits = 8;
                if (cbDataBits.SelectedItem != null && cbDataBits.SelectedItem is ComboBoxItem dataBitsItem)
                {
                    string dataBitsText = dataBitsItem.Content.ToString();
                    int.TryParse(dataBitsText, out dataBits);
                }

                _serialPort = new SerialPort(portName, baudRate)
                {
                    DataBits = dataBits,
                    Parity = GetParity(cbParity.SelectedIndex),
                    StopBits = GetStopBits(cbStopBits.SelectedIndex),
                    Handshake = GetHandshake(cbFlowControl.SelectedIndex),
                    ReadTimeout = 10000,
                    WriteTimeout = 10000,
                    Encoding = Encoding.GetEncoding("iso-8859-1")
                };

                AddLog($"正在连接串口: {portName} ({baudRate} baud)...", LogLevel.Info);

                _serialPort.Open();
                IsConnected = true;

                _ymodemSender = new Hmt070YmodemSender(_serialPort, new Progress<string>(s => AddLog(s, LogLevel.Info)));

                AddLog($"已连接到串口: {portName} ({baudRate} baud)", LogLevel.Success);
                UpdateStatusBar($"已连接到 {portName}", true);

                indicatorConnection.Fill = Brushes.Green;
                txtConnectionStatus.Text = $"已连接 {portName}";

                // 尝试进入Ymodem模式（带重试）
                AddLog("正在进入Ymodem模式...", LogLevel.Info);
                var result = await _ymodemSender.EnterYmodemModeWithRetryAsync(3);
                if (result)
                {
                    AddLog("✓ 模块已进入Ymodem模式，可以开始传输", LogLevel.Success);
                    btnStartTransfer.IsEnabled = true;
                    btnTestYmodem.IsEnabled = true;
                }
                else
                {
                    AddLog("× 模块未正确响应，但可以尝试传输", LogLevel.Warning);
                    btnStartTransfer.IsEnabled = true;
                    btnTestYmodem.IsEnabled = true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                AddLog($"串口 {portName} 被占用或无访问权限", LogLevel.Error);
                IsConnected = false;
            }
            catch (IOException ioEx)
            {
                AddLog($"串口 {portName} IO错误: {ioEx.Message}", LogLevel.Error);
                IsConnected = false;
            }
            catch (Exception ex)
            {
                AddLog($"连接串口失败: {ex.Message}", LogLevel.Error);
                IsConnected = false;
            }
        }

        private void DisconnectSerialPort()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    AddLog("正在断开串口连接...", LogLevel.Info);
                    _serialPort.Close();
                    AddLog("已断开串口连接", LogLevel.Info);
                }

                IsConnected = false;
                indicatorConnection.Fill = Brushes.Gray;
                txtConnectionStatus.Text = "未连接";
                UpdateStatusBar("已断开连接", false);

                // 隐藏复位提示
                ShowResetPrompt = false;
                btnReset.IsEnabled = false;
                btnStartTransfer.IsEnabled = false;
                btnTestYmodem.IsEnabled = false;
            }
            catch (Exception ex)
            {
                AddLog($"断开连接失败: {ex.Message}", LogLevel.Error);
            }
        }

        private Parity GetParity(int index)
        {
            switch (index)
            {
                case 1: return Parity.Odd;
                case 2: return Parity.Even;
                case 3: return Parity.Mark;
                case 4: return Parity.Space;
                default: return Parity.None;
            }
        }

        private StopBits GetStopBits(int index)
        {
            switch (index)
            {
                case 1: return StopBits.OnePointFive;
                case 2: return StopBits.Two;
                default: return StopBits.One;
            }
        }

        private Handshake GetHandshake(int index)
        {
            switch (index)
            {
                case 1: return Handshake.XOnXOff;
                case 2: return Handshake.RequestToSend;
                case 3: return Handshake.RequestToSendXOnXOff;
                default: return Handshake.None;
            }
        }

        // 复位模块按钮事件
        private async void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (!IsConnected || _serialPort == null || !_serialPort.IsOpen)
            {
                AddLog("请先连接串口", LogLevel.Warning);
                return;
            }

            try
            {
                AddLog("正在发送复位指令...", LogLevel.Info);

                // 先清空缓冲区
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                // 等待一会儿确保清空完成
                await Task.Delay(100);

                // 发送复位指令
                bool success = _ymodemSender.SendResetCommand();

                if (success)
                {
                    AddLog("复位指令已发送，模块正在复位...", LogLevel.Success);
                    AddLog("请等待设备重启（约10秒）", LogLevel.Info);

                    // 等待足够时间让设备重启
                    await Task.Delay(5000);

                    // 检查串口是否仍然可用
                    try
                    {
                        // 尝试发送一些数据看看设备是否还在响应
                        _serialPort.Write("AT\r\n");
                        await Task.Delay(1000);
                    }
                    catch
                    {
                        // 设备已断开是正常现象
                    }

                    // 断开串口连接
                    DisconnectSerialPort();

                    AddLog("模块复位完成，请重新连接串口", LogLevel.Info);

                    // 隐藏复位提示
                    ShowResetPrompt = false;
                    btnReset.IsEnabled = false;
                    _needReset = false;
                }
                else
                {
                    AddLog("发送复位指令失败", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                AddLog($"发送复位指令失败: {ex.Message}", LogLevel.Error);
            }
        }
        #endregion

        #region 文件列表操作

        // 文件选择变化事件
        private void LbFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = lbFiles.SelectedItems != null && lbFiles.SelectedItems.Count > 0;
            btnRemoveSelected.IsEnabled = hasSelection;
            btnMoveUp.IsEnabled = hasSelection && lbFiles.SelectedIndex > 0;
            btnMoveDown.IsEnabled = hasSelection && lbFiles.SelectedIndex < FileList.Count - 1;
        }

        // 移除单个文件按钮事件
        private void RemoveFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string filePath)
            {
                RemoveFileFromList(filePath);
            }
        }

        // 移除选中文件按钮事件
        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (lbFiles.SelectedItems != null && lbFiles.SelectedItems.Count > 0)
            {
                var itemsToRemove = lbFiles.SelectedItems.Cast<FileItemViewModel>().ToList();

                foreach (var item in itemsToRemove)
                {
                    FileList.Remove(item);
                    AddLog($"已移除文件: {item.FileName}", LogLevel.Info);
                }

                UpdateFileStatistics();
            }
        }

        // 上移文件位置
        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (lbFiles.SelectedIndex > 0)
            {
                int selectedIndex = lbFiles.SelectedIndex;
                var selectedItem = FileList[selectedIndex];

                FileList.RemoveAt(selectedIndex);
                FileList.Insert(selectedIndex - 1, selectedItem);

                lbFiles.SelectedIndex = selectedIndex - 1;
                AddLog($"已将文件上移: {selectedItem.FileName}", LogLevel.Info);
            }
        }

        // 下移文件位置
        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (lbFiles.SelectedIndex >= 0 && lbFiles.SelectedIndex < FileList.Count - 1)
            {
                int selectedIndex = lbFiles.SelectedIndex;
                var selectedItem = FileList[selectedIndex];

                FileList.RemoveAt(selectedIndex);
                FileList.Insert(selectedIndex + 1, selectedItem);

                lbFiles.SelectedIndex = selectedIndex + 1;
                AddLog($"已将文件下移: {selectedItem.FileName}", LogLevel.Info);
            }
        }

        // 从列表中移除文件
        private void RemoveFileFromList(string filePath)
        {
            var fileToRemove = FileList.FirstOrDefault(f => f.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            if (fileToRemove != null)
            {
                FileList.Remove(fileToRemove);
                AddLog($"已移除文件: {fileToRemove.FileName}", LogLevel.Info);
                UpdateFileStatistics();
            }
        }

        #endregion

        #region 事件处理
        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (chkAutoRefresh.IsChecked == true)
                RefreshSerialPorts();
        }

        private void TimeTimer_Tick(object sender, EventArgs e)
        {
            txtCurrentTime.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            //ConnectSerialPort();
            // 检查年份，如果是2025年可以使用，否则提示升级
            int currentYear = DateTime.Now.Year;
            int currentMonth = DateTime.Now.Month;

            // 检查是否在支持范围内
            // 条件1：2025年全年
            // 条件2：2026年且月份<=3
            bool isSupported = (currentYear == 2025) ||
                              (currentYear == 2026 && currentMonth <= 2);

            if (isSupported)
            {
                ConnectSerialPort();
            }
            else
            {
                string message;

                if (currentYear == 2026 && currentMonth > 3)
                {
                    message = $"当前时间：{DateTime.Now:yyyy年M月}\n\n本软件支持范围：\n- 2025年全年\n- 2026年1-3月\n\n已超过支持期限，请升级到最新版本后使用。";
                }
                else if (currentYear > 2026)
                {
                    message = $"当前时间：{DateTime.Now:yyyy年}\n\n本软件支持范围：\n- 2025年全年\n- 2026年1-3月\n\n已超过支持期限，请升级到最新版本后使用。";
                }
                else if (currentYear < 2025)
                {
                    message = $"当前时间：{DateTime.Now:yyyy年}\n\n本软件支持从2025年1月开始使用。";
                }
                else
                {
                    message = "软件不在支持的时间范围内，请升级到最新版本。";
                }

                MessageBox.Show(message, "软件有效期", MessageBoxButton.OK, MessageBoxImage.Warning);

                // 禁用按钮
                if (btnConnect != null)
                {
                    btnConnect.IsEnabled = false;
                    btnConnect.Content = "已过期";
                    btnConnect.ToolTip = $"支持到2026年3月，当前：{DateTime.Now:yyyy年M月}";
                }
            }
        }
        private readonly IProgress<string> _logProgress;
        private void Log(string message)
        {
            _logProgress?.Report(message);
        }

        private void LogWithTimestamp(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Log($"[{timestamp}] {message}");
        }
        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            DisconnectSerialPort();
        }

        private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            AddLog("手动刷新串口列表...", LogLevel.Info);
            RefreshSerialPorts();
        }

        // 测试Ymodem按钮事件
        private async void BtnTestYmodem_Click(object sender, RoutedEventArgs e)
        {
            if (!IsConnected || _ymodemSender == null || _serialPort == null)
            {
                AddLog("请先连接串口", LogLevel.Warning);
                return;
            }

            AddLog("开始测试Ymodem模式...", LogLevel.Info);

            try
            {
                // 清空缓冲区
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                // 发送进入Ymodem模式指令
                AddLog("发送Ymodem模式进入指令...", LogLevel.Info);
                string cmdHex = BytesToHex(YmodemConstants.ENTER_YMODEM_CMD);
                AddLog($"发→◇{cmdHex} □", LogLevel.Info);

                // 使用Write方法发送字节数组
                _serialPort.Write(YmodemConstants.ENTER_YMODEM_CMD, 0, YmodemConstants.ENTER_YMODEM_CMD.Length);

                // 给设备一点处理时间
                await Task.Delay(100);

                // 等待响应
                AddLog("等待模块响应（最多等待15秒）...", LogLevel.Info);

                DateTime startTime = DateTime.Now;
                bool receivedC = false;

                while ((DateTime.Now - startTime).TotalSeconds < 15)
                {
                    if (_serialPort.BytesToRead > 0)
                    {
                        // 读取所有可用数据
                        int bytesToRead = _serialPort.BytesToRead;
                        byte[] buffer = new byte[bytesToRead];
                        int bytesRead = _serialPort.Read(buffer, 0, bytesToRead);

                        string receivedHex = BytesToHex(buffer, bytesRead);
                        AddLog($"收←◆{receivedHex}", LogLevel.Info);

                        // 检查是否收到'C'
                        foreach (byte b in buffer)
                        {
                            if (b == YmodemConstants.C)
                            {
                                receivedC = true;
                                AddLog("✓ 检测到字符'C'", LogLevel.Success);
                            }
                        }

                        // 如果收到'C'，可以提前退出
                        if (receivedC)
                            break;
                    }

                    await Task.Delay(100);
                }

                if (receivedC)
                {
                    AddLog("✓ Ymodem模式测试成功，模块已准备就绪", LogLevel.Success);

                    // 显示更多信息
                    AddLog($"设备会每4秒发送一次'C'字符", LogLevel.Info);
                    AddLog($"现在可以点击【开始传输】按钮上传文件", LogLevel.Info);
                }
                else
                {
                    AddLog("× Ymodem模式测试失败，未收到字符'C'", LogLevel.Error);

                    // 给出可能的解决方案
                    AddLog("可能的解决方案：", LogLevel.Warning);
                    AddLog("1. 检查串口线是否连接正确", LogLevel.Warning);
                    AddLog("2. 检查设备是否已上电", LogLevel.Warning);
                    AddLog("3. 尝试重新连接串口", LogLevel.Warning);
                    AddLog("4. 检查波特率设置是否正确", LogLevel.Warning);
                }
            }
            catch (UnauthorizedAccessException)
            {
                AddLog("串口被占用或无访问权限", LogLevel.Error);
            }
            catch (IOException ioEx)
            {
                AddLog($"串口IO错误: {ioEx.Message}", LogLevel.Error);
            }
            catch (Exception ex)
            {
                AddLog($"测试Ymodem模式失败: {ex.Message}", LogLevel.Error);
            }
        }

        // 添加辅助方法：字节数组转十六进制字符串
        private string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        // 添加辅助方法：字节数组转十六进制字符串（指定长度）
        private string BytesToHex(byte[] bytes, int length)
        {
            if (bytes == null || length <= 0 || length > bytes.Length)
                return string.Empty;

            return BitConverter.ToString(bytes, 0, length).Replace("-", " ");
        }

        private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "所有文件|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                Console.WriteLine($"开始添加 {openFileDialog.FileNames.Length} 个文件");

                foreach (var filePath in openFileDialog.FileNames)
                {
                    try
                    {
                        var fileInfo = new System.IO.FileInfo(filePath);
                        Console.WriteLine($"处理文件: {filePath}, 大小: {fileInfo.Length}");

                        var fileModel = new FileItemViewModel
                        {
                            FileName = System.IO.Path.GetFileName(filePath),
                            FullPath = filePath,
                            FileSize = fileInfo.Length,
                            Progress = 0,
                            IsTransferring = false
                        };

                        // 检查FileList是否为null
                        if (FileList == null)
                        {
                            Console.WriteLine("错误: FileList 为 null!");
                            FileList = new ObservableCollection<FileItemViewModel>();
                        }

                        FileList.Add(fileModel);
                        Console.WriteLine($"添加到FileList成功，当前数量: {FileList.Count}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"添加文件异常: {ex.Message}");
                    }
                }

                UpdateFileStatistics();
                Console.WriteLine($"添加完成，总共 {FileList.Count} 个文件");
            }
        }
        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            // 使用OpenFileDialog模拟文件夹选择
            var dialog = new OpenFileDialog
            {
                Title = "选择文件夹",
                Filter = "文件夹|*.none",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "选择文件夹"
            };

            if (dialog.ShowDialog() == true)
            {
                string folderPath = Path.GetDirectoryName(dialog.FileName);

                if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    var extensions = new[] { "*.bin", "*.tml", "*.BIN", "*.TML", "*.lua", "*.LUA" };
                    var files = new List<string>();

                    foreach (var ext in extensions)
                    {
                        files.AddRange(Directory.GetFiles(folderPath, ext));
                    }

                    foreach (var file in files)
                    {
                        AddFileToList(file);
                    }
                    UpdateFileStatistics();
                }
            }
        }

        private void BtnClearFiles_Click(object sender, RoutedEventArgs e)
        {
            FileList.Clear();
            UpdateFileStatistics();
            AddLog("已清空文件列表", LogLevel.Info);
        }

        private async void BtnStartTransfer_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.Count == 0)
            {
                AddLog("请先选择要传输的文件", LogLevel.Warning);
                return;
            }

            if (!IsConnected || _ymodemSender == null)
            {
                AddLog("请先连接串口", LogLevel.Warning);
                return;
            }

            IsTransferring = true;
            _transferCts = new CancellationTokenSource();

            // 初始化进度和统计
            progressBar.Value = 0;
            _currentFileIndex = 0;
            _totalFiles = FileList.Count;
            _transferredBytes = 0;
            _totalTransferSize = FileList.Sum(f => f.FileSize);

            UpdateStatusBar("传输进行中...", true);
            txtTransferStatus.Text = "传输中";
            indicatorTransfer.Fill = Brushes.Yellow;

            // 更新统计显示
            txtCurrentFileStat.Text = "准备中...";
            txtFileProgressStat.Text = "0%";
            txtBytesTransferred.Text = "0 bytes";
            txtOverallProgressStat.Text = "0%";
            txtTransferredSize.Text = "0 bytes";
            txtOverallProgress.Text = "0%";

            // 禁用复位按钮
            btnReset.IsEnabled = false;
            _needReset = false;

            try
            {
                var filePaths = FileList.Select(f => f.FullPath).ToList();
                bool success = false;

                if (filePaths.Count == 1)
                {
                    // 单文件传输
                    var fileItem = FileList[0];
                    fileItem.IsTransferring = true;

                    string fileName = Path.GetFileName(fileItem.FullPath);
                    Dispatcher.Invoke(() =>
                    {
                        txtCurrentFile.Text = fileName;
                        txtCurrentFileStat.Text = fileName;
                    });

                    var fileInfo = new FileInfo(filePaths[0]);
                    long fileSize = fileInfo.Length;

                    success = await _ymodemSender.SendSingleFileAsync(filePaths[0],
                        new Progress<Tuple<long, long, int>>(progress =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                long transferred = progress.Item1;
                                long total = progress.Item2;
                                int percent = progress.Item3;

                                // 更新文件项进度
                                fileItem.Progress = percent;

                                // 更新主进度条
                                progressBar.Value = percent;
                                txtFileProgress.Text = $"{percent}%";
                                txtFileProgressStat.Text = $"{percent}%";

                                _transferredBytes = transferred;
                                txtTransferredSize.Text = FormatFileSize(transferred);
                                txtBytesTransferred.Text = $"{FormatFileSize(transferred)} / {FormatFileSize(total)}";

                                // 更新总体进度（单个文件就是文件进度）
                                txtOverallProgress.Text = $"{percent}%";
                                txtOverallProgressStat.Text = $"{percent}%";
                            });
                        }));

                    fileItem.IsTransferring = false;
                }
                else
                {
                    // 多文件传输
                    AddLog($"开始批量传输 {filePaths.Count} 个文件", LogLevel.Info);
                    AddLog("每个文件将单独传输，传输完成后设备会自动复位", LogLevel.Info);

                    success = await _ymodemSender.SendMultipleFilesAsync(filePaths,
                        new Progress<Tuple<int, long, long, int>>(progressInfo =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                int fileIndex = progressInfo.Item1;
                                long transferred = progressInfo.Item2;
                                long fileSize = progressInfo.Item3;
                                int filePercent = progressInfo.Item4;

                                // 更新当前文件的进度
                                if (fileIndex < FileList.Count)
                                {
                                    // 重置之前所有文件的进度为100%（已完成的文件）
                                    for (int i = 0; i < fileIndex; i++)
                                    {
                                        FileList[i].Progress = 100;
                                        FileList[i].IsTransferring = false;
                                    }

                                    // 更新当前文件的进度
                                    FileList[fileIndex].Progress = filePercent;
                                    FileList[fileIndex].IsTransferring = true;
                                }

                                string fileName = System.IO.Path.GetFileName(filePaths[fileIndex]);
                                txtCurrentFile.Text = fileName;
                                txtCurrentFileStat.Text = fileName;
                                txtFileProgress.Text = $"{filePercent}%";
                                txtFileProgressStat.Text = $"{filePercent}%";

                                // 计算已传输的总字节数
                                long totalTransferredSoFar = 0;
                                for (int i = 0; i < fileIndex; i++)
                                {
                                    totalTransferredSoFar += new FileInfo(filePaths[i]).Length;
                                }
                                totalTransferredSoFar += transferred;

                                _transferredBytes = totalTransferredSoFar;
                                txtTransferredSize.Text = FormatFileSize(totalTransferredSoFar);
                                txtBytesTransferred.Text = $"{FormatFileSize(transferred)} / {FormatFileSize(fileSize)}";

                                // 计算总体进度
                                int overallPercent = (int)((totalTransferredSoFar * 100) / _totalTransferSize);
                                progressBar.Value = overallPercent;
                                txtOverallProgress.Text = $"{overallPercent}%";
                                txtOverallProgressStat.Text = $"{overallPercent}%";
                            });
                        }));
                }

                if (success)
                {
                    AddLog("✓ 文件传输完成", LogLevel.Success);
                    AddLog("⚠️ 模块需要复位，请点击【复位模块】按钮重启设备", LogLevel.Warning);

                    UpdateStatusBar("传输完成，请复位模块", true);
                    txtTransferStatus.Text = "完成";
                    indicatorTransfer.Fill = Brushes.Green;

                    // 显示复位提示
                    _needReset = true;
                    ShowResetPrompt = true;
                    btnReset.IsEnabled = true;


                    #region 复位功能
                    if (!IsConnected || _serialPort == null || !_serialPort.IsOpen)
                    {
                        AddLog("请先连接串口", LogLevel.Warning);
                        return;
                    }

                    try
                    {
                        AddLog("正在发送复位指令...", LogLevel.Info);

                        // 先清空缓冲区
                        _serialPort.DiscardInBuffer();
                        _serialPort.DiscardOutBuffer();

                        // 等待一会儿确保清空完成
                        await Task.Delay(100);

                        // 发送复位指令
                        success = _ymodemSender.SendResetCommand();

                        if (success)
                        {
                            AddLog("复位指令已发送，模块正在复位...", LogLevel.Success);
                            AddLog("请等待设备重启（约10秒）", LogLevel.Info);

                            // 等待足够时间让设备重启
                            await Task.Delay(5000);

                            // 检查串口是否仍然可用
                            try
                            {
                                // 尝试发送一些数据看看设备是否还在响应
                                _serialPort.Write("AT\r\n");
                                await Task.Delay(1000);
                            }
                            catch
                            {
                                // 设备已断开是正常现象
                            }

                            // 断开串口连接
                            DisconnectSerialPort();

                            AddLog("模块复位完成，请重新连接串口", LogLevel.Info);

                            // 隐藏复位提示
                            ShowResetPrompt = false;
                            btnReset.IsEnabled = false;
                            _needReset = false;
                        }
                        else
                        {
                            AddLog("发送复位指令失败", LogLevel.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"发送复位指令失败: {ex.Message}", LogLevel.Error);
                    }
                    #endregion





                    // 播放提示音
                    System.Media.SystemSounds.Asterisk.Play();

                    // 显示最终统计
                    txtOverallProgress.Text = "100%";
                    txtOverallProgressStat.Text = "100%";
                    txtTransferredSize.Text = FormatFileSize(_totalTransferSize);
                    txtBytesTransferred.Text = FormatFileSize(_totalTransferSize);

                    // 所有文件进度设为100%，并重置传输状态
                    foreach (var file in FileList)
                    {
                        file.Progress = 100;
                        file.IsTransferring = false;
                    }
                }
                else
                {
                    AddLog("× 文件传输失败", LogLevel.Error);
                    UpdateStatusBar("传输失败", false);
                    txtTransferStatus.Text = "失败";
                    indicatorTransfer.Fill = Brushes.Red;

                    // 重置所有文件的传输状态
                    foreach (var file in FileList)
                    {
                        file.IsTransferring = false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AddLog("传输已取消", LogLevel.Warning);
                UpdateStatusBar("传输已取消", false);
                txtTransferStatus.Text = "已取消";
                indicatorTransfer.Fill = Brushes.Gray;

                // 重置所有文件的传输状态
                foreach (var file in FileList)
                {
                    file.IsTransferring = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"传输失败: {ex.Message}", LogLevel.Error);
                UpdateStatusBar("传输失败", false);
                txtTransferStatus.Text = "错误";
                indicatorTransfer.Fill = Brushes.Red;

                // 重置所有文件的传输状态
                foreach (var file in FileList)
                {
                    file.IsTransferring = false;
                }
            }
            finally
            {
                IsTransferring = false;
                if (_transferCts != null)
                {
                    _transferCts.Dispose();
                    _transferCts = null;
                }

                // 如果传输完成，进度条保持100%
                if (progressBar.Value < 100)
                {
                    progressBar.Value = 0;
                    txtTransferStatus.Text = "空闲";
                    indicatorTransfer.Fill = Brushes.Gray;
                }
            }
        }

        private void BtnCancelTransfer_Click(object sender, RoutedEventArgs e)
        {
            if (_transferCts != null && !_transferCts.IsCancellationRequested)
            {
                _transferCts.Cancel();
                AddLog("正在取消传输...", LogLevel.Info);
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            rtbLog.Document.Blocks.Clear();
        }

        private void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = $"Ymodem_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var textRange = new TextRange(
                        rtbLog.Document.ContentStart,
                        rtbLog.Document.ContentEnd);

                    using (var stream = new FileStream(saveFileDialog.FileName, FileMode.Create))
                    {
                        textRange.Save(stream, DataFormats.Text);
                    }

                    AddLog($"日志已保存到: {saveFileDialog.FileName}", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    AddLog($"保存日志失败: {ex.Message}", LogLevel.Error);
                }
            }
        }
        #endregion

        #region 辅助方法
        private void AddFileToList(string filePath)
        {
            if (!File.Exists(filePath))
            {
                AddLog($"文件不存在: {filePath}", LogLevel.Warning);
                return;
            }

            var fileInfo = new FileInfo(filePath);
            var fileItem = new FileItemViewModel
            {
                FileName = Path.GetFileName(filePath),
                FullPath = filePath,
                FileSize = fileInfo.Length,
                Progress = 0,
                IsTransferring = false
            };

            // 检查是否已存在
            if (!FileList.Any(f => f.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            {
                FileList.Add(fileItem);
                AddLog($"已添加文件: {fileItem.FileName} ({FormatFileSize(fileItem.FileSize)})", LogLevel.Info);
            }
            else
            {
                AddLog($"文件已存在: {fileItem.FileName}", LogLevel.Warning);
            }
        }

        private void UpdateFileStatistics()
        {
            _totalFileSize = FileList.Sum(f => f.FileSize);
            txtTotalFiles.Text = $"{FileList.Count} 个文件";
            txtTotalSize.Text = FormatFileSize(_totalFileSize);
            txtFileCount.Text = FileList.Count.ToString();
        }

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

        private void UpdateUIState()
        {
            Dispatcher.Invoke(() =>
            {
                btnConnect.IsEnabled = !IsConnected;
                btnDisconnect.IsEnabled = IsConnected;
                btnStartTransfer.IsEnabled = IsConnected && FileList.Count > 0 && !IsTransferring;
                btnCancelTransfer.IsEnabled = IsTransferring;
                btnTestYmodem.IsEnabled = IsConnected && !IsTransferring;

                cbComPort.IsEnabled = !IsConnected && !IsTransferring;
                cbBaudRate.IsEnabled = !IsConnected && !IsTransferring;
                cbDataBits.IsEnabled = !IsConnected && !IsTransferring;
                cbStopBits.IsEnabled = !IsConnected && !IsTransferring;
                cbParity.IsEnabled = !IsConnected && !IsTransferring;
                cbFlowControl.IsEnabled = !IsConnected && !IsTransferring;

                btnSelectFile.IsEnabled = !IsTransferring;
                btnSelectFolder.IsEnabled = !IsTransferring;
                btnClearFiles.IsEnabled = !IsTransferring;
                btnRefreshPorts.IsEnabled = !IsTransferring;

                // 复位按钮只有在需要复位时才启用
                btnReset.IsEnabled = IsConnected && (_needReset || !IsTransferring);
            });
        }

        private void UpdateConnectionIndicator()
        {
            Dispatcher.Invoke(() =>
            {
                if (IsConnected)
                    indicatorConnection.Fill = Brushes.Green;
                else
                    indicatorConnection.Fill = Brushes.Gray;
            });
        }

        private void UpdateStatusBar(string message, bool isSuccess)
        {
            Dispatcher.Invoke(() =>
            {
                txtStatus.Text = message;
                txtStatus.Foreground = isSuccess ? Brushes.Green : Brushes.Red;
            });
        }

        private void AddLog(string message, LogLevel level)
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var paragraph = new Paragraph();

                // 时间戳
                var timeRun = new Run($"[{timestamp}]")
                {
                    Foreground = Brushes.Gray,
                    FontSize = 11
                };
                paragraph.Inlines.Add(timeRun);

                // 分隔符
                paragraph.Inlines.Add(new Run(" "));

                // 消息内容
                var messageRun = new Run(message);

                switch (level)
                {
                    case LogLevel.Info:
                        messageRun.Foreground = Brushes.Black;
                        break;
                    case LogLevel.Success:
                        messageRun.Foreground = Brushes.Green;
                        messageRun.FontWeight = FontWeights.Bold;
                        break;
                    case LogLevel.Warning:
                        messageRun.Foreground = Brushes.Orange;
                        messageRun.FontWeight = FontWeights.Bold;
                        break;
                    case LogLevel.Error:
                        messageRun.Foreground = Brushes.Red;
                        messageRun.FontWeight = FontWeights.Bold;
                        break;
                }

                paragraph.Inlines.Add(messageRun);
                rtbLog.Document.Blocks.Add(paragraph);

                // 滚动到底部
                rtbLog.ScrollToEnd();
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 停止所有定时器
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
            }

            if (_timeTimer != null)
            {
                _timeTimer.Stop();
            }

            // 取消传输
            if (_transferCts != null && !_transferCts.IsCancellationRequested)
            {
                _transferCts.Cancel();
            }

            // 断开串口连接
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    _serialPort.Close();
                }
                catch { }
            }
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

    #region 辅助类
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class FileItemViewModel : INotifyPropertyChanged
    {
        private string _fileName;
        private string _fullPath;
        private long _fileSize;
        private int _progress;
        private bool _isTransferring;

        public string FileName
        {
            get => _fileName;
            set
            {
                _fileName = value;
                OnPropertyChanged(nameof(FileName));
            }
        }

        public string FullPath
        {
            get => _fullPath;
            set
            {
                _fullPath = value;
                OnPropertyChanged(nameof(FullPath));
            }
        }

        public long FileSize
        {
            get => _fileSize;
            set
            {
                _fileSize = value;
                OnPropertyChanged(nameof(FileSize));
            }
        }

        public int Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged(nameof(Progress));
            }
        }

        public bool IsTransferring
        {
            get => _isTransferring;
            set
            {
                _isTransferring = value;
                OnPropertyChanged(nameof(IsTransferring));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class NaturalStringComparer : IComparer<string>
    {
        private static readonly Regex regex = new Regex(@"(\d+|\D+)", RegexOptions.Compiled);

        public int Compare(string x, string y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var partsX = regex.Matches(x);
            var partsY = regex.Matches(y);

            int count = Math.Min(partsX.Count, partsY.Count);
            for (int i = 0; i < count; i++)
            {
                var partX = partsX[i].Value;
                var partY = partsY[i].Value;

                if (int.TryParse(partX, out int numX) && int.TryParse(partY, out int numY))
                {
                    if (numX != numY)
                        return numX.CompareTo(numY);
                }
                else
                {
                    int strCompare = string.Compare(partX, partY, StringComparison.OrdinalIgnoreCase);
                    if (strCompare != 0)
                        return strCompare;
                }
            }

            return partsX.Count.CompareTo(partsY.Count);
        }
    }
    #endregion
}