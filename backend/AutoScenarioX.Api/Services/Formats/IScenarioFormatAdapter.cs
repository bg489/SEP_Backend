using System.Text.Json.Nodes;

namespace AutoScenarioX.Api.Services.Formats;

/// <summary>
/// Chuyển đổi giữa một định dạng file bên ngoài và nội dung scenario dạng JSON nội bộ.
///
/// JSON nội bộ (schema v1.0) là CHUẨN CHÍNH: mọi adapter phải trả về đúng dạng đó,
/// và nhận đúng dạng đó khi ghi ra. Thêm định dạng mới = thêm một lớp cài đặt
/// interface này rồi đăng ký DI, KHÔNG sửa import/export service.
/// </summary>
public interface IScenarioFormatAdapter
{
    /// <summary>Mã định dạng, lấy từ <see cref="Common.Constants.ScenarioFormats"/>.</summary>
    string Format { get; }

    /// <summary>Đuôi file nhận biết định dạng này, gồm cả dấu chấm (".yaml").</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Đuôi file dùng khi export.</summary>
    string DefaultExtension { get; }

    /// <summary>Kiểu MIME trả về khi export.</summary>
    string MediaType { get; }

    /// <summary>
    /// Đọc nội dung file thành JSON object.
    /// Ném <see cref="ScenarioFormatException"/> khi cú pháp sai hoặc gốc không phải object.
    /// </summary>
    JsonObject Parse(string text);

    /// <summary>Ghi nội dung scenario ra chuỗi theo định dạng này.</summary>
    string Serialize(JsonObject content);
}

/// <summary>Lỗi cú pháp của file import. Service dịch thành 400 cho người dùng.</summary>
public sealed class ScenarioFormatException(string message) : Exception(message);
