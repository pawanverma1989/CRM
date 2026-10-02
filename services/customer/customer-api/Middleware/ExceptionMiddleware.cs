namespace CustomerApi.Middleware;
using System.Text.Json;
using CustomerApi.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>
/// RFC 7807 problem responses, same shape as the identity service. Never logs personal data —
/// ids only (NFR-7) — and never leaks a stack trace.
/// </summary>
public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    /// <summary>Unique-violation SQLSTATE; the partial unique indexes are the duplicate backstop (DUP-1).</summary>
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";

    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception ex) { await HandleAsync(context, ex); }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var (status, title) = ex switch
        {
            NotFoundException => (404, "Not Found"),
            UnauthorizedException => (401, "Unauthorized"),
            ForbiddenException => (403, "Forbidden"),
            ConflictException => (409, "Conflict"),
            DbUpdateConcurrencyException => (409, "Conflict"),
            CustomerValidationException => (400, "Validation Failed"),
            FluentValidation.ValidationException => (400, "Validation Failed"),
            DbUpdateException due when SqlState(due) == UniqueViolation => (409, "Conflict"),
            DbUpdateException due when SqlState(due) == CheckViolation => (400, "Validation Failed"),
            _ => (500, "Internal Server Error")
        };

        if (status == 500)
            logger.LogError(ex, "Unhandled exception on {Method} {Path}.", context.Request.Method, context.Request.Path);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = Detail(ex, status)
        };

        switch (ex)
        {
            case ConflictException conflict when conflict.ConflictingRecordId.HasValue:
                // DUP-1 / DEL-2: name the record that blocks the save (AC-3, AC-4, AC-14).
                problem.Extensions["conflictingRecord"] = new
                {
                    id = conflict.ConflictingRecordId,
                    name = conflict.ConflictingRecordName
                };
                break;

            case CustomerValidationException cve:
                problem.Extensions["errors"] = cve.Errors;
                break;

            case FluentValidation.ValidationException fve:
                problem.Extensions["errors"] = fve.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                break;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }

    private static string Detail(Exception ex, int status) => (ex, status) switch
    {
        (DbUpdateConcurrencyException, _) =>
            "The record changed since you loaded it. Reload it and apply your edit again.",
        (DbUpdateException, 409) =>
            "Another live record already uses one of these values.",
        (DbUpdateException, 400) =>
            "One or more values are not allowed by the database rules.",
        (_, >= 500) => "An unexpected error occurred.",
        _ => ex.Message
    };

    private static string? SqlState(DbUpdateException ex)
        => (ex.InnerException as PostgresException)?.SqlState;
}
