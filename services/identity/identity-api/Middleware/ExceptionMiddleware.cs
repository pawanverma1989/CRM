namespace IdentityApi.Middleware;
using IdentityApi.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
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
            FluentValidation.ValidationException => (400, "Validation Failed"),
            _ => (500, "Internal Server Error")
        };

        if (status == 500)
            logger.LogError(ex, "Unhandled exception.");

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status < 500 ? ex.Message : "An unexpected error occurred."
        };

        if (ex is FluentValidation.ValidationException ve)
        {
            problem.Extensions["errors"] = ve.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
