namespace SmartFillMonitorPractice.Models
{
    public class DeviceState
    {
        public int ActualCount { get; set; }

        public int TargetCount { get; set; }

        public double CurrentTemp { get; set; }

        public double SettingTemp { get; set; }

        public double RunningTime { get; set; }

        public string? DeviceStatus { get; set; }

        public double CurrentCycleTime { get; set; }

        public double LiquidLevel { get; set; }

        public double StandardCycleTime { get; set; }

        public bool ValveOpen { get; set; }

        public string BarCode { get; set; } = string.Empty;
    }
}
