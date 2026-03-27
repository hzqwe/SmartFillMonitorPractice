using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;

namespace SmartFillMonitorPractice.ViewModels
{
    public partial class AlarmsViewModel : ObservableObject
    {
        private readonly IAlarmService _alarmService;
        private readonly IUserService _userService;

        public ObservableCollection<AlarmUiModel> ActiveAlarms { get; } = new();

        public ObservableCollection<AlarmUiModel> HistoryAlarms { get; } = new();

        public ObservableCollection<KeyValuePair<AlarmSeverity, string>> HistorySeverityOptions { get; } = new();

        [ObservableProperty]
        private int activeAlarmCount;

        [ObservableProperty]
        private DateTime historyStartDate = DateTime.Today.AddDays(-1);

        [ObservableProperty]
        private DateTime historyEndDate = DateTime.Today;

        [ObservableProperty]
        private AlarmSeverity selectedHistorySeverity = AlarmSeverity.All;

        [ObservableProperty]
        private int historyPageIndex = 1;

        [ObservableProperty]
        private int historyPageSize = 20;

        [ObservableProperty]
        private long historyTotalCount;

        public string HistoryPageText => $"第 {HistoryPageIndex} / {HistoryTotalPageCount} 页";

        public long HistoryTotalPageCount => HistoryTotalCount <= 0 ? 1 : (HistoryTotalCount + HistoryPageSize - 1) / HistoryPageSize;

        public bool CanPrevHistoryPage => HistoryPageIndex > 1;

        public bool CanNextHistoryPage => HistoryPageIndex < HistoryTotalPageCount;

        public AlarmsViewModel(IAlarmService alarmService, IUserService userService)
        {
            _alarmService = alarmService;
            _userService = userService;

            HistorySeverityOptions.Add(new KeyValuePair<AlarmSeverity, string>(AlarmSeverity.All, AlarmSeverity.All.GetDescription()));
            HistorySeverityOptions.Add(new KeyValuePair<AlarmSeverity, string>(AlarmSeverity.Info, AlarmSeverity.Info.GetDescription()));
            HistorySeverityOptions.Add(new KeyValuePair<AlarmSeverity, string>(AlarmSeverity.Warning, AlarmSeverity.Warning.GetDescription()));
            HistorySeverityOptions.Add(new KeyValuePair<AlarmSeverity, string>(AlarmSeverity.Error, AlarmSeverity.Error.GetDescription()));
            HistorySeverityOptions.Add(new KeyValuePair<AlarmSeverity, string>(AlarmSeverity.Critical, AlarmSeverity.Critical.GetDescription()));

            _alarmService.AlarmTriggered += OnAlarmTriggered;
            _alarmService.AlarmAcknowledged += OnAlarmAcknowledged;
            _alarmService.AlarmRecovered += OnAlarmRecovered;

            _ = RefreshAsync();
        }

        partial void OnHistoryPageIndexChanged(int value)
        {
            RaiseHistoryPageState();
        }

        partial void OnHistoryPageSizeChanged(int value)
        {
            RaiseHistoryPageState();
        }

        partial void OnHistoryTotalCountChanged(long value)
        {
            RaiseHistoryPageState();
        }

        private void RaiseHistoryPageState()
        {
            OnPropertyChanged(nameof(HistoryPageText));
            OnPropertyChanged(nameof(HistoryTotalPageCount));
            OnPropertyChanged(nameof(CanPrevHistoryPage));
            OnPropertyChanged(nameof(CanNextHistoryPage));
        }

        private async Task LoadActiveAlarmsAsync()
        {
            try
            {
                var records = await _alarmService.GetActiveAlarmsAsync();

                RunOnUi(() =>
                {
                    ActiveAlarms.Clear();
                    foreach (var item in records)
                    {
                        ActiveAlarms.Add(AlarmUiModel.FormRecord(item));
                    }

                    ActiveAlarmCount = ActiveAlarms.Count;
                });
            }
            catch (Exception ex)
            {
                LogService.Error("加载活动报警失败", ex);
            }
        }

        private async Task LoadHistoryPageAsync(int pageIndex)
        {
            try
            {
                if (pageIndex <= 0)
                {
                    pageIndex = 1;
                }

                var endTime = HistoryEndDate.Date.AddDays(1);
                var records = await _alarmService.GetAlarmHistoryAsync(pageIndex, HistoryPageSize, HistoryStartDate, endTime, SelectedHistorySeverity);

                RunOnUi(() =>
                {
                    HistoryPageIndex = pageIndex;
                    HistoryTotalCount = records.Total;

                    HistoryAlarms.Clear();
                    foreach (var item in records.Item)
                    {
                        HistoryAlarms.Add(AlarmUiModel.FormRecord(item));
                    }
                });
            }
            catch (Exception ex)
            {
                LogService.Error("加载历史报警失败", ex);
            }
        }

        private void OnAlarmTriggered(object? sender, AlarmRecord record)
        {
            RunOnUi(() =>
            {
                if (ActiveAlarms.Any(a => a.Id == record.Id))
                {
                    return;
                }

                ActiveAlarms.Insert(0, AlarmUiModel.FormRecord(record));
                ActiveAlarmCount = ActiveAlarms.Count;
            });
        }

        private void OnAlarmAcknowledged(object? sender, AlarmRecord record)
        {
            _ = LoadActiveAlarmsAsync();
            _ = LoadHistoryPageAsync(HistoryPageIndex);
        }

        private void OnAlarmRecovered(object? sender, AlarmRecord record)
        {
            RunOnUi(() =>
            {
                RemoveActiveAlarm(record.Id);
            });

            _ = LoadHistoryPageAsync(1);
        }

        private void RemoveActiveAlarm(long alarmId)
        {
            var item = ActiveAlarms.FirstOrDefault(a => a.Id == alarmId);
            if (item != null)
            {
                ActiveAlarms.Remove(item);
            }

            ActiveAlarmCount = ActiveAlarms.Count;
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

        [RelayCommand]
        private async Task RefreshAsync()
        {
            await LoadActiveAlarmsAsync();
            await LoadHistoryPageAsync(HistoryPageIndex);
        }

        [RelayCommand]
        private async Task LoadHistoryAlarmsAsync()
        {
            await LoadHistoryPageAsync(1);
        }

        [RelayCommand]
        private async Task PrevHistoryPageAsync()
        {
            if (!CanPrevHistoryPage)
            {
                return;
            }

            await LoadHistoryPageAsync(HistoryPageIndex - 1);
        }

        [RelayCommand]
        private async Task NextHistoryPageAsync()
        {
            if (!CanNextHistoryPage)
            {
                return;
            }

            await LoadHistoryPageAsync(HistoryPageIndex + 1);
        }

        [RelayCommand]
        private async Task AcknowledgeAlarmAsync(AlarmUiModel? alarm)
        {
            if (alarm == null)
            {
                return;
            }

            try
            {
                if (!alarm.IsAcknowledged)
                {
                    var success = await _alarmService.AcknowledgeAlarmAsync(alarm.Id, _userService.GetCurrentUserDisplayName(), alarm.ProcessSuggestion);
                    if (!success)
                    {
                        MessageBox.Show("报警确认失败，请刷新后重试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    await LoadActiveAlarmsAsync();
                    await LoadHistoryPageAsync(1);
                    LogService.Info($"确认报警成功：{alarm.Code}");
                    return;
                }

                var recoverSuccess = await _alarmService.RecoverAlarmAsync(alarm.Id, _userService.GetCurrentUserDisplayName());
                if (!recoverSuccess)
                {
                    MessageBox.Show("报警恢复失败，请刷新后重试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await LoadActiveAlarmsAsync();
                await LoadHistoryPageAsync(1);
                LogService.Info($"恢复报警成功：{alarm.Code}");
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
                LogService.Error($"处理报警异常：{alarm.Code}", ex);
                MessageBox.Show("处理报警失败，请稍后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task TestAlarmAsync()
        {
            try
            {
                await _alarmService.TriggerTestAlarmAsync();
            }
            catch (Exception ex)
            {
                LogService.Error("触发测试报警失败", ex);
                MessageBox.Show("触发测试报警失败，请稍后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
