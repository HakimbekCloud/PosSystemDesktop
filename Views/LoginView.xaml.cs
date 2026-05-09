using System.Windows.Controls;
using System.Windows.Input;
using PosSystem.ViewModels;

namespace PosSystem.Views;

public partial class LoginView : UserControl
{
    private readonly LoginViewModel _vm;
    private bool _showingPassword;

    public LoginView(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        PasswordBox.PasswordChanged += (_, _) =>
        {
            _vm.Password = PasswordBox.Password;
            if (!_showingPassword)
                PasswordText.Text = PasswordBox.Password;
        };

        PasswordText.TextChanged += (_, _) =>
        {
            if (_showingPassword)
            {
                _vm.Password = PasswordText.Text;
                PasswordBox.Password = PasswordText.Text;
            }
        };

        ServerBox.KeyDown  += OnEnterKey;
        UsernameBox.KeyDown += OnEnterKey;
        PasswordBox.KeyDown += OnEnterKey;
        PasswordText.KeyDown += OnEnterKey;
    }

    private void OnTogglePassword(object sender, System.Windows.RoutedEventArgs e)
    {
        _showingPassword = !_showingPassword;

        if (_showingPassword)
        {
            PasswordText.Text = PasswordBox.Password;
            PasswordBox.Visibility = System.Windows.Visibility.Collapsed;
            PasswordText.Visibility = System.Windows.Visibility.Visible;
            PasswordText.Focus();
            PasswordText.CaretIndex = PasswordText.Text.Length;
        }
        else
        {
            PasswordBox.Password = PasswordText.Text;
            PasswordText.Visibility = System.Windows.Visibility.Collapsed;
            PasswordBox.Visibility = System.Windows.Visibility.Visible;
            PasswordBox.Focus();
        }
    }

    private void OnEnterKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.LoginCommand.CanExecute(null))
            _vm.LoginCommand.Execute(null);
    }
}
