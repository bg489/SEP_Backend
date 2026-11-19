namespace AutoScenarioX.Api.Common.Exceptions;

/// <summary>
/// Gốc của mọi exception nghiệp vụ. Mỗi loại con được GlobalExceptionHandler
/// chuyển thành một HTTP status tương ứng. Service chỉ ném exception, không biết về HTTP.
/// </summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Chưa đăng nhập hoặc token không hợp lệ -> 401.</summary>
public sealed class UnauthorizedException(string code, string message) : AppException(code, message);

/// <summary>Đã đăng nhập nhưng không đủ quyền -> 403.</summary>
public sealed class ForbiddenException(string code, string message) : AppException(code, message);

/// <summary>Không tìm thấy tài nguyên -> 404.</summary>
public sealed class NotFoundException(string code, string message) : AppException(code, message);

/// <summary>Xung đột trạng thái / trùng dữ liệu / ghi đè đồng thời -> 409.</summary>
public sealed class ConflictException(string code, string message) : AppException(code, message);

/// <summary>Vi phạm luật nghiệp vụ -> 422.</summary>
public sealed class BusinessRuleException(string code, string message) : AppException(code, message);

/// <summary>
/// File import đúng cú pháp nhưng nội dung không qua được validate 3 tầng -> 422,
/// kèm nguyên report để người dùng biết sai ở đâu mà sửa file gốc.
/// </summary>
public sealed class ImportValidationException(string message, object report)
    : AppException(Constants.ErrorCodes.ImportValidationFailed, message)
{
    /// <summary>Report dạng { stoppedAt, errors[], warnings[] }.</summary>
    public object Report { get; } = report;
}

/// <summary>Dữ liệu đầu vào không hợp lệ -> 400, kèm danh sách lỗi theo trường.</summary>
public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : AppException(Constants.ErrorCodes.InvalidRequest, "The submitted data is invalid")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
