using SmartFillMonitorPractice.Models;
using Xunit;

namespace SmartFillMonitorPractice.Tests;

public class DataServiceTests
{
    [Fact]
    public void BuildIdempotencyKey_ReturnsSameKey_ForSameBusinessSignature()
    {
        using var scope = new TestAppScope();
        var time = new DateTime(2026, 3, 26, 8, 30, 15);

        var record1 = new ProductionRecord
        {
            Time = time,
            BatchNo = "BATCH-001",
            ActualCount = 10,
            TargetCount = 100,
            CycleTime = 1.25,
            Operator = "admin"
        };

        var record2 = new ProductionRecord
        {
            Time = time,
            BatchNo = "BATCH-001",
            ActualCount = 10,
            TargetCount = 100,
            CycleTime = 1.25,
            Operator = "admin"
        };

        Assert.Equal(scope.DataService.BuildIdempotencyKey(record1), scope.DataService.BuildIdempotencyKey(record2));
    }

    [Fact]
    public void BuildIdempotencyKey_ReturnsDifferentKey_WhenBusinessSignatureChanges()
    {
        using var scope = new TestAppScope();
        var time = new DateTime(2026, 3, 26, 8, 30, 15);

        var record1 = new ProductionRecord
        {
            Time = time,
            BatchNo = "BATCH-001",
            ActualCount = 10,
            TargetCount = 100,
            CycleTime = 1.25,
            Operator = "admin"
        };

        var record2 = new ProductionRecord
        {
            Time = time,
            BatchNo = "BATCH-001",
            ActualCount = 11,
            TargetCount = 100,
            CycleTime = 1.25,
            Operator = "admin"
        };

        Assert.NotEqual(scope.DataService.BuildIdempotencyKey(record1), scope.DataService.BuildIdempotencyKey(record2));
    }

    [Fact]
    public async Task SaveProductionRecordAsync_IsIdempotent_ForSameKey()
    {
        using var scope = new TestAppScope();
        var record = new ProductionRecord
        {
            Time = new DateTime(2026, 3, 26, 8, 30, 15),
            BatchNo = "BATCH-001",
            ActualCount = 10,
            TargetCount = 100,
            CycleTime = 1.25,
            Operator = "admin"
        };

        await scope.DataService.SaveProductionRecordAsync(record);
        await scope.DataService.SaveProductionRecordAsync(new ProductionRecord
        {
            Time = record.Time,
            BatchNo = record.BatchNo,
            ActualCount = record.ActualCount,
            TargetCount = record.TargetCount,
            CycleTime = record.CycleTime,
            Operator = record.Operator
        });

        var count = scope.DbContext.Fsql.Select<ProductionRecord>().Count();
        Assert.Equal(1, count);
    }
}
