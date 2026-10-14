using FluentValidation;
using TaskApi.Idempotency;
using TaskApi.Messaging;
using TaskApi.Observability;
using TaskApi.Persistence;
using TaskApi.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddOperationTransformer<IdempotencyKeyOperationTransformer>());
builder.Services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton);
builder.Services.AddSingleton<TimeProvider>(new MicrosecondTimeProvider(TimeProvider.System));
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddIdempotency(builder.Configuration);
builder.AddObservability();

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.UseIdempotency();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "TaskApi"));
}

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}

app.MapHealthEndpoints();
app.MapTaskEndpoints();

await app.RunAsync();

public partial class Program;
