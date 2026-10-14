namespace TaskApi.Idempotency;

public static class IdempotencyExtensions
{
    public static IServiceCollection AddIdempotency(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHostedService<IdempotencyCleanupService>();

        return services;
    }

    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotencyMiddleware>();

    public static RouteHandlerBuilder WithIdempotency(this RouteHandlerBuilder builder) =>
        builder
            .WithMetadata(new IdempotentAttribute())
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
}
