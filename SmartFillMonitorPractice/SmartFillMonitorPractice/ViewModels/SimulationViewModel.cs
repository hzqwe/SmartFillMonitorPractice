using System;
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
    public partial class SimulationViewModel : ObservableObject, IDisposable
    {
        private enum PlcLinkState
        {
            Disconnected,
            ConnectedReadable,
        }

        private enum SimulationRunState
        {
            Ready,
            Running,
            Stopped,
        }

        private readonly IPlcService _plcService;
        private PlcLinkState _plcLinkState = PlcLinkState.Disconnected;
        private SimulationRunState _runState = SimulationRunState.Ready;
        private bool _disposed;
        private int _maxActualCount;
        private const double CountZeroDisplayHeight = 0.3;

        [ObservableProperty]
        private string plcConnectionText = "未连接";

        [ObservableProperty]
        private string runStateText = "未连接";

        [ObservableProperty]
        private string currentPhaseText = "未连接";

        [ObservableProperty]
        private string syncStatusText = "未检测到有效 Modbus 数据";

        [ObservableProperty]
        private string currentBatchNo = string.Empty;

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
        private double currentCycleTime;

        [ObservableProperty]
        private double standardCycleTime;

        [ObservableProperty]
        private double liquidLevel;

        [ObservableProperty]
        private bool valveOpen;

        [ObservableProperty]
        private string valveStatusText = "关闭";

        [ObservableProperty]
        private int currentScriptIndex;

        [ObservableProperty]
        private LightState indicatorState = LightState.Red;

        [ObservableProperty]
        private SeriesCollection? tempCharts;

        [ObservableProperty]
        private SeriesCollection? countCharts;

        [ObservableProperty]
        private double countAxisMax = 1;

        public Func<double, string> CountAxisFormatter { get; } = value => value.ToString("F0");

        public SimulationViewModel(IPlcService plcService)
        {
            _plcService = plcService;

            TempCharts = new SeriesCollection
            {
                new ColumnSeries
                {
                    Title = "温度采样",
                    Values = new ChartValues<double>(),
                    Fill = Brushes.DodgerBlue,
                    Stroke = Brushes.Cyan,
                    StrokeThickness = 1,
                    Foreground = Brushes.White,
                    MaxColumnWidth = 22
                }
            };

            CountCharts = new SeriesCollection
            {
                new ColumnSeries
                {
                    Title = "产量采样",
                    Values = new ChartValues<double>(),
                    Fill = Brushes.DodgerBlue,
                    Stroke = Brushes.Cyan,
                    StrokeThickness = 1,
                    Foreground = Brushes.White,
                    MaxColumnWidth = 22,
                    DataLabels = false
                }
            };

            _plcService.ConnectionChanged += OnConnectionChanged;
            _plcService.DataReceived += OnDataReceived;

            ResetDisplayValues();

            if (_plcService.HasSuccessfulRead && _plcService.LastDeviceState != null)
            {
                ApplyReadableState(_plcService.LastDeviceState);
            }
            else
            {
                ApplyDisconnectedState();
            }
        }

        private void OnConnectionChanged(object? sender, bool connected)
        {
            RunOnUi(() =>
            {
                if (!connected)
                {
                    ApplyDisconnectedState();
                    return;
                }

                if (_plcService.LastDeviceState != null)
                {
                    ApplyReadableState(_plcService.LastDeviceState);
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
                ApplyReadableState(state);
                UpdateRealtimeValues(state);

                if (_runState == SimulationRunState.Running)
                {
                    CurrentScriptIndex++;
                    AppendChartPoint(TempCharts, CurrentTemp, 48);
                    AppendChartPoint(CountCharts, GetCountDisplayValue(ActualCount), 24);
                    CurrentPhaseText = "采样中";
                }
            });
        }

        private void ApplyReadableState(DeviceState state)
        {
            _plcLinkState = PlcLinkState.ConnectedReadable;
            PlcConnectionText = "已连接";
            SyncStatusText = _plcService.LastReadSuccessTime.HasValue
                ? $"最近采样：{_plcService.LastReadSuccessTime.Value:HH:mm:ss}"
                : "已读到有效 Modbus 数据";

            if (_runState == SimulationRunState.Running)
            {
                RunStateText = "运行中";
                IndicatorState = LightState.Green;
                return;
            }

            if (_runState == SimulationRunState.Stopped)
            {
                RunStateText = "已停止";
                CurrentPhaseText = "采样暂停";
                IndicatorState = LightState.Yellow;
                return;
            }

            _runState = SimulationRunState.Ready;
            RunStateText = "已连接 / 已就绪";
            CurrentPhaseText = "待启动";
            IndicatorState = LightState.Yellow;
        }

        private void ApplyDisconnectedState()
        {
            _plcLinkState = PlcLinkState.Disconnected;
            _runState = SimulationRunState.Ready;
            PlcConnectionText = "未连接";
            RunStateText = "未连接";
            CurrentPhaseText = "未连接";
            SyncStatusText = "未检测到有效 Modbus 数据";
            IndicatorState = LightState.Red;
        }

        private void UpdateRealtimeValues(DeviceState state)
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
            CurrentBatchNo = state.BarCode ?? string.Empty;
            UpdateCountAxisRange(state.ActualCount);
        }

        private void ResetDisplayValues()
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
            CurrentBatchNo = string.Empty;
            CurrentScriptIndex = 0;
            _maxActualCount = 0;
            CountAxisMax = 1;
        }

        private void AppendChartPoint(SeriesCollection? collection, double value, int maxCount)
        {
            if (collection == null || collection.Count == 0)
            {
                return;
            }

            collection[0].Values ??= new ChartValues<double>();
            collection[0].Values.Add(value);
            if (collection[0].Values.Count > maxCount)
            {
                collection[0].Values.RemoveAt(0);
            }
        }

        private double GetCountDisplayValue(int actualCount)
        {
            return actualCount <= 0 ? CountZeroDisplayHeight : actualCount;
        }

        private void UpdateCountAxisRange(int actualCount)
        {
            if (actualCount > _maxActualCount)
            {
                _maxActualCount = actualCount;
            }

            CountAxisMax = _maxActualCount <= 0
                ? 1
                : Math.Max(1, Math.Ceiling(_maxActualCount * 1.2));
        }

        private void ClearCharts()
        {
            TempCharts?[0].Values?.Clear();
            CountCharts?[0].Values?.Clear();
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

        partial void OnValveOpenChanged(bool value)
        {
            ValveStatusText = value ? "打开" : "关闭";
        }

        public void PauseSimulation()
        {
            if (_plcLinkState != PlcLinkState.ConnectedReadable || _runState != SimulationRunState.Running)
            {
                return;
            }

            _runState = SimulationRunState.Stopped;
            RunStateText = "已停止";
            CurrentPhaseText = "采样暂停";
            IndicatorState = LightState.Yellow;
        }

        public void StopAndDispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            LogService.Info("仿真联调视图模型正在解除 PLC 事件订阅。");
            _plcService.ConnectionChanged -= OnConnectionChanged;
            _plcService.DataReceived -= OnDataReceived;
        }

        public void Dispose()
        {
            StopAndDispose();
        }

        [RelayCommand]
        private void StartSimulation()
        {
            if (!_plcService.HasSuccessfulRead || _plcService.LastDeviceState == null)
            {
                MessageBox.Show("未检测到有效 Modbus 数据，当前不能启动仿真联调。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                ApplyDisconnectedState();
                return;
            }

            UpdateRealtimeValues(_plcService.LastDeviceState);
            _plcLinkState = PlcLinkState.ConnectedReadable;
            _runState = SimulationRunState.Running;
            RunStateText = "运行中";
            CurrentPhaseText = "采样中";
            IndicatorState = LightState.Green;
            SyncStatusText = _plcService.LastReadSuccessTime.HasValue
                ? $"最近采样：{_plcService.LastReadSuccessTime.Value:HH:mm:ss}"
                : "已读到有效 Modbus 数据";
        }

        [RelayCommand]
        private void StopSimulation()
        {
            if (_plcLinkState != PlcLinkState.ConnectedReadable || _runState != SimulationRunState.Running)
            {
                return;
            }

            _runState = SimulationRunState.Stopped;
            RunStateText = "已停止";
            CurrentPhaseText = "采样暂停";
            IndicatorState = LightState.Yellow;
        }

        [RelayCommand]
        private void ResetSimulation()
        {
            ClearCharts();
            CurrentScriptIndex = 0;
            CurrentBatchNo = string.Empty;

            if (!_plcService.HasSuccessfulRead || _plcService.LastDeviceState == null)
            {
                ResetDisplayValues();
                ApplyDisconnectedState();
                return;
            }

            UpdateRealtimeValues(_plcService.LastDeviceState);
            _plcLinkState = PlcLinkState.ConnectedReadable;
            _runState = SimulationRunState.Ready;
            RunStateText = "已连接 / 已就绪";
            CurrentPhaseText = "待启动";
            IndicatorState = LightState.Yellow;
            SyncStatusText = _plcService.LastReadSuccessTime.HasValue
                ? $"最近采样：{_plcService.LastReadSuccessTime.Value:HH:mm:ss}"
                : "已读到有效 Modbus 数据";
        }
    }
}
