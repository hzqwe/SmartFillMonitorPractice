using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using SmartFillMonitorPractice.Models;

namespace SmartFillMonitorPractice.Services
{
    public class SystemLogService : ISystemLogService
    {
        private readonly IAppDbContext _dbContext;
        private readonly IAuthorizationService _authorizationService;
        private readonly IExportService _exportService;
        private readonly IAuditService _auditService;

        public SystemLogService(IAppDbContext dbContext, IAuthorizationService authorizationService, IExportService exportService, IAuditService auditService)
        {
            _dbContext = dbContext;
            _authorizationService = authorizationService;
            _exportService = exportService;
            _auditService = auditService;
        }

        public ISelect<SystemLog> BuildQuery(SystemLogQueryFilter filter)
        {
            filter ??= new SystemLogQueryFilter();

            var query = _dbContext.Fsql.Select<SystemLog>()
                .Where(x => x.Timestamp >= filter.StartTime && x.Timestamp <= filter.EndTime);

            if (!string.IsNullOrWhiteSpace(filter.SearchText))
            {
                var keyword = filter.SearchText.Trim();
                query = query.Where(x => x.RenderedMessage.Contains(keyword) || (x.Exception != null && x.Exception.Contains(keyword)));
            }

            if (!string.IsNullOrWhiteSpace(filter.Level) && !string.Equals(filter.Level, "All"))
            {
                var level = filter.Level.Trim();
                query = query.Where(x => x.Level == level);
            }

            return query;
        }

        public async Task<(List<SystemLog> Items, long Total)> QueryAsync(SystemLogQueryFilter filter, int pageIndex, int pageSize)
        {
            var query = BuildQuery(filter);
            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.Timestamp)
                .Page(pageIndex, pageSize)
                .ToListAsync();
            return (items, total);
        }

        public async Task<string> ExportAsync(SystemLogQueryFilter filter, string filePath)
        {
            _authorizationService.EnsurePermission(Permission.ExportLogs, "导出日志");

            var query = BuildQuery(filter);
            var allData = await query.OrderByDescending(x => x.Timestamp).ToListAsync();
            if (allData.Count == 0)
            {
                throw new BusinessException("没有数据可导出。");
            }

            var fullPath = await _exportService.ExportAsync(allData.Select(x => new SystemLogExportItem
            {
                Timestamp = x.Timestamp,
                Level = x.Level,
                RenderedMessage = x.RenderedMessage,
                Exception = x.Exception
            }), filePath);

            _auditService.Operation("ExportLogs", "Success", $"已导出 {allData.Count} 条日志到 {fullPath}");
            return fullPath;
        }

        private sealed class SystemLogExportItem
        {
            public System.DateTime Timestamp { get; set; }

            public string Level { get; set; } = string.Empty;

            public string RenderedMessage { get; set; } = string.Empty;

            public string Exception { get; set; } = string.Empty;
        }
    }
}
