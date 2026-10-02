using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using ValidationException = AgriSage.Application.Common.Exceptions.ValidationException;

namespace AgriSage.Api.Filters;

// Runs the FluentValidation validator of every action argument that has one (request validation, coding rule #11),
// before the action and the service. Failures become a 400 through GlobalExceptionHandler.
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, List<string>>();

        foreach (var argument in context.ActionArguments.Values.Where(value => value is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument),
                context.HttpContext.RequestAborted);

            foreach (var failure in result.Errors)
            {
                var key = string.IsNullOrEmpty(failure.PropertyName) ? "request" : ToCamelCase(failure.PropertyName);
                if (!errors.TryGetValue(key, out var messages))
                {
                    errors[key] = messages = [];
                }

                messages.Add(failure.ErrorMessage);
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()));
        }

        await next();
    }

    private static string ToCamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
