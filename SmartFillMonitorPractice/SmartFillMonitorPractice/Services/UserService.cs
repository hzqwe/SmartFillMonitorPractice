using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SmartFillMonitorPractice.Models;

namespace SmartFillMonitorPractice.Services
{
    public class UserService : IUserService
    {
        private const string StaticSalt = "MysuperSecretSalt_2026!@#";
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int DefaultPbkdf2Iterations = 120000;
        private const int MaxFailedLoginCount = 5;
        private const string LocalResetFileName = "admin-reset.json";
        private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);

        private readonly IAppDbContext _dbContext;
        private readonly IUserSessionService _userSessionService;
        private readonly IAuthorizationService _authorizationService;
        private readonly IAuditService _auditService;

        public UserService(
            IAppDbContext dbContext,
            IUserSessionService userSessionService,
            IAuthorizationService authorizationService,
            IAuditService auditService)
        {
            _dbContext = dbContext;
            _userSessionService = userSessionService;
            _authorizationService = authorizationService;
            _auditService = auditService;
        }

        public event Action<User?>? LoginStateChanged
        {
            add => _userSessionService.LoginStateChanged += value;
            remove => _userSessionService.LoginStateChanged -= value;
        }

        public string LastErrorMessage { get; private set; } = string.Empty;

        public User? CurrentUser => _userSessionService.CurrentUser;

        public bool IsAdministrator(User? user) => user?.Role == Role.Admin;

        public bool IsEngineer(User? user) => user?.Role == Role.Engineer;

        public string GetCurrentUserName()
        {
            return _userSessionService.GetCurrentUserName();
        }

        public string GetCurrentUserDisplayName()
        {
            return _userSessionService.GetCurrentUserDisplayName();
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public async Task<bool> HasAnyUserAsync()
        {
            try
            {
                return await _dbContext.Fsql.Select<User>().AnyAsync();
            }
            catch (Exception ex)
            {
                LogService.Error("检查用户数据失败", ex);
                throw new InfrastructureException("检查用户数据失败。", ex);
            }
        }

        public async Task<bool> CanRegisterAdminPubliclyAsync()
        {
            return !await HasAnyUserAsync();
        }

        public async Task RegisterPublicUserAsync(string userName, string password, Role role, string displayName = "")
        {
            userName = (userName ?? string.Empty).Trim();
            displayName = (displayName ?? string.Empty).Trim();
            password = NormalizePasswordInput(password);

            var hasUsers = await HasAnyUserAsync();
            if (hasUsers && role == Role.Admin)
            {
                _auditService.Security("PublicRegister", "Denied", $"公开注册尝试创建管理员账号被拒绝：{userName}", userName);
                throw new AuthorizationException("系统已有用户时，公开注册只允许创建工程师账号。");
            }

            await InsertUserAsync(
                userName,
                password,
                role,
                displayName,
                requirePasswordChange: false,
                auditAction: "PublicRegister",
                auditActor: userName);
        }

        public async Task CreateUserByAdminAsync(string userName, string password, Role role, string displayName = "", bool requirePasswordChange = true)
        {
            _authorizationService.EnsurePermission(Permission.ManageUsers, "创建用户");

            if (role == Role.Admin && !IsAdministrator(CurrentUser))
            {
                throw new AuthorizationException("只有管理员可以创建新的管理员账号。");
            }

            await InsertUserAsync(
                userName,
                password,
                role,
                displayName,
                requirePasswordChange,
                auditAction: role == Role.Admin ? "CreateAdminByAdmin" : "CreateEngineerByAdmin",
                auditActor: GetCurrentUserName());
        }

        public async Task<bool> AuthenticateAsync(string userName, string password)
        {
            LastErrorMessage = string.Empty;
            userName = (userName ?? string.Empty).Trim();
            password = NormalizePasswordInput(password);

            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                LastErrorMessage = "用户名或密码不能为空。";
                return false;
            }

            try
            {
                var user = await _dbContext.Fsql.Select<User>()
                    .Where(u => u.UserName == userName)
                    .FirstAsync();

                if (user == null)
                {
                    LastErrorMessage = "用户名或密码错误。";
                    _auditService.Security("Login", "Failed", "未找到用户。", userName);
                    return false;
                }

                if (user.IsDisabled)
                {
                    LastErrorMessage = "当前用户已被禁用。";
                    _auditService.Security("Login", "Failed", "用户已被禁用。", userName);
                    return false;
                }

                if (await TryAutoUnlockAsync(user))
                {
                    _auditService.Security("Unlock", "Success", "临时锁定已到期，账户已自动解锁。", userName);
                }

                if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.Now)
                {
                    LastErrorMessage = $"账户已锁定，请于 {user.LockedUntil.Value:yyyy-MM-dd HH:mm:ss} 后重试。";
                    _auditService.Security("Login", "Locked", $"账户已锁定，截止时间：{user.LockedUntil.Value:yyyy-MM-dd HH:mm:ss}", userName);
                    return false;
                }

                var isValid = VerifyPassword(user, password, out var needUpgrade);
                if (!isValid)
                {
                    await RegisterFailedLoginAsync(user);
                    LastErrorMessage = user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.Now
                        ? $"密码错误次数过多，账户已锁定至 {user.LockedUntil.Value:yyyy-MM-dd HH:mm:ss}。"
                        : "用户名或密码错误。";
                    return false;
                }

                if (needUpgrade)
                {
                    var passwordInfo = CreatePassword(password);
                    user.PasswordHash = passwordInfo.PasswordHash;
                    user.PasswordSalt = passwordInfo.PasswordSalt;
                    user.PasswordIterations = passwordInfo.Iterations;
                    user.PasswordChangedAt = DateTime.Now;
                    _auditService.Security("PasswordUpgrade", "Success", "旧版密码已自动升级为 PBKDF2。", userName);
                }

                if (string.IsNullOrWhiteSpace(user.DisplayName))
                {
                    user.DisplayName = user.UserName;
                }

                user.FailedLoginCount = 0;
                user.LockedUntil = null;
                user.LastFailedLoginTime = null;
                user.LastLoginTime = DateTime.Now;

                await _dbContext.Fsql.Update<User>()
                    .SetSource(user)
                    .UpdateColumns(u => new
                    {
                        u.PasswordHash,
                        u.PasswordSalt,
                        u.PasswordIterations,
                        u.DisplayName,
                        u.FailedLoginCount,
                        u.LockedUntil,
                        u.LastFailedLoginTime,
                        u.LastLoginTime,
                        u.PasswordChangedAt
                    })
                    .ExecuteAffrowsAsync();

                _userSessionService.SetCurrentUser(user);
                _auditService.Security("Login", "Success", $"登录成功。RequirePasswordChange={user.RequirePasswordChange}", userName);
                return true;
            }
            catch (Exception ex)
            {
                LastErrorMessage = "登录验证失败，请稍后重试。";
                LogService.Error("用户登录验证失败", ex);
                _auditService.Security("Login", "Error", ex.Message, userName);
                return false;
            }
        }

        public async Task ChangeCurrentUserPasswordAsync(string newPassword)
        {
            if (CurrentUser == null)
            {
                throw new BusinessException("当前未登录，无法修改密码。");
            }

            await ApplyPasswordChangeAsync(CurrentUser, newPassword, false, "ChangePassword", "当前登录用户已修改密码。", CurrentUser.UserName);
        }

        public async Task ChangePasswordWithCurrentPasswordAsync(string userName, string currentPassword, string newPassword)
        {
            userName = (userName ?? string.Empty).Trim();
            currentPassword = NormalizePasswordInput(currentPassword);

            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new BusinessException("请选择需要修改密码的用户。");
            }

            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                throw new BusinessException("请输入当前密码。");
            }

            var user = await _dbContext.Fsql.Select<User>()
                .Where(u => u.UserName == userName)
                .FirstAsync();
            if (user == null)
            {
                throw new BusinessException($"用户 {userName} 不存在。");
            }

            if (user.IsDisabled)
            {
                throw new BusinessException("当前用户已被禁用，无法修改密码。");
            }

            if (!VerifyPassword(user, currentPassword, out _))
            {
                throw new BusinessException("当前密码不正确。");
            }

            await ApplyPasswordChangeAsync(user, newPassword, false, "ChangePassword", "用户已通过旧密码校验完成主动改密。", user.UserName);
        }

        public async Task ResetPasswordAsync(string userName, string newPassword, bool requirePasswordChange = true, bool skipAuthorization = false, string? actor = null)
        {
            if (!skipAuthorization)
            {
                _authorizationService.EnsurePermission(Permission.ManageUsers, "重置用户密码");
            }

            userName = (userName ?? string.Empty).Trim();
            newPassword = NormalizePasswordInput(newPassword);

            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new BusinessException("待重置用户名不能为空。");
            }

            var user = await _dbContext.Fsql.Select<User>()
                .Where(u => u.UserName == userName)
                .FirstAsync();
            if (user == null)
            {
                throw new BusinessException($"用户 {userName} 不存在。");
            }

            await ApplyPasswordChangeAsync(user, newPassword, requirePasswordChange, "ResetPassword", $"已重置用户 {userName} 的密码，RequirePasswordChange={requirePasswordChange}", actor ?? GetCurrentUserName());
        }

        public async Task TryApplyDevelopmentResetAsync()
        {
#if DEBUG
            var filePath = Path.Combine(AppContext.BaseDirectory, LocalResetFileName);
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var request = JsonSerializer.Deserialize<LocalResetRequest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (request == null || string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.NewPassword))
                {
                    throw new BusinessException("本地密码重置文件内容无效。");
                }

                await ResetPasswordAsync(request.UserName, request.NewPassword, request.RequirePasswordChange, true, "debug-local-reset");

                var appliedPath = $"{filePath}.{DateTime.Now:yyyyMMddHHmmss}.applied";
                File.Move(filePath, appliedPath, true);
                _auditService.Security("DebugResetPassword", "Success", $"已应用本地密码重置文件，目标用户：{request.UserName}", "debug-local-reset");
            }
            catch (Exception ex)
            {
                LogService.Error("处理本地密码重置文件失败", ex);
                _auditService.Security("DebugResetPassword", "Failed", ex.Message, "debug-local-reset");
            }
#endif
        }

        public Task LogoutAsync()
        {
            if (CurrentUser != null)
            {
                _auditService.Security("Logout", "Success", "用户已退出登录。", CurrentUser.UserName);
                _userSessionService.SetCurrentUser(null);
            }

            LastErrorMessage = string.Empty;
            return Task.CompletedTask;
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            _authorizationService.EnsurePermission(Permission.ManageUsers, "查询用户列表");
            return await _dbContext.Fsql.Select<User>()
                .OrderBy(u => u.UserName)
                .ToListAsync();
        }

        public async Task<List<User>> GetLoginUsersAsync()
        {
            try
            {
                return await _dbContext.Fsql.Select<User>()
                    .Where(u => !u.IsDisabled)
                    .OrderBy(u => u.UserName)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                LogService.Error("加载登录用户列表失败", ex);
                throw new InfrastructureException("加载登录用户列表失败。", ex);
            }
        }

        public string NormalizePasswordInput(string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return string.Empty;
            }

            return TrimBoundaryNoise(password);
        }

        private async Task InsertUserAsync(string userName, string password, Role role, string displayName, bool requirePasswordChange, string auditAction, string auditActor)
        {
            userName = (userName ?? string.Empty).Trim();
            displayName = (displayName ?? string.Empty).Trim();
            password = NormalizePasswordInput(password);

            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new BusinessException("用户名不能为空。");
            }

            EnsureStrongPassword(password);

            var exists = await _dbContext.Fsql.Select<User>()
                .Where(u => u.UserName == userName)
                .AnyAsync();
            if (exists)
            {
                throw new BusinessException($"用户 {userName} 已存在。");
            }

            var passwordInfo = CreatePassword(password);
            var user = new User
            {
                UserName = userName,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName : displayName,
                PasswordHash = passwordInfo.PasswordHash,
                PasswordSalt = passwordInfo.PasswordSalt,
                PasswordIterations = passwordInfo.Iterations,
                Role = role,
                RequirePasswordChange = requirePasswordChange,
                PasswordChangedAt = DateTime.Now,
                CreatedAt = DateTime.Now,
            };

            await _dbContext.Fsql.Insert(user).ExecuteAffrowsAsync();
            _auditService.Security(auditAction, "Success", $"已创建 {role} 用户：{userName}", auditActor);
        }

        private async Task RegisterFailedLoginAsync(User user)
        {
            user.FailedLoginCount += 1;
            user.LastFailedLoginTime = DateTime.Now;

            if (user.FailedLoginCount >= MaxFailedLoginCount)
            {
                user.LockedUntil = DateTime.Now.Add(LockDuration);
                _auditService.Security("Lock", "Success", $"账户因连续失败 {user.FailedLoginCount} 次已被锁定。", user.UserName);
            }
            else
            {
                _auditService.Security("Login", "Failed", $"密码错误，当前失败次数={user.FailedLoginCount}", user.UserName);
            }

            await _dbContext.Fsql.Update<User>()
                .Where(u => u.Id == user.Id)
                .Set(u => u.FailedLoginCount, user.FailedLoginCount)
                .Set(u => u.LastFailedLoginTime, user.LastFailedLoginTime)
                .Set(u => u.LockedUntil, user.LockedUntil)
                .ExecuteAffrowsAsync();
        }

        private async Task<bool> TryAutoUnlockAsync(User user)
        {
            if (!user.LockedUntil.HasValue || user.LockedUntil.Value > DateTime.Now)
            {
                return false;
            }

            user.FailedLoginCount = 0;
            user.LockedUntil = null;
            user.LastFailedLoginTime = null;

            await _dbContext.Fsql.Update<User>()
                .Where(u => u.Id == user.Id)
                .Set(u => u.FailedLoginCount, 0)
                .Set(u => u.LockedUntil, null)
                .Set(u => u.LastFailedLoginTime, null)
                .ExecuteAffrowsAsync();
            return true;
        }

        private async Task ApplyPasswordChangeAsync(User user, string newPassword, bool requirePasswordChange, string auditAction, string auditDetail, string auditActor)
        {
            newPassword = NormalizePasswordInput(newPassword);
            EnsureStrongPassword(newPassword);

            var passwordInfo = CreatePassword(newPassword);
            var now = DateTime.Now;

            user.PasswordHash = passwordInfo.PasswordHash;
            user.PasswordSalt = passwordInfo.PasswordSalt;
            user.PasswordIterations = passwordInfo.Iterations;
            user.RequirePasswordChange = requirePasswordChange;
            user.PasswordChangedAt = now;
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
            user.LastFailedLoginTime = null;

            await _dbContext.Fsql.Update<User>()
                .Where(u => u.Id == user.Id)
                .Set(u => u.PasswordHash, user.PasswordHash)
                .Set(u => u.PasswordSalt, user.PasswordSalt)
                .Set(u => u.PasswordIterations, user.PasswordIterations)
                .Set(u => u.RequirePasswordChange, requirePasswordChange)
                .Set(u => u.PasswordChangedAt, now)
                .Set(u => u.FailedLoginCount, 0)
                .Set(u => u.LockedUntil, null)
                .Set(u => u.LastFailedLoginTime, null)
                .ExecuteAffrowsAsync();

            if (CurrentUser?.Id == user.Id)
            {
                _userSessionService.SetCurrentUser(user);
            }

            _auditService.Security(auditAction, "Success", auditDetail, auditActor);
        }

        private (string PasswordHash, string PasswordSalt, int Iterations) CreatePassword(string password)
        {
            var normalizedPassword = NormalizePasswordInput(password);
            var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
            var hashBytes = Rfc2898DeriveBytes.Pbkdf2(normalizedPassword, saltBytes, DefaultPbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);
            return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes), DefaultPbkdf2Iterations);
        }

        private bool VerifyPassword(User user, string password, out bool needUpgrade)
        {
            needUpgrade = user.PasswordIterations <= 0 || string.IsNullOrWhiteSpace(user.PasswordSalt);
            var normalizedPassword = NormalizePasswordInput(password);

            if (needUpgrade)
            {
                return string.Equals(HashLegacyPassword(normalizedPassword), user.PasswordHash, StringComparison.Ordinal);
            }

            try
            {
                var saltBytes = Convert.FromBase64String(user.PasswordSalt);
                var hashBytes = Rfc2898DeriveBytes.Pbkdf2(normalizedPassword, saltBytes, user.PasswordIterations, HashAlgorithmName.SHA256, HashSize);
                var storedHash = Convert.FromBase64String(user.PasswordHash);
                return CryptographicOperations.FixedTimeEquals(hashBytes, storedHash);
            }
            catch (FormatException ex)
            {
                LogService.Warn($"检测到异常密码存储格式，尝试按旧密码逻辑兼容：{user.UserName}，{ex.Message}");
                needUpgrade = true;
                return string.Equals(HashLegacyPassword(normalizedPassword), user.PasswordHash, StringComparison.Ordinal);
            }
            catch (CryptographicException ex)
            {
                LogService.Error($"校验 PBKDF2 密码失败：{user.UserName}", ex);
                return false;
            }
        }

        private string HashLegacyPassword(string password)
        {
            using var sha = SHA256.Create();
            var rawBytes = Encoding.UTF8.GetBytes(NormalizePasswordInput(password) + StaticSalt);
            var hashBytes = sha.ComputeHash(rawBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static void EnsureStrongPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 10)
            {
                throw new BusinessException("密码至少需要 10 位，并包含大写字母、小写字母和数字。");
            }

            var hasUpper = password.Any(char.IsUpper);
            var hasLower = password.Any(char.IsLower);
            var hasDigit = password.Any(char.IsDigit);
            if (!hasUpper || !hasLower || !hasDigit)
            {
                throw new BusinessException("密码必须同时包含大写字母、小写字母和数字。");
            }
        }

        private string TrimBoundaryNoise(string password)
        {
            var start = 0;
            var end = password.Length - 1;

            while (start <= end && IsBoundaryNoise(password[start]))
            {
                start++;
            }

            while (end >= start && IsBoundaryNoise(password[end]))
            {
                end--;
            }

            return start > end ? string.Empty : password.Substring(start, end - start + 1);
        }

        private static bool IsBoundaryNoise(char ch)
        {
            return char.IsWhiteSpace(ch) || ch == '\u200B' || ch == '\uFEFF';
        }

        private sealed class LocalResetRequest
        {
            public string UserName { get; set; } = "admin";

            public string NewPassword { get; set; } = string.Empty;

            public bool RequirePasswordChange { get; set; } = true;
        }
    }
}
