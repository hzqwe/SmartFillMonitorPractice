using System;
using System.ComponentModel;
using FreeSql.DataAnnotations;

namespace SmartFillMonitorPractice.Models
{
    [Table(Name = "Users")]
    [Index("idx_unique_username", "UserName", true)]
    public class User
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        [Column(StringLength = 50, IsNullable = false)]
        public string UserName { get; set; } = string.Empty;

        [Column(StringLength = 50)]
        public string DisplayName { get; set; } = string.Empty;

        [Column(StringLength = 256, IsNullable = false)]
        public string PasswordHash { get; set; } = string.Empty;

        [Column(StringLength = 128)]
        public string PasswordSalt { get; set; } = string.Empty;

        public int PasswordIterations { get; set; }

        [Column(MapType = typeof(int))]
        public Role Role { get; set; }

        public bool IsDisabled { get; set; }

        public int FailedLoginCount { get; set; }

        public DateTime? LockedUntil { get; set; }

        public DateTime? LastFailedLoginTime { get; set; }

        public bool RequirePasswordChange { get; set; }

        public DateTime? PasswordChangedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? LastLoginTime { get; set; }

        [Column(IsIgnore = true)]
        public string DisplayNameOrUserName => string.IsNullOrWhiteSpace(DisplayName) ? UserName : DisplayName;

        [Column(IsIgnore = true)]
        public string DisplayText => string.IsNullOrWhiteSpace(DisplayName) || string.Equals(DisplayName, UserName, StringComparison.OrdinalIgnoreCase)
            ? UserName
            : $"{DisplayName} ({UserName})";

        [Column(IsIgnore = true)]
        public string RoleName => Role switch
        {
            Role.Admin => "管理员",
            Role.Engineer => "工程师",
            _ => "未知",
        };
    }

    public enum Role
    {
        [Description("管理员")]
        Admin = 0,

        [Description("工程师")]
        Engineer = 1,
    }
}
