using Microsoft.Data.SqlClient;
using ShopApp.Function.Models;

namespace ShopApp.Function.Services;

public interface IOutboxRepository
{
    Task<SqlAppLockHandle?> TryAcquireProcessingLockAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<OutboxMessage>> GetUnpublishedMessagesAsync(SqlConnection connection, CancellationToken cancellationToken);
    Task MarkPublishedAsync(SqlConnection connection, long outboxMessageId, DateTime publishedUtc, CancellationToken cancellationToken);
    Task MarkFailedAsync(SqlConnection connection, long outboxMessageId, Exception exception, CancellationToken cancellationToken);
}