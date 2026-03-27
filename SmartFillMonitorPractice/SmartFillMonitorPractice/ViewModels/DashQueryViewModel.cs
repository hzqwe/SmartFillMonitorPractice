using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;

namespace SmartFillMonitorPractice.ViewModels
{
    public partial class DashQueryViewModel : ObservableObject
    {
        private readonly IDataService _dataService;

        [ObservableProperty]
        private ObservableCollection<ProductionRecord> records = new();

        [ObservableProperty]
        private ProductionRecord? selectedRecord;

        [ObservableProperty]
        private DateTime? startDate = DateTime.Today.AddDays(-7);

        [ObservableProperty]
        private DateTime? endDate = DateTime.Today;

        public DashQueryViewModel(IDataService dataService)
        {
            _dataService = dataService;
        }

        [RelayCommand]
        private async Task QueryAsync()
        {
            var start = StartDate ?? DateTime.Today.AddDays(-7);
            var end = EndDate ?? DateTime.Today;
            var endInclusive = end.AddDays(1).AddMicroseconds(-1);
            var list = await _dataService.QueryRecordAsync(start, endInclusive);

            Records.Clear();
            foreach (var item in list.OrderByDescending(r => r.Time))
            {
                Records.Add(item);
            }
        }

        [RelayCommand]
        private async Task ExportAsync()
        {
            if (Records == null || Records.Count == 0)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV 文件|*.csv",
                FileName = $"ProductionRecords_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                await _dataService.ExportToCsvAsync(Records.ToList(), dialog.FileName);
            }
            catch (Exception ex)
            {
                LogService.Error("导出生产记录失败", ex);
            }
        }
    }
}
