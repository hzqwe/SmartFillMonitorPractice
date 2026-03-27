using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FreeSql.DataAnnotations;

namespace SmartFillMonitorPractice.Models
{
    [Table(Name = "AlarmRecord")]
    public class AlarmRecord
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        public AlarmCode AlarmCode { get; set; }

        [Column(Name = "AlarmServerity")]
        public AlarmSeverity AlarmSeverity { get; set; }

        public DateTime StartTime { get; set; } = DateTime.Now;

        public DateTime EndTime { get; set; }

        public double? DurationSeconds { get; set; }

        public bool IsActive { get; set; }

        [Column(Name = "IsAcKnowledged")]
        public bool IsAcknowledged { get; set; }

        public DateTime? AckTime { get; set; }

        [Column(Name = "Ackuser", StringLength = 50)]
        public string? AckUser { get; set; }

        public long? AckUserId { get; set; }

        [Column(StringLength = 50)]
        public string? AckUserName { get; set; }

        public long? RecoverUserId { get; set; }

        [Column(StringLength = 50)]
        public string? RecoverUserName { get; set; }

        [Column(StringLength = 100)]
        public string? TriggeredBy { get; set; }

        public AlarmTriggeredByType TriggeredByType { get; set; } = AlarmTriggeredByType.System;

        [Column(StringLength = 500)]
        public string? Description { get; set; }

        [Column(StringLength = 500)]
        public string? Message { get; set; }

        [Column(StringLength = 500)]
        public string? ProcessSuggestion { get; set; }
    }

    public enum AlarmSeverity
    {
        [Description("全部")]
        All = 0,

        [Description("提示")]
        Info = 1,

        [Description("警告")]
        Warning = 2,

        [Description("错误")]
        Error = 3,

        [Description("致命")]
        Critical = 4,
    }

    public enum AlarmCode
    {
        [Description("无")]
        None = 0,

        [Description("原料箱液位过低")]
        LowLiquidLevel = 1,

        [Description("压缩空气压力偏低")]
        LowAirPressure = 2001,

        [Description("加热温度过高")]
        HighTemperature = 3001,

        [Description("PLC 通信故障")]
        CommunicationError = 4001,

        [Description("系统内部错误")]
        SystemError = 5001,

        [Description("测试报警")]
        TestAlarm = 9001,
    }

    public enum AlarmTriggeredByType
    {
        [Description("系统")]
        System = 0,

        [Description("PLC")]
        Plc = 1,

        [Description("用户")]
        User = 2,
    }

    public class AlarmUiModel : INotifyPropertyChanged
    {
        private long _id;
        private string _code = string.Empty;
        private string _title = string.Empty;
        private string _timeStr = string.Empty;
        private string _description = string.Empty;
        private string _severityText = string.Empty;
        private string _statusText = string.Empty;
        private string _ackUser = string.Empty;
        private string _ackTimeStr = string.Empty;
        private string _recoverUser = string.Empty;
        private string _recoverTimeStr = string.Empty;
        private string _triggeredBy = string.Empty;
        private string _triggeredByTypeText = string.Empty;
        private string _processSuggestion = string.Empty;
        private string _durationText = string.Empty;
        private bool _isActive;
        private bool _isAcknowledged;
        private bool _canAcknowledge = true;
        private string _acknowledgeButtonText = "确认报警";

        public long Id
        {
            get => _id;
            set => SetField(ref _id, value);
        }

        public string Code
        {
            get => _code;
            set => SetField(ref _code, value);
        }

        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        public string TimeStr
        {
            get => _timeStr;
            set => SetField(ref _timeStr, value);
        }

        public string Description
        {
            get => _description;
            set => SetField(ref _description, value);
        }

        public string SeverityText
        {
            get => _severityText;
            set => SetField(ref _severityText, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        public string AckUser
        {
            get => _ackUser;
            set => SetField(ref _ackUser, value);
        }

        public string AckTimeStr
        {
            get => _ackTimeStr;
            set => SetField(ref _ackTimeStr, value);
        }

        public string RecoverUser
        {
            get => _recoverUser;
            set => SetField(ref _recoverUser, value);
        }

        public string RecoverTimeStr
        {
            get => _recoverTimeStr;
            set => SetField(ref _recoverTimeStr, value);
        }

        public string TriggeredBy
        {
            get => _triggeredBy;
            set => SetField(ref _triggeredBy, value);
        }

        public string TriggeredByTypeText
        {
            get => _triggeredByTypeText;
            set => SetField(ref _triggeredByTypeText, value);
        }

        public string ProcessSuggestion
        {
            get => _processSuggestion;
            set => SetField(ref _processSuggestion, value);
        }

        public string DurationText
        {
            get => _durationText;
            set => SetField(ref _durationText, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetField(ref _isActive, value);
        }

        public bool IsAcknowledged
        {
            get => _isAcknowledged;
            set => SetField(ref _isAcknowledged, value);
        }

        public bool CanAcknowledge
        {
            get => _canAcknowledge;
            set => SetField(ref _canAcknowledge, value);
        }

        public string AcknowledgeButtonText
        {
            get => _acknowledgeButtonText;
            set => SetField(ref _acknowledgeButtonText, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public static AlarmUiModel FormRecord(AlarmRecord record)
        {
            var description = string.IsNullOrWhiteSpace(record.Message) ? record.Description ?? string.Empty : record.Message;
            var ackUser = string.IsNullOrWhiteSpace(record.AckUserName) ? record.AckUser ?? string.Empty : record.AckUserName;
            var recoverUser = record.RecoverUserName ?? string.Empty;
            var statusText = record.IsActive
                ? (record.IsAcknowledged ? "已确认" : "活动中")
                : "已恢复";

            return new AlarmUiModel
            {
                Id = record.Id,
                Code = $"E{(int)record.AlarmCode}",
                Title = record.AlarmCode.GetDescription(),
                Description = description,
                SeverityText = record.AlarmSeverity.GetDescription(),
                TimeStr = record.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                StatusText = statusText,
                AckUser = ackUser,
                AckTimeStr = record.AckTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                RecoverUser = recoverUser,
                RecoverTimeStr = record.EndTime == default ? string.Empty : record.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                TriggeredBy = record.TriggeredBy ?? string.Empty,
                TriggeredByTypeText = record.TriggeredByType.GetDescription(),
                ProcessSuggestion = record.ProcessSuggestion ?? string.Empty,
                DurationText = FormatDuration(record.DurationSeconds),
                IsActive = record.IsActive,
                IsAcknowledged = record.IsAcknowledged,
                CanAcknowledge = record.IsActive,
                AcknowledgeButtonText = record.IsActive
                    ? (record.IsAcknowledged ? "恢复报警" : "确认报警")
                    : "已处理",
            };
        }

        private static string FormatDuration(double? durationSeconds)
        {
            if (!durationSeconds.HasValue || durationSeconds.Value <= 0)
            {
                return string.Empty;
            }

            var span = TimeSpan.FromSeconds(Math.Round(durationSeconds.Value));
            var result = string.Empty;

            if (span.Days > 0)
            {
                result += $"{span.Days}天";
            }

            if (span.Hours > 0)
            {
                result += $"{span.Hours}小时";
            }

            if (span.Minutes > 0)
            {
                result += $"{span.Minutes}分";
            }

            if (span.Seconds > 0 || string.IsNullOrWhiteSpace(result))
            {
                result += $"{span.Seconds}秒";
            }

            return result;
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public static class EnumExtensions
    {
        public static string GetDescription(this Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            if (field == null)
            {
                return value.ToString();
            }

            var attribute = Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;
            return attribute?.Description ?? value.ToString();
        }
    }
}
