using System;
using FreeSql.DataAnnotations;

namespace SmartFillMonitorPractice.Models
{
    [Table(Name = "SystemLog", DisableSyncStructure = true)]
    public class SystemLog
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "Timestamp")]
        public DateTime Timestamp { get; set; }

        [Column(Name = "Level", StringLength = 50)]
        public string Level { get; set; } = string.Empty;

        [Column(Name = "Exception", StringLength = 2000)]
        public string Exception { get; set; } = string.Empty;

        [Column(Name = "RenderedMessage", StringLength = 500)]
        public string RenderedMessage { get; set; } = string.Empty;

        [Column(Name = "Properties", StringLength = 2000)]
        public string Properties { get; set; } = string.Empty;
    }
}
