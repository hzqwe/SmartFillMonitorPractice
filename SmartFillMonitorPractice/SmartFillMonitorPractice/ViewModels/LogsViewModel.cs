using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;

namespace SmartFillMonitorPractice.ViewModels
{
    public partial class LogsViewModel : ObservableObject
    {
        private readonly ISystemLogService _systemLogService;

        [ObservableProperty]
        private DateTime startDate = new(2026, 2, 1);

        [ObservableProperty]
        private DateTime endDate = DateTime.Today.AddDays(1).AddSeconds(-1);

        [ObservableProperty]
        private string selectedLevel = "All";

        public ObservableCollection<string> LogLevels { get; } = new()
        {
            "All",
            "Debug",
            "Information",
            "Warning",
            "Error",
            "Fatal"
        };

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private bool isBusy;

        [ObservableProperty]
        private int totalCount;

        [ObservableProperty]
        private int pageIndex = 1;

        private const int PageSize = 50;

        [ObservableProperty]
        private ObservableCollection<SystemLog> logs = new();

        public LogsViewModel(ISystemLogService systemLogService)
        {
            _systemLogService = systemLogService;
            _ = LoadLogsAsync();
        }

        [RelayCommand]
        private async Task PreviousPageAsync()
        {
            if (PageIndex <= 1)
            {
                return;
            }

            PageIndex--;
            await LoadLogsAsync();
        }

        [RelayCommand]
        private async Task NextPageAsync()
        {
            if (Logs.Count < PageSize)
            {
                return;
            }

            PageIndex++;
            await LoadLogsAsync();
        }

        [RelayCommand]
        private async Task SearchAsync()
        {
            PageIndex = 1;
            await LoadLogsAsync();
        }

        [RelayCommand]
        private async Task ExportAsync()
        {
            try
            {
                var path = $"Logs_Export_{DateTime.Now:yyyyMMddHHmmss}.csv";
                var fullPath = await _systemLogService.ExportAsync(BuildFilter(), path);
                MessageBox.Show($"日志已导出到文件：{Path.GetFullPath(fullPath)}");
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
                LogService.Error("导出日志失败", ex);
                MessageBox.Show("日志导出失败，请稍后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadLogsAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var result = await _systemLogService.QueryAsync(BuildFilter(), PageIndex, PageSize);
                TotalCount = (int)result.Total;
                Logs = new ObservableCollection<SystemLog>(result.Items);
            }
            catch (Exception ex)
            {
                LogService.Error("加载日志失败", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private SystemLogQueryFilter BuildFilter()
        {
            var endTime = EndDate < StartDate ? StartDate : EndDate;
            return new SystemLogQueryFilter
            {
                StartTime = StartDate.Date,
                EndTime = endTime,
                Level = SelectedLevel,
                SearchText = SearchText
            };
        }
    }
}
