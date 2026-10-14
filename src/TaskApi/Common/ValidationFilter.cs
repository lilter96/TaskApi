using System.Text.Json;
using FluentValidation;

namespace TaskApi.Common;

internal sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().Single();
        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);

        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(error => JsonNamingPolicy.CamelCase.ConvertName(error.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());

        return TypedResults.ValidationProblem(errors);
    }
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
