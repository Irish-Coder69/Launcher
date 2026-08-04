using System.Windows;
using System.Windows.Controls;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App;

public partial class EditUserWindow : Window
{
    private readonly LauncherUserProfileService _userProfileService;
    private readonly LauncherUserProfile _user;

    public EditUserWindow(LauncherUserProfileService userProfileService, LauncherUserProfile user)
    {
        InitializeComponent();
        _userProfileService = userProfileService;
        _user = user;

        UserNameTextBox.Text = user.UserName;
        DisplayNameTextBox.Text = user.DisplayName;
        EmailTextBox.Text = user.Email;
        DepartmentTextBox.Text = user.Department;
        NotesTextBox.Text = user.Notes;
    }

    public string UpdatedUserName { get; private set; } = string.Empty;

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var currentPassword = GetPasswordValue(CurrentPasswordBox, CurrentPasswordTextBox);
        var newPassword = GetPasswordValue(NewPasswordBox, NewPasswordTextBox);
        var confirmNewPassword = GetPasswordValue(ConfirmNewPasswordBox, ConfirmNewPasswordTextBox);

        if (!string.Equals(newPassword, confirmNewPassword, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "New passwords do not match.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var updateInput = new LauncherUpdateUserInput
        {
            OriginalUserName = _user.UserName,
            NewUserName = UserNameTextBox.Text,
            DisplayName = DisplayNameTextBox.Text,
            Email = EmailTextBox.Text,
            Department = DepartmentTextBox.Text,
            Notes = NotesTextBox.Text,
            CurrentPassword = currentPassword,
            NewPassword = newPassword
        };

        if (!_userProfileService.UpdateUser(updateInput, out var errorMessage))
        {
            MessageBox.Show(this, errorMessage, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        UpdatedUserName = updateInput.NewUserName.Trim();
        DialogResult = true;
        Close();
    }

    private void ToggleCurrentPasswordVisibilityButton_OnClick(object sender, RoutedEventArgs e)
    {
        TogglePasswordVisibility(CurrentPasswordBox, CurrentPasswordTextBox, ToggleCurrentPasswordVisibilityButton);
    }

    private void ToggleNewPasswordVisibilityButton_OnClick(object sender, RoutedEventArgs e)
    {
        TogglePasswordVisibility(NewPasswordBox, NewPasswordTextBox, ToggleNewPasswordVisibilityButton);
    }

    private void ToggleConfirmNewPasswordVisibilityButton_OnClick(object sender, RoutedEventArgs e)
    {
        TogglePasswordVisibility(ConfirmNewPasswordBox, ConfirmNewPasswordTextBox, ToggleConfirmNewPasswordVisibilityButton);
    }

    private static string GetPasswordValue(PasswordBox hiddenPasswordBox, TextBox visiblePasswordTextBox)
    {
        return visiblePasswordTextBox.Visibility == Visibility.Visible
            ? visiblePasswordTextBox.Text
            : hiddenPasswordBox.Password;
    }

    private static void TogglePasswordVisibility(PasswordBox hiddenPasswordBox, TextBox visiblePasswordTextBox, Button toggleButton)
    {
        if (visiblePasswordTextBox.Visibility == Visibility.Visible)
        {
            hiddenPasswordBox.Password = visiblePasswordTextBox.Text;
            visiblePasswordTextBox.Visibility = Visibility.Collapsed;
            hiddenPasswordBox.Visibility = Visibility.Visible;
            toggleButton.Content = "Show";
            hiddenPasswordBox.Focus();
            hiddenPasswordBox.SelectAll();
            return;
        }

        visiblePasswordTextBox.Text = hiddenPasswordBox.Password;
        hiddenPasswordBox.Visibility = Visibility.Collapsed;
        visiblePasswordTextBox.Visibility = Visibility.Visible;
        toggleButton.Content = "Hide";
        visiblePasswordTextBox.Focus();
        visiblePasswordTextBox.SelectAll();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
