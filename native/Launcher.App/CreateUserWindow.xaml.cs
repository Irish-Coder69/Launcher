using System.Windows;
using System.Windows.Controls;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App;

public partial class CreateUserWindow : Window
{
    private readonly LauncherUserProfileService _userProfileService;

    public CreateUserWindow(LauncherUserProfileService userProfileService)
    {
        InitializeComponent();
        _userProfileService = userProfileService;
    }

    private void CreateButton_OnClick(object sender, RoutedEventArgs e)
    {
        var password = GetPasswordValue(PasswordBox, PasswordTextBox);
        var confirmPassword = GetPasswordValue(ConfirmPasswordBox, ConfirmPasswordTextBox);

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Passwords do not match.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var input = new LauncherCreateUserInput
        {
            UserName = UserNameTextBox.Text,
            DisplayName = DisplayNameTextBox.Text,
            Email = EmailTextBox.Text,
            Department = DepartmentTextBox.Text,
            Notes = NotesTextBox.Text,
            Password = password
        };

        if (!_userProfileService.CreateUser(input, out var errorMessage))
        {
            MessageBox.Show(this, errorMessage, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void TogglePasswordVisibilityButton_OnClick(object sender, RoutedEventArgs e)
    {
        TogglePasswordVisibility(PasswordBox, PasswordTextBox, TogglePasswordVisibilityButton);
    }

    private void ToggleConfirmPasswordVisibilityButton_OnClick(object sender, RoutedEventArgs e)
    {
        TogglePasswordVisibility(ConfirmPasswordBox, ConfirmPasswordTextBox, ToggleConfirmPasswordVisibilityButton);
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
