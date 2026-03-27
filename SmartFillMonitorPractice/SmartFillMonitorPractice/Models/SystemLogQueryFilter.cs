using System;

namespace SmartFillMonitorPractice.Models
{
    public class SystemLogQueryFilter
    {
        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        public string Level { get; set; } = "All";

        public string SearchText { get; set; } = string.Empty;
    }
}
