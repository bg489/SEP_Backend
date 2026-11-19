using System.Text.RegularExpressions;
using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AutoScenarioX.Api.Tests.Data;

public class CoreSchemaContractTests
{
    [Fact]
    public void Every_supplied_core_column_is_mapped_by_the_active_backend()
    {
        using var db = new AsxDbContext(new DbContextOptionsBuilder<AsxDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").UseSnakeCaseNamingConvention().Options);
        var text = File.ReadAllText(Path.Combine(TestData.RepoRoot, "docs", "schema-core.dbml"));
        var tables = Regex.Matches(text, @"(?m)^Table (\w+) \{");
        Assert.Equal(21, tables.Count);
        var columns = 0;
        foreach (Match match in tables)
        {
            var name = match.Groups[1].Value;
            var start = match.Index + match.Length;
            var cursor = start;
            var depth = 1;
            while (depth > 0 && cursor < text.Length)
            {
                if (text[cursor] == '{') depth++;
                if (text[cursor] == '}') depth--;
                cursor++;
            }
            var entity = Assert.Single(db.Model.GetEntityTypes(), e => e.GetTableName() == name);
            var identifier = StoreObjectIdentifier.Table(name, "asx");
            var properties = entity.GetProperties().ToDictionary(p => p.GetColumnName(identifier)!);
            foreach (Match column in Regex.Matches(text[start..(cursor - 1)],
                         @"(?m)^\s+(\w+)\s+(uuid|text|bigint|integer|boolean|timestamptz|jsonb|numeric)(?:\s+\[([^\r\n]*)\])?\s*$"))
            {
                columns++;
                var columnName = column.Groups[1].Value;
                Assert.True(properties.ContainsKey(columnName), $"Missing EF mapping: {name}.{columnName}");
                var attributes = column.Groups[3].Value;
                var nullable = !attributes.Contains("not null") && !Regex.IsMatch(attributes, @"\bpk\b");
                // PostgreSQL reports generated columns as nullable even where
                // their expression yields a required CLR value after insert.
                var generated = properties[columnName].ValueGenerated != ValueGenerated.Never;
                Assert.True(nullable == properties[columnName].IsNullable || nullable && generated,
                    $"{name}.{columnName}: DBML nullable={nullable}, EF nullable={properties[columnName].IsNullable}");
            }
        }
        Assert.Equal(199, columns);
    }
}
