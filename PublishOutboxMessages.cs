using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ShopApp.Function.Models;
using ShopApp.Function.Services;

namespace ShopApp.Function;

public sealed class PublishOutboxMessages
{
    private readonly IOutboxRepository _outboxRepository;
    private readonly ServiceBusSender _serviceBusSender;
    private readonly ILogger<PublishOutboxMessages> _logger;

    public PublishOutboxMessages(IOutboxRepository outboxRepository, ServiceBusSender serviceBusSender, 
    ILogger<PublishOutboxMessages> logger)
    {
        _outboxRepository = outboxRepository;
        _serviceBusSender = serviceBusSender;
        _logger = logger;
    }

    [Function(nameof(PublishOutboxMessages))]
    public async Task RunAsync([TimerTrigger("%OutboxScanSchedule%", UseMonitor = true)] TimerInfo timerInfo, 
    CancellationToken cancellationToken)
    {
        if (timerInfo.IsPastDue)
        {
            _logger.LogWarning("Outbox scan is running later than scheduled.");
        }

        await using var appLock = await _outboxRepository.TryAcquireProcessingLockAsync(cancellationToken);

        if (appLock is null)
        {
            _logger.LogInformation("Skipped this run because another instance is already scanning the outbox.");
            return;
        }

        var pendingMessages = await _outboxRepository.GetUnpublishedMessagesAsync(appLock.Connection, cancellationToken);

        if (pendingMessages.Count == 0)
        {
            _logger.LogInformation("No unpublished outbox messages were found.");
            return;
        }

        _logger.LogInformation("Found {Count} unpublished outbox message(s).", pendingMessages.Count);

        var publishedCount = 0;
        var failedCount = 0;

        foreach (var outboxMessage in pendingMessages)
        {
            try
            {
                var serviceBusMessage = CreateServiceBusMessage(outboxMessage);

                await _serviceBusSender.SendMessageAsync(serviceBusMessage, cancellationToken);
                await _outboxRepository.MarkPublishedAsync(
                    appLock.Connection,
                    outboxMessage.OutboxMessageId,
                    DateTime.UtcNow,
                    cancellationToken);

                publishedCount++;
            }
            catch (Exception exception)
            {
                failedCount++;

                await _outboxRepository.MarkFailedAsync(
                    appLock.Connection,
                    outboxMessage.OutboxMessageId,
                    exception,
                    cancellationToken);

                _logger.LogError(
                    exception,
                    "Failed to publish outbox message {OutboxMessageId} with EventId {EventId}.",
                    outboxMessage.OutboxMessageId,
                    outboxMessage.EventId);
            }
        }

        _logger.LogInformation(
            "Outbox scan finished. Published: {PublishedCount}. Failed: {FailedCount}.",
            publishedCount,
            failedCount);
    }

    private static ServiceBusMessage CreateServiceBusMessage(OutboxMessage outboxMessage)
    {
        var message = new ServiceBusMessage(BinaryData.FromString(outboxMessage.Payload))
        {
            MessageId = outboxMessage.EventId.ToString(),
            Subject = outboxMessage.EventType,
            ContentType = "application/json",
            CorrelationId = $"{outboxMessage.AggregateType}:{outboxMessage.AggregateId}",
        };

        message.ApplicationProperties["eventId"] = outboxMessage.EventId.ToString();
        message.ApplicationProperties["eventType"] = outboxMessage.EventType;
        message.ApplicationProperties["aggregateType"] = outboxMessage.AggregateType;
        message.ApplicationProperties["aggregateId"] = outboxMessage.AggregateId;
        message.ApplicationProperties["occurredUtc"] = outboxMessage.OccurredUtc.ToString("O");
        message.ApplicationProperties["outboxMessageId"] = outboxMessage.OutboxMessageId;

        return message;
    }
}