using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SmartFillMonitorPractice.Models;

namespace SmartFillMonitorPractice.Services
{
    public class AlarmService : IAlarmService
    {
        private readonly IAppDbContext _dbContext;
        private readonly IUserSessionService _userSessionService;
        private readonly IAuthorizationService _authorizationService;
        private readonly IAuditService _auditService;

        public AlarmService(IAppDbContext dbContext, IUserSessionService userSessionService, IAuthorizationService authorizationService, IAuditService auditService)
        {
            _dbContext = dbContext;
            _userSessionService = userSessionService;
            _authorizationService = authorizationService;
            _auditService = auditService;
        }

        public event EventHandler<AlarmRecord>? AlarmTriggered;

        public event EventHandler<AlarmRecord>? AlarmAcknowledged;

        public event EventHandler<AlarmRecord>? AlarmRecovered;

        public async Task InitializeAsync()
        {
            await NormalizeActiveAlarmAsync();
            const string sql = "CREATE UNIQUE INDEX IF NOT EXISTS idx_alarm_active_unique ON AlarmRecord(AlarmCode) WHERE IsActive = 1;";
            await _dbContext.Fsql.Ado.ExecuteNonQueryAsync(sql);
        }

        public async Task TriggerAlarmAsync(AlarmRecord alarmRecord)
        {
            if (alarmRecord == null || alarmRecord.AlarmCode == AlarmCode.None)
            {
                return;
            }

            try
            {
                var now = DateTime.Now;
                alarmRecord.StartTime = alarmRecord.StartTime == default ? now : alarmRecord.StartTime;
                alarmRecord.IsActive = true;
                alarmRecord.IsAcknowledged = false;
                alarmRecord.AckTime = null;
                alarmRecord.AckUser = null;
                alarmRecord.AckUserId = null;
                alarmRecord.AckUserName = null;
                alarmRecord.RecoverUserId = null;
                alarmRecord.RecoverUserName = null;
                alarmRecord.EndTime = default;
                alarmRecord.DurationSeconds = null;
                alarmRecord.ProcessSuggestion = null;
                alarmRecord.TriggeredBy = NormalizeTriggeredBy(alarmRecord.TriggeredBy, alarmRecord.TriggeredByType);

                if (string.IsNullOrWhiteSpace(alarmRecord.Description))
                {
                    alarmRecord.Description = alarmRecord.AlarmCode.GetDescription();
                }

                await _dbContext.Fsql.Insert(alarmRecord).ExecuteAffrowsAsync();

                var latestRecord = await _dbContext.Fsql.Select<AlarmRecord>()
                    .Where(a => a.AlarmCode == alarmRecord.AlarmCode && a.IsActive)
                    .OrderByDescending(a => a.Id)
                    .FirstAsync();

                if (latestRecord != null)
                {
                    alarmRecord = latestRecord;
                }

                LogService.Warn($"[报警触发] {alarmRecord.AlarmCode}: {alarmRecord.Message ?? alarmRecord.Description}");
                _auditService.Operation("TriggerAlarm", "Success", $"报警已触发：编码={alarmRecord.AlarmCode}；ID={alarmRecord.Id}；来源={alarmRecord.TriggeredByType}；触发者={alarmRecord.TriggeredBy}", alarmRecord.TriggeredBy);
                AlarmTriggered?.Invoke(this, alarmRecord);
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                LogService.Debug($"报警 {alarmRecord.AlarmCode} 已存在活动记录，忽略重复触发。");
            }
            catch (Exception ex)
            {
                LogService.Error($"触发报警异常：{alarmRecord.AlarmCode}", ex);
                throw new InfrastructureException("触发报警失败。", ex);
            }
        }

        public Task TriggerTestAlarmAsync()
        {
            var currentUser = _userSessionService.CurrentUser;
            var triggeredBy = currentUser?.DisplayNameOrUserName ?? "system";
            var sourceType = currentUser == null ? AlarmTriggeredByType.System : AlarmTriggeredByType.User;

            return TriggerAlarmAsync(new AlarmRecord
            {
                AlarmCode = AlarmCode.TestAlarm,
                AlarmSeverity = AlarmSeverity.Warning,
                StartTime = DateTime.Now,
                Description = "用于测试报警链路",
                Message = "测试报警已触发",
                TriggeredBy = triggeredBy,
                TriggeredByType = sourceType
            });
        }

        public async Task<bool> AcknowledgeAlarmAsync(long alarmId, string operatorName, string processSuggestion = "")
        {
            _authorizationService.EnsurePermission(Permission.ManageAlarms, "确认报警");

            var (userId, actor) = ResolveCurrentUserForAlarmAction(operatorName);
            var activeAlarm = await _dbContext.Fsql.Select<AlarmRecord>()
                .Where(a => a.Id == alarmId && a.IsActive)
                .FirstAsync();

            if (activeAlarm == null)
            {
                return false;
            }

            if (activeAlarm.IsAcknowledged)
            {
                return true;
            }

            var now = DateTime.Now;
            activeAlarm.IsAcknowledged = true;
            activeAlarm.AckTime = now;
            activeAlarm.AckUser = actor;
            activeAlarm.AckUserId = userId;
            activeAlarm.AckUserName = actor;
            activeAlarm.ProcessSuggestion = string.IsNullOrWhiteSpace(processSuggestion) ? null : processSuggestion.Trim();

            await _dbContext.Fsql.Update<AlarmRecord>()
                .Where(a => a.Id == activeAlarm.Id)
                .Set(a => a.IsAcknowledged, true)
                .Set(a => a.AckTime, now)
                .Set(a => a.AckUser, actor)
                .Set(a => a.AckUserId, userId)
                .Set(a => a.AckUserName, actor)
                .Set(a => a.ProcessSuggestion, activeAlarm.ProcessSuggestion)
                .ExecuteAffrowsAsync();

            _auditService.Operation("AcknowledgeAlarm", "Success", $"报警已确认：ID={activeAlarm.Id}；编码={activeAlarm.AlarmCode}", actor);
            AlarmAcknowledged?.Invoke(this, activeAlarm);
            return true;
        }

        public Task<bool> RecoverAlarmAsync(AlarmCode alarmCode)
        {
            return RecoverAlarmInternalAsync(
                () => _dbContext.Fsql.Select<AlarmRecord>()
                    .Where(a => a.AlarmCode == alarmCode && a.IsActive)
                    .OrderByDescending(a => a.Id)
                    .FirstAsync(),
                null,
                "system",
                false);
        }

        public Task<bool> RecoverAlarmAsync(long alarmId, string operatorName = "")
        {
            var (userId, actor) = ResolveCurrentUserForAlarmAction(operatorName);
            return RecoverAlarmInternalAsync(
                () => _dbContext.Fsql.Select<AlarmRecord>()
                    .Where(a => a.Id == alarmId && a.IsActive)
                    .FirstAsync(),
                userId,
                actor,
                true);
        }

        public Task<bool> RecoverTestAlarmAsync()
        {
            return RecoverAlarmAsync(AlarmCode.TestAlarm);
        }

        public async Task<List<AlarmRecord>> GetActiveAlarmsAsync()
        {
            return await _dbContext.Fsql.Select<AlarmRecord>()
                .Where(a => a.IsActive)
                .OrderBy(a => a.IsAcknowledged)
                .OrderByDescending(a => a.StartTime)
                .ToListAsync();
        }

        public async Task<(List<AlarmRecord> Item, long Total)> GetAlarmHistoryAsync(int pageIndex, int pageSize, DateTime? startTime = null, DateTime? endTime = null, AlarmSeverity alarmSeverity = AlarmSeverity.All)
        {
            try
            {
                var query = _dbContext.Fsql.Select<AlarmRecord>()
                    .Where(w => !w.IsActive);

                if (startTime.HasValue)
                {
                    query = query.Where(w => w.StartTime >= startTime.Value);
                }

                if (endTime.HasValue)
                {
                    query = query.Where(w => w.StartTime < endTime.Value);
                }

                if (alarmSeverity != AlarmSeverity.All)
                {
                    query = query.Where(w => w.AlarmSeverity == alarmSeverity);
                }

                var total = await query.CountAsync();
                var list = await query
                    .OrderByDescending(a => a.EndTime)
                    .OrderByDescending(a => a.StartTime)
                    .Page(pageIndex, pageSize)
                    .ToListAsync();

                return (list, total);
            }
            catch (Exception ex)
            {
                LogService.Error("查询历史报警失败", ex);
                return (new List<AlarmRecord>(), 0);
            }
        }

        private async Task<bool> RecoverAlarmInternalAsync(Func<Task<AlarmRecord>> finder, long? recoverUserId, string actor, bool requireAuthorization)
        {
            if (requireAuthorization)
            {
                _authorizationService.EnsurePermission(Permission.ManageAlarms, "恢复报警");
            }

            var activeAlarm = await finder();
            if (activeAlarm == null)
            {
                return false;
            }

            var now = DateTime.Now;
            activeAlarm.IsActive = false;
            activeAlarm.EndTime = now;
            activeAlarm.DurationSeconds = Math.Max(0, (now - activeAlarm.StartTime).TotalSeconds);
            activeAlarm.RecoverUserId = recoverUserId;
            activeAlarm.RecoverUserName = actor;

            await _dbContext.Fsql.Update<AlarmRecord>()
                .Where(a => a.Id == activeAlarm.Id)
                .Set(a => a.IsActive, false)
                .Set(a => a.EndTime, now)
                .Set(a => a.DurationSeconds, activeAlarm.DurationSeconds)
                .Set(a => a.RecoverUserId, recoverUserId)
                .Set(a => a.RecoverUserName, actor)
                .ExecuteAffrowsAsync();

            _auditService.Operation("RecoverAlarm", "Success", $"报警已恢复：ID={activeAlarm.Id}；编码={activeAlarm.AlarmCode}", actor);
            AlarmRecovered?.Invoke(this, activeAlarm);
            return true;
        }

        private (long? UserId, string Actor) ResolveCurrentUserForAlarmAction(string operatorName)
        {
            var currentUser = _userSessionService.CurrentUser;
            if (currentUser == null)
            {
                throw new AuthorizationException("当前未登录，无法执行报警操作。");
            }

            var actor = currentUser.DisplayNameOrUserName;
            if (!string.IsNullOrWhiteSpace(actor))
            {
                return (currentUser.Id, actor);
            }

            operatorName = operatorName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(operatorName))
            {
                throw new AuthorizationException("当前用户信息无效，无法执行报警操作。");
            }

            return (currentUser.Id, operatorName);
        }

        private string NormalizeTriggeredBy(string? triggeredBy, AlarmTriggeredByType sourceType)
        {
            if (!string.IsNullOrWhiteSpace(triggeredBy))
            {
                return triggeredBy.Trim();
            }

            return sourceType switch
            {
                AlarmTriggeredByType.Plc => "PLC",
                AlarmTriggeredByType.User => string.IsNullOrWhiteSpace(_userSessionService.GetCurrentUserDisplayName()) ? "user" : _userSessionService.GetCurrentUserDisplayName(),
                _ => "system",
            };
        }

        private async Task NormalizeActiveAlarmAsync()
        {
            var activeAlarms = await _dbContext.Fsql.Select<AlarmRecord>()
                .Where(a => a.IsActive)
                .OrderByDescending(a => a.StartTime)
                .ToListAsync();

            foreach (var group in activeAlarms.GroupBy(a => a.AlarmCode))
            {
                var keep = group.OrderByDescending(a => a.StartTime).ThenByDescending(a => a.Id).First();
                foreach (var duplicate in group.Where(a => a.Id != keep.Id))
                {
                    var endTime = duplicate.EndTime == default ? (duplicate.AckTime ?? duplicate.StartTime) : duplicate.EndTime;
                    var duration = Math.Max(0, (endTime - duplicate.StartTime).TotalSeconds);

                    await _dbContext.Fsql.Update<AlarmRecord>()
                        .Where(a => a.Id == duplicate.Id)
                        .Set(a => a.IsActive, false)
                        .Set(a => a.EndTime, endTime)
                        .Set(a => a.DurationSeconds, duration)
                        .Set(a => a.RecoverUserName, string.IsNullOrWhiteSpace(duplicate.RecoverUserName) ? "system" : duplicate.RecoverUserName)
                        .ExecuteAffrowsAsync();
                }
            }
        }

        private static bool IsUniqueViolation(Exception ex)
        {
            var message = ex.ToString();
            return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("SQLITE_CONSTRAINT", StringComparison.OrdinalIgnoreCase);
        }
    }
}
