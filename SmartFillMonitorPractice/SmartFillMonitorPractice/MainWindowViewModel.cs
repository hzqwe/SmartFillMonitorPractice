using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;
using SmartFillMonitorPractice.ViewModels;

namespace SmartFillMonitorPractice
{
    public partial class MainWindowViewModel : ObservableObject, IDisposable
    {
        private readonly IUserService _userService;
        private readonly INavigationService _navigationService;
        private readonly IHeaderStateService _headerStateService;
        private readonly DashBoardViewModel _dashBoardViewModel;
        private readonly SimulationViewModel _simulationViewModel;
        private bool _isExitCleanedUp;

        [ObservableProperty]
        private object mainContent = null!;

        [ObservableProperty]
        private bool isAdmin;

        [ObservableProperty]
        private bool isUserLoggedIn;

        [ObservableProperty]
        private string currentUserDisplayName = "未登录";

        [ObservableProperty]
        private string loginButtonText = "切换用户";

        [ObservableProperty]
        private string secondaryButtonText = "退出系统";

        public string CurrentBatchNo => _headerStateService.CurrentBatchNo;

        public string CurrentTime => _headerStateService.CurrentTime;

        public bool IsPlcConnected => _headerStateService.IsPlcConnected;

        public LightState IndicatorState => _headerStateService.IndicatorState;

        public MainWindowViewModel(IUserService userService, INavigationService navigationService, IHeaderStateService headerStateService, DashBoardViewModel dashBoardViewModel, SimulationViewModel simulationViewModel)
        {
            _userService = userService;
            _navigationService = navigationService;
            _headerStateService = headerStateService;
            _dashBoardViewModel = dashBoardViewModel;
            _simulationViewModel = simulationViewModel;

            _userService.LoginStateChanged += UserService_LoginStateChanged;
            _navigationService.CurrentContentChanged += NavigationService_CurrentContentChanged;
            _headerStateService.PropertyChanged += HeaderStateService_PropertyChanged;

            UpdateUser(_userService.CurrentUser);
            MainContent = _navigationService.CurrentContent ?? _dashBoardViewModel;
            _headerStateService.Activate(MainContent);
            LogService.Info("主窗口视图模型已初始化。");
        }

        private void UserService_LoginStateChanged(User? user)
        {
            RunOnUi(() =>
            {
                LogService.Info($"用户会话已变更。用户={(user?.UserName ?? "空")}");
                UpdateUser(user);
            });
        }

        private void NavigationService_CurrentContentChanged(object? content)
        {
            RunOnUi(() =>
            {
                MainContent = content ?? _dashBoardViewModel;
                _headerStateService.Activate(MainContent);
            });
        }

        private void HeaderStateService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IHeaderStateService.CurrentBatchNo))
            {
                OnPropertyChanged(nameof(CurrentBatchNo));
            }
            else if (e.PropertyName == nameof(IHeaderStateService.CurrentTime))
            {
                OnPropertyChanged(nameof(CurrentTime));
            }
            else if (e.PropertyName == nameof(IHeaderStateService.IsPlcConnected))
            {
                OnPropertyChanged(nameof(IsPlcConnected));
            }
            else if (e.PropertyName == nameof(IHeaderStateService.IndicatorState))
            {
                OnPropertyChanged(nameof(IndicatorState));
            }
        }

        private void UpdateUser(User? user)
        {
            if (user == null)
            {
                CurrentUserDisplayName = "未登录";
                IsUserLoggedIn = false;
                IsAdmin = false;
            }
            else
            {
                CurrentUserDisplayName = user.DisplayNameOrUserName;
                IsUserLoggedIn = true;
                IsAdmin = _userService.IsAdministrator(user);
            }

            LoginButtonText = "切换用户";
            SecondaryButtonText = "退出系统";

            if (!IsAdmin && MainContent is SettingViewModel)
            {
                MainContent = _navigationService.Navigate("DashBoard");
            }
        }

        [RelayCommand]
        private void Navigate(string? destination)
        {
            if (!IsUserLoggedIn || string.IsNullOrWhiteSpace(destination))
            {
                return;
            }

            try
            {
                MainContent = _navigationService.Navigate(destination);
            }
            catch (AuthorizationException ex)
            {
                MessageBox.Show(ex.Message, "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task LoginAsync()
        {
            if (Application.Current is not App app)
            {
                return;
            }

            var result = MessageBox.Show("确定要切换当前用户吗？", "切换用户确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            LogService.Info("主窗口已确认切换用户命令。");
            _simulationViewModel.PauseSimulation();
            await app.ReturnToLoginAsync();
        }

        [RelayCommand]
        private async Task SecondaryActionAsync()
        {
            var result = MessageBox.Show("确定要退出系统吗？", "退出确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            LogService.Info("主窗口已确认退出系统命令。");

            if (Application.Current is App app)
            {
                await app.ExitApplicationAsync();
            }
        }

        public void StopForExit()
        {
            if (_isExitCleanedUp)
            {
                LogService.Debug("主窗口视图模型退出清理已执行过，本次请求忽略。");
                return;
            }

            _isExitCleanedUp = true;
            LogService.Info("主窗口视图模型退出清理开始。");

            _userService.LoginStateChanged -= UserService_LoginStateChanged;
            _navigationService.CurrentContentChanged -= NavigationService_CurrentContentChanged;
            _headerStateService.PropertyChanged -= HeaderStateService_PropertyChanged;
            _headerStateService.Stop();

            _dashBoardViewModel.StopAndDispose();
            _simulationViewModel.StopAndDispose();

            LogService.Info("主窗口视图模型退出清理完成。");
        }

        public void Dispose()
        {
            StopForExit();
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
