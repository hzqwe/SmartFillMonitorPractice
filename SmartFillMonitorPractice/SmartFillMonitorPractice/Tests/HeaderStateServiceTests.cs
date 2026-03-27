using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;
using SmartFillMonitorPractice.ViewModels;
using Xunit;

namespace SmartFillMonitorPractice.Tests;

public class HeaderStateServiceTests
{
    [Fact]
    public async Task Activate_UsesDashboardAndFallbackSourcesCorrectly()
    {
        await StaTestHelper.RunAsync(() =>
        {
            var plcService = new FakePlcService
            {
                HasSuccessfulRead = true,
                LastDeviceState = new DeviceState { BarCode = "PLC-001" }
            };
            var dashboard = new DashBoardViewModel(plcService, new FakeAlarmService(), new FakeDataService(), new FakeUserService())
            {
                IndicatorState = LightState.Green
            };
            var simulation = new SimulationViewModel(plcService)
            {
                IndicatorState = LightState.Yellow,
                CurrentBatchNo = "SIM-001"
            };

            using var service = new HeaderStateService(plcService);

            service.Activate(dashboard);
            Assert.Equal(LightState.Green, service.IndicatorState);
            Assert.Equal("PLC-001", service.CurrentBatchNo);

            service.Activate(simulation);
            Assert.Equal(LightState.Yellow, service.IndicatorState);
            Assert.Equal("SIM-001", service.CurrentBatchNo);

            service.Activate(new object());
            Assert.Equal(LightState.Yellow, service.IndicatorState);
            Assert.Equal("PLC-001", service.CurrentBatchNo);

            dashboard.Dispose();
            simulation.Dispose();
        });
    }

    [Fact]
    public void DataReceived_UpdatesBatch_WhenNotOnSimulationPage_AndStopPreventsFurtherChanges()
    {
        var plcService = new FakePlcService();
        using var service = new HeaderStateService(plcService);

        service.Activate(new object());
        plcService.RaiseDataReceived(new DeviceState { BarCode = "BATCH-100" });
        Assert.Equal("BATCH-100", service.CurrentBatchNo);

        service.Stop();
        plcService.RaiseDataReceived(new DeviceState { BarCode = "BATCH-200" });
        Assert.Equal("BATCH-100", service.CurrentBatchNo);
    }
}
