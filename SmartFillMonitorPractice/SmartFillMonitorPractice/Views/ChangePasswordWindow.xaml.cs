using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SmartFillMonitorPractice.Services;

namespace SmartFillMonitorPractice.Views
{
    public partial class ChangePasswordWindow : Window
    {
        private readonly IUserService _userService;
        private readonly string _userName;
        private readonly bool _isForceChangeMode;

        public ChangePasswordWindow(string userName, string userDisplayName, bool isForceChangeMode)
        {
            InitializeComponent();
            _userService = ResolveService<IUserService>();
            _userName = (userName ?? string.Empty).Trim();
            _isForceChangeMode = isForceChangeMode;

            TitleTextBlock.Text = _isForceChangeMode ? "首次登录修改密码" : "修改密码";
            HintTextBlock.Text = _isForceChangeMode
                ? "首次登录必须先完成密码修改，修改成功后才能继续进入系统。"
                : "请输入当前密码，再设置新的登录密码。";
            LeftHintTextBlock.Text = _isForceChangeMode
                ? "当前账号启用了首次登录强制改密，请先完成修改后再进入系统。"
                : "主动修改密码需要先验证当前密码，修改成功后请使用新密码登录。";
            UserDisplayTextBlock.Text = $"当前用户：{(string.IsNullOrWhiteSpace(userDisplayName) ? _userName : userDisplayName)}";
            UserNameTextBlock.Text = $"登录账号：{_userName}";

            if (_isForceChangeMode)
            {
                CurrentPasswordLabel.Visibility = Visibility.Collapsed;
                CurrentPasswordBorder.Visibility = Visibility.Collapsed;
                CurrentPasswordCapsLockHintTextBlock.Visibility = Visibility.Collapsed;
                SaveButton.Content = "保存并继续";
                CancelActionButton.Content = "取消";
            }
            else
            {
                SaveButton.Content = "确认修改";
                CancelActionButton.Content = "返回登录";
            }

            Loaded += (_, _) => FocusPrimaryInput();
        }

        private static T ResolveService<T>() where T : notnull
        {
            if (Application.Current is not App app || app.ServiceProvider == null)
            {
                throw new InvalidOperationException("服务提供程序尚未初始化。");
            }

            return app.ServiceProvider.GetRequiredService<T>();
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
            CurrentPasswordCapsLockHintTextBlock.Visibility =
                !_isForceChangeMode && CurrentPasswordBox.IsKeyboardFocused && capsLockOn ? Visibility.Visible : Visibility.Collapsed;
            NewPasswordCapsLockHintTextBlock.Visibility =
                NewPasswordBox.IsKeyboardFocused && capsLockOn ? Visibility.Visible : Visibility.Collapsed;
            ConfirmPasswordCapsLockHintTextBlock.Visibility =
                ConfirmPasswordBox.IsKeyboardFocused && capsLockOn ? Visibility.Visible : Visibility.Collapsed;
        }

        private void FocusPrimaryInput()
        {
            if (_isForceChangeMode)
            {
                NewPasswordBox.Focus();
                Keyboard.Focus(NewPasswordBox);
                return;
            }

            CurrentPasswordBox.Focus();
            Keyboard.Focus(CurrentPasswordBox);
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var currentPassword = CurrentPasswordBox.Password ?? string.Empty;
            var newPassword = NewPasswordBox.Password ?? string.Empty;
            var confirmPassword = ConfirmPasswordBox.Password ?? string.Empty;

            if (!_isForceChangeMode && string.IsNullOrWhiteSpace(currentPassword))
            {
                MessageBox.Show("请输入当前密码。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                CurrentPasswordBox.Focus();
                return;
            }

            if (newPassword != confirmPassword)
            {
                MessageBox.Show("两次输入的新密码不一致。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                ConfirmPasswordBox.Clear();
                UpdateCapsLockHints();
                ConfirmPasswordBox.Focus();
                return;
            }

            IsEnabled = false;
            try
            {
                if (_isForceChangeMode)
                {
                    await _userService.ChangeCurrentUserPasswordAsync(newPassword);
                    MessageBox.Show("密码修改成功。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    await _userService.ChangePasswordWithCurrentPasswordAsync(_userName, currentPassword, newPassword);
                    MessageBox.Show("密码修改成功，请使用新密码登录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                DialogResult = true;
                Close();
            }
            catch (BusinessException ex)
            {
                MessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                FocusErrorInput(ex.Message);
            }
            catch (Exception ex)
            {
                LogService.Error("修改密码失败", ex);
                MessageBox.Show("密码修改失败，请稍后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                FocusErrorInput(null);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void FocusErrorInput(string? message)
        {
            if (!_isForceChangeMode && !string.IsNullOrWhiteSpace(message) && message.Contains("当前密码", StringComparison.Ordinal))
            {
                CurrentPasswordBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(NewPasswordBox.Password) ||
                (!string.IsNullOrWhiteSpace(message) && (message.Contains("密码至少", StringComparison.Ordinal) || message.Contains("密码必须", StringComparison.Ordinal))))
            {
                NewPasswordBox.Focus();
                return;
            }

            ConfirmPasswordBox.Focus();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
