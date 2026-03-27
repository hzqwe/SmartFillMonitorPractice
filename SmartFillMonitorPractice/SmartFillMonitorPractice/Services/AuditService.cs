namespace SmartFillMonitorPractice.Services
{
    public class AuditService : IAuditService
    {
        private readonly IUserSessionService _userSessionService;

        public AuditService(IUserSessionService userSessionService)
        {
            _userSessionService = userSessionService;
        }

        public void Security(string action, string result, string detail, string? userName = null)
        {
            var actor = string.IsNullOrWhiteSpace(userName) ? "匿名用户" : userName;
            LogService.Info($"[审计][安全][{action}][{result}] 用户={actor}；详情={detail}");
        }

        public void Operation(string action, string result, string detail, string? userName = null)
        {
            var actor = string.IsNullOrWhiteSpace(userName) ? _userSessionService.GetCurrentUserName() : userName;
            actor = string.IsNullOrWhiteSpace(actor) ? "匿名用户" : actor;
            LogService.Info($"[审计][操作][{action}][{result}] 用户={actor}；详情={detail}");
        }
    }
}
