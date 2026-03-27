using System.Collections.Generic;
using SmartFillMonitorPractice.Models;

namespace SmartFillMonitorPractice.Services
{
    public class AuthorizationService : IAuthorizationService
    {
        private static readonly Dictionary<Role, HashSet<Permission>> PermissionMap = new()
        {
            {
                Role.Admin,
                new HashSet<Permission>
                {
                    Permission.ManageSettings,
                    Permission.ManageUsers,
                    Permission.ControlPlc,
                    Permission.ManageAlarms,
                    Permission.ExportLogs,
                }
            },
            {
                Role.Engineer,
                new HashSet<Permission>
                {
                    Permission.ControlPlc,
                    Permission.ManageAlarms,
                    Permission.ExportLogs,
                }
            }
        };

        private readonly IUserSessionService _userSessionService;
        private readonly IAuditService _auditService;

        public AuthorizationService(IUserSessionService userSessionService, IAuditService auditService)
        {
            _userSessionService = userSessionService;
            _auditService = auditService;
        }

        public bool HasPermission(User? user, Permission permission)
        {
            if (user == null)
            {
                return false;
            }

            return PermissionMap.TryGetValue(user.Role, out var permissions) && permissions.Contains(permission);
        }

        public void EnsurePermission(Permission permission, string action)
        {
            var user = _userSessionService.CurrentUser;
            if (HasPermission(user, permission))
            {
                return;
            }

            var actor = user?.DisplayNameOrUserName ?? "未登录用户";
            _auditService.Security("PermissionDenied", "Denied", $"{actor} 尝试执行“{action}”，但缺少权限 {permission}", actor);
            throw new AuthorizationException($"当前用户无权执行：{action}");
        }
    }
}
