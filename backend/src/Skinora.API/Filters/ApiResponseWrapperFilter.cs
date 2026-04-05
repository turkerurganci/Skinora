using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Skinora.Shared.Models;

namespace Skinora.API.Filters;

public class ApiResponseWrapperFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult objectResult
            && objectResult.StatusCode is null or (>= 200 and < 300)
            && !IsAlreadyWrapped(objectResult.Value))
        {
            var traceId = context.HttpContext.TraceIdentifier;
            var wrapped = ApiResponse<object>.Ok(objectResult.Value!, traceId);
            objectResult.Value = wrapped;
            objectResult.StatusCode ??= 200;
        }

        await next();
    }

    private static bool IsAlreadyWrapped(object? value)
    {
        if (value is null) return false;

        var type = value.GetType();
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ApiResponse<>);
    }
}
