using AssignHub.Api.Contracts;
using AssignHub.Domain;

namespace AssignHub.Api.Middleware;

// SPEC §9.0: DomainException → 400 with its message; anything else → 500 with a generic message.
internal sealed partial class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public const string UnexpectedError = "An unexpected error occurred.";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
        }
        catch (DomainException ex)
        {
            await WriteAsync(context, StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (BadHttpRequestException ex)
        {
            await WriteAsync(context, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            LogUnhandled(ex, context.Request.Method, context.Request.Path);
            await WriteAsync(context, StatusCodes.Status500InternalServerError, UnexpectedError);
        }
    }

    private static async Task WriteAsync(HttpContext context, int statusCode, string error)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(ApiResponse.Failure(error), context.RequestAborted);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}.")]
    private partial void LogUnhandled(Exception exception, string method, string path);
}
