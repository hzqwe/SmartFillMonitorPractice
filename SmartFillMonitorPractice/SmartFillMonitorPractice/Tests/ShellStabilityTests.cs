using Microsoft.Extensions.DependencyInjection;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;
using SmartFillMonitorPractice.ViewModels;
using Xunit;

namespace SmartFillMonitorPractice.Tests;

public class ShellStabilityTests
{
    [Fact]
    public async Task NavigationService_RejectsSetting_ForNonAdministrator()
    {
        await StaTestHelper.RunAsync(() =>
        {
            var services = BuildShellServices(new User
            {
                UserName = "eng1",
                Role = Role.Engineer
            });

            var navigationService = services.GetRequiredService<INavigationService>();
            Assert.Throws<AuthorizationException>(() => navigationService.Navigate("Setting"));
        });
    }

    [Fact]
    public async Task MainWindowViewModel_UpdatesUserState_AndActivatesHeader_OnNavigation()
    {
        await StaTestHelper.RunAsync(() =>
        {
            var userService = new FakeUserService();
            var headerStateService = new FakeHeaderStateService();
            var navigationService = new FakeNavigationService();
            var plcService = new FakePlcService();
            var dashboard = new DashBoardViewModel(plcService, new FakeAlarmService(), new FakeDataService(), userService);
            var simulation = new SimulationViewModel(plcService);

            var vm = new MainWindowViewModel(userService, navigationService, headerStateService, dashboard, simulation);
            var admin = new User { UserName = "admin", DisplayName = "管理员", Role = Role.Admin };

            userService.SetCurrentUser(admin);
            Assert.True(vm.IsUserLoggedIn);
            Assert.True(vm.IsAdmin);
            Assert.Equal("管理员", vm.CurrentUserDisplayName);

            vm.NavigateCommand.Execute("Simulation");
            Assert.Equal("Simulation", vm.MainContent);
            Assert.Equal(2, headerStateService.ActivateCount);

            vm.StopForExit();
            vm.StopForExit();

            Assert.Equal(1, headerStateService.StopCount);

            dashboard.Dispose();
            simulation.Dispose();
            vm.Dispose();
        });
    }

    private static ServiceProvider BuildShellServices(User currentUser)
    {
        var services = new ServiceCollection();
        var userService = new FakeUserService();
        userService.SetCurrentUser(currentUser);

        services.AddSingleton<IUserService>(userService);
        services.AddSingleton<IPlcService, FakePlcService>();
        services.AddSingleton<IAlarmService, FakeAlarmService>();
        services.AddSingleton<IDataService, FakeDataService>();
        services.AddSingleton<ISystemLogService, FakeSystemLogService>();
        services.AddSingleton<IConfigService, FakeConfigService>();
        services.AddSingleton<IAuthorizationService, FakeAuthorizationService>();
        services.AddSingleton<DashBoardViewModel>();
        services.AddSingleton<SimulationViewModel>();
        services.AddSingleton<DashQueryViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<AlarmsViewModel>();
        services.AddSingleton<SettingViewModel>();
        services.AddSingleton<INavigationService, NavigationService>();
        return services.BuildServiceProvider();
    }
}
