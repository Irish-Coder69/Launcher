using System.Windows;
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
        if (!string.Equals(PasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
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
            Password = PasswordBox.Password
        };

        if (!_userProfileService.CreateUser(input, out var errorMessage))
        {
            MessageBox.Show(this, errorMessage, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
