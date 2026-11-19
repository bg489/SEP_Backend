namespace AutoScenarioX.Api.Models.Entities;

/// <summary>asx.failure_analyses — một lần đi Flow 4 trên một campaign.</summary>
public class AnalysisSession
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public string Status { get; set; } = default!;
    public string Summary { get; set; } = "";
    public string? ReviewNote { get; set; }
    public string? Effectiveness { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Campaign Campaign { get; set; } = default!;
    public ICollection<AnalysisFault> Faults { get; set; } = new List<AnalysisFault>();
}

/// <summary>Một fault đang đi trong phiên phân tích. Trước bước similar, mỗi fault gắn một lượt.</summary>
public class AnalysisFault
{
    public Guid Id { get; set; }
    public Guid AnalysisId { get; set; }
    public Guid RootScenarioId { get; set; }
    public string RootScenarioCode { get; set; } = default!;
    public string RootScenarioName { get; set; } = default!;
    public string CheckName { get; set; } = default!;
    public string Status { get; set; } = default!;
    public Guid FirstRunId { get; set; }
    public decimal FirstT { get; set; }
    public string? Explanation { get; set; }
    public string? ReportText { get; set; }

    public AnalysisSession Analysis { get; set; } = default!;
    public ICollection<AnalysisFaultRun> Runs { get; set; } = new List<AnalysisFaultRun>();
}

public class AnalysisFaultRun
{
    public Guid FaultId { get; set; }
    public Guid RunId { get; set; }
    public bool IsPrimary { get; set; }

    public AnalysisFault Fault { get; set; } = default!;
}
