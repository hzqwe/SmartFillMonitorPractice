using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;
using Xunit;

namespace SmartFillMonitorPractice.Tests;

public class UserServiceTests
{
    [Fact]
    public async Task CanRegisterAdminPubliclyAsync_ReturnsTrue_WhenNoUsersExist()
    {
        using var scope = new TestAppScope();

        var canRegisterAdmin = await scope.UserService.CanRegisterAdminPubliclyAsync();

        Assert.True(canRegisterAdmin);
    }

    [Fact]
    public async Task RegisterPublicUserAsync_RejectsPublicAdminRegistration_WhenUsersAlreadyExist()
    {
        using var scope = new TestAppScope();
        await scope.UserService.RegisterPublicUserAsync("admin", "StrongPass123", Role.Admin, "管理员");

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            scope.UserService.RegisterPublicUserAsync("admin2", "StrongPass123", Role.Admin, "管理员2"));
    }

    [Fact]
    public async Task AuthenticateAsync_LocksUserAfterTooManyFailures()
    {
        using var scope = new TestAppScope();
        await scope.UserService.RegisterPublicUserAsync("admin", "StrongPass123", Role.Admin, "管理员");

        for (var i = 0; i < 5; i++)
        {
            await scope.UserService.AuthenticateAsync("admin", "WrongPass123");
        }

        var user = scope.GetUser("admin");
        Assert.NotNull(user.LockedUntil);
        Assert.True(user.LockedUntil > DateTime.Now);
    }

    [Fact]
    public async Task ResetPasswordAsync_AllowsAdminToResetEngineerPassword()
    {
        using var scope = new TestAppScope();
        await scope.UserService.RegisterPublicUserAsync("admin", "StrongPass123", Role.Admin, "管理员");

        var admin = scope.GetUser("admin");
        scope.SetCurrentUser(admin);
        await scope.UserService.CreateUserByAdminAsync("eng1", "Engineer123A", Role.Engineer, "工程师");

        await scope.UserService.ResetPasswordAsync("eng1", "ResetPass123", true);
        await scope.UserService.LogoutAsync();

        var success = await scope.UserService.AuthenticateAsync("eng1", "ResetPass123");

        Assert.True(success);
        Assert.True(scope.UserService.CurrentUser?.RequirePasswordChange);
    }
}
