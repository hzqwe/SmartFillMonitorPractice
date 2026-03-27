using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.ViewModels;

namespace SmartFillMonitorPractice.Services
{
    public partial class HeaderStateService : ObservableObject, IHeaderStateService, IDisposable
    {
        [ObservableProperty]
        private string currentBatchNo = string.Empty;

        [ObservableProperty]
        private string currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        [ObservableProperty]
        private bool isPlcConnected;

        [ObservableProperty]
        private LightState indicatorState = LightState.Red;

        private readonly IPlcService _plcService;
        private readonly DispatcherTimer _timer;
        private object? _activeContent;
        private INotifyPropertyChanged? _activeContentNotifier;
        private bool _stopped;

        public HeaderStateService(IPlcService plcService)
        {
            _plcService = plcService;
            _plcService.ConnectionChanged += OnConnectionChanged;
            _plcService.DataReceived += OnDataReceived;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            IsPlcConnected = _plcService.HasSuccessfulRead;
            SyncFromContent();
        }

        public void Activate(object? mainContent)
        {
            if (_stopped)
            {
                return;
            }

            if (ReferenceEquals(_activeContent, mainContent))
            {
                return;
            }

            if (_activeContentNotifier != null)
            {
                _activeContentNotifier.PropertyChanged -= ActiveContent_PropertyChanged;
            }

            _activeContent = mainContent;
            _activeContentNotifier = mainContent as INotifyPropertyChanged;
            if (_activeContentNotifier != null)
            {
                _activeContentNotifier.PropertyChanged += ActiveContent_PropertyChanged;
            }

            LogService.Debug($"Header 状态服务已激活到：{mainContent?.GetType().Name ?? "空"}。");
            RunOnUi(SyncFromContent);
        }

        public void Stop()
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            LogService.Info("Header 状态服务收到停止请求。");

            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _plcService.ConnectionChanged -= OnConnectionChanged;
            _plcService.DataReceived -= OnDataReceived;

            if (_activeContentNotifier != null)
            {
                _activeContentNotifier.PropertyChanged -= ActiveContent_PropertyChanged;
                _activeContentNotifier = null;
            }

            _activeContent = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private void OnConnectionChanged(object? sender, bool connected)
        {
            if (_stopped)
            {
                return;
            }

            RunOnUi(() =>
            {
                IsPlcConnected = connected && _plcService.HasSuccessfulRead;
                SyncFromContent();
            });
        }

        private void OnDataReceived(object? sender, DeviceState state)
        {
            if (_stopped || state == null)
            {
                return;
            }

            RunOnUi(() =>
            {
                if (_activeContent is not SimulationViewModel)
                {
                    CurrentBatchNo = state.BarCode ?? string.Empty;
                }

                if (_activeContent is not DashBoardViewModel &&
                    _activeContent is not SimulationViewModel)
                {
                    SyncFromContent();
                }
            });
        }

        private void ActiveContent_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_stopped)
            {
                return;
            }

            if (e.PropertyName == nameof(DashBoardViewModel.IndicatorState) ||
                e.PropertyName == nameof(SimulationViewModel.IndicatorState) ||
                e.PropertyName == nameof(SimulationViewModel.CurrentBatchNo))
            {
                RunOnUi(SyncFromContent);
            }
        }

        private void SyncFromContent()
        {
            if (_stopped)
            {
                return;
            }

            if (_activeContent is SimulationViewModel simulation)
            {
                IndicatorState = simulation.IndicatorState;
                CurrentBatchNo = simulation.CurrentBatchNo;
                IsPlcConnected = _plcService.HasSuccessfulRead;
                return;
            }

            if (_activeContent is DashBoardViewModel dashboard)
            {
                IndicatorState = dashboard.IndicatorState;
                CurrentBatchNo = _plcService.LastDeviceState?.BarCode ?? string.Empty;
                IsPlcConnected = _plcService.HasSuccessfulRead;
                return;
            }

            IndicatorState = _plcService.HasSuccessfulRead ? LightState.Yellow : LightState.Red;
            CurrentBatchNo = _plcService.LastDeviceState?.BarCode ?? string.Empty;
            IsPlcConnected = _plcService.HasSuccessfulRead;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_stopped)
            {
                return;
            }

            CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _ = dispatcher.BeginInvoke(action);
        }
    }
}
