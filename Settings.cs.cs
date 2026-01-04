using System;
using System.Configuration;

namespace WPFYmodem
{
    /// <summary>
    /// 应用程序设置类
    /// </summary>
    public sealed class AppSettings : ApplicationSettingsBase
    {
        private static AppSettings defaultInstance =
            (AppSettings)Synchronized(new AppSettings());

        public static AppSettings Default => defaultInstance;

        #region 串口设置

        [UserScopedSetting]
        [DefaultSettingValue("COM3")]
        public string LastComPort
        {
            get => (string)this[nameof(LastComPort)];
            set => this[nameof(LastComPort)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("115200")]
        public int LastBaudRate
        {
            get => (int)this[nameof(LastBaudRate)];
            set => this[nameof(LastBaudRate)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("8")]
        public int LastDataBits
        {
            get => (int)this[nameof(LastDataBits)];
            set => this[nameof(LastDataBits)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("1")]
        public int LastStopBits
        {
            get => (int)this[nameof(LastStopBits)];
            set => this[nameof(LastStopBits)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("0")]
        public int LastParity
        {
            get => (int)this[nameof(LastParity)];
            set => this[nameof(LastParity)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("0")]
        public int LastFlowControl
        {
            get => (int)this[nameof(LastFlowControl)];
            set => this[nameof(LastFlowControl)] = value;
        }

        #endregion

        #region 应用程序设置

        [UserScopedSetting]
        [DefaultSettingValue("True")]
        public bool AutoConnect
        {
            get => (bool)this[nameof(AutoConnect)];
            set => this[nameof(AutoConnect)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("True")]
        public bool AutoRefresh
        {
            get => (bool)this[nameof(AutoRefresh)];
            set => this[nameof(AutoRefresh)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string LastFileDirectory
        {
            get => (string)this[nameof(LastFileDirectory)];
            set => this[nameof(LastFileDirectory)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("C:\\")]
        public string DefaultSavePath
        {
            get => (string)this[nameof(DefaultSavePath)];
            set => this[nameof(DefaultSavePath)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("10")]
        public int MaxRetryCount
        {
            get => (int)this[nameof(MaxRetryCount)];
            set => this[nameof(MaxRetryCount)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("5000")]
        public int ResponseTimeout
        {
            get => (int)this[nameof(ResponseTimeout)];
            set => this[nameof(ResponseTimeout)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("1024")]
        public int PacketSize
        {
            get => (int)this[nameof(PacketSize)];
            set => this[nameof(PacketSize)] = value;
        }

        #endregion

        #region 窗口设置

        [UserScopedSetting]
        [DefaultSettingValue("800")]
        public double WindowWidth
        {
            get => (double)this[nameof(WindowWidth)];
            set => this[nameof(WindowWidth)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("600")]
        public double WindowHeight
        {
            get => (double)this[nameof(WindowHeight)];
            set => this[nameof(WindowHeight)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("0")]
        public double WindowLeft
        {
            get => (double)this[nameof(WindowLeft)];
            set => this[nameof(WindowLeft)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("0")]
        public double WindowTop
        {
            get => (double)this[nameof(WindowTop)];
            set => this[nameof(WindowTop)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("False")]
        public bool WindowMaximized
        {
            get => (bool)this[nameof(WindowMaximized)];
            set => this[nameof(WindowMaximized)] = value;
        }

        #endregion

        #region 日志设置

        [UserScopedSetting]
        [DefaultSettingValue("True")]
        public bool EnableFileLogging
        {
            get => (bool)this[nameof(EnableFileLogging)];
            set => this[nameof(EnableFileLogging)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue(".\\Logs")]
        public string LogDirectory
        {
            get => (string)this[nameof(LogDirectory)];
            set => this[nameof(LogDirectory)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("7")]
        public int LogRetentionDays
        {
            get => (int)this[nameof(LogRetentionDays)];
            set => this[nameof(LogRetentionDays)] = value;
        }

        #endregion
    }
}