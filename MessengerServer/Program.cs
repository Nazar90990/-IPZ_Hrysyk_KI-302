using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Data.Sqlite;

const int Port = 5000;
const string Host = "127.0.0.1";

var databasePath = Path.Combine(AppContext.BaseDirectory, "messenger_server.db");
var isNewDatabase = !File.Exists(databasePath);
var connectionString = $"Data Source={databasePath};Cache=Shared";
var schemaPath = Path.Combine(AppContext.BaseDirectory, "Sql", "schema.sql");

await EnsureDatabaseAsync(connectionString, schemaPath);
await EnsureSeedUsersAsync(connectionString);
if (isNewDatabase)
{
    await EnsureSeedMessagesAsync(connectionString);
}
await EnsureChatMigrationAsync(connectionString);

var listener = new TcpListener(IPAddress.Parse(Host), Port);
listener.Start();

Console.WriteLine($"[SERVER] Started on {Host}:{Port}");
Console.WriteLine("[SERVER] Commands: REGISTER, LOGIN, GET_USERS, GET_CHATS, CREATE_CHAT, DELETE_CHAT, SEND_MESSAGE, GET_HISTORY, PING");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleClientAsync(client, connectionString));
}

static async Task EnsureChatMigrationAsync(string connectionString)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    var command = connection.CreateCommand();
    command.CommandText = @"
        BEGIN IMMEDIATE;
        INSERT OR IGNORE INTO Chats (UserId1, UserId2)
        SELECT DISTINCT min(SenderId, ReceiverId), max(SenderId, ReceiverId)
        FROM Messages
        WHERE SenderId <> ReceiverId
          AND NOT EXISTS (SELECT 1 FROM SchemaMigrations WHERE Name = 'chats-v1');
        INSERT OR IGNORE INTO SchemaMigrations (Name) VALUES ('chats-v1');
        COMMIT;";
    await command.ExecuteNonQueryAsync();
}

static async Task HandleClientAsync(TcpClient client, string connectionString)
{
    var endPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
    Console.WriteLine($"[CONNECT] {endPoint}");

    try
    {
        await using var networkStream = client.GetStream();
        using var reader = new StreamReader(networkStream, Encoding.UTF8);
        using var writer = new StreamWriter(networkStream, Encoding.UTF8) { AutoFlush = true };

        while (true)
        {
            var request = await reader.ReadLineAsync();
            if (request is null)
            {
                break;
            }

            Console.WriteLine($"[RECV] {request}");
            var response = await ProcessCommandAsync(request, connectionString);
            Console.WriteLine($"[SEND] {response}");
            await writer.WriteLineAsync(response);
        }
    }
    catch (Exception exception)
    {
        Console.WriteLine($"[ERROR] {exception.Message}");
    }
    finally
    {
        client.Close();
        Console.WriteLine($"[DISCONNECT] {endPoint}");
    }
}

static async Task<string> ProcessCommandAsync(string request, string connectionString)
{
    if (string.IsNullOrWhiteSpace(request))
    {
        return "ERROR|EMPTY_REQUEST";
    }

    var parts = request.Split('|');
    var command = parts[0].Trim().ToUpperInvariant();

    try
    {
        return command switch
        {
            "REGISTER" => await RegisterAsync(parts, connectionString),
            "LOGIN" => await LoginAsync(parts, connectionString),
            "GET_USERS" => await GetUsersAsync(parts, connectionString),
            "GET_CHATS" => await GetChatsAsync(parts, connectionString),
            "CREATE_CHAT" => await CreateChatAsync(parts, connectionString),
            "DELETE_CHAT" => await DeleteChatAsync(parts, connectionString),
            "SEND_MESSAGE" => await SendMessageAsync(parts, connectionString),
            "GET_HISTORY" => await GetHistoryAsync(parts, connectionString),
            "PING" => "PONG",
            _ => "ERROR|UNKNOWN_COMMAND"
        };
    }
    catch (Exception exception)
    {
        return $"ERROR|SERVER_EXCEPTION|{exception.Message}";
    }
}

static async Task<string> RegisterAsync(string[] parts, string connectionString)
{
    if (parts.Length < 3)
    {
        return "ERROR|INVALID_REGISTER_FORMAT";
    }

    var login = parts[1].Trim();
    var password = parts[2].Trim();

    if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
    {
        return "ERROR|INVALID_REGISTER_DATA";
    }

    await using var connection = await OpenConnectionAsync(connectionString);

    var checkCommand = connection.CreateCommand();
    checkCommand.CommandText = "SELECT COUNT(1) FROM Users WHERE Login = $login;";
    checkCommand.Parameters.AddWithValue("$login", login);

    var exists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync()) > 0;
    if (exists)
    {
        return "ERROR|LOGIN_EXISTS";
    }

    var insertCommand = connection.CreateCommand();
    insertCommand.CommandText = "INSERT INTO Users (Login, PasswordHash, CreatedAt) VALUES ($login, $password, $createdAt);";
    insertCommand.Parameters.AddWithValue("$login", login);
    insertCommand.Parameters.AddWithValue("$password", password);
    insertCommand.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));

    await insertCommand.ExecuteNonQueryAsync();
    return "OK|REGISTERED";
}

static async Task<string> LoginAsync(string[] parts, string connectionString)
{
    if (parts.Length < 3)
    {
        return "ERROR|INVALID_LOGIN_FORMAT";
    }

    var login = parts[1].Trim();
    var password = parts[2].Trim();

    await using var connection = await OpenConnectionAsync(connectionString);

    var command = connection.CreateCommand();
    command.CommandText = "SELECT Id FROM Users WHERE Login = $login AND PasswordHash = $password LIMIT 1;";
    command.Parameters.AddWithValue("$login", login);
    command.Parameters.AddWithValue("$password", password);

    var result = await command.ExecuteScalarAsync();
    if (result is null)
    {
        return "ERROR|INVALID_CREDENTIALS";
    }

    return $"OK|AUTHORIZED|{result}";
}

static async Task<string> GetUsersAsync(string[] parts, string connectionString)
{
    if (parts.Length < 2 || !int.TryParse(parts[1], out var currentUserId))
    {
        return "ERROR|INVALID_GET_USERS_FORMAT";
    }

    await using var connection = await OpenConnectionAsync(connectionString);

    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT Id, Login
        FROM Users
        WHERE Id <> $currentUserId
        ORDER BY Login ASC;";
    command.Parameters.AddWithValue("$currentUserId", currentUserId);

    await using var reader = await command.ExecuteReaderAsync();
    var users = new List<string>();

    while (await reader.ReadAsync())
    {
        users.Add($"{reader.GetInt32(0)},{Escape(reader.GetString(1))}");
    }

    return $"OK|USERS|{string.Join(';', users)}";
}

static async Task<string> GetChatsAsync(string[] parts, string connectionString)
{
    if (parts.Length < 2 || !int.TryParse(parts[1], out var userId))
    {
        return "ERROR|INVALID_USER_ID";
    }

    await using var connection = await OpenConnectionAsync(connectionString);
    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT u.Id, u.Login FROM Chats c
        JOIN Users u ON u.Id = CASE WHEN c.UserId1 = $userId THEN c.UserId2 ELSE c.UserId1 END
        WHERE c.UserId1 = $userId OR c.UserId2 = $userId
        ORDER BY u.Login;";
    command.Parameters.AddWithValue("$userId", userId);
    await using var reader = await command.ExecuteReaderAsync();
    var users = new List<string>();
    while (await reader.ReadAsync())
    {
        users.Add($"{reader.GetInt32(0)},{Escape(reader.GetString(1))}");
    }
    return $"OK|CHATS|{string.Join(';', users)}";
}

static async Task<string> CreateChatAsync(string[] parts, string connectionString)
{
    if (parts.Length < 3 || !int.TryParse(parts[1], out var user1) || !int.TryParse(parts[2], out var user2) || user1 == user2)
    {
        return "ERROR|INVALID_USER_ID";
    }

    await using var connection = await OpenConnectionAsync(connectionString);
    var command = connection.CreateCommand();
    command.CommandText = @"
        INSERT OR IGNORE INTO Chats (UserId1, UserId2)
        SELECT min($user1, $user2), max($user1, $user2)
        WHERE (SELECT COUNT(*) FROM Users WHERE Id IN ($user1, $user2)) = 2;
        SELECT CASE WHEN EXISTS (SELECT 1 FROM Chats WHERE UserId1 = min($user1, $user2) AND UserId2 = max($user1, $user2)) THEN 1 ELSE 0 END;";
    command.Parameters.AddWithValue("$user1", user1);
    command.Parameters.AddWithValue("$user2", user2);
    return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1 ? "OK|CHAT_CREATED" : "ERROR|USER_NOT_FOUND";
}

static async Task<string> DeleteChatAsync(string[] parts, string connectionString)
{
    if (parts.Length < 3 || !int.TryParse(parts[1], out var user1) || !int.TryParse(parts[2], out var user2) || user1 == user2)
    {
        return "ERROR|INVALID_USER_ID";
    }

    await using var connection = await OpenConnectionAsync(connectionString);
    using var transaction = connection.BeginTransaction();
    var deleteMessages = connection.CreateCommand();
    deleteMessages.Transaction = transaction;
    deleteMessages.CommandText = @"DELETE FROM Messages WHERE (SenderId = $user1 AND ReceiverId = $user2) OR (SenderId = $user2 AND ReceiverId = $user1);";
    deleteMessages.Parameters.AddWithValue("$user1", user1);
    deleteMessages.Parameters.AddWithValue("$user2", user2);
    await deleteMessages.ExecuteNonQueryAsync();
    var deleteChat = connection.CreateCommand();
    deleteChat.Transaction = transaction;
    deleteChat.CommandText = "DELETE FROM Chats WHERE UserId1 = min($user1, $user2) AND UserId2 = max($user1, $user2);";
    deleteChat.Parameters.AddWithValue("$user1", user1);
    deleteChat.Parameters.AddWithValue("$user2", user2);
    await deleteChat.ExecuteNonQueryAsync();
    await transaction.CommitAsync();
    return "OK|CHAT_DELETED";
}

static async Task<string> SendMessageAsync(string[] parts, string connectionString)
{
    if (parts.Length < 4)
    {
        return "ERROR|INVALID_SEND_FORMAT";
    }

    if (!int.TryParse(parts[1], out var senderId) || !int.TryParse(parts[2], out var receiverId))
    {
        return "ERROR|INVALID_USER_ID";
    }

    var messageText = string.Join('|', parts.Skip(3)).Trim();
    if (string.IsNullOrWhiteSpace(messageText))
    {
        return "ERROR|EMPTY_MESSAGE";
    }

    await using var connection = await OpenConnectionAsync(connectionString);

    var command = connection.CreateCommand();
    command.CommandText = @"
        INSERT INTO Messages (SenderId, ReceiverId, MessageText, CreatedAt)
        SELECT $senderId, $receiverId, $messageText, $createdAt
        WHERE EXISTS (SELECT 1 FROM Chats WHERE UserId1 = min($senderId, $receiverId) AND UserId2 = max($senderId, $receiverId));
        SELECT CASE WHEN changes() = 1 THEN last_insert_rowid() ELSE 0 END;";

    command.Parameters.AddWithValue("$senderId", senderId);
    command.Parameters.AddWithValue("$receiverId", receiverId);
    command.Parameters.AddWithValue("$messageText", messageText);
    command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));

    var messageId = await command.ExecuteScalarAsync();
    return Convert.ToInt64(messageId) == 0 ? "ERROR|CHAT_NOT_FOUND" : $"OK|MESSAGE_SAVED|{messageId}";
}

static async Task<string> GetHistoryAsync(string[] parts, string connectionString)
{
    if (parts.Length < 3)
    {
        return "ERROR|INVALID_HISTORY_FORMAT";
    }

    if (!int.TryParse(parts[1], out var userId1) || !int.TryParse(parts[2], out var userId2))
    {
        return "ERROR|INVALID_USER_ID";
    }

    await using var connection = await OpenConnectionAsync(connectionString);

    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT Id, SenderId, ReceiverId, MessageText, CreatedAt
        FROM Messages
        WHERE (SenderId = $userId1 AND ReceiverId = $userId2)
           OR (SenderId = $userId2 AND ReceiverId = $userId1)
        ORDER BY datetime(CreatedAt) ASC;";

    command.Parameters.AddWithValue("$userId1", userId1);
    command.Parameters.AddWithValue("$userId2", userId2);

    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<string>();

    while (await reader.ReadAsync())
    {
        var id = reader.GetInt64(0);
        var senderId = reader.GetInt32(1);
        var receiverId = reader.GetInt32(2);
        var text = Escape(reader.GetString(3));
        var createdAt = Escape(reader.GetString(4));
        rows.Add($"{id},{senderId},{receiverId},{text},{createdAt}");
    }

    return rows.Count == 0
        ? "OK|HISTORY_EMPTY"
        : $"OK|HISTORY|{string.Join(';', rows)}";
}

static async Task EnsureDatabaseAsync(string connectionString, string schemaPath)
{
    if (!File.Exists(schemaPath))
    {
        throw new FileNotFoundException("Schema file not found.", schemaPath);
    }

    var schema = await File.ReadAllTextAsync(schemaPath);
    await using var connection = await OpenConnectionAsync(connectionString);

    var command = connection.CreateCommand();
    command.CommandText = schema;
    await command.ExecuteNonQueryAsync();
}

static async Task EnsureSeedUsersAsync(string connectionString)
{
    var seedUsers = new[]
    {
        (Login: "nazar", Password: "1234"),
        (Login: "olya", Password: "1234"),
        (Login: "anna", Password: "1234"),
        (Login: "max", Password: "1234"),
        (Login: "denys", Password: "1234")
    };

    await using var connection = await OpenConnectionAsync(connectionString);

    foreach (var user in seedUsers)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Users (Login, PasswordHash, CreatedAt)
            SELECT $login, $password, $createdAt
            WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Login = $login);";

        command.Parameters.AddWithValue("$login", user.Login);
        command.Parameters.AddWithValue("$password", user.Password);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }
}

static async Task EnsureSeedMessagesAsync(string connectionString)
{
    var seedMessages = new[]
    {
        (From: "anna", To: "nazar", Text: "Привіт! Як справи?"),
        (From: "nazar", To: "anna", Text: "Все добре, дякую. А ти як?"),
        (From: "anna", To: "nazar", Text: "Також добре. Побачимось ввечері."),
        (From: "olya", To: "nazar", Text: "Не забудь, зустріч о 18:30."),
        (From: "nazar", To: "olya", Text: "Пам'ятаю, буду вчасно."),
        (From: "denys", To: "nazar", Text: "Я вже в центрі, чекаю."),
        (From: "max", To: "nazar", Text: "Скинь, будь ласка, адресу."),
        (From: "nazar", To: "max", Text: "Зараз скину в особисті."),
        (From: "nazar", To: "denys", Text: "Виїжджаю, буду за 10 хвилин.")
    };

    await using var connection = await OpenConnectionAsync(connectionString);

    foreach (var item in seedMessages)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Messages (SenderId, ReceiverId, MessageText, CreatedAt)
            SELECT sender.Id, receiver.Id, $text, $createdAt
            FROM Users sender, Users receiver
            WHERE sender.Login = $fromLogin AND receiver.Login = $toLogin
              AND NOT EXISTS (
                  SELECT 1 FROM Messages m
                  WHERE m.SenderId = sender.Id
                    AND m.ReceiverId = receiver.Id
                    AND m.MessageText = $text
              );";

        command.Parameters.AddWithValue("$fromLogin", item.From);
        command.Parameters.AddWithValue("$toLogin", item.To);
        command.Parameters.AddWithValue("$text", item.Text);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }
}

static async Task EnsureCleanupDemoMessagesAsync(string connectionString)
{
    await using var connection = await OpenConnectionAsync(connectionString);

    var command = connection.CreateCommand();
    command.CommandText = @"
        DELETE FROM Messages
        WHERE lower(trim(MessageText)) = 'privit test';";

    await command.ExecuteNonQueryAsync();
}

static async Task<SqliteConnection> OpenConnectionAsync(string connectionString)
{
    var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    var pragma = connection.CreateCommand();
    pragma.CommandText = "PRAGMA busy_timeout = 5000;";
    await pragma.ExecuteNonQueryAsync();

    return connection;
}

static string Escape(string value) =>
    value.Replace("\\", "\\\\", StringComparison.Ordinal)
         .Replace(",", "\\,", StringComparison.Ordinal)
         .Replace(";", "\\;", StringComparison.Ordinal)
         .Replace("|", "\\|", StringComparison.Ordinal);
