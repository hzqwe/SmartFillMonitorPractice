using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;
using Xunit;

namespace SmartFillMonitorPractice.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void Validate_Throws_WhenPortNameIsEmpty()
    {
        using var scope = new TestAppScope();
        var settings = new DeviceSettings
        {
            PortName = string.Empty,
            BaudRate = 9600,
            DataBits = 8,
            Parity = "None",
            StopBits = "One"
        };

        Assert.Throws<BusinessException>(() => scope.ConfigService.Validate(settings));
    }

    [Fact]
    public void Validate_Throws_WhenBaudRateIsInvalid()
    {
        using var scope = new TestAppScope();
        var settings = new DeviceSettings
        {
            PortName = "COM1",
            BaudRate = 100,
            DataBits = 8,
            Parity = "None",
            StopBits = "One"
        };

        Assert.Throws<BusinessException>(() => scope.ConfigService.Validate(settings));
    }
}
