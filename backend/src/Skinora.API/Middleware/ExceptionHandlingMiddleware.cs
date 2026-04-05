using System.Text.Json;
using FluentValidation;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Models;

namespace Skinora.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, errorCode, message, details) = MapException(exception);
        var traceId = context.TraceIdentifier;

        if (statusCode == 500)
        {
            _logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", traceId);
        }
        else
        {
            _logger.LogWarning(exception, "Handled exception ({ErrorCode}). TraceId: {TraceId}", errorCode, traceId);
        }

        var response = ApiResponse<object>.Fail(errorCode, message, details, traceId);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private static (int StatusCode, string ErrorCode, string Message, object? Details) MapException(Exception exception)
    {
        return exception switch
        {
            ValidationException ve => (
                400,
                "VALIDATION_ERROR",
                "Validation failed.",
                ve.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray())),

            NotFoundException nf => (
                404,
                "NOT_FOUND",
                nf.Message,
                null),

            BusinessRuleException br => (
                422,
                br.ErrorCode,
                br.Message,
                null),

            DomainException de => (
                409,
                de.ErrorCode,
                de.Message,
                null),

            IntegrationException ie => (
                502,
                "INTEGRATION_ERROR",
                ie.Message,
                null),

            _ => (
                500,
                "INTERNAL_ERROR",
                "An unexpected error occurred.",
                null)
        };
    }
}
