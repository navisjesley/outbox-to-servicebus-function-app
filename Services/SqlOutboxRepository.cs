using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ShopApp.Function.Models;
using ShopApp.Function.Options;
using System.Data;

namespace ShopApp.Function.Services;

public sealed class SqlOutboxRepository : IOutboxRepository
{
    private readonly OutboxOptions _options;
    private readonly ILogger<SqlOutboxRepository> _logger;

    public SqlOutboxRepository(OutboxOptions options, ILogger<SqlOutboxRepository> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<SqlAppLockHandle?> TryAcquireProcessingLockAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_options.SqlConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            """
            DECLARE @result INT;
            EXEC @result = sp_getapplock
                @Resource = @Resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 0;
            SELECT @result;
            """,
            connection);

        command.Parameters.AddWithValue("@Resource", _options.AppLockName);

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));

        if (result < 0)
        {
            _logger.LogInformation("Could not acquire SQL application lock {LockName}. sp_getapplock returned {Result}.", _options.AppLockName, result);
            await connection.CloseAsync();
            await connection.DisposeAsync();
            return null;
        }

        return new SqlAppLockHandle(connection, _options.AppLockName);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetUnpublishedMessagesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var messages = new List<OutboxMessage>();

        await using var command = new SqlCommand(
            """
            SELECT TOP (@BatchSize)
                OutboxMessageId,
                EventId,
                EventType,
                AggregateType,
                AggregateId,
                Payload,
                OccurredUtc,
                PublishedUtc,
                PublishAttempts,
                LastPublishError
            FROM dbo.OutboxMessages WITH (READPAST)
            WHERE PublishedUtc IS NULL
            ORDER BY OutboxMessageId;
            """,
            connection);

        command.Parameters.Add(new SqlParameter("@BatchSize", SqlDbType.Int) { Value = _options.BatchSize });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new OutboxMessage
            {
                OutboxMessageId = reader.GetInt64(0),
                EventId = reader.GetGuid(1),
                EventType = reader.GetString(2),
                AggregateType = reader.GetString(3),
                AggregateId = reader.GetInt32(4),
                Payload = reader.GetString(5),
                OccurredUtc = reader.GetDateTime(6),
                PublishedUtc = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                PublishAttempts = reader.GetInt32(8),
                LastPublishError = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return messages;
    }

    public async Task MarkPublishedAsync(SqlConnection connection, long outboxMessageId, DateTime publishedUtc, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            UPDATE dbo.OutboxMessages
            SET PublishedUtc = @PublishedUtc,
                PublishAttempts = PublishAttempts + 1,
                LastPublishError = NULL
            WHERE OutboxMessageId = @OutboxMessageId
              AND PublishedUtc IS NULL;
            """,
            connection);

        command.Parameters.Add(new SqlParameter("@PublishedUtc", SqlDbType.DateTime2) { Value = publishedUtc });
        command.Parameters.Add(new SqlParameter("@OutboxMessageId", SqlDbType.BigInt) { Value = outboxMessageId });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(SqlConnection connection, long outboxMessageId, Exception exception, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            UPDATE dbo.OutboxMessages
            SET PublishAttempts = PublishAttempts + 1,
                LastPublishError = @LastPublishError
            WHERE OutboxMessageId = @OutboxMessageId
              AND PublishedUtc IS NULL;
            """,
            connection);

        command.Parameters.Add(new SqlParameter("@LastPublishError", SqlDbType.NVarChar, 2000)
        {
            Value = Truncate(exception.ToString(), 2000)
        });
        command.Parameters.Add(new SqlParameter("@OutboxMessageId", SqlDbType.BigInt) { Value = outboxMessageId });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}