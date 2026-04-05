namespace Skinora.Shared.Models;

public class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public ApiError? Error { get; init; }
    public string? TraceId { get; init; }

    public static ApiResponse<T> Ok(T data, string? traceId = null) => new()
    {
        Success = true,
        Data = data,
        Error = null,
        TraceId = traceId
    };

    public static ApiResponse<T> Fail(string code, string message, object? details = null, string? traceId = null) => new()
    {
        Success = false,
        Data = default,
        Error = new ApiError { Code = code, Message = message, Details = details },
        TraceId = traceId
    };
}

public class ApiError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public object? Details { get; init; }
}
