using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SmartFillMonitorPractice.Models;

namespace SmartFillMonitorPractice.Services
{
    public class DataService : IDataService
    {
        private readonly IAppDbContext _dbContext;
        private readonly IAuthorizationService _authorizationService;
        private readonly IAuditService _auditService;
        private readonly IExportService _exportService;

        public DataService(IAppDbContext dbContext, IAuthorizationService authorizationService, IAuditService auditService, IExportService exportService)
        {
            _dbContext = dbContext;
            _authorizationService = authorizationService;
            _auditService = auditService;
            _exportService = exportService;
        }

        public async Task InitializeAsync()
        {
            await BackfillIdempotencyKeyAsync();
            await CreateIdempotencyIndexAsync();
        }

        public async Task<bool> SaveProductionRecordAsync(ProductionRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.BatchNo))
            {
                return false;
            }

            record.BatchNo = record.BatchNo.Trim();
            record.Time = record.Time == default ? DateTime.Now : record.Time;
            record.IdempotencyKey = BuildIdempotencyKey(record);

            for (var retry = 1; retry <= 3; retry++)
            {
                try
                {
                    if (await ExistsAsync(record.IdempotencyKey))
                    {
                        LogService.Info($"生产记录重复写入已按幂等处理：{record.BatchNo}");
                        return true;
                    }

                    await _dbContext.Fsql.Insert(record).ExecuteAffrowsAsync();
                    return true;
                }
                catch (Exception ex) when (IsUniqueViolation(ex))
                {
                    LogService.Info($"生产记录命中唯一约束，按幂等成功处理：{record.BatchNo}");
                    return true;
                }
                catch (Exception ex) when (IsTransientDbException(ex) && retry < 3)
                {
                    LogService.Warn($"保存生产记录失败，准备重试：{record.BatchNo}，第 {retry} 次，{ex.Message}");
                    await Task.Delay(200 * retry);
                }
                catch (Exception ex)
                {
                    LogService.Error("保存生产记录失败", ex);
                    return false;
                }
            }

            return false;
        }

        public async Task<List<ProductionRecord>> QueryRecordAsync(DateTime start, DateTime end)
        {
            return await _dbContext.Fsql.Select<ProductionRecord>()
                .Where(r => r.Time >= start && r.Time <= end)
                .OrderByDescending(r => r.Time)
                .ToListAsync();
        }

        public async Task ExportToCsvAsync(List<ProductionRecord> records, string filePath)
        {
            _authorizationService.EnsurePermission(Permission.ExportLogs, "导出生产记录");
            var fullPath = await _exportService.ExportAsync(records ?? new List<ProductionRecord>(), filePath);
            _auditService.Operation("ExportProductionRecords", "Success", $"导出的生产记录文件路径：{fullPath}");
        }

        public string BuildIdempotencyKey(ProductionRecord? record)
        {
            if (record == null)
            {
                return string.Empty;
            }

            var batchNo = NormalizeBatchNo(record.BatchNo);
            var operatorName = NormalizeOperator(record.Operator);
            var cycleTime = Math.Round(record.CycleTime, 2).ToString("F2", CultureInfo.InvariantCulture);
            var secondBucket = record.Time == default
                ? DateTime.Now.ToString("yyyyMMddHHmmss")
                : record.Time.ToString("yyyyMMddHHmmss");

            var signature = $"{batchNo}|{record.ActualCount}|{record.TargetCount}|{cycleTime}|{operatorName}|{secondBucket}";
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(signature));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private async Task<bool> ExistsAsync(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return await _dbContext.Fsql.Select<ProductionRecord>()
                .Where(r => r.IdempotencyKey == key)
                .AnyAsync();
        }

        private async Task BackfillIdempotencyKeyAsync()
        {
            var records = await _dbContext.Fsql.Select<ProductionRecord>()
                .OrderBy(r => r.Id)
                .ToListAsync();
            if (records.Count == 0)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicateIds = new List<long>();

            foreach (var record in records)
            {
                record.IdempotencyKey = BuildIdempotencyKey(record);
                if (string.IsNullOrWhiteSpace(record.IdempotencyKey))
                {
                    continue;
                }

                if (!seen.Add(record.IdempotencyKey))
                {
                    duplicateIds.Add(record.Id);
                }
            }

            foreach (var record in records.Where(r => !duplicateIds.Contains(r.Id)))
            {
                await _dbContext.Fsql.Update<ProductionRecord>()
                    .Where(r => r.Id == record.Id)
                    .Set(r => r.IdempotencyKey, record.IdempotencyKey)
                    .ExecuteAffrowsAsync();
            }

            if (duplicateIds.Count > 0)
            {
                await _dbContext.Fsql.Delete<ProductionRecord>()
                    .Where(r => duplicateIds.Contains(r.Id))
                    .ExecuteAffrowsAsync();
                LogService.Warn($"检测到重复生产记录，已保留首条并清理重复数据：{duplicateIds.Count} 条");
            }
        }

        private async Task CreateIdempotencyIndexAsync()
        {
            const string sql = "CREATE UNIQUE INDEX IF NOT EXISTS idx_unique_production_idempotency_sql ON ProductionRecords(IdempotencyKey) WHERE IdempotencyKey IS NOT NULL AND IdempotencyKey <> '';";
            await _dbContext.Fsql.Ado.ExecuteNonQueryAsync(sql);
        }

        private static string NormalizeBatchNo(string? batchNo)
        {
            return (batchNo ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string NormalizeOperator(string? operatorName)
        {
            return (operatorName ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static bool IsTransientDbException(Exception ex)
        {
            var message = ex.ToString();
            return message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("SQLITE_BUSY", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("busy", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUniqueViolation(Exception ex)
        {
            var message = ex.ToString();
            return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("SQLITE_CONSTRAINT", StringComparison.OrdinalIgnoreCase);
        }
    }
}
