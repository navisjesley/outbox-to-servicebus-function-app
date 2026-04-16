namespace ShopApp.Function.Options;

public sealed class OutboxOptions
{
    public string SqlConnectionString { get; init; } = string.Empty;
    public int BatchSize { get; init; } = 100;
    public string AppLockName { get; init; } = "OutboxPublisher";
}