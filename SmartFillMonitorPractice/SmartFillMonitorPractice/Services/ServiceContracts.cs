using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using SmartFillMonitorPractice.Models;

namespace SmartFillMonitorPractice.Services
{
    public interface IAppDbContext
    {
        IFreeSql Fsql { get; }

        void Initialize(string connectionString, DataType dataType = DataType.Sqlite);
    }

    public interface IUserSessionService
    {
        event Action<User?>? LoginStateChanged;

        User? CurrentUser { get; }

        void SetCurrentUser(User? user);

        string GetCurrentUserName();

        string GetCurrentUserDisplayName();
    }

    public interface IAuditService
    {
        void Security(string action, string result, string detail, string? userName = null);

        void Operation(string action, string result, string detail, string? userName = null);
    }

    public interface IAuthorizationService
    {
        bool HasPermission(User? user, Permission permission);

        void EnsurePermission(Permission permission, string action);
    }

    public interface IExportService
    {
        Task<string> ExportAsync<T>(IEnumerable<T> records, string filePath);
    }

    public interface IUserService
    {
        event Action<User?>? LoginStateChanged;

        string LastErrorMessage { get; }

        User? CurrentUser { get; }

        bool IsAdministrator(User? user);

        bool IsEngineer(User? user);

        string GetCurrentUserName();

        string GetCurrentUserDisplayName();

        Task InitializeAsync();

        Task<bool> HasAnyUserAsync();

        Task<bool> CanRegisterAdminPubliclyAsync();

        Task RegisterPublicUserAsync(string userName, string password, Role role, string displayName = "");

        Task CreateUserByAdminAsync(string userName, string password, Role role, string displayName = "", bool requirePasswordChange = true);

        Task<bool> AuthenticateAsync(string userName, string password);

        Task ChangeCurrentUserPasswordAsync(string newPassword);

        Task ChangePasswordWithCurrentPasswordAsync(string userName, string currentPassword, string newPassword);

        Task ResetPasswordAsync(string userName, string newPassword, bool requirePasswordChange = true, bool skipAuthorization = false, string? actor = null);

        Task TryApplyDevelopmentResetAsync();

        Task LogoutAsync();

        Task<List<User>> GetAllUsersAsync();

        Task<List<User>> GetLoginUsersAsync();

        string NormalizePasswordInput(string? password);
    }

    public interface IAlarmService
    {
        event EventHandler<AlarmRecord>? AlarmTriggered;

        event EventHandler<AlarmRecord>? AlarmAcknowledged;

        event EventHandler<AlarmRecord>? AlarmRecovered;

        Task InitializeAsync();

        Task TriggerAlarmAsync(AlarmRecord alarmRecord);

        Task TriggerTestAlarmAsync();

        Task<bool> AcknowledgeAlarmAsync(long alarmId, string operatorName, string processSuggestion = "");

        Task<bool> RecoverAlarmAsync(AlarmCode alarmCode);

        Task<bool> RecoverAlarmAsync(long alarmId, string operatorName = "");

        Task<bool> RecoverTestAlarmAsync();

        Task<List<AlarmRecord>> GetActiveAlarmsAsync();

        Task<(List<AlarmRecord> Item, long Total)> GetAlarmHistoryAsync(int pageIndex, int pageSize, DateTime? startTime = null, DateTime? endTime = null, AlarmSeverity alarmSeverity = AlarmSeverity.All);
    }

    public interface IDataService
    {
        Task InitializeAsync();

        Task<bool> SaveProductionRecordAsync(ProductionRecord record);

        Task<List<ProductionRecord>> QueryRecordAsync(DateTime start, DateTime end);

        Task ExportToCsvAsync(List<ProductionRecord> records, string filePath);

        string BuildIdempotencyKey(ProductionRecord? record);
    }

    public interface ISystemLogService
    {
        ISelect<SystemLog> BuildQuery(SystemLogQueryFilter filter);

        Task<(List<SystemLog> Items, long Total)> QueryAsync(SystemLogQueryFilter filter, int pageIndex, int pageSize);

        Task<string> ExportAsync(SystemLogQueryFilter filter, string filePath);
    }

    public interface IConfigService
    {
        string GetSettingFilePath();

        Task<DeviceSettings> LoadSettingsAsync();

        Task<bool> SaveDeviceSettingsAsync(DeviceSettings settings, bool requireAuthorization = true);

        void BackCorruptFile(string originalPath);

        void Validate(DeviceSettings settings);
    }

    public interface IPlcTransport : IDisposable
    {
        bool IsConnected { get; }

        Task ConnectAsync(DeviceSettings settings, CancellationToken cancellationToken = default);

        Task DisconnectAsync();

        Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken = default);

        Task WriteSingleCoilAsync(byte slaveId, ushort address, bool value, CancellationToken cancellationToken = default);

        Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] values, CancellationToken cancellationToken = default);
    }

    public interface IDeviceStateMapper
    {
        DeviceState Map(ushort[] registers, string barcode);
    }

    public interface IPlcService
    {
        event EventHandler<DeviceState>? DataReceived;

        event EventHandler<bool>? ConnectionChanged;

        bool IsConnected { get; }

        bool HasSuccessfulRead { get; }

        DateTime? LastReadSuccessTime { get; }

        DeviceState? LastDeviceState { get; }

        Task InitializeAsync(DeviceSettings settings);

        Task ConnectAsync();

        Task DisconnectAsync();

        Task<DeviceState> ReadStateAsync(CancellationToken token);

        Task<bool> PulseCommandAsync(string command, int delayMs = 120);

        Task<bool> WriteCommandAsync(string command, bool value);

        Task<bool> WriteHoldingRegisterAsync(ushort address, ushort value);

        Task<bool> WriteHoldingRegistersAsync(ushort startAddress, ushort[] values);

        Task<bool> WriteAsciiStringToHoldingRegistersAsync(ushort startAddress, string text, int registerLength);

        string[] GetAvailablePorts();
    }

    public interface INavigationService
    {
        event Action<object?>? CurrentContentChanged;

        object? CurrentContent { get; }

        object Navigate(string destination);

        void SetCurrentContent(object? content);
    }

    public interface IHeaderStateService : INotifyPropertyChanged
    {
        string CurrentBatchNo { get; }

        string CurrentTime { get; }

        bool IsPlcConnected { get; }

        LightState IndicatorState { get; }

        void Activate(object? mainContent);

        void Stop();
    }
}
