using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContentReview.Api.Errors;

/// <summary>
/// Maps the exceptions that the application throws on purpose to their status code. Only text written for the
/// user is ever returned; the exception object itself is never serialized.
/// </summary>
public sealed class KnownExceptionHandler(IProblemDetailsService problems, ILogger<KnownExceptionHandler> logger)
    : IExceptionHandler
{
    public const string ConflictTitle = "The request conflicts with the current state of the resource.";

    private const string UniqueViolation = "23505";

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? details = exception switch
        {
            ValidationException validation => new ValidationProblemDetails(ModelStateErrors.ToErrors(validation.Errors))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            NotFoundException => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Resource not found." },
            ForbiddenException => new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "You do not have permission to perform this action.",
            },
            InvitationNotRedeemableException notRedeemable => InvitationNotRedeemable(notRedeemable.Reason),
            StaleVersionException stale => StaleVersion(stale),
            ReviewConflictException review => ReviewConflict(review),
            ConflictException conflict => Conflict(conflict.Message),
            DbUpdateConcurrencyException => Conflict("The resource was changed by someone else. Reload it and try again."),
            DbUpdateException { InnerException: PostgresException { SqlState: UniqueViolation } } =>
                Conflict("A resource with the same unique value already exists."),
            _ => null,
        };

        if (details is null)
        {
            return false;
        }

        logger.LogInformation("Request failed with {Status} ({ExceptionType})", details.Status, exception.GetType().Name);

        context.Response.StatusCode = details.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = details });
    }

    /// <summary>400 with the reason in words (<c>title</c>) and as a stable code (<c>reason</c>).</summary>
    private static ProblemDetails InvitationNotRedeemable(InvitationValidationResult reason)
    {
        var details = new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = reason.Message() };
        details.Extensions["reason"] = reason.ToString();
        return details;
    }

    /// <summary>409 that also says which version is current (<c>currentVersion</c>).</summary>
    private static ProblemDetails StaleVersion(StaleVersionException stale)
    {
        var details = Conflict(stale.Message);
        details.Extensions["currentVersion"] = stale.CurrentVersion;
        return details;
    }

    /// <summary>409 that says what the article is now (<c>currentStatus</c>, <c>currentVersion</c>).</summary>
    private static ProblemDetails ReviewConflict(ReviewConflictException review)
    {
        var details = Conflict(review.Message);
        details.Extensions["currentStatus"] = review.CurrentStatus;
        details.Extensions["currentVersion"] = review.CurrentVersion;
        return details;
    }

    private static ProblemDetails Conflict(string detail) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = ConflictTitle,
        Detail = detail,
    };
}

/// <summary>
/// Last resort: any other exception becomes a generic 500. The exception (message, stack trace) is logged on the
/// server only and never reaches the response, in any environment.
/// </summary>
public sealed class UnhandledExceptionHandler(IProblemDetailsService problems, ILogger<UnhandledExceptionHandler> logger)
    : IExceptionHandler
{
    public const string Title = "An unexpected error occurred.";

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = Title },
        });
    }
}
