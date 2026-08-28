using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace VoteCheck.Api;

/// <summary>
/// Turns upstream failures into honest status codes.
///
/// Everything this API serves comes from api.eduskunta.fi, so that service being unreachable,
/// slow, or rate-limiting us is a normal operating condition rather than a bug in VoteCheck.
/// Reporting those as a bare 500 would tell a client nothing and imply the fault is ours, so
/// they surface as 502/504 with a message naming upstream.
/// </summary>
internal sealed class UpstreamExceptionHandler : IExceptionHandler
{
    private readonly ILogger<UpstreamExceptionHandler> _logger;

    public UpstreamExceptionHandler(ILogger<UpstreamExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int status, string title, string detail)? mapped = exception switch
        {
            // A timeout surfaces as TaskCanceledException, but so does a client disconnecting.
            // Only the former should become a gateway timeout — if the caller went away, the
            // request token is the one that fired.
            TaskCanceledException or OperationCanceledException
                when !httpContext.RequestAborted.IsCancellationRequested =>
                (StatusCodes.Status504GatewayTimeout,
                 "Upstream timed out",
                 "api.eduskunta.fi did not respond in time. Try again shortly."),

            HttpRequestException http =>
                (StatusCodes.Status502BadGateway,
                 "Upstream unavailable",
                 $"api.eduskunta.fi could not be reached or returned an error" +
                 $"{(http.StatusCode is null ? "" : $" ({(int)http.StatusCode})")}."),

            _ => null,
        };

        if (mapped is null)
            return false; // Not ours to explain — fall through to the default handler.

        var (statusCode, title, detail) = mapped.Value;

        _logger.LogError(exception, "Upstream failure on {Path}: {Title}",
            httpContext.Request.Path, title);

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response
            .WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = statusCode,
                    Title = title,
                    Detail = detail,
                    Instance = httpContext.Request.Path,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }
}
