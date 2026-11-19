using System.Text.Json;
using System.Text.Json.Nodes;
using AutoScenarioX.Api.Models.Entities;

namespace AutoScenarioX.Api.Validators.ScenarioContent;

/// <summary>Dữ liệu đầu vào dùng chung cho mọi luật validate.</summary>
public sealed class ScenarioValidationContext
{
    /// <summary>Nội dung NGUYÊN VĂN người dùng gửi - tầng 1 kiểm tra trên bản này.</summary>
    public required JsonElement Raw { get; init; }

    /// <summary>Nội dung đã điền default - tầng 2 và 3 dùng bản này.</summary>
    public required JsonObject Normalized { get; init; }

    /// <summary>Tên map lấy từ nội dung (có thể null nếu nội dung sai cấu trúc).</summary>
    public string? MapName { get; init; }

    /// <summary>Metadata của map đang dùng; null khi map không có trong DB.</summary>
    public MapMetadata? Map { get; init; }

    /// <summary>
    /// Bảng asx.map_metadata đã có dữ liệu hay chưa (bước B6 cần máy có CARLA).
    /// Chưa có thì tầng 3 chỉ cảnh báo chứ không báo lỗi, để không chặn cả luồng làm việc.
    /// </summary>
    public bool MapMetadataAvailable { get; init; }
}
