using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace TaskApi.IntegrationTests;

public sealed class TaskApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.2-management-alpine").Build();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public string NewDatabaseConnectionString() =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = $"tasks_{Guid.NewGuid():N}",
        }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Tasks", _postgres.GetConnectionString());
        builder.UseSetting("RabbitMq:Uri", _rabbitMq.GetConnectionString());
    }
}

[CollectionDefinition(nameof(TaskApiCollection))]
public sealed class TaskApiCollection : ICollectionFixture<TaskApiFactory>;
