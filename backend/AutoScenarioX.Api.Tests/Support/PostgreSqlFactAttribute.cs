namespace AutoScenarioX.Api.Tests.Support;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASX_TEST_DATABASE")))
            Skip = "Disposable PostgreSQL was not supplied. Set ASX_TEST_DATABASE to an asx_ci_* database after applying migrations.";
    }
}
