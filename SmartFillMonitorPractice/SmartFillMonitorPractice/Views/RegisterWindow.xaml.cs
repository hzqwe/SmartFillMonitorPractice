using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SmartFillMonitorPractice.Models;
using SmartFillMonitorPractice.Services;

namespace SmartFillMonitorPractice.Views
{
    public partial class RegisterWindow : Window
    {
        private readonly IUserService _userService;

        public string CreatedUserName { get; private set; } = string.Empty;

        public RegisterWindow()
        {
            InitializeComponent();
            _userService = ResolveService<IUserService>();

            Loaded += async (_, _) =>
            {
                await LoadRoleOptionsAsync();
                UserNameTextBox.Focus();
            };
        }

        private static T ResolveService<T>() where T : notnull
        {
            if (Application.Current is not App app || app.ServiceProvider == null)
            {
                throw new InvalidOperationException("服务提供程序尚未初始化。");
            }

            return app.ServiceProvider.GetRequiredService<T>();
        }

        private async Task LoadRoleOptionsAsync()
        {
            var canRegisterAdmin = await _userService.CanRegisterAdminPubliclyAsync();
            var roleOptions = canRegisterAdmin
                ? new List<RoleOption>
                {
                    new(Role.Admin, "管理员"),
                    new(Role.Engineer, "工程师")
                }
                : new List<RoleOption>
                {
                    new(Role.Engineer, "工程师")
                };

            RoleComboBox.ItemsSource = roleOptions;
            RoleComboBox.SelectedIndex = 0;
            HintTextBlock.Text = canRegisterAdmin
                ? "当前系统暂无用户，请创建首个账户。"
                : "系统已有用户时，公开注册仅允许创建工程师。";
            RoleHintTextBlock.Text = canRegisterAdmin
                ? "首个用户建议创建管理员，后续再由管理员在系统内管理账户。"
                : "当前为公开注册入口，管理员账户只能由已登录管理员创建。";
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
            UpdateCapsLockHints();
        }

        private void PasswordBox_KeyChanged(object sender, KeyEventArgs e)
        {
            UpdateCapsLockHints();
        }

        private void UpdateCapsLockHints()
        {
            var capsLockOn = Keyboard.IsKeyToggled(Key.CapsLock);
            PasswordCapsLockHintTextBlock.Visibility =
                PasswordBox.IsKeyboardFocused && capsLockOn ? Visibility.Visible : Visibility.Collapsed;
            ConfirmPasswordCapsLockHintTextBlock.Visibility =
                ConfirmPasswordBox.IsKeyboardFocused && capsLockOn ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            var userName = (UserNameTextBox.Text ?? string.Empty).Trim();
            var displayName = (UserNameTextBox.Text ?? string.Empty).Trim();
            var password = PasswordBox.Password ?? string.Empty;
            var confirmPassword = ConfirmPasswordBox.Password ?? string.Empty;
            var selectedRole = RoleComboBox.SelectedItem as RoleOption;

            if (selectedRole == null)
            {
                MessageBox.Show("请选择用户类型。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (password != confirmPassword)
            {
                MessageBox.Show("两次输入的密码不一致。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                ConfirmPasswordBox.Clear();
                UpdateCapsLockHints();
                ConfirmPasswordBox.Focus();
                return;
            }

            IsEnabled = false;
            try
            {
                await _userService.RegisterPublicUserAsync(userName, password, selectedRole.Role, displayName);
                CreatedUserName = userName;
                MessageBox.Show("注册成功，请返回登录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (AuthorizationException ex)
            {
                MessageBox.Show(ex.Message, "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (BusinessException ex)
            {
                MessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                LogService.Error("公开注册失败", ex);
                MessageBox.Show("注册失败，请稍后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private sealed class RoleOption
        {
            public RoleOption(Role role, string displayName)
            {
                Role = role;
                DisplayName = displayName;
            }

            public Role Role { get; }

            public string DisplayName { get; }
        }
    }
}
