using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Modbus.Device;
using SmartFillMonitorPractice.Models;

namespace SmartFillMonitorPractice.Services
{
    public class ModbusRtuTransport : IPlcTransport
    {
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private SerialPort? _serialPort;
        private IModbusMaster? _modbusMaster;
        private bool _disposed;

        public bool IsConnected => !_disposed && _serialPort != null && _serialPort.IsOpen;

        public async Task ConnectAsync(DeviceSettings settings, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _ioLock.WaitAsync(cancellationToken);
            try
            {
                await DisconnectInternalAsync();

                _serialPort = new SerialPort
                {
                    PortName = settings.PortName,
                    BaudRate = settings.BaudRate,
                    DataBits = settings.DataBits,
                    Parity = Enum.TryParse(settings.Parity, true, out Parity parity) ? parity : Parity.None,
                    StopBits = Enum.TryParse(settings.StopBits, true, out StopBits stopBits) ? stopBits : StopBits.One,
                };

                _serialPort.Open();
                _modbusMaster = ModbusSerialMaster.CreateRtu(_serialPort);
                _modbusMaster.Transport.ReadTimeout = 1000;
                _modbusMaster.Transport.WriteTimeout = 1000;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            if (_disposed)
            {
                return;
            }

            await _ioLock.WaitAsync();
            try
            {
                await DisconnectInternalAsync();
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _ioLock.WaitAsync(cancellationToken);
            try
            {
                if (_modbusMaster == null || !IsConnected)
                {
                    throw new InvalidOperationException("PLC 未连接。");
                }

                return await _modbusMaster.ReadHoldingRegistersAsync(slaveId, startAddress, numberOfPoints);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task WriteSingleCoilAsync(byte slaveId, ushort address, bool value, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _ioLock.WaitAsync(cancellationToken);
            try
            {
                if (_modbusMaster == null || !IsConnected)
                {
                    throw new InvalidOperationException("PLC 未连接。");
                }

                await _modbusMaster.WriteSingleCoilAsync(slaveId, address, value);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] values, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _ioLock.WaitAsync(cancellationToken);
            try
            {
                if (_modbusMaster == null || !IsConnected)
                {
                    throw new InvalidOperationException("PLC 未连接。");
                }

                await _modbusMaster.WriteMultipleRegistersAsync(slaveId, startAddress, values);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                DisconnectAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogService.Error("释放 Modbus 传输层时断开连接失败。", ex);
            }
            finally
            {
                _disposed = true;
                _ioLock.Dispose();
            }
        }

        private Task DisconnectInternalAsync()
        {
            if (_modbusMaster != null)
            {
                try
                {
                    _modbusMaster.Dispose();
                }
                catch (Exception ex)
                {
                    LogService.Warn($"释放 Modbus 主站对象失败：{ex.Message}");
                }

                _modbusMaster = null;
            }

            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                }
                catch (Exception ex)
                {
                    LogService.Warn($"关闭串口失败：{ex.Message}");
                }

                try
                {
                    _serialPort.Dispose();
                }
                catch (Exception ex)
                {
                    LogService.Warn($"释放串口对象失败：{ex.Message}");
                }

                _serialPort = null;
            }

            return Task.CompletedTask;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ModbusRtuTransport));
            }
        }
    }
}
