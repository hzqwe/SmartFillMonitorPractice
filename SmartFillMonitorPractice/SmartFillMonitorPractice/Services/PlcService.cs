using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SmartFillMonitorPractice.Models;

namespace SmartFillMonitorPractice.Services
{
    public class PlcService : IPlcService, IDisposable, IAsyncDisposable
    {
        private const byte SlaveId = 1;
        private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(3);
        private static readonly Dictionary<string, ushort> CommandAddressMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Start", 1 },
            { "Stop", 2 },
            { "Reset", 3 },
            { "Test", 4 }
        };

        private readonly IPlcTransport _transport;
        private readonly IDeviceStateMapper _deviceStateMapper;
        private readonly IAuthorizationService _authorizationService;
        private readonly IAuditService _auditService;
        private readonly object _pollSyncRoot = new();
        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        private CancellationTokenSource? _pollCancellationTokenSource;
        private Task? _pollTask;
        private DeviceSettings? _currentSettings;
        private DateTime _lastReconnectTime = DateTime.MinValue;
        private bool _manualDisconnect = true;
        private bool _disposed;
        private bool _hasPublishedConnectionState;
        private bool _lastPublishedConnectionState;

        public PlcService(IPlcTransport transport, IDeviceStateMapper deviceStateMapper, IAuthorizationService authorizationService, IAuditService auditService)
        {
            _transport = transport;
            _deviceStateMapper = deviceStateMapper;
            _authorizationService = authorizationService;
            _auditService = auditService;
        }

        public event EventHandler<DeviceState>? DataReceived;

        public event EventHandler<bool>? ConnectionChanged;

        public bool IsConnected => _transport.IsConnected;

        public bool HasSuccessfulRead { get; private set; }

        public DateTime? LastReadSuccessTime { get; private set; }

        public DeviceState? LastDeviceState { get; private set; }

        public async Task InitializeAsync(DeviceSettings settings)
        {
            ThrowIfDisposed();

            _currentSettings = settings;
            LogService.Info($"收到 PLC 服务初始化请求。AutoConnect={settings.AutoConnect}，Port={settings.PortName}");
            await DisconnectAsync();

            if (settings.AutoConnect)
            {
                await ConnectAsync();
            }
        }

        public async Task ConnectAsync()
        {
            ThrowIfDisposed();

            await _connectionLock.WaitAsync();
            try
            {
                if (_currentSettings == null)
                {
                    LogService.Warn("PLC 连接已跳过，因为尚未完成配置初始化。");
                    return;
                }

                _manualDisconnect = false;

                if (!IsConnected)
                {
                    try
                    {
                        await _transport.ConnectAsync(_currentSettings);
                        _lastReconnectTime = DateTime.Now;
                        LogService.Info($"PLC 传输层已连接。端口={_currentSettings.PortName}");
                    }
                    catch (Exception ex)
                    {
                        ResetReadState();
                        PublishConnectionChanged(false);
                        LogService.Error($"PLC 传输层连接失败。端口={_currentSettings.PortName}", ex);
                        return;
                    }
                }

                EnsurePollingLoopStarted();
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            if (_disposed)
            {
                return;
            }

            _manualDisconnect = true;
            LogService.Info("收到 PLC 断开连接请求。");

            var pollTask = CancelPollingLoop();
            if (pollTask != null)
            {
                var completedTask = await Task.WhenAny(pollTask, Task.Delay(1500));
                if (completedTask != pollTask)
                {
                    LogService.Warn("PLC 轮询未能立即停止，当前关闭速度仍受串口读取超时限制。");
                }
            }

            await _connectionLock.WaitAsync();
            try
            {
                await _transport.DisconnectAsync();
                ResetReadState(clearLastReconnectTime: true);
                PublishConnectionChanged(false);
                LogService.Info("PLC 传输层已断开。");
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async Task<DeviceState> ReadStateAsync(CancellationToken token)
        {
            ThrowIfDisposed();

            if (!IsConnected)
            {
                throw new InvalidOperationException("PLC 未连接。");
            }

            var registers = await _transport.ReadHoldingRegistersAsync(SlaveId, 0, 10, token);
            string barcode = string.Empty;

            try
            {
                var barcodeRegisters = await _transport.ReadHoldingRegistersAsync(SlaveId, 10, 10, token);
                barcode = ConvertRegistersToString(barcodeRegisters);
            }
            catch (Exception ex)
            {
                LogService.Warn($"读取 PLC 条码失败：{ex.Message}");
            }

            return _deviceStateMapper.Map(registers, barcode);
        }

        public async Task<bool> PulseCommandAsync(string command, int delayMs = 120)
        {
            var setHigh = await WriteCommandAsync(command, true);
            if (!setHigh)
            {
                return false;
            }

            await Task.Delay(delayMs);
            return await WriteCommandAsync(command, false);
        }

        public async Task<bool> WriteCommandAsync(string command, bool value)
        {
            ThrowIfDisposed();

            _authorizationService.EnsurePermission(Permission.ControlPlc, $"PLC 命令写入：{command}");

            if (!CommandAddressMap.TryGetValue(command, out var address))
            {
                LogService.Warn($"PLC 命令地址映射缺失。命令={command}");
                return false;
            }

            try
            {
                if (!IsConnected)
                {
                    LogService.Warn($"PLC 命令已忽略，因为传输层未连接。命令={command}，值={value}");
                    return false;
                }

                await _transport.WriteSingleCoilAsync(SlaveId, address, value);
                LogService.Info($"PLC 命令写入成功。命令={command}，值={value}，地址={address}");
                _auditService.Operation("PlcWriteCommand", "Success", $"PLC 命令写入成功：命令={command}；值={value}；地址={address}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Error($"PLC 命令写入失败。命令={command}，值={value}", ex);
                _auditService.Operation("PlcWriteCommand", "Failed", $"PLC 命令写入失败：命令={command}；值={value}；错误={ex.Message}");
                return false;
            }
        }

        public Task<bool> WriteHoldingRegisterAsync(ushort address, ushort value)
        {
            return WriteHoldingRegistersAsync(address, new[] { value });
        }

        public async Task<bool> WriteHoldingRegistersAsync(ushort startAddress, ushort[] values)
        {
            ThrowIfDisposed();

            _authorizationService.EnsurePermission(Permission.ControlPlc, $"PLC 保持寄存器写入：{startAddress}");

            if (values == null || values.Length == 0)
            {
                return false;
            }

            try
            {
                if (!IsConnected)
                {
                    LogService.Warn($"PLC 保持寄存器写入已忽略，因为传输层未连接。起始地址={startAddress}，长度={values.Length}");
                    return false;
                }

                await _transport.WriteMultipleRegistersAsync(SlaveId, startAddress, values);
                _auditService.Operation("PlcWriteHoldingRegisters", "Success", $"PLC 保持寄存器写入成功：起始地址={startAddress}；长度={values.Length}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Warn($"PLC 保持寄存器写入失败。起始地址={startAddress}，长度={values.Length}，错误={ex.Message}");
                _auditService.Operation("PlcWriteHoldingRegisters", "Failed", $"PLC 保持寄存器写入失败：起始地址={startAddress}；长度={values.Length}；错误={ex.Message}");
                return false;
            }
        }

        public async Task<bool> WriteAsciiStringToHoldingRegistersAsync(ushort startAddress, string text, int registerLength)
        {
            ThrowIfDisposed();

            if (registerLength <= 0)
            {
                return false;
            }

            text ??= string.Empty;
            var maxCharCount = registerLength * 2;
            if (text.Length > maxCharCount)
            {
                text = text.Substring(0, maxCharCount);
            }

            var bytes = Encoding.ASCII.GetBytes(text);
            var values = new ushort[registerLength];
            var byteIndex = 0;

            for (var i = 0; i < registerLength; i++)
            {
                var high = byteIndex < bytes.Length ? bytes[byteIndex++] : (byte)0;
                var low = byteIndex < bytes.Length ? bytes[byteIndex++] : (byte)0;
                values[i] = (ushort)((high << 8) | low);
            }

            return await WriteHoldingRegistersAsync(startAddress, values);
        }

        public string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await DisconnectAsync();
            }
            catch (Exception ex)
            {
                LogService.Error("释放 PLC 服务时断开连接失败。", ex);
            }
            finally
            {
                _disposed = true;
            }

            _connectionLock.Dispose();
        }

        private void EnsurePollingLoopStarted()
        {
            lock (_pollSyncRoot)
            {
                if (_pollCancellationTokenSource != null &&
                    !_pollCancellationTokenSource.IsCancellationRequested &&
                    _pollTask != null &&
                    !_pollTask.IsCompleted)
                {
                    return;
                }

                _pollCancellationTokenSource?.Dispose();
                _pollCancellationTokenSource = new CancellationTokenSource();
                _pollTask = Task.Run(() => PollDataLoop(_pollCancellationTokenSource.Token));
                LogService.Info("PLC 轮询已启动。");
            }
        }

        private Task? CancelPollingLoop()
        {
            lock (_pollSyncRoot)
            {
                if (_pollCancellationTokenSource == null && _pollTask == null)
                {
                    return null;
                }

                if (_pollCancellationTokenSource != null && !_pollCancellationTokenSource.IsCancellationRequested)
                {
                    _pollCancellationTokenSource.Cancel();
                }

                var task = _pollTask;
                _pollCancellationTokenSource?.Dispose();
                _pollCancellationTokenSource = null;
                _pollTask = null;
                return task;
            }
        }

        private void UpdateReadStateOnSuccess(DeviceState state)
        {
            HasSuccessfulRead = true;
            LastReadSuccessTime = DateTime.Now;
            LastDeviceState = state;
        }

        private void ResetReadState(bool clearLastReconnectTime = false)
        {
            HasSuccessfulRead = false;
            LastReadSuccessTime = null;
            LastDeviceState = null;

            if (clearLastReconnectTime)
            {
                _lastReconnectTime = DateTime.MinValue;
            }
        }

        private void PublishConnectionChanged(bool connected)
        {
            if (!_hasPublishedConnectionState)
            {
                _hasPublishedConnectionState = true;
                _lastPublishedConnectionState = connected;

                if (!connected)
                {
                    return;
                }
            }
            else if (_lastPublishedConnectionState == connected)
            {
                return;
            }

            _lastPublishedConnectionState = connected;
            ConnectionChanged?.Invoke(this, connected);
        }

        private async Task PollDataLoop(CancellationToken token)
        {
            var errorCount = 0;
            LogService.Debug("PLC 轮询循环已进入。");

            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!IsConnected)
                        {
                            await TryReconnectAsync(token);
                            await Task.Delay(500, token);
                            continue;
                        }

                        var state = await ReadStateAsync(token);
                        if (token.IsCancellationRequested || _manualDisconnect || !IsConnected)
                        {
                            break;
                        }

                        UpdateReadStateOnSuccess(state);
                        PublishConnectionChanged(true);

                        errorCount = 0;
                        DataReceived?.Invoke(this, state);
                        await Task.Delay(200, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;

                        if (HasSuccessfulRead)
                        {
                            ResetReadState();
                            PublishConnectionChanged(false);
                        }

                        LogService.Warn($"PLC 轮询发生异常。次数={errorCount}，ManualDisconnect={_manualDisconnect}，消息={ex.Message}");

                        if (errorCount >= 3)
                        {
                            try
                            {
                                await _transport.DisconnectAsync();
                            }
                            catch (Exception disconnectEx)
                            {
                                LogService.Error("轮询异常次数达到阈值后断开 PLC 传输层失败。", disconnectEx);
                            }

                            errorCount = 0;
                        }

                        await Task.Delay(1000, token);
                    }
                }
            }
            finally
            {
                LogService.Debug("PLC 轮询循环已退出。");
            }
        }

        private async Task TryReconnectAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested || _manualDisconnect || _disposed)
            {
                return;
            }

            if (_currentSettings is not { AutoConnect: true })
            {
                return;
            }

            if (DateTime.Now - _lastReconnectTime < ReconnectInterval)
            {
                return;
            }

            _lastReconnectTime = DateTime.Now;
            LogService.Warn("开始尝试重新连接 PLC。");
            await ConnectAsync();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PlcService));
            }
        }

        private static string ConvertRegistersToString(ushort[] registers)
        {
            if (registers == null || registers.Length == 0)
            {
                return string.Empty;
            }

            var bytes = new List<byte>();
            foreach (var register in registers)
            {
                if (register == 0)
                {
                    break;
                }

                var high = (byte)(register >> 8);
                var low = (byte)(register & 0xFF);

                if (high != 0)
                {
                    bytes.Add(high);
                }

                if (low != 0)
                {
                    bytes.Add(low);
                }
            }

            return Encoding.ASCII.GetString(bytes.ToArray()).Trim();
        }
    }
}
