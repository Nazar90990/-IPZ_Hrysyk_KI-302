using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MessengerClient.Models;
using MessengerClient.Services;

namespace MessengerClient;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly TcpMessengerApi _api = new();
    private readonly ObservableCollection<Chat> _allChats = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private Chat? _selectedChat;
    private bool _refreshing;

    public ObservableCollection<Chat> Chats { get; } = new();
    public UserProfile CurrentUser { get; } = new();

    public Chat? SelectedChat
    {
        get => _selectedChat;
        set
        {
            if (ReferenceEquals(_selectedChat, value))
            {
                return;
            }

            _selectedChat = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow(int userId, string userName)
    {
        InitializeComponent();
        CurrentUser.UserId = userId;
        CurrentUser.DisplayName = userName;
        DataContext = this;

        SelectedChat = null;
        ChatsListBox.SelectedItem = null;
        UpdateChatState();

        _refreshTimer.Tick += RefreshTimer_Tick;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await ReloadChatsAsync();
        _refreshTimer.Start();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _refreshTimer.Stop();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_refreshing)
        {
            return;
        }

        try
        {
            _refreshing = true;
            await ReloadChatsAsync();
            if (SelectedChat is not null)
            {
                await RefreshSelectedChatHistoryAsync();
            }
        }
        catch
        {
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task ReloadChatsAsync()
    {
        try
        {
            var serverChats = await _api.GetChatsAsync(CurrentUser.UserId);
            var serverIds = serverChats.Select(chat => chat.UserId).ToHashSet();
            foreach (var removed in _allChats.Where(chat => !serverIds.Contains(chat.UserId)).ToList())
            {
                _allChats.Remove(removed);
            }

            foreach (var chat in serverChats)
            {
                if (_allChats.Any(existing => existing.UserId == chat.UserId))
                {
                    continue;
                }
                chat.Preview = "Немає повідомлень";
                chat.Time = "";
                _allChats.Add(chat);
            }

            ApplyChatFilter(SearchTextBox.Text.Trim());
        }
        catch (Exception exception)
        {
            if (!_refreshing)
            {
                MessageBox.Show($"Не вдалося завантажити список чатів: {exception.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ApplyChatFilter(string query)
    {
        var filtered = _allChats.Where(chat => string.IsNullOrWhiteSpace(query) || chat.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(chat => chat.Title).ToList();
        foreach (var chat in Chats.Where(chat => !filtered.Contains(chat)).ToList())
        {
            Chats.Remove(chat);
        }
        for (var index = 0; index < filtered.Count; index++)
        {
            if (!Chats.Contains(filtered[index]))
            {
                Chats.Insert(index, filtered[index]);
            }
        }
    }

    private async void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var users = await _api.GetUsersAsync(CurrentUser.UserId);
            var available = users.Where(user => _allChats.All(chat => chat.UserId != user.UserId)).ToList();
            if (available.Count == 0)
            {
                MessageBox.Show("Немає користувачів для створення нового чату.", "Новий чат", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new NewChatWindow(available) { Owner = this };
            if (window.ShowDialog() != true || window.SelectedUser is not Chat user)
            {
                return;
            }

            var result = await _api.CreateChatAsync(CurrentUser.UserId, user.UserId);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Error);
            }
            SearchTextBox.Clear();
            await ReloadChatsAsync();
            ChatsListBox.SelectedItem = Chats.FirstOrDefault(chat => chat.UserId == user.UserId);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Не вдалося створити чат: {exception.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteChatButton_Click(object sender, RoutedEventArgs e)
    {
        var chat = SelectedChat;
        if (chat is null || MessageBox.Show($"Видалити чат із {chat.Title} та всю історію повідомлень для обох користувачів? Цю дію неможливо скасувати.", "Видалення чату", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var result = await _api.DeleteChatAsync(CurrentUser.UserId, chat.UserId);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Error);
            }
            _allChats.Remove(chat);
            ApplyChatFilter(SearchTextBox.Text.Trim());
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Не вдалося видалити чат: {exception.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RefreshSelectedChatHistoryAsync()
    {
        var chat = SelectedChat;
        if (chat is null)
        {
            return;
        }

        var history = await _api.GetHistoryAsync(CurrentUser.UserId, chat, CurrentUser.DisplayName);

        chat.Messages.Clear();
        DateTime? previousDate = null;
        foreach (var message in history)
        {
            message.ShowDateSeparator = message.LocalDate.HasValue && message.LocalDate != previousDate;
            previousDate = message.LocalDate;
            chat.Messages.Add(message);
        }

        if (history.Count > 0)
        {
            var last = history[^1];
            chat.Preview = last.Text;
            chat.Time = last.DateTimeDisplay;
        }
        else
        {
            chat.Preview = "Немає повідомлень";
            chat.Time = "";
        }

        OnPropertyChanged(nameof(SelectedChat));
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = MessageTextBox.Text.Trim();
            if (SelectedChat is null)
            {
                throw new InvalidOperationException("Спочатку виберіть чат.");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Введіть текст повідомлення.");
            }

            var result = await _api.SendMessageAsync(CurrentUser.UserId, SelectedChat.UserId, text);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Сервер повернув помилку: {result.Error}");
            }

            MessageTextBox.Clear();
            await RefreshSelectedChatHistoryAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Помилка відправлення", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SendButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyChatFilter(SearchTextBox.Text.Trim());
    }

    private async void ChatsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChatsListBox.SelectedItem is Chat chat)
        {
            SelectedChat = chat;
            await RefreshSelectedChatHistoryAsync();
        }
        else
        {
            SelectedChat = null;
        }

        UpdateChatState();
    }

    private void UpdateChatState()
    {
        EmptyChatState.Visibility = SelectedChat is null ? Visibility.Visible : Visibility.Collapsed;
        ChatContent.Visibility = SelectedChat is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(CurrentUser) { Owner = this };
        settingsWindow.ShowDialog();
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var loginWindow = new LoginWindow();
        Application.Current.MainWindow = loginWindow;
        loginWindow.Show();
        Close();
    }
}
