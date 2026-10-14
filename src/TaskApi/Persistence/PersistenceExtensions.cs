using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TaskApi.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration) =>
        services.AddDbContext<TaskDbContext>((serviceProvider, options) => options
            .UseNpgsql(configuration.GetConnectionString("Tasks"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(serviceProvider.GetServices<IInterceptor>()));

    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
        await db.Database.MigrateAsync();
    }
}
