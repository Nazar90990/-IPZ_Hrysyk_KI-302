using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using MessengerClient.Models;

namespace MessengerClient.Services;

public class TcpMessengerApi
{
    private readonly string _host;
    private readonly int _port;

    public TcpMessengerApi(string host = "127.0.0.1", int port = 5000)
    {
        _host = host;
        _port = port;
    }

    public async Task<(bool Success, string Error)> RegisterAsync(string login, string password)
    {
        var response = await SendCommandAsync($"REGISTER|{login}|{password}");
        if (response.StartsWith("OK|REGISTERED", StringComparison.Ordinal))
        {
            return (true, string.Empty);
        }

        return (false, response);
    }

    public async Task<(bool Success, int UserId, string Error)> LoginAsync(string login, string password)
    {
        var response = await SendCommandAsync($"LOGIN|{login}|{password}");
        var parts = response.Split('|');

        if (parts.Length >= 3 && parts[0] == "OK" && parts[1] == "AUTHORIZED" && int.TryParse(parts[2], out var userId))
        {
            return (true, userId, string.Empty);
        }

        return (false, 0, response);
    }

    public async Task<List<Chat>> GetUsersAsync(int currentUserId)
    {
        var response = await SendCommandAsync($"GET_USERS|{currentUserId}");
        return ParseUsers(response, "OK|USERS|");
    }

    public async Task<List<Chat>> GetChatsAsync(int currentUserId)
    {
        var response = await SendCommandAsync($"GET_CHATS|{currentUserId}");
        return ParseUsers(response, "OK|CHATS|");
    }

    public async Task<(bool Success, string Error)> CreateChatAsync(int userId, int otherUserId)
    {
        var response = await SendCommandAsync($"CREATE_CHAT|{userId}|{otherUserId}");
        return (response == "OK|CHAT_CREATED", response);
    }

    public async Task<(bool Success, string Error)> DeleteChatAsync(int userId, int otherUserId)
    {
        var response = await SendCommandAsync($"DELETE_CHAT|{userId}|{otherUserId}");
        return (response == "OK|CHAT_DELETED", response);
    }

    private static List<Chat> ParseUsers(string response, string prefix)
    {
        var users = new List<Chat>();

        if (!response.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Сервер повернув помилку: {response}");
        }

        var payload = response.Substring(prefix.Length);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return users;
        }

        foreach (var row in SplitEscaped(payload, ';'))
        {
            if (string.IsNullOrWhiteSpace(row))
            {
                continue;
            }

            var fields = SplitEscaped(row, ',');
            if (fields.Count < 2)
            {
                continue;
            }

            if (!int.TryParse(fields[0], out var userId))
            {
                continue;
            }

            users.Add(new Chat
            {
                UserId = userId,
                Title = Unescape(fields[1]),
                IsOnline = true
            });
        }

        return users;
    }

    public async Task<(bool Success, string Error)> SendMessageAsync(int senderId, int receiverId, string text)
    {
        var response = await SendCommandAsync($"SEND_MESSAGE|{senderId}|{receiverId}|{text}");
        if (response.StartsWith("OK|MESSAGE_SAVED|", StringComparison.Ordinal))
        {
            return (true, string.Empty);
        }

        return (false, response);
    }

    public async Task<List<Message>> GetHistoryAsync(int currentUserId, Chat chat, string currentUserName)
    {
        var response = await SendCommandAsync($"GET_HISTORY|{currentUserId}|{chat.UserId}");
        var history = new List<Message>();

        if (response.StartsWith("OK|HISTORY_EMPTY", StringComparison.Ordinal))
        {
            return history;
        }

        if (!response.StartsWith("OK|HISTORY|", StringComparison.Ordinal))
        {
            return history;
        }

        var payload = response.Substring("OK|HISTORY|".Length);
        foreach (var row in SplitEscaped(payload, ';'))
        {
            if (string.IsNullOrWhiteSpace(row))
            {
                continue;
            }

            if (!TryParseHistoryRow(row, out var parsed))
            {
                continue;
            }

            var hasDate = DateTime.TryParse(parsed.CreatedAtRaw, null, DateTimeStyles.RoundtripKind, out var date);
            var localDate = hasDate ? date.ToLocalTime() : default;
            var createdAt = hasDate ? localDate.ToString("HH:mm") : parsed.CreatedAtRaw;

            history.Add(new Message
            {
                Id = parsed.Id,
                SenderId = parsed.SenderId,
                ReceiverId = parsed.ReceiverId,
                Text = parsed.Text,
                Author = parsed.SenderId == currentUserId ? currentUserName : chat.Title,
                Time = createdAt,
                DateTimeDisplay = hasDate ? localDate.ToString("dd.MM.yyyy HH:mm") : parsed.CreatedAtRaw,
                LocalDate = hasDate ? localDate.Date : null,
                DateLabel = hasDate ? localDate.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("uk-UA")) : string.Empty,
                IsOwn = parsed.SenderId == currentUserId
            });
        }

        return history;
    }

    public async Task<string> SendCommandAsync(string command)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_host, _port);

        await using var stream = client.GetStream();
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8);

        await writer.WriteLineAsync(command);
        return await reader.ReadLineAsync() ?? "ERROR|NO_RESPONSE";
    }

    private static bool TryParseHistoryRow(string row, out (long Id, int SenderId, int ReceiverId, string Text, string CreatedAtRaw) message)
    {
        message = default;

        var firstComma = IndexOfUnescaped(row, ',');
        if (firstComma < 0)
        {
            return false;
        }

        var secondComma = IndexOfUnescaped(row, ',', firstComma + 1);
        if (secondComma < 0)
        {
            return false;
        }

        var thirdComma = IndexOfUnescaped(row, ',', secondComma + 1);
        if (thirdComma < 0)
        {
            return false;
        }

        var idRaw = row[..firstComma];
        var senderRaw = row[(firstComma + 1)..secondComma];
        var receiverRaw = row[(secondComma + 1)..thirdComma];
        var tail = row[(thirdComma + 1)..];

        var lastCommaInTail = LastIndexOfUnescaped(tail, ',');
        if (lastCommaInTail < 0)
        {
            return false;
        }

        var textRaw = tail[..lastCommaInTail];
        var createdAtRaw = tail[(lastCommaInTail + 1)..];

        if (!long.TryParse(idRaw, out var id) ||
            !int.TryParse(senderRaw, out var senderId) ||
            !int.TryParse(receiverRaw, out var receiverId))
        {
            return false;
        }

        message = (id, senderId, receiverId, Unescape(textRaw), Unescape(createdAtRaw));
        return true;
    }

    private static int IndexOfUnescaped(string input, char target, int startIndex = 0)
    {
        var escaped = false;

        for (var index = startIndex; index < input.Length; index++)
        {
            var ch = input[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == target)
            {
                return index;
            }
        }

        return -1;
    }

    private static int LastIndexOfUnescaped(string input, char target)
    {
        var escaped = false;

        for (var index = input.Length - 1; index >= 0; index--)
        {
            var ch = input[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == target)
            {
                return index;
            }

            if (ch == '\\')
            {
                escaped = true;
            }
        }

        return -1;
    }

    private static List<string> SplitEscaped(string input, char separator)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var escaped = false;

        foreach (var ch in input)
        {
            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == separator)
            {
                parts.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        parts.Add(sb.ToString());
        return parts;
    }

    private static string Unescape(string value)
    {
        var sb = new StringBuilder();
        var escaped = false;

        foreach (var ch in value)
        {
            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
