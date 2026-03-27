using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;
using Xunit;

namespace SmartFillMonitorPractice.Tests;

public class PlcServiceTests
{
    [Fact]
    public async Task InitializeAndConnect_PublishesSuccessfulRead()
    {
        var transport = new FakePlcTransport();
        var service = new PlcService(transport, new FakeDeviceStateMapper(), new FakeAuthorizationService(), new AuditService(new UserSessionService()));

        var connectedEvents = 0;
        service.ConnectionChanged += (_, connected) =>
        {
            if (connected)
            {
                connectedEvents++;
            }
        };

        await service.InitializeAsync(new DeviceSettings
        {
            PortName = "COM1",
            BaudRate = 9600,
            DataBits = 8,
            Parity = "None",
            StopBits = "One",
            AutoConnect = true
        });

        await Task.Delay(350);

        Assert.True(service.HasSuccessfulRead);
        Assert.NotNull(service.LastReadSuccessTime);
        Assert.NotNull(service.LastDeviceState);
        Assert.True(connectedEvents >= 1);

        await service.DisposeAsync();
    }

    [Fact]
    public async Task DisconnectAsync_StopsPolling_AndManualDisconnectPreventsReconnect()
    {
        var transport = new FakePlcTransport();
        var service = new PlcService(transport, new FakeDeviceStateMapper(), new FakeAuthorizationService(), new AuditService(new UserSessionService()));
        var dataReceivedCount = 0;
        service.DataReceived += (_, _) => dataReceivedCount++;

        await service.InitializeAsync(new DeviceSettings
        {
            PortName = "COM1",
            BaudRate = 9600,
            DataBits = 8,
            Parity = "None",
            StopBits = "One",
            AutoConnect = true
        });

        await Task.Delay(350);
        var beforeDisconnect = dataReceivedCount;

        await service.DisconnectAsync();
        await Task.Delay(700);

        Assert.False(service.HasSuccessfulRead);
        Assert.Equal(beforeDisconnect, dataReceivedCount);
        Assert.Equal(1, transport.ConnectCount);

        await service.DisposeAsync();
    }

    [Fact]
    public async Task WriteCommandAsync_ReturnsFalse_WhenTransportIsNotConnected()
    {
        var transport = new FakePlcTransport();
        var service = new PlcService(transport, new FakeDeviceStateMapper(), new FakeAuthorizationService(), new AuditService(new UserSessionService()));

        var result = await service.WriteCommandAsync("Start", true);

        Assert.False(result);

        await service.DisposeAsync();
    }
}
