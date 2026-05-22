using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using RonAuth.Application.Abstractions;
using RonAuth.Application.Models;
using RonAuth.Domain.Sessions;
using RonAuth.Domain.Users;

namespace RonAuth.Infrastructure.Persistence;

public sealed class SqliteIdentityStore : IUserRepository, IUserAccessRepository, ISessionRepository, IOtpCodeRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string connectionString;

    public SqliteIdentityStore(IOptions<SqlitePersistenceOptions> persistenceOptions, IPasswordHashService passwordHashService)
    {
        var configuredPath = persistenceOptions.Value.DatabasePath;
        var fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine("App_Data", "ronauth.db")
            : configuredPath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        EnsureInitialized();
        EnsureSeeded(passwordHashService);
    }

    public Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id,
       UserName,
       Email,
       PasswordHash,
       IsLoginAllowed,
       AttributesJson,
       TwoFactorMethodsJson,
       FailedLoginCount,
       LockedUntilUtc,
       PasswordChangedAtUtc,
       PasswordHistoryHashesJson
FROM Users
WHERE UserName = @UserName COLLATE NOCASE
LIMIT 1;";
        command.Parameters.AddWithValue("@UserName", userName);

        using var reader = command.ExecuteReader();
        return Task.FromResult(ReadUser(reader));
    }

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id,
       UserName,
       Email,
       PasswordHash,
       IsLoginAllowed,
       AttributesJson,
       TwoFactorMethodsJson,
       FailedLoginCount,
       LockedUntilUtc,
       PasswordChangedAtUtc,
       PasswordHistoryHashesJson
FROM Users
WHERE Id = @Id
LIMIT 1;";
        command.Parameters.AddWithValue("@Id", userId.ToString("D"));

        using var reader = command.ExecuteReader();
        return Task.FromResult(ReadUser(reader));
    }

    public Task CreateAsync(User user, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO Users (
    Id,
    UserName,
    Email,
    PasswordHash,
    IsLoginAllowed,
    AttributesJson,
    TwoFactorMethodsJson,
    FailedLoginCount,
    LockedUntilUtc,
    PasswordChangedAtUtc,
    PasswordHistoryHashesJson)
VALUES (
    @Id,
    @UserName,
    @Email,
    @PasswordHash,
    @IsLoginAllowed,
    @AttributesJson,
    @TwoFactorMethodsJson,
    @FailedLoginCount,
    @LockedUntilUtc,
    @PasswordChangedAtUtc,
    @PasswordHistoryHashesJson);";
        BindUser(command, user);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE Users
SET UserName = @UserName,
    Email = @Email,
    PasswordHash = @PasswordHash,
    IsLoginAllowed = @IsLoginAllowed,
    AttributesJson = @AttributesJson,
    TwoFactorMethodsJson = @TwoFactorMethodsJson,
    FailedLoginCount = @FailedLoginCount,
    LockedUntilUtc = @LockedUntilUtc,
    PasswordChangedAtUtc = @PasswordChangedAtUtc,
    PasswordHistoryHashesJson = @PasswordHistoryHashesJson
WHERE Id = @Id;";
        BindUser(command, user);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserAccess>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var accesses = new List<UserAccess>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT RoleId,
       RoleName,
       ScopeId,
       ScopeName
FROM UserAccesses
WHERE UserId = @UserId;";
        command.Parameters.AddWithValue("@UserId", userId.ToString("D"));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            accesses.Add(new UserAccess
            {
                RoleId = Guid.Parse(reader.GetString(0)),
                RoleName = reader.GetString(1),
                ScopeId = Guid.Parse(reader.GetString(2)),
                ScopeName = reader.GetString(3),
            });
        }

        return Task.FromResult<IReadOnlyList<UserAccess>>(accesses);
    }

    public Task CreateAsync(AuthSession session, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO Sessions (
    SessionId,
    UserId,
    IdentityProvider,
    Subject,
    IssuedAtUtc,
    ExpiresAtUtc,
    RevokedAtUtc,
    LastSeenAtUtc)
VALUES (
    @SessionId,
    @UserId,
    @IdentityProvider,
    @Subject,
    @IssuedAtUtc,
    @ExpiresAtUtc,
    @RevokedAtUtc,
    @LastSeenAtUtc);";
        BindSession(command, session);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<AuthSession?> GetActiveAsync(string sessionId, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT SessionId,
       UserId,
       IdentityProvider,
       Subject,
       IssuedAtUtc,
       ExpiresAtUtc,
       RevokedAtUtc,
       LastSeenAtUtc
FROM Sessions
WHERE SessionId = @SessionId
  AND RevokedAtUtc IS NULL
  AND ExpiresAtUtc > @CurrentTime
LIMIT 1;";
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@CurrentTime", ToDbValue(currentTime));

        using var reader = command.ExecuteReader();
        return Task.FromResult(ReadSession(reader));
    }

    public Task TouchAsync(string sessionId, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE Sessions
SET LastSeenAtUtc = @CurrentTime
WHERE SessionId = @SessionId;";
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@CurrentTime", ToDbValue(currentTime));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task RevokeAsync(string sessionId, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE Sessions
SET RevokedAtUtc = @CurrentTime
WHERE SessionId = @SessionId;";
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@CurrentTime", ToDbValue(currentTime));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task SaveAsync(OtpCodeRecord code, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = @"
DELETE FROM OtpCodes
WHERE UserId = @UserId
  AND ProviderName = @ProviderName COLLATE NOCASE;";
            deleteCommand.Parameters.AddWithValue("@UserId", code.UserId.ToString("D"));
            deleteCommand.Parameters.AddWithValue("@ProviderName", code.ProviderName);
            deleteCommand.ExecuteNonQuery();
        }

        using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = @"
INSERT INTO OtpCodes (
    Id,
    UserId,
    ProviderName,
    Code,
    ExpiresAtUtc,
    UsedAtUtc)
VALUES (
    @Id,
    @UserId,
    @ProviderName,
    @Code,
    @ExpiresAtUtc,
    @UsedAtUtc);";
            BindOtpCode(insertCommand, code);
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<OtpCodeRecord?> GetActiveAsync(Guid userId, string providerName, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id,
       UserId,
       ProviderName,
       Code,
       ExpiresAtUtc,
       UsedAtUtc
FROM OtpCodes
WHERE UserId = @UserId
  AND ProviderName = @ProviderName COLLATE NOCASE
  AND UsedAtUtc IS NULL
  AND ExpiresAtUtc > @CurrentTime
ORDER BY ExpiresAtUtc DESC
LIMIT 1;";
        command.Parameters.AddWithValue("@UserId", userId.ToString("D"));
        command.Parameters.AddWithValue("@ProviderName", providerName);
        command.Parameters.AddWithValue("@CurrentTime", ToDbValue(currentTime));

        using var reader = command.ExecuteReader();
        return Task.FromResult(ReadOtpCode(reader));
    }

    public Task MarkUsedAsync(Guid codeId, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE OtpCodes
SET UsedAtUtc = @CurrentTime
WHERE Id = @Id;";
        command.Parameters.AddWithValue("@Id", codeId.ToString("D"));
        command.Parameters.AddWithValue("@CurrentTime", ToDbValue(currentTime));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureInitialized()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
    Id TEXT NOT NULL PRIMARY KEY,
    UserName TEXT NOT NULL COLLATE NOCASE UNIQUE,
    Email TEXT NOT NULL,
    PasswordHash TEXT NOT NULL,
    IsLoginAllowed INTEGER NOT NULL,
    AttributesJson TEXT NOT NULL,
    TwoFactorMethodsJson TEXT NOT NULL,
    FailedLoginCount INTEGER NOT NULL,
    LockedUntilUtc TEXT NULL,
    PasswordChangedAtUtc TEXT NULL,
    PasswordHistoryHashesJson TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS UserAccesses (
    UserId TEXT NOT NULL,
    RoleId TEXT NOT NULL,
    RoleName TEXT NOT NULL,
    ScopeId TEXT NOT NULL,
    ScopeName TEXT NOT NULL,
    PRIMARY KEY (UserId, RoleId, ScopeId)
);

CREATE TABLE IF NOT EXISTS Sessions (
    SessionId TEXT NOT NULL PRIMARY KEY,
    UserId TEXT NOT NULL,
    IdentityProvider TEXT NOT NULL,
    Subject TEXT NOT NULL,
    IssuedAtUtc TEXT NOT NULL,
    ExpiresAtUtc TEXT NOT NULL,
    RevokedAtUtc TEXT NULL,
    LastSeenAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS OtpCodes (
    Id TEXT NOT NULL PRIMARY KEY,
    UserId TEXT NOT NULL,
    ProviderName TEXT NOT NULL COLLATE NOCASE,
    Code TEXT NOT NULL,
    ExpiresAtUtc TEXT NOT NULL,
    UsedAtUtc TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_Sessions_UserId ON Sessions(UserId);
CREATE INDEX IF NOT EXISTS IX_OtpCodes_UserProvider ON OtpCodes(UserId, ProviderName);";
        command.ExecuteNonQuery();
    }

    private void EnsureSeeded(IPasswordHashService passwordHashService)
    {
        using var connection = OpenConnection();
        using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = "SELECT COUNT(1) FROM Users WHERE UserName = @UserName COLLATE NOCASE;";
        existsCommand.Parameters.AddWithValue("@UserName", "admin");
        var hasAdmin = Convert.ToInt32(existsCommand.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        if (hasAdmin)
        {
            return;
        }

        var adminUser = new User
        {
            Id = Guid.Parse("4be2af39-79fa-4c13-bae3-f0a3ddfd0d73"),
            UserName = "admin",
            Email = "admin@ronauth.local",
            IsLoginAllowed = true,
            PasswordChangedAtUtc = DateTimeOffset.UtcNow,
        };
        adminUser.PasswordHash = passwordHashService.HashPassword(adminUser, "Admin123!");

        using var transaction = connection.BeginTransaction();
        using (var userCommand = connection.CreateCommand())
        {
            userCommand.Transaction = transaction;
            userCommand.CommandText = @"
INSERT INTO Users (
    Id,
    UserName,
    Email,
    PasswordHash,
    IsLoginAllowed,
    AttributesJson,
    TwoFactorMethodsJson,
    FailedLoginCount,
    LockedUntilUtc,
    PasswordChangedAtUtc,
    PasswordHistoryHashesJson)
VALUES (
    @Id,
    @UserName,
    @Email,
    @PasswordHash,
    @IsLoginAllowed,
    @AttributesJson,
    @TwoFactorMethodsJson,
    @FailedLoginCount,
    @LockedUntilUtc,
    @PasswordChangedAtUtc,
    @PasswordHistoryHashesJson);";
            BindUser(userCommand, adminUser);
            userCommand.ExecuteNonQuery();
        }

        using (var accessCommand = connection.CreateCommand())
        {
            accessCommand.Transaction = transaction;
            accessCommand.CommandText = @"
INSERT INTO UserAccesses (
    UserId,
    RoleId,
    RoleName,
    ScopeId,
    ScopeName)
VALUES (
    @UserId,
    @RoleId,
    @RoleName,
    @ScopeId,
    @ScopeName);";
            accessCommand.Parameters.AddWithValue("@UserId", adminUser.Id.ToString("D"));
            accessCommand.Parameters.AddWithValue("@RoleId", "8d03b969-4914-4f9f-af39-9b4ff1df8078");
            accessCommand.Parameters.AddWithValue("@RoleName", "SystemAdministrator");
            accessCommand.Parameters.AddWithValue("@ScopeId", "40fef5fc-98af-4318-b23d-8db53dd779e5");
            accessCommand.Parameters.AddWithValue("@ScopeName", "RonFlow");
            accessCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void BindUser(SqliteCommand command, User user)
    {
        command.Parameters.AddWithValue("@Id", user.Id.ToString("D"));
        command.Parameters.AddWithValue("@UserName", user.UserName);
        command.Parameters.AddWithValue("@Email", user.Email);
        command.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
        command.Parameters.AddWithValue("@IsLoginAllowed", user.IsLoginAllowed ? 1 : 0);
        command.Parameters.AddWithValue("@AttributesJson", Serialize(user.Attributes));
        command.Parameters.AddWithValue("@TwoFactorMethodsJson", Serialize(user.TwoFactorMethods));
        command.Parameters.AddWithValue("@FailedLoginCount", user.FailedLoginCount);
        command.Parameters.AddWithValue("@LockedUntilUtc", ToNullableDbValue(user.LockedUntilUtc));
        command.Parameters.AddWithValue("@PasswordChangedAtUtc", ToNullableDbValue(user.PasswordChangedAtUtc));
        command.Parameters.AddWithValue("@PasswordHistoryHashesJson", Serialize(user.PasswordHistoryHashes));
    }

    private static void BindSession(SqliteCommand command, AuthSession session)
    {
        command.Parameters.AddWithValue("@SessionId", session.SessionId);
        command.Parameters.AddWithValue("@UserId", session.UserId.ToString("D"));
        command.Parameters.AddWithValue("@IdentityProvider", session.IdentityProvider);
        command.Parameters.AddWithValue("@Subject", session.Subject);
        command.Parameters.AddWithValue("@IssuedAtUtc", ToDbValue(session.IssuedAtUtc));
        command.Parameters.AddWithValue("@ExpiresAtUtc", ToDbValue(session.ExpiresAtUtc));
        command.Parameters.AddWithValue("@RevokedAtUtc", ToNullableDbValue(session.RevokedAtUtc));
        command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbValue(session.LastSeenAtUtc));
    }

    private static void BindOtpCode(SqliteCommand command, OtpCodeRecord code)
    {
        command.Parameters.AddWithValue("@Id", code.Id.ToString("D"));
        command.Parameters.AddWithValue("@UserId", code.UserId.ToString("D"));
        command.Parameters.AddWithValue("@ProviderName", code.ProviderName);
        command.Parameters.AddWithValue("@Code", code.Code);
        command.Parameters.AddWithValue("@ExpiresAtUtc", ToDbValue(code.ExpiresAtUtc));
        command.Parameters.AddWithValue("@UsedAtUtc", ToNullableDbValue(code.UsedAtUtc));
    }

    private static User? ReadUser(SqliteDataReader reader)
    {
        if (!reader.Read())
        {
            return null;
        }

        return new User
        {
            Id = Guid.Parse(reader.GetString(0)),
            UserName = reader.GetString(1),
            Email = reader.GetString(2),
            PasswordHash = reader.GetString(3),
            IsLoginAllowed = reader.GetInt64(4) == 1,
            Attributes = DeserializeAttributes(reader.GetString(5)),
            TwoFactorMethods = DeserializeList<UserTwoFactorMethod>(reader.GetString(6)),
            FailedLoginCount = reader.GetInt32(7),
            LockedUntilUtc = ReadNullableDateTimeOffset(reader, 8),
            PasswordChangedAtUtc = ReadNullableDateTimeOffset(reader, 9),
            PasswordHistoryHashes = DeserializeList<string>(reader.GetString(10)),
        };
    }

    private static AuthSession? ReadSession(SqliteDataReader reader)
    {
        if (!reader.Read())
        {
            return null;
        }

        return new AuthSession
        {
            SessionId = reader.GetString(0),
            UserId = Guid.Parse(reader.GetString(1)),
            IdentityProvider = reader.GetString(2),
            Subject = reader.GetString(3),
            IssuedAtUtc = ReadRequiredDateTimeOffset(reader, 4),
            ExpiresAtUtc = ReadRequiredDateTimeOffset(reader, 5),
            RevokedAtUtc = ReadNullableDateTimeOffset(reader, 6),
            LastSeenAtUtc = ReadRequiredDateTimeOffset(reader, 7),
        };
    }

    private static OtpCodeRecord? ReadOtpCode(SqliteDataReader reader)
    {
        if (!reader.Read())
        {
            return null;
        }

        return new OtpCodeRecord
        {
            Id = Guid.Parse(reader.GetString(0)),
            UserId = Guid.Parse(reader.GetString(1)),
            ProviderName = reader.GetString(2),
            Code = reader.GetString(3),
            ExpiresAtUtc = ReadRequiredDateTimeOffset(reader, 4),
            UsedAtUtc = ReadNullableDateTimeOffset(reader, 5),
        };
    }

    private static Dictionary<string, string> DeserializeAttributes(string json)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
            ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    private static List<T> DeserializeList<T>(string json)
    {
        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string ToDbValue(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static object ToNullableDbValue(DateTimeOffset? value)
    {
        return value.HasValue ? ToDbValue(value.Value) : DBNull.Value;
    }

    private static DateTimeOffset ReadRequiredDateTimeOffset(SqliteDataReader reader, int ordinal)
    {
        return DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}