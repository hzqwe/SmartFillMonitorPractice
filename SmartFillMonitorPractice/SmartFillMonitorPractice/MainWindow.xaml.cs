using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace SmartFillMonitorPractice
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            if (Application.Current is App app && app.ServiceProvider != null)
            {
                DataContext = app.ServiceProvider.GetRequiredService<MainWindowViewModel>();
                Services.LogService.Info("主窗口已创建。");
            }
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            if (Application.Current is App app)
            {
                if (!app.IsSwitchingUser && !app.IsExiting)
                {
                    Services.LogService.Info("主窗口关闭请求已拦截，转交应用退出协调器处理。");
                    e.Cancel = true;
                    await app.ExitApplicationAsync();
                    return;
                }

                Services.LogService.Info($"允许主窗口继续关闭。IsSwitchingUser={app.IsSwitchingUser}, IsExiting={app.IsExiting}");
            }

            base.OnClosing(e);
        }
    }
}
