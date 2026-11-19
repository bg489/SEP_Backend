namespace AutoScenarioX.Api.Validators.ScenarioContent;

public interface IScenarioValidator
{
    Task<ScenarioValidationResult> ValidateAsync(ScenarioValidationContext context, CancellationToken ct = default);
}
