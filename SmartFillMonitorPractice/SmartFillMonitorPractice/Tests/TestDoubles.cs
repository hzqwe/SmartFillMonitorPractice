using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;

namespace SmartFillMonitorPractice.Tests;

internal sealed class FakePlcService : IPlcService
{
    public event EventHandler<DeviceState>? DataReceived;
    public event EventHandler<bool>? ConnectionChanged;

    public bool IsConnected { get; set; }
    public bool HasSuccessfulRead { get; set; }
    public DateTime? LastReadSuccessTime { get; set; }
    public DeviceState? LastDeviceState { get; set; }

    public int InitializeCallCount { get; private set; }
    public int ConnectCallCount { get; private set; }
    public int DisconnectCallCount { get; private set; }

    public Task InitializeAsync(DeviceSettings settings)
    {
        InitializeCallCount++;
        return Task.CompletedTask;
    }

    public Task ConnectAsync()
    {
        ConnectCallCount++;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        DisconnectCallCount++;
        return Task.CompletedTask;
    }

    public Task<DeviceState> ReadStateAsync(CancellationToken token)
    {
        return Task.FromResult(LastDeviceState ?? new DeviceState());
    }

    public Task<bool> PulseCommandAsync(string command, int delayMs = 120)
    {
        return Task.FromResult(true);
    }

    public Task<bool> WriteCommandAsync(string command, bool value)
    {
        return Task.FromResult(IsConnected);
    }

    public Task<bool> WriteHoldingRegisterAsync(ushort address, ushort value)
    {
        return Task.FromResult(IsConnected);
    }

    public Task<bool> WriteHoldingRegistersAsync(ushort startAddress, ushort[] values)
    {
        return Task.FromResult(IsConnected);
    }

    public Task<bool> WriteAsciiStringToHoldingRegistersAsync(ushort startAddress, string text, int registerLength)
    {
        return Task.FromResult(IsConnected);
    }

    public string[] GetAvailablePorts()
    {
        return new[] { "COM1" };
    }

    public void RaiseConnectionChanged(bool connected)
    {
        IsConnected = connected;
        ConnectionChanged?.Invoke(this, connected);
    }

    public void RaiseDataReceived(DeviceState state)
    {
        LastDeviceState = state;
        LastReadSuccessTime = DateTime.Now;
        HasSuccessfulRead = true;
        DataReceived?.Invoke(this, state);
    }
}

internal sealed class FakeAlarmService : IAlarmService
{
#pragma warning disable CS0067
    public event EventHandler<AlarmRecord>? AlarmTriggered;
    public event EventHandler<AlarmRecord>? AlarmAcknowledged;
    public event EventHandler<AlarmRecord>? AlarmRecovered;
#pragma warning restore CS0067

    public List<AlarmRecord> ActiveAlarms { get; } = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task TriggerAlarmAsync(AlarmRecord alarmRecord)
    {
        ActiveAlarms.Add(alarmRecord);
        AlarmTriggered?.Invoke(this, alarmRecord);
        return Task.CompletedTask;
    }

    public Task TriggerTestAlarmAsync() => Task.CompletedTask;

    public Task<bool> AcknowledgeAlarmAsync(long alarmId, string operatorName, string processSuggestion = "") => Task.FromResult(true);

    public Task<bool> RecoverAlarmAsync(AlarmCode alarmCode) => Task.FromResult(true);

    public Task<bool> RecoverAlarmAsync(long alarmId, string operatorName = "") => Task.FromResult(true);

    public Task<bool> RecoverTestAlarmAsync() => Task.FromResult(true);

    public Task<List<AlarmRecord>> GetActiveAlarmsAsync() => Task.FromResult(ActiveAlarms.ToList());

    public Task<(List<AlarmRecord> Item, long Total)> GetAlarmHistoryAsync(int pageIndex, int pageSize, DateTime? startTime = null, DateTime? endTime = null, AlarmSeverity alarmSeverity = AlarmSeverity.All)
    {
        return Task.FromResult((new List<AlarmRecord>(), 0L));
    }
}

internal sealed class FakeDataService : IDataService
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task<bool> SaveProductionRecordAsync(ProductionRecord record) => Task.FromResult(true);

    public Task<List<ProductionRecord>> QueryRecordAsync(DateTime start, DateTime end) => Task.FromResult(new List<ProductionRecord>());

    public Task ExportToCsvAsync(List<ProductionRecord> records, string filePath) => Task.CompletedTask;

    public string BuildIdempotencyKey(ProductionRecord? record) => record?.BatchNo ?? string.Empty;
}

internal sealed class FakeUserService : IUserService
{
    public event Action<User?>? LoginStateChanged;

    public string LastErrorMessage { get; private set; } = string.Empty;

    public User? CurrentUser { get; private set; }

    public bool IsAdministrator(User? user) => user?.Role == Role.Admin;

    public bool IsEngineer(User? user) => user?.Role == Role.Engineer;

    public string GetCurrentUserName() => CurrentUser?.UserName ?? string.Empty;

    public string GetCurrentUserDisplayName() => CurrentUser?.DisplayNameOrUserName ?? string.Empty;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<bool> HasAnyUserAsync() => Task.FromResult(CurrentUser != null);

    public Task<bool> CanRegisterAdminPubliclyAsync() => Task.FromResult(CurrentUser == null);

    public Task RegisterPublicUserAsync(string userName, string password, Role role, string displayName = "") => Task.CompletedTask;

    public Task CreateUserByAdminAsync(string userName, string password, Role role, string displayName = "", bool requirePasswordChange = true) => Task.CompletedTask;

    public Task<bool> AuthenticateAsync(string userName, string password) => Task.FromResult(true);

    public Task ChangeCurrentUserPasswordAsync(string newPassword) => Task.CompletedTask;

    public Task ChangePasswordWithCurrentPasswordAsync(string userName, string currentPassword, string newPassword) => Task.CompletedTask;

    public Task ResetPasswordAsync(string userName, string newPassword, bool requirePasswordChange = true, bool skipAuthorization = false, string? actor = null) => Task.CompletedTask;

    public Task TryApplyDevelopmentResetAsync() => Task.CompletedTask;

    public Task LogoutAsync()
    {
        SetCurrentUser(null);
        return Task.CompletedTask;
    }

    public Task<List<User>> GetAllUsersAsync() => Task.FromResult(new List<User>());

    public Task<List<User>> GetLoginUsersAsync() => Task.FromResult(new List<User>());

    public string NormalizePasswordInput(string? password) => password ?? string.Empty;

    public void SetCurrentUser(User? user)
    {
        CurrentUser = user;
        LoginStateChanged?.Invoke(user);
    }
}

internal sealed class FakeHeaderStateService : IHeaderStateService
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentBatchNo { get; set; } = string.Empty;
    public string CurrentTime { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public bool IsPlcConnected { get; set; }
    public LightState IndicatorState { get; set; } = LightState.Red;
    public int ActivateCount { get; private set; }
    public int StopCount { get; private set; }
    public object? LastActivatedContent { get; private set; }

    public void Activate(object? mainContent)
    {
        ActivateCount++;
        LastActivatedContent = mainContent;
    }

    public void Stop()
    {
        StopCount++;
    }

    public void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class FakeNavigationService : INavigationService
{
    public event Action<object?>? CurrentContentChanged;

    public object? CurrentContent { get; private set; }

    public object Navigate(string destination)
    {
        CurrentContent = destination;
        CurrentContentChanged?.Invoke(CurrentContent);
        return CurrentContent;
    }

    public void SetCurrentContent(object? content)
    {
        CurrentContent = content;
        CurrentContentChanged?.Invoke(CurrentContent);
    }
}

internal sealed class FakeSystemLogService : ISystemLogService
{
    public ISelect<SystemLog> BuildQuery(SystemLogQueryFilter filter)
    {
        throw new NotSupportedException();
    }

    public Task<(List<SystemLog> Items, long Total)> QueryAsync(SystemLogQueryFilter filter, int pageIndex, int pageSize)
    {
        return Task.FromResult((new List<SystemLog>(), 0L));
    }

    public Task<string> ExportAsync(SystemLogQueryFilter filter, string filePath)
    {
        return Task.FromResult(filePath);
    }
}

internal sealed class FakeConfigService : IConfigService
{
    public string GetSettingFilePath() => string.Empty;
    public Task<DeviceSettings> LoadSettingsAsync() => Task.FromResult(new DeviceSettings { PortName = "COM1", BaudRate = 9600, DataBits = 8, Parity = "None", StopBits = "One", AutoConnect = false });
    public Task<bool> SaveDeviceSettingsAsync(DeviceSettings settings, bool requireAuthorization = true) => Task.FromResult(true);
    public void BackCorruptFile(string originalPath) { }
    public void Validate(DeviceSettings settings) { }
}

internal sealed class FakeAuthorizationService : IAuthorizationService
{
    public bool HasPermission(User? user, Permission permission) => true;
    public void EnsurePermission(Permission permission, string action) { }
}

internal sealed class FakePlcTransport : IPlcTransport
{
    private readonly ushort[] _registers;
    private readonly ushort[] _barcodeRegisters;

    public FakePlcTransport(ushort[]? registers = null, string barcode = "BATCH-001")
    {
        _registers = registers ?? new ushort[] { 10, 100, 2500, 3000, 1, 12, 15, 500, 1, 2 };
        _barcodeRegisters = EncodeBarcode(barcode);
    }

    public bool IsConnected { get; private set; }

    public int ConnectCount { get; private set; }

    public int DisconnectCount { get; private set; }

    public int ReadCount { get; private set; }

    public Task ConnectAsync(DeviceSettings settings, CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        DisconnectCount++;
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken = default)
    {
        ReadCount++;
        if (!IsConnected)
        {
            throw new InvalidOperationException("Disconnected");
        }

        return Task.FromResult(startAddress == 0 ? _registers : _barcodeRegisters);
    }

    public Task WriteSingleCoilAsync(byte slaveId, ushort address, bool value, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Disconnected");
        }

        return Task.CompletedTask;
    }

    public Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] values, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Disconnected");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        IsConnected = false;
    }

    private static ushort[] EncodeBarcode(string barcode)
    {
        var text = barcode ?? string.Empty;
        var chars = text.ToCharArray();
        var values = new List<ushort>();
        for (var i = 0; i < chars.Length; i += 2)
        {
            var high = (byte)chars[i];
            var low = i + 1 < chars.Length ? (byte)chars[i + 1] : (byte)0;
            values.Add((ushort)((high << 8) | low));
        }

        while (values.Count < 10)
        {
            values.Add(0);
        }

        return values.ToArray();
    }
}

internal sealed class FakeDeviceStateMapper : IDeviceStateMapper
{
    public DeviceState Map(ushort[] registers, string barcode)
    {
        return new DeviceState
        {
            ActualCount = registers.Length > 0 ? registers[0] : 0,
            TargetCount = registers.Length > 1 ? registers[1] : 0,
            CurrentTemp = registers.Length > 2 ? registers[2] / 100d : 0,
            SettingTemp = registers.Length > 3 ? registers[3] / 100d : 0,
            RunningTime = registers.Length > 4 ? registers[4] : 0,
            CurrentCycleTime = registers.Length > 5 ? registers[5] : 0,
            StandardCycleTime = registers.Length > 6 ? registers[6] : 0,
            LiquidLevel = registers.Length > 7 ? registers[7] / 100d : 0,
            ValveOpen = registers.Length > 8 && registers[8] > 0,
            DeviceStatus = registers.Length > 9 ? registers[9].ToString() : "0",
            BarCode = barcode
        };
    }
}
