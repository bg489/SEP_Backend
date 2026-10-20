namespace AutoScenarioX.AuthApi.Auth;

public static class RoleNames
{
    public const string ScenarioEngineer = "Scenario Engineer";
    public const string TestEngineer = "Test Engineer";
    public const string TestReviewer = "Test Reviewer";
    public const string Administrator = "Administrator";

    public static readonly string[] All =
    [
        ScenarioEngineer,
        TestEngineer,
        TestReviewer,
        Administrator
    ];

    public static readonly string[] SelfSelectable =
    [
        ScenarioEngineer,
        TestEngineer,
        TestReviewer
    ];
}
