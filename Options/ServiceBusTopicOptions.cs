namespace ShopApp.Function.Options;

public sealed class ServiceBusTopicOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string TopicName { get; init; } = string.Empty;
}