using SmartFillMonitorPractice.Models;

namespace SmartFillMonitorPractice.Services
{
    public class DeviceStateMapper : IDeviceStateMapper
    {
        public DeviceState Map(ushort[] registers, string barcode)
        {
            return new DeviceState
            {
                ActualCount = registers[ModbusConfigHelper.ActualCount],
                TargetCount = registers[ModbusConfigHelper.TargetCount],
                CurrentTemp = registers[ModbusConfigHelper.CurrentTemp] / 100.0,
                SettingTemp = registers[ModbusConfigHelper.SettingTemp] / 100.0,
                RunningTime = registers[ModbusConfigHelper.RunningTime] / 100.0,
                CurrentCycleTime = registers[ModbusConfigHelper.CurrentCycleTime] / 100.0,
                StandardCycleTime = registers[ModbusConfigHelper.StandardCycleTime] / 100.0,
                LiquidLevel = registers[ModbusConfigHelper.LiquidLevel] / 100.0,
                ValveOpen = registers[ModbusConfigHelper.ValveOpen] == 1,
                BarCode = barcode
            };
        }
    }
}
