using System.Windows;
using MessengerClient.Services;

namespace MessengerClient;

public partial class LoginWindow : Window
{
    private readonly TcpMessengerApi _api = new();
    private bool _isRegistrationMode;

    public LoginWindow()
    {
        InitializeComponent();
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorTextBlock.Text = string.Empty;
            var username = UsernameTextBox.Text.Trim();
            var password = PasswordInput.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Заповніть логін і пароль.");
            }

            if (password.Length < 4)
            {
                throw new InvalidOperationException("Пароль повинен містити щонайменше 4 символи.");
            }

            if (_isRegistrationMode)
            {
                var registerResult = await _api.RegisterAsync(username, password);
                if (!registerResult.Success)
                {
                    throw new InvalidOperationException(MapServerError(registerResult.Error));
                }

                MessageBox.Show($"Профіль «{username}» успішно створено.", "Реєстрація",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            var loginResult = await _api.LoginAsync(username, password);
            if (!loginResult.Success)
            {
                throw new InvalidOperationException(MapServerError(loginResult.Error));
            }

            MessageBox.Show($"Вітаємо, {username}!", "Авторизація", MessageBoxButton.OK, MessageBoxImage.Information);

            var mainWindow = new MainWindow(loginResult.UserId, username);
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
        }
        catch (Exception exception)
        {
            ErrorTextBlock.Text = exception.Message;
        }
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isRegistrationMode = !_isRegistrationMode;
        ActionButton.Content = _isRegistrationMode ? "Зареєструватися" : "Увійти";
        ModeButton.Content = _isRegistrationMode
            ? "Вже є обліковий запис? Увійти"
            : "Немає облікового запису? Зареєструватися";
        ErrorTextBlock.Text = string.Empty;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        UsernameTextBox.Clear();
        PasswordInput.Clear();
        ErrorTextBlock.Text = string.Empty;
        UsernameTextBox.Focus();
    }

    private static string MapServerError(string error)
    {
        if (error.Contains("LOGIN_EXISTS", StringComparison.Ordinal))
        {
            return "Користувач із таким логіном уже існує.";
        }

        if (error.Contains("INVALID_CREDENTIALS", StringComparison.Ordinal))
        {
            return "Невірний логін або пароль.";
        }

        if (error.Contains("NO_RESPONSE", StringComparison.Ordinal) ||
            error.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("connection", StringComparison.OrdinalIgnoreCase))
        {
            return "Сервер недоступний. Запустіть MessengerServer і повторіть спробу.";
        }

        return $"Помилка сервера: {error}";
    }
}
