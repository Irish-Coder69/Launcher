using System.Windows;
using System.Windows.Input;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App;

public partial class LoginWindow : Window
{
    private readonly LauncherUserProfileService _userProfileService;

    public LoginWindow(LauncherUserProfileService userProfileService)
    {
        InitializeComponent();
        _userProfileService = userProfileService;
        LoadUsers();
    }

    public LauncherUserProfile? AuthenticatedUser { get; private set; }

    private void LoadUsers(string? preferredUserName = null)
    {
        var users = _userProfileService.GetUsers();
        UserComboBox.ItemsSource = users;
        if (users.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(preferredUserName))
            {
                var match = users.FirstOrDefault(user => string.Equals(user.UserName, preferredUserName, StringComparison.OrdinalIgnoreCase));
                UserComboBox.SelectedItem = match;
            }

            if (UserComboBox.SelectedItem is null)
            {
                UserComboBox.SelectedIndex = 0;
            }

            LoginStatusTextBlock.Text = "Select a user and sign in.";
            LoginButton.IsEnabled = true;
            EditUserButton.IsEnabled = true;
            PasswordBox.IsEnabled = true;
        }
        else
        {
            LoginStatusTextBlock.Text = "No users exist yet. Create the first user to continue.";
            LoginButton.IsEnabled = false;
            EditUserButton.IsEnabled = false;
            PasswordBox.IsEnabled = false;
        }
    }

    private void LoginButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedUser = UserComboBox.SelectedItem as LauncherUserProfile;
        var selectedUserName = selectedUser?.UserName ?? UserComboBox.SelectedValue?.ToString() ?? string.Empty;

        try
        {
            if (!_userProfileService.TryAuthenticate(selectedUserName, PasswordBox.Password, out var user, out var errorMessage) || user is null)
            {
                LoginStatusTextBlock.Text = string.IsNullOrWhiteSpace(errorMessage)
                    ? "Login failed. Check user and password and try again."
                    : errorMessage;
                MessageBox.Show(this, LoginStatusTextBlock.Text, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Warning);
                PasswordBox.SelectAll();
                PasswordBox.Focus();
                return;
            }

            _userProfileService.RecordLogin(user.UserName, IssueNoteTextBox.Text);
            LoginStatusTextBlock.Text = "Login successful. Opening Launcher...";
            AuthenticatedUser = user;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            LoginStatusTextBlock.Text = "Login failed due to an unexpected error.";
            MessageBox.Show(this, "Login failed. " + ex.Message, "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SubmitOnEnter_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        LoginButton_OnClick(LoginButton, new RoutedEventArgs());
    }

    private void CreateUserButton_OnClick(object sender, RoutedEventArgs e)
    {
        var createWindow = new CreateUserWindow(_userProfileService)
        {
            Owner = this
        };

        if (createWindow.ShowDialog() == true)
        {
            LoadUsers();
        }
    }

    private void EditUserButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (UserComboBox.SelectedItem is not LauncherUserProfile selectedUser)
        {
            MessageBox.Show(this, "Select a user to edit first.", "Launcher Native", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editWindow = new EditUserWindow(_userProfileService, selectedUser)
        {
            Owner = this
        };

        if (editWindow.ShowDialog() == true)
        {
            LoadUsers(editWindow.UpdatedUserName);
            PasswordBox.Clear();
            LoginStatusTextBlock.Text = "User updated. Sign in with the latest credentials.";
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
