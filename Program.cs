using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopApp.Function.Options;
using ShopApp.Function.Services;

var host = new HostBuilder().ConfigureFunctionsWorkerDefaults().ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        var outboxOptions = new OutboxOptions
        {
            SqlConnectionString = GetRequiredSetting(configuration, "Outbox:SqlConnectionString"),
            AppLockName = configuration["Outbox:AppLockName"] ?? "OutboxPublisher",
            BatchSize = ParseInt(configuration["Outbox:BatchSize"], 100)
        };

        var serviceBusOptions = new ServiceBusTopicOptions
        {
            ConnectionString = GetRequiredSetting(configuration, "ServiceBusTopic:ConnectionString"),
            TopicName = GetRequiredSetting(configuration, "ServiceBusTopic:TopicName")
        };

        services.AddSingleton(outboxOptions);
        services.AddSingleton(serviceBusOptions);

        services.AddSingleton(_ => new ServiceBusClient(serviceBusOptions.ConnectionString));
        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<ServiceBusClient>();
            return client.CreateSender(serviceBusOptions.TopicName);
        });

        services.AddSingleton<IOutboxRepository, SqlOutboxRepository>();
    })
    .Build();

await host.RunAsync();

static string GetRequiredSetting(IConfiguration configuration, string key)
{
    var value = configuration[key];

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Missing required configuration value '{key}'.");
    }

    return value;
}

static int ParseInt(string? value, int defaultValue)
{
    return int.TryParse(value, out var parsed) ? parsed : defaultValue;
}