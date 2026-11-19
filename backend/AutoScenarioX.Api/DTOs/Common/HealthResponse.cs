namespace AutoScenarioX.Api.DTOs.Common;

/// <summary>MissingTables: bảng Flow 1 cần có mà schema asx chưa có (rỗng khi OK). Bảng của flow khác được bỏ qua.</summary>
public sealed record HealthResponse(string Status, IReadOnlyList<string> MissingTables, string Postgres);
