using System;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;

namespace SmartFillMonitorPractice.ViewModels
{
    public partial class SettingViewModel : ObservableObject
    {
        private readonly IConfigService _configService;
        private readonly IPlcService _plcService;
        private readonly IAuthorizationService _authorizationService;

        public ObservableCollection<string> PortNames { get; } = new();

        public ObservableCollection<int> BaudRates { get; } = new()
        {
            9600, 19200, 38400, 57600, 115200
        };

        public ObservableCollection<int> DataBitsOptions { get; } = new()
        {
            7, 8
        };

        public ObservableCollection<string> ParityOptions { get; } = new()
        {
            "None", "Odd", "Even"
        };

        public ObservableCollection<string> StopBitsOptions { get; } = new()
        {
            "None", "One", "Two"
        };

        [ObservableProperty]
        private string portName = "COM3";

        [ObservableProperty]
        private int selectedBaud = 115200;

        [ObservableProperty]
        private int selectedDataBits = 8;

        [ObservableProperty]
        private string selectedParity = "None";

        [ObservableProperty]
        private string selectedStopBits = "One";

        [ObservableProperty]
        private bool autoConnect = true;

        [ObservableProperty]
        private bool alarmSound = true;

        [ObservableProperty]
        private bool debugLogMode;

        public SettingViewModel(IConfigService configService, IPlcService plcService, IAuthorizationService authorizationService)
        {
            _configService = configService;
            _plcService = plcService;
            _authorizationService = authorizationService;

            RefreshPortList();
            _ = LoadSettingsAsync();
        }

        private void RefreshPortList()
        {
            PortNames.Clear();
            try
            {
                var ports = _plcService.GetAvailablePorts() ?? SerialPort.GetPortNames();
                foreach (var item in ports)
                {
                    PortNames.Add(item);
                }

                if (!string.IsNullOrEmpty(PortName) && !PortNames.Contains(PortName))
                {
                    PortName = PortNames.Count > 0 ? PortNames[0] : PortName;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"获取串口列表失败：{ex.Message}");
                PortNames.Clear();
                PortNames.Add("COM1");
                PortNames.Add("COM2");
            }
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                var settings = await _configService.LoadSettingsAsync();
                PortName = settings.PortName;
                SelectedBaud = settings.BaudRate;
                SelectedDataBits = settings.DataBits;
                SelectedParity = settings.Parity;
                SelectedStopBits = settings.StopBits;
                AutoConnect = settings.AutoConnect;
                AlarmSound = settings.AlarmSound;
                DebugLogMode = settings.DebugLogMode;
            }
            catch (Exception ex)
            {
                LogService.Error($"加载配置失败，使用默认值，原因：{ex.Message}");
            }
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            try
            {
                _authorizationService.EnsurePermission(Permission.ManageSettings, "保存系统设置");

                var model = new DeviceSettings
                {
                    PortName = PortName,
                    BaudRate = SelectedBaud,
                    DataBits = SelectedDataBits,
                    Parity = SelectedParity,
                    StopBits = SelectedStopBits,
                    AutoConnect = AutoConnect,
                    AlarmSound = AlarmSound,
                    DebugLogMode = DebugLogMode
                };

                var saved = await _configService.SaveDeviceSettingsAsync(model);
                if (!saved)
                {
                    MessageBox.Show("配置保存失败，请稍后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                await _plcService.InitializeAsync(model);
                RefreshPortList();
                LogService.Info("配置保存成功，PLC 已按最新参数重新初始化。");
            }
            catch (AuthorizationException ex)
            {
                MessageBox.Show(ex.Message, "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (BusinessException ex)
            {
                MessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                LogService.Error($"保存配置失败，原因：{ex.Message}");
                MessageBox.Show("保存配置失败，请稍后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
