using System;
using Microsoft.Extensions.DependencyInjection;
using SmartFillMonitorPractice.ViewModels;

namespace SmartFillMonitorPractice.Services
{
    public class NavigationService : INavigationService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IUserService _userService;
        private readonly SimulationViewModel _simulationViewModel;
        private readonly DashBoardViewModel _dashBoardViewModel;

        public NavigationService(IServiceProvider serviceProvider, IUserService userService, DashBoardViewModel dashBoardViewModel, SimulationViewModel simulationViewModel)
        {
            _serviceProvider = serviceProvider;
            _userService = userService;
            _dashBoardViewModel = dashBoardViewModel;
            _simulationViewModel = simulationViewModel;
            CurrentContent = _dashBoardViewModel;
        }

        public event Action<object?>? CurrentContentChanged;

        public object? CurrentContent { get; private set; }

        public object Navigate(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                return CurrentContent!;
            }

            LogService.Debug($"收到导航请求。目标={destination}，当前内容={CurrentContent?.GetType().Name ?? "空"}。");

            if (!string.Equals(destination, "Simulation", StringComparison.Ordinal) &&
                ReferenceEquals(CurrentContent, _simulationViewModel))
            {
                _simulationViewModel.PauseSimulation();
            }

            var content = destination switch
            {
                "DashBoard" => _dashBoardViewModel,
                "DataQuery" => _serviceProvider.GetRequiredService<DashQueryViewModel>(),
                "Logs" => _serviceProvider.GetRequiredService<LogsViewModel>(),
                "Alarms" => _serviceProvider.GetRequiredService<AlarmsViewModel>(),
                "Simulation" => _simulationViewModel,
                "Setting" => _userService.IsAdministrator(_userService.CurrentUser)
                    ? _serviceProvider.GetRequiredService<SettingViewModel>()
                    : throw new AuthorizationException("当前用户无权进入系统设置页面。"),
                _ => CurrentContent ?? _dashBoardViewModel
            };

            SetCurrentContent(content);
            return content;
        }

        public void SetCurrentContent(object? content)
        {
            if (ReferenceEquals(CurrentContent, content))
            {
                return;
            }

            CurrentContent = content;
            LogService.Info($"导航内容已切换到：{CurrentContent?.GetType().Name ?? "空"}。");
            CurrentContentChanged?.Invoke(CurrentContent);
        }
    }
}
