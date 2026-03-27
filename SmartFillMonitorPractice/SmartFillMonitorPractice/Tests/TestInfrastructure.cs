using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SmartFillMonitorPractice.Tests;

internal sealed class TestAppScope : IDisposable
{
    public string DbPath { get; }

    public IAppDbContext DbContext { get; }

    public IUserSessionService Session { get; }

    public IAuditService AuditService { get; }

    public IAuthorizationService AuthorizationService { get; }

    public IUserService UserService { get; }

    public IAlarmService AlarmService { get; }

    public IDataService DataService { get; }

    public IConfigService ConfigService { get; }

    public TestAppScope()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"smartfill-practice-test-{Guid.NewGuid():N}.db");

        var dbContext = new AppDbContext();
        dbContext.Initialize($"Data Source={DbPath}");
        DbContext = dbContext;

        Session = new UserSessionService();
        AuditService = new AuditService(Session);
        AuthorizationService = new AuthorizationService(Session, AuditService);
        var exportService = new CsvExportService();

        UserService = new UserService(DbContext, Session, AuthorizationService, AuditService);
        AlarmService = new AlarmService(DbContext, Session, AuthorizationService, AuditService);
        DataService = new DataService(DbContext, AuthorizationService, AuditService, exportService);
        ConfigService = new ConfigService(AuthorizationService, AuditService);

        AlarmService.InitializeAsync().GetAwaiter().GetResult();
        DataService.InitializeAsync().GetAwaiter().GetResult();
    }

    public void SetCurrentUser(User user)
    {
        Session.SetCurrentUser(user);
    }

    public User GetUser(string userName)
    {
        return DbContext.Fsql.Select<User>().Where(x => x.UserName == userName).First()!;
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(DbPath))
            {
                File.Delete(DbPath);
            }
        }
        catch
        {
        }
    }
}

internal static class StaTestHelper
{
    public static Task RunAsync(Action action)
    {
        var tcs = new TaskCompletionSource<object?>();
        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }
}
