using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace TaskApi.Idempotency;

internal sealed class IdempotencyKeyOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotentAttribute>().Any())
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = IdempotencyMiddleware.KeyHeader,
                In = ParameterLocation.Header,
                Description = "Repeating the request with the same key returns the stored response instead of executing it again.",
                Schema = new OpenApiSchema { Type = "string", MaxLength = IdempotencyRecord.MaxKeyLength },
            });
        }

        return Task.CompletedTask;
    }
}
