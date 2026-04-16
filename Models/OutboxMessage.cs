namespace ShopApp.Function.Models;

public sealed class OutboxMessage
{
    public long OutboxMessageId { get; init; }
    public Guid EventId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public int AggregateId { get; init; }
    public string Payload { get; init; } = string.Empty;
    public DateTime OccurredUtc { get; init; }
    public DateTime? PublishedUtc { get; init; }
    public int PublishAttempts { get; init; }
    public string? LastPublishError { get; init; }
}