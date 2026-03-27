using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCharts;
using LiveCharts.Wpf;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;

namespace SmartFillMonitorPractice.ViewModels
{
    public partial class DashBoardViewModel : ObservableObject, IDisposable
    {
        private enum ProductionRunState
        {
            Disconnected,
            Ready,
            Running,
            Stopped,
        }

        private readonly IPlcService _plcService;
        private readonly IAlarmService _alarmService;
        private readonly IDataService _dataService;
        private readonly IUserService _userService;
        private readonly ObservableCollection<AlarmUiModel> _recentAlarmMirror = new();
        private string _lastBarCode = string.Empty;
        private ProductionRunState _runState = ProductionRunState.Disconnected;
        private bool _disposed;

        [ObservableProperty]
        private int actualCount;

        [ObservableProperty]
        private int targetCount;

        [ObservableProperty]
        private double currentTemp;

        [ObservableProperty]
        private double settingTemp;

        [ObservableProperty]
        private double runningTime;

        [ObservableProperty]
        private string deviceStatus = "未连接";

        [ObservableProperty]
        private double currentCycleTime;

        [ObservableProperty]
        private double standardCycleTime;

        [ObservableProperty]
        private bool valveOpen;

        [ObservableProperty]
        private double liquidLevel;

        [ObservableProperty]
        private SeriesCollection? tempLiveCharts;

        [ObservableProperty]
        private LightState indicatorState = LightState.Red;

        public ObservableCollection<AlarmUiModel> RecentAlarms => _recentAlarmMirror;

        public DashBoardViewModel(IPlcService plcService, IAlarmService alarmService, IDataService dataService, IUserService userService)
        {
            _plcService = plcService;
            _alarmService = alarmService;
            _dataService = dataService;
            _userService = userService;

            _plcService.DataReceived += OnDataReceived;
            _plcService.ConnectionChanged += OnConnectionChanged;
            _alarmService.AlarmTriggered += OnAlarmTriggered;
            _alarmService.AlarmAcknowledged += OnAlarmAcknowledged;
            _alarmService.AlarmRecovered += OnAlarmRecovered;

            TempLiveCharts = new SeriesCollection
            {
                new ColumnSeries
                {
                    Title = "温度趋势",
                    Values = new ChartValues<double>(),
                    Fill = Brushes.DodgerBlue,
                    Stroke = Brushes.Cyan,
                    StrokeThickness = 1,
                }
            };

            _ = LoadRecentAlarmsAsync();
            UpdateStatusView();
        }

        private async Task LoadRecentAlarmsAsync()
        {
            try
            {
                var activeAlarms = await _alarmService.GetActiveAlarmsAsync();
                RunOnUi(() =>
                {
                    _recentAlarmMirror.Clear();
                    foreach (var item in activeAlarms.Take(10))
                    {
                        _recentAlarmMirror.Add(AlarmUiModel.FormRecord(item));
                    }
                });
            }
            catch (Exception ex)
            {
                LogService.Error("加载首页最近报警失败", ex);
            }
        }

        private void OnConnectionChanged(object? sender, bool connected)
        {
            RunOnUi(() =>
            {
                if (!connected)
                {
                    _runState = ProductionRunState.Disconnected;
                    ClearRealtimeValues();
                    ClearChart();
                    _lastBarCode = string.Empty;
                }
                else
                {
                    _runState = _plcService.HasSuccessfulRead ? ProductionRunState.Ready : ProductionRunState.Disconnected;
                }

                UpdateStatusView();
            });
        }

        private void OnAlarmTriggered(object? sender, AlarmRecord record)
        {
            RunOnUi(() =>
            {
                if (_recentAlarmMirror.Any(a => a.Id == record.Id))
                {
                    return;
                }

                _recentAlarmMirror.Insert(0, AlarmUiModel.FormRecord(record));
                while (_recentAlarmMirror.Count > 10)
                {
                    _recentAlarmMirror.RemoveAt(_recentAlarmMirror.Count - 1);
                }
            });
        }

        private void OnAlarmAcknowledged(object? sender, AlarmRecord record)
        {
            RunOnUi(() =>
            {
                var item = _recentAlarmMirror.FirstOrDefault(a => a.Id == record.Id);
                if (item == null)
                {
                    return;
                }

                var updated = AlarmUiModel.FormRecord(record);
                var index = _recentAlarmMirror.IndexOf(item);
                _recentAlarmMirror[index] = updated;
            });
        }

        private void OnAlarmRecovered(object? sender, AlarmRecord record)
        {
            RunOnUi(() =>
            {
                var item = _recentAlarmMirror.FirstOrDefault(a => a.Id == record.Id);
                if (item != null)
                {
                    _recentAlarmMirror.Remove(item);
                }
            });
        }

        private void OnDataReceived(object? sender, DeviceState state)
        {
            if (state == null)
            {
                return;
            }

            RunOnUi(() =>
            {
                ActualCount = state.ActualCount;
                TargetCount = state.TargetCount;
                CurrentTemp = state.CurrentTemp;
                SettingTemp = state.SettingTemp;
                RunningTime = state.RunningTime;
                CurrentCycleTime = state.CurrentCycleTime;
                StandardCycleTime = state.StandardCycleTime;
                LiquidLevel = state.LiquidLevel;
                ValveOpen = state.ValveOpen;

                if (_runState == ProductionRunState.Running)
                {
                    AppendChartPoint(state.CurrentTemp);
                }
            });

            _ = SaveProductionRecordAsync(state);
        }

        private void AppendChartPoint(double value)
        {
            if (TempLiveCharts == null || TempLiveCharts.Count == 0)
            {
                return;
            }

            TempLiveCharts[0].Values ??= new ChartValues<double>();
            TempLiveCharts[0].Values.Add(value);
            if (TempLiveCharts[0].Values.Count > 40)
            {
                TempLiveCharts[0].Values.RemoveAt(0);
            }
        }

        private void ClearChart()
        {
            if (TempLiveCharts == null || TempLiveCharts.Count == 0)
            {
                return;
            }

            TempLiveCharts[0].Values?.Clear();
        }

        private void ClearRealtimeValues()
        {
            ActualCount = 0;
            TargetCount = 0;
            CurrentTemp = 0;
            SettingTemp = 0;
            RunningTime = 0;
            CurrentCycleTime = 0;
            StandardCycleTime = 0;
            LiquidLevel = 0;
            ValveOpen = false;
        }

        private async Task SaveProductionRecordAsync(DeviceState state)
        {
            if (_runState != ProductionRunState.Running || _userService.CurrentUser == null)
            {
                return;
            }

            var barcode = state.BarCode ?? string.Empty;
            if (string.IsNullOrWhiteSpace(barcode) || barcode == _lastBarCode)
            {
                return;
            }

            _lastBarCode = barcode;

            var record = new ProductionRecord
            {
                Time = DateTime.Now,
                BatchNo = barcode,
                SettingTemp = state.SettingTemp,
                ActualTemp = state.CurrentTemp,
                ActualCount = state.ActualCount,
                TargetCount = state.TargetCount,
                IsNG = false,
                CycleTime = state.CurrentCycleTime,
                Operator = _userService.GetCurrentUserName()
            };

            await _dataService.SaveProductionRecordAsync(record);
        }

        private void UpdateStatusView()
        {
            switch (_runState)
            {
                case ProductionRunState.Running:
                    DeviceStatus = "运行中";
                    IndicatorState = LightState.Green;
                    break;
                case ProductionRunState.Stopped:
                    DeviceStatus = "已停止";
                    IndicatorState = LightState.Yellow;
                    break;
                case ProductionRunState.Ready:
                    DeviceStatus = "已连接 / 已就绪";
                    IndicatorState = LightState.Yellow;
                    break;
                default:
                    DeviceStatus = "未连接";
                    IndicatorState = LightState.Red;
                    break;
            }
        }

        private void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(action);
        }

        public void StopAndDispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            LogService.Info("主页视图模型正在解除 PLC 和报警事件订阅。");
            _plcService.DataReceived -= OnDataReceived;
            _plcService.ConnectionChanged -= OnConnectionChanged;
            _alarmService.AlarmTriggered -= OnAlarmTriggered;
            _alarmService.AlarmAcknowledged -= OnAlarmAcknowledged;
            _alarmService.AlarmRecovered -= OnAlarmRecovered;
        }

        public void Dispose()
        {
            StopAndDispose();
        }

        [RelayCommand]
        private async Task StartProductionAsync()
        {
            try
            {
                if (_runState == ProductionRunState.Disconnected)
                {
                    UpdateStatusView();
                    return;
                }

                DeviceStatus = "启动中...";
                IndicatorState = LightState.Green;

                var success = await _plcService.PulseCommandAsync("Start");
                if (!success)
                {
                    _runState = _plcService.HasSuccessfulRead ? ProductionRunState.Ready : ProductionRunState.Disconnected;
                    UpdateStatusView();
                    return;
                }

                _runState = ProductionRunState.Running;
                UpdateStatusView();
                LogService.Info("发送启动命令到PLC");
            }
            catch (AuthorizationException ex)
            {
                _runState = _plcService.HasSuccessfulRead ? ProductionRunState.Ready : ProductionRunState.Disconnected;
                UpdateStatusView();
                MessageBox.Show(ex.Message, "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                _runState = _plcService.HasSuccessfulRead ? ProductionRunState.Ready : ProductionRunState.Disconnected;
                UpdateStatusView();
                LogService.Error("发送启动命令到PLC失败", ex);
            }
        }

        [RelayCommand]
        private async Task StopProductionAsync()
        {
            try
            {
                if (_runState == ProductionRunState.Disconnected)
                {
                    UpdateStatusView();
                    return;
                }

                DeviceStatus = "停止中...";
                IndicatorState = LightState.Yellow;

                var success = await _plcService.PulseCommandAsync("Stop");
                if (!success)
                {
                    _runState = _plcService.HasSuccessfulRead ? ProductionRunState.Ready : ProductionRunState.Disconnected;
                    UpdateStatusView();
                    return;
                }

                _runState = ProductionRunState.Stopped;
                UpdateStatusView();
                LogService.Info("发送停止命令到PLC");
            }
            catch (AuthorizationException ex)
            {
                _runState = _plcService.HasSuccessfulRead ? ProductionRunState.Ready : ProductionRunState.Disconnected;
                UpdateStatusView();
                MessageBox.Show(ex.Message, "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                _runState = _plcService.HasSuccessfulRead ? ProductionRunState.Ready : ProductionRunState.Disconnected;
                UpdateStatusView();
                LogService.Error("发送停止命令到PLC失败", ex);
            }
        }

        [RelayCommand]
        private async Task ResetProductionAsync()
        {
            try
            {
                var wasConnected = _plcService.HasSuccessfulRead;
                if (wasConnected)
                {
                    DeviceStatus = "复位中...";
                    IndicatorState = LightState.Yellow;

                    await _plcService.PulseCommandAsync("Stop");
                    await Task.Delay(100);
                    await _plcService.PulseCommandAsync("Reset");
                }

                _lastBarCode = string.Empty;
                ClearChart();
                _runState = _plcService.HasSuccessfulRead ? ProductionRunState.Ready : ProductionRunState.Disconnected;
                UpdateStatusView();
                LogService.Info("发送复位命令到PLC");
            }
            catch (AuthorizationException ex)
            {
                _runState = _plcService.HasSuccessfulRead ? ProductionRunState.Ready : ProductionRunState.Disconnected;
                UpdateStatusView();
                MessageBox.Show(ex.Message, "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                _runState = _plcService.HasSuccessfulRead ? ProductionRunState.Ready : ProductionRunState.Disconnected;
                UpdateStatusView();
                LogService.Error("发送复位命令到PLC失败", ex);
            }
        }
    }
}
