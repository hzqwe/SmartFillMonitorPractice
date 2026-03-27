using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartFillMonitorPractice.Models;

namespace SmartFillMonitorPractice.Services
{
    public class ConfigService : IConfigService
    {
        private const string SettingFileName = "device-settings.json";
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private readonly IAuthorizationService _authorizationService;
        private readonly IAuditService _auditService;

        public ConfigService(IAuthorizationService authorizationService, IAuditService auditService)
        {
            _authorizationService = authorizationService;
            _auditService = auditService;
        }

        public string GetSettingFilePath()
        {
            return Path.Combine(AppContext.BaseDirectory, SettingFileName);
        }

        public async Task<DeviceSettings> LoadSettingsAsync()
        {
            var path = GetSettingFilePath();
            DeviceSettings? settings = null;

            await _ioLock.WaitAsync();
            try
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(path);
                        settings = JsonSerializer.Deserialize<DeviceSettings>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (settings != null)
                        {
                            Validate(settings);
                            LogService.Info($"配置文件加载成功：{path}");
                            return settings;
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        LogService.Error($"配置文件格式错误，将回退到默认配置：{jsonEx.Message}");
                        BackCorruptFile(path);
                    }
                    catch (BusinessException)
                    {
                        LogService.Warn("检测到非法配置值，已回退到默认配置。");
                        BackCorruptFile(path);
                    }
                    catch (Exception ex)
                    {
                        LogService.Error($"读取配置文件失败：{ex.Message}");
                    }
                }
                else
                {
                    LogService.Warn($"配置文件不存在：{path}，将创建默认配置。");
                }
            }
            finally
            {
                _ioLock.Release();
            }

            settings = new DeviceSettings();
            await SaveDeviceSettingsAsync(settings, false);
            return settings;
        }

        public async Task<bool> SaveDeviceSettingsAsync(DeviceSettings settings, bool requireAuthorization = true)
        {
            if (settings == null)
            {
                return false;
            }

            Validate(settings);

            if (requireAuthorization)
            {
                _authorizationService.EnsurePermission(Permission.ManageSettings, "保存系统设置");
            }

            var path = GetSettingFilePath();
            var tempPath = path + ".tmp";

            await _ioLock.WaitAsync();
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, path, true);

                LogService.Info("配置文件保存成功。");
                if (requireAuthorization)
                {
                    _auditService.Operation("SaveSettings", "Success", $"Port={settings.PortName}, BaudRate={settings.BaudRate}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogService.Error($"配置文件保存失败：{ex.Message}");
                if (requireAuthorization)
                {
                    _auditService.Operation("SaveSettings", "Failed", ex.Message);
                }

                return false;
            }
            finally
            {
                _ioLock.Release();

                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void BackCorruptFile(string originalPath)
        {
            try
            {
                var backupPath = originalPath + ".corrupt" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Copy(originalPath, backupPath, true);
                LogService.Warn($"已备份损坏的配置文件：{backupPath}");
            }
            catch (Exception ex)
            {
                LogService.Error("备份损坏配置文件失败", ex);
            }
        }

        public void Validate(DeviceSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.PortName))
            {
                throw new BusinessException("串口号不能为空。");
            }

            if (settings.BaudRate is < 1200 or > 921600)
            {
                throw new BusinessException("波特率超出允许范围。");
            }

            if (settings.DataBits is < 5 or > 8)
            {
                throw new BusinessException("数据位必须在 5 到 8 之间。");
            }

            if (!Enum.TryParse(typeof(System.IO.Ports.Parity), settings.Parity, true, out _))
            {
                throw new BusinessException("校验位配置无效。");
            }

            if (!Enum.TryParse(typeof(System.IO.Ports.StopBits), settings.StopBits, true, out _))
            {
                throw new BusinessException("停止位配置无效。");
            }
        }
    }
}
