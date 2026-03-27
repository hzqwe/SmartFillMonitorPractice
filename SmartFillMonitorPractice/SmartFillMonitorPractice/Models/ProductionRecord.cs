using System;
using FreeSql.DataAnnotations;

namespace SmartFillMonitorPractice.Models
{
    [Table(Name = "ProductionRecords")]
    public class ProductionRecord
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        public DateTime Time { get; set; } = DateTime.Now;

        [Column(StringLength = 50)]
        public string? BatchNo { get; set; }

        [Column(StringLength = 120)]
        public string? IdempotencyKey { get; set; }

        public double SettingTemp { get; set; }

        public double ActualTemp { get; set; }

        public double FillWeight { get; set; }

        public int ActualCount { get; set; }

        public int TargetCount { get; set; }

        public bool IsNG { get; set; }

        [Column(StringLength = 100)]
        public string? NgReason { get; set; }

        public double CycleTime { get; set; }

        [Column(StringLength = 100)]
        public string? Operator { get; set; }
    }
}
