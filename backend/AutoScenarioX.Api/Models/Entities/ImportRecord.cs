namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.import_records - mỗi lần import (kể cả thất bại).</summary>
public class ImportRecord
{
    public Guid Id { get; set; }
    public Guid? ScenarioId { get; set; }
    public string Format { get; set; } = default!;
    public string OriginalFileName { get; set; } = default!;
    public string OriginalHash { get; set; } = default!;
    /// <summary>Nguyên văn file người dùng nạp lên (migration 102). Nguồn truy xuất chính.</summary>
    public string? OriginalContent { get; set; }

    /// <summary>Đường dẫn Supabase Storage - chỉ dùng nếu sau này chuyển sang lưu file ngoài.</summary>
    public string? StoragePath { get; set; }
    public string Status { get; set; } = default!;           // SUCCEEDED | FAILED
    public string ImportReport { get; set; } = "{}";         // jsonb
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
