using System;
using FreeSql;

namespace SmartFillMonitorPractice.Services
{
    public class AppDbContext : IAppDbContext
    {
        private readonly object _lock = new();

        public IFreeSql Fsql { get; private set; } = null!;

        public void Initialize(string connectionString, DataType dataType = DataType.Sqlite)
        {
            if (Fsql != null)
            {
                return;
            }

            lock (_lock)
            {
                if (Fsql != null)
                {
                    return;
                }

                Fsql = new FreeSqlBuilder()
                    .UseConnectionString(dataType, connectionString)
                    .UseAdoConnectionPool(true)
                    .UseMonitorCommand(
                        _ => { },
                        (cmd, traceLog) =>
                        {
                            Console.WriteLine($"[SQL]: {cmd.CommandText}\r\n->{traceLog}");
                        })
                    .UseAutoSyncStructure(true)
                    .UseLazyLoading(true)
                    .Build();
            }
        }
    }
}
