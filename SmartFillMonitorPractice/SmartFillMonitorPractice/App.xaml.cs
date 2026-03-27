using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SmartFillMonitorPractice.Services;
using SmartFillMonitorPractice.ViewModels;
using SmartFillMonitorPractice.Views;

namespace SmartFillMonitorPractice
{
    public partial class App : Application
    {
        public static RichTextBox LogView = new()
        {
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Black,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas"),
        };

        private const string LogTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss fff} [{Level}] ({ThreadId}) {Message}{NewLine}{Exception}";
        private const string LogPath = "Logs\\log-.txt";
        private const string DbFilePath = "SmartFillMonitorPractice.db";
        private const string DbConnectionString = "Data Source=SmartFillMonitorPractice.db";

        private bool _isExiting;
        private bool _exitCleanupCompleted;

        public IServiceProvider ServiceProvider { get; private set; } = null!;

        public bool IsExiting => _isExiting;

        public bool IsSwitchingUser { get; private set; }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            SetExceptionHandling();
            ConfigureLogging();

            try
            {
                var services = new ServiceCollection();
                ConfigureServices(services);
                ServiceProvider = services.BuildServiceProvider();

                await InitializeCoreServicesAsync();
                await ServiceProvider.GetRequiredService<IUserService>().TryApplyDevelopmentResetAsync();

                if (!ShowLoginAndMainWindow())
                {
                    LogService.Info("登录窗口在完成身份验证前已关闭，应用即将退出。");
                    Shutdown();
                    return;
                }

                LogService.Debug("正在根据当前配置初始化 PLC 服务。");
                var plcSettings = await ServiceProvider.GetRequiredService<IConfigService>().LoadSettingsAsync();
                await ServiceProvider.GetRequiredService<IPlcService>().InitializeAsync(plcSettings);
            }
            catch (Exception ex)
            {
                LogService.Fatal("应用启动失败。", ex);
                MessageBox.Show($"应用程序启动失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        public async Task<bool> ReturnToLoginAsync()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            IsSwitchingUser = true;
            LogService.Info("开始执行切换用户流程。");

            try
            {
                var currentMainWindow = Current.MainWindow;
                if (currentMainWindow != null)
                {
                    Current.MainWindow = null;
                    currentMainWindow.Close();
                }

                await ServiceProvider.GetRequiredService<IUserService>().LogoutAsync();

                if (ShowLoginAndMainWindow())
                {
                    LogService.Info("切换用户流程已完成。");
                    return true;
                }

                LogService.Warn("登录窗口取消了切换用户流程，应用即将退出。");
                Shutdown();
                return false;
            }
            finally
            {
                IsSwitchingUser = false;
            }
        }

        public async Task ExitApplicationAsync()
        {
            if (_isExiting)
            {
                LogService.Debug("退出流程已在进行中，本次退出请求被忽略。");
                return;
            }

            _isExiting = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            LogService.Info("收到应用退出请求。");

            try
            {
                await CleanupForExitAsync();
            }
            catch (Exception ex)
            {
                LogService.Error("应用退出清理失败。", ex);
            }

            Current.MainWindow = null;
            LogService.Info("已调用应用关闭。");
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                CleanupForExitAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogService.Error("在 OnExit 阶段执行应用退出清理失败。", ex);
            }

            try
            {
                if (ServiceProvider is IAsyncDisposable asyncDisposable)
                {
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                else if (ServiceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogService.Error("应用退出时释放服务提供程序失败。", ex);
            }

            Log.CloseAndFlush();
            base.OnExit(e);
        }

        private bool ShowLoginAndMainWindow()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var loginWindow = new LoginWindow
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var result = loginWindow.ShowDialog();
            if (result != true)
            {
                return false;
            }

            LogService.Info("身份验证成功，正在打开主窗口。");

            var mainWindow = new MainWindow();
            Current.MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            return true;
        }

        private void SetExceptionHandling()
        {
            DispatcherUnhandledException += (s, e) =>
            {
                LogService.Error($"发生未处理的界面异常。窗口={Current?.MainWindow?.GetType().Name ?? "无"}", e.Exception);

#if DEBUG
                if (Debugger.IsAttached)
                {
                    return;
                }
#endif

                e.Handled = true;
                MessageBox.Show($"UI 异常：{e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    LogService.Fatal($"发生未处理的非界面异常。IsTerminating={e.IsTerminating}", ex);
                }
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogService.Error("发现未观察到的任务异常。", e.Exception);
                e.SetObserved();
            };
        }

        private async Task CleanupForExitAsync()
        {
            if (_exitCleanupCompleted || ServiceProvider == null)
            {
                return;
            }

            _exitCleanupCompleted = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            LogService.Info("应用退出清理开始。");

            var mainWindowViewModel = Current.MainWindow?.DataContext as MainWindowViewModel
                                      ?? ServiceProvider.GetService<MainWindowViewModel>();
            try
            {
                mainWindowViewModel?.StopForExit();
            }
            catch (Exception ex)
            {
                LogService.Error("退出时清理主窗口失败。", ex);
            }

            var plcService = ServiceProvider.GetService<IPlcService>();
            if (plcService != null)
            {
                try
                {
                    await plcService.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    LogService.Error("退出时断开 PLC 失败。", ex);
                }
            }

            LogService.Info("应用退出清理完成。");
        }

        private async Task InitializeCoreServicesAsync()
        {
            Log.Debug("正在初始化核心服务。");

            var dbContext = ServiceProvider.GetRequiredService<IAppDbContext>();
            dbContext.Initialize(DbConnectionString);

            await ServiceProvider.GetRequiredService<IUserService>().InitializeAsync();
            await ServiceProvider.GetRequiredService<IAlarmService>().InitializeAsync();
            await ServiceProvider.GetRequiredService<IDataService>().InitializeAsync();

            LogService.Info("核心服务初始化成功。");
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IAppDbContext, AppDbContext>();
            services.AddSingleton<IUserSessionService, UserSessionService>();
            services.AddSingleton<IAuditService, AuditService>();
            services.AddSingleton<IAuthorizationService, AuthorizationService>();
            services.AddSingleton<IExportService, CsvExportService>();
            services.AddSingleton<IConfigService, ConfigService>();
            services.AddSingleton<IUserService, UserService>();
            services.AddSingleton<IAlarmService, AlarmService>();
            services.AddSingleton<IDataService, DataService>();
            services.AddSingleton<ISystemLogService, SystemLogService>();
            services.AddSingleton<IPlcTransport, ModbusRtuTransport>();
            services.AddSingleton<IDeviceStateMapper, DeviceStateMapper>();
            services.AddSingleton<IPlcService, PlcService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IHeaderStateService, HeaderStateService>();

            services.AddSingleton<AlarmsViewModel>();
            services.AddSingleton<DashBoardViewModel>();
            services.AddSingleton<SimulationViewModel>();
            services.AddSingleton<DashQueryViewModel>();
            services.AddSingleton<LogsViewModel>();
            services.AddSingleton<SettingViewModel>();
            services.AddSingleton<MainWindowViewModel>();
        }

        private void ConfigureLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithThreadId()
                .WriteTo.RichTextBox(LogView, outputTemplate: LogTemplate)
                .WriteTo.Console(outputTemplate: LogTemplate)
                .WriteTo.File(LogPath, rollingInterval: RollingInterval.Day, outputTemplate: LogTemplate, shared: true)
                .WriteTo.SQLite(DbFilePath, tableName: "SystemLog", storeTimestampInUtc: false)
                .CreateLogger();
        }
    }
}
