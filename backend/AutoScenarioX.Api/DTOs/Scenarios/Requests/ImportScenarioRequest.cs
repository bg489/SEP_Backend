namespace AutoScenarioX.Api.DTOs.Scenarios.Requests;

/// <summary>Body của POST /api/v1/scenarios/import (multipart/form-data).</summary>
public sealed class ImportScenarioRequest
{
    /// <summary>File scenario: .json, .yaml hoặc .yml.</summary>
    public IFormFile? File { get; set; }

    /// <summary>
    /// Ép định dạng thay vì suy từ đuôi file (json | yaml).
    /// Dùng khi file không có đuôi hoặc đuôi đặt sai.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>Đặt lại tên scenario. Bỏ trống thì lấy metadata.name trong file.</summary>
    public string? Name { get; set; }

    /// <summary>Đặt lại category. Bỏ trống thì lấy metadata.category trong file.</summary>
    public string? Category { get; set; }
}
