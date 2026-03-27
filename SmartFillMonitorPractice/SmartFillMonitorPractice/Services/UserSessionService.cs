using System;
using SmartFillMonitorPractice.Models;

namespace SmartFillMonitorPractice.Services
{
    public class UserSessionService : IUserSessionService
    {
        public event Action<User?>? LoginStateChanged;

        public User? CurrentUser { get; private set; }

        public void SetCurrentUser(User? user)
        {
            if (ReferenceEquals(CurrentUser, user))
            {
                return;
            }

            CurrentUser = user;
            LoginStateChanged?.Invoke(CurrentUser);
        }

        public string GetCurrentUserName()
        {
            return CurrentUser?.UserName ?? string.Empty;
        }

        public string GetCurrentUserDisplayName()
        {
            return CurrentUser?.DisplayNameOrUserName ?? string.Empty;
        }
    }
}
