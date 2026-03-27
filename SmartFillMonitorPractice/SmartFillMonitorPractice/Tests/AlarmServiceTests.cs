using System.Linq;
using SmartFillMonitorPractice.Models;
using Xunit;

namespace SmartFillMonitorPractice.Tests;

public class AlarmServiceTests
{
    [Fact]
    public async Task TriggerAlarmAsync_DoesNotCreateDuplicateActiveAlarm()
    {
        using var scope = new TestAppScope();

        var record = new AlarmRecord
        {
            AlarmCode = AlarmCode.TestAlarm,
            AlarmSeverity = AlarmSeverity.Warning,
            Description = "测试报警",
            TriggeredBy = "PLC",
            TriggeredByType = AlarmTriggeredByType.Plc,
            StartTime = DateTime.Now
        };

        await Task.WhenAll(
            scope.AlarmService.TriggerAlarmAsync(record),
            scope.AlarmService.TriggerAlarmAsync(new AlarmRecord
            {
                AlarmCode = AlarmCode.TestAlarm,
                AlarmSeverity = AlarmSeverity.Warning,
                Description = "测试报警",
                TriggeredBy = "PLC",
                TriggeredByType = AlarmTriggeredByType.Plc,
                StartTime = DateTime.Now
            }));

        var active = await scope.AlarmService.GetActiveAlarmsAsync();
        var saved = Assert.Single(active, x => x.AlarmCode == AlarmCode.TestAlarm);
        Assert.Equal(AlarmCode.TestAlarm, saved.AlarmCode);
    }

    [Fact]
    public async Task AcknowledgeAndRecoverAlarmAsync_PersistsResponsibleUser()
    {
        using var scope = new TestAppScope();
        await scope.UserService.RegisterPublicUserAsync("admin", "StrongPass123", Role.Admin, "管理员");
        var admin = scope.GetUser("admin");
        scope.SetCurrentUser(admin);

        await scope.AlarmService.TriggerAlarmAsync(new AlarmRecord
        {
            AlarmCode = AlarmCode.TestAlarm,
            AlarmSeverity = AlarmSeverity.Warning,
            Description = "测试报警",
            TriggeredBy = "管理员",
            TriggeredByType = AlarmTriggeredByType.User,
            StartTime = DateTime.Now
        });

        var active = (await scope.AlarmService.GetActiveAlarmsAsync()).Single();
        await scope.AlarmService.AcknowledgeAlarmAsync(active.Id, admin.DisplayNameOrUserName, "已确认");
        await scope.AlarmService.RecoverAlarmAsync(active.Id, admin.DisplayNameOrUserName);

        var history = await scope.AlarmService.GetAlarmHistoryAsync(1, 10);
        var saved = history.Item.Single();

        Assert.Equal(admin.Id, saved.AckUserId);
        Assert.Equal(admin.DisplayNameOrUserName, saved.AckUserName);
        Assert.Equal(admin.Id, saved.RecoverUserId);
        Assert.Equal(admin.DisplayNameOrUserName, saved.RecoverUserName);
    }
}
