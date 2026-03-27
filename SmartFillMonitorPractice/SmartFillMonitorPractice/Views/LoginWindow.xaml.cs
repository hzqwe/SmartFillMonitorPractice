using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;

namespace SmartFillMonitorPractice.Views
{
    public partial class LoginWindow : Window
    {
        private readonly IUserService _userService;

        public LoginWindow()
        {
            InitializeComponent();
            _userService = ResolveService<IUserService>();

            Loaded += async (_, _) =>
            {
                await LoadUserAsync();
                FocusPrimaryInput();
            };

            KeyDown += LoginWindow_KeyDown;
        }

        private static T ResolveService<T>() where T : notnull
        {
            if (Application.Current is not App app || app.ServiceProvider == null)
            {
                throw new InvalidOperationException("服务提供程序尚未初始化。");
            }

            return app.ServiceProvider.GetRequiredService<T>();
        }

        private void LoginWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Cancel_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private async Task LoadUserAsync(string? preferredUserName = null)
        {
            try
            {
                var users = await _userService.GetLoginUsersAsync();
                UserNameCombo.ItemsSource = users;

                var hasUsers = users.Count > 0;
                NoUsersHintTextBlock.Visibility = hasUsers ? Visibility.Collapsed : Visibility.Visible;
                HintTextBlock.Text = hasUsers
                    ? "请选择用户后输入密码登录。"
                    : "当前系统中还没有可登录用户，请先点击“注册”创建首个账户。";

                if (hasUsers)
                {
                    SelectUser(users, preferredUserName);
                }
                else
                {
                    UserNameCombo.Text = preferredUserName ?? string.Empty;
                    UserNameCombo.SelectedItem = null;
                }
            }
            catch (InfrastructureException ex)
            {
                LogService.Error("加载登录用户列表失败", ex);
                HintTextBlock.Text = "加载用户列表失败，请检查数据库连接后重试。";
                MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                LogService.Error("加载登录用户列表异常", ex);
                HintTextBlock.Text = "加载用户列表失败，请稍后重试。";
                MessageBox.Show("加载用户列表失败，请稍后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string ResolveUserName(User? selectedUser, string comboText)
        {
            return (selectedUser?.UserName ?? comboText ?? string.Empty).Trim();
        }

        private void SelectUser(IReadOnlyCollection<User> users, string? preferredUserName)
        {
            if (!string.IsNullOrWhiteSpace(preferredUserName))
            {
                var matched = users.FirstOrDefault(x => string.Equals(x.UserName, preferredUserName, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    UserNameCombo.SelectedItem = matched;
                    UserNameCombo.Text = matched.UserName;
                    return;
                }
            }

            UserNameCombo.SelectedIndex = users.Count > 0 ? 0 : -1;
            if (UserNameCombo.SelectedItem is User user)
            {
                UserNameCombo.Text = user.UserName;
            }
        }

        private void FocusPrimaryInput()
        {
            if (UserNameCombo.Items.Count == 0)
            {
                UserNameCombo.Focus();
                Keyboard.Focus(UserNameCombo);
                return;
            }

            PasswordBox.Focus();
            Keyboard.Focus(PasswordBox);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void PasswordBox_FocusChanged(object sender, RoutedEventArgs e)
        {
            UpdateCapsLockHint();
        }

        private void PasswordBox_KeyChanged(object sender, KeyEventArgs e)
        {
            UpdateCapsLockHint();
        }

        private void UpdateCapsLockHint()
        {
            PasswordCapsLockHintTextBlock.Visibility =
                PasswordBox.IsKeyboardFocused && Keyboard.IsKeyToggled(Key.CapsLock)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            var selectedUser = UserNameCombo.SelectedItem as User;
            var userName = ResolveUserName(selectedUser, UserNameCombo.Text);
            var password = PasswordBox.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(userName))
            {
                MessageBox.Show("请选择或输入用户名。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                UserNameCombo.Focus();
                return;
            }

            IsEnabled = false;
            try
            {
                var ok = await _userService.AuthenticateAsync(userName, password);
                if (!ok)
                {
                    var message = string.IsNullOrWhiteSpace(_userService.LastErrorMessage)
                        ? "用户名或密码错误。"
                        : _userService.LastErrorMessage;

                    MessageBox.Show(message, "登录失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        PasswordBox.Clear();
                        UpdateCapsLockHint();
                        PasswordBox.Focus();
                        Keyboard.Focus(PasswordBox);
                    }));
                    return;
                }

                if (_userService.CurrentUser?.RequirePasswordChange == true && _userService.CurrentUser != null)
                {
                    var currentUser = _userService.CurrentUser;
                    var changePasswordWindow = new ChangePasswordWindow(
                        currentUser.UserName,
                        currentUser.DisplayNameOrUserName,
                        true)
                    {
                        Owner = this
                    };

                    var changed = changePasswordWindow.ShowDialog();
                    if (changed != true)
                    {
                        await _userService.LogoutAsync();
                        MessageBox.Show("首次登录必须先完成密码修改。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        PasswordBox.Clear();
                        PasswordBox.Focus();
                        return;
                    }
                }

                DialogResult = true;
                Close();
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            var window = new RegisterWindow
            {
                Owner = this
            };

            var result = window.ShowDialog();
            if (result != true)
            {
                return;
            }

            await LoadUserAsync(window.CreatedUserName);
            PasswordBox.Clear();
            PasswordBox.Focus();
            Keyboard.Focus(PasswordBox);
        }

        private async void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (UserNameCombo.Items.Count == 0)
            {
                MessageBox.Show("当前暂无用户，请先注册。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedUser = UserNameCombo.SelectedItem as User;
            var userName = ResolveUserName(selectedUser, UserNameCombo.Text);
            if (string.IsNullOrWhiteSpace(userName))
            {
                MessageBox.Show("请先选择需要修改密码的用户。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                UserNameCombo.Focus();
                return;
            }

            var displayName = selectedUser?.DisplayNameOrUserName ?? userName;
            var window = new ChangePasswordWindow(userName, displayName, false)
            {
                Owner = this
            };

            var result = window.ShowDialog();
            if (result != true)
            {
                return;
            }

            await LoadUserAsync(userName);
            PasswordBox.Clear();
            PasswordBox.Focus();
            Keyboard.Focus(PasswordBox);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
