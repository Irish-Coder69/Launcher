using System.Windows;
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
        if (!string.Equals(NewPasswordBox.Password, ConfirmNewPasswordBox.Password, StringComparison.Ordinal))
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
            CurrentPassword = CurrentPasswordBox.Password,
            NewPassword = NewPasswordBox.Password
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

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
