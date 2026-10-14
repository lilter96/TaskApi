using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TaskApi.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<IInterceptor, OutboxInterceptor>();
        services.AddSingleton<RabbitMqPublisher>();
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}
