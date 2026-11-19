using AutoScenarioX.Api.Common.Constants;
using AutoScenarioX.Api.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace AutoScenarioX.Api.Middlewares;

/// <summary>
/// Xử lý lỗi TẬP TRUNG cho toàn API: chuyển exception thành ProblemDetails (RFC 7807).
/// Response lỗi luôn có: status, title, code (mã lỗi nghiệp vụ), traceId.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, code, title) = exception switch
        {
            UnauthorizedException e => (StatusCodes.Status401Unauthorized, e.Code, e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Code, e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Code, e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, e.Code, e.Message),
            ImportValidationException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Message),
            VariantValidationException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Message),
            BusinessRuleException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Message),
            RequestValidationException e => (StatusCodes.Status400BadRequest, e.Code, e.Message),
            // Lỗi SQL (thiếu bảng, sai cột...) là lỗi server, không phải mất kết nối DB.
            PostgresException => (StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "An unexpected error occurred"),
            NpgsqlException or TimeoutException =>
                (StatusCodes.Status503ServiceUnavailable, ErrorCodes.DatabaseUnavailable, "Unable to connect to the database"),
            _ => (StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "An unexpected error occurred")
        };

        if (status >= 500)
            logger.LogError(exception, "Lỗi {Code} tại {Path}", code, httpContext.Request.Path);
        else
            logger.LogInformation("Lỗi nghiệp vụ {Code} ({Status}) tại {Path}: {Message}",
                code, status, httpContext.Request.Path, exception.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://autoscenariox.local/errors/{code.ToLowerInvariant()}",
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        if (exception is RequestValidationException validation)
            problem.Extensions["errors"] = validation.Errors;

        if (exception is ImportValidationException import)
            problem.Extensions["report"] = import.Report;

        if (exception is VariantValidationException variant)
        {
            problem.Extensions["variantIndex"] = variant.VariantIndex;
            problem.Extensions["parameterValues"] = variant.ParameterValues;
            problem.Extensions["report"] = variant.Report;
        }

        // Chỉ lộ chi tiết lỗi hệ thống khi đang dev
        if (status >= 500 && environment.IsDevelopment())
            problem.Detail = exception.GetBaseException().Message;

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
