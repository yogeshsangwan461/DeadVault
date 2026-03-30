using DeadVault.Core.Services;
using DeadVault.Core.Models;
using DeadVault.Store.Models;
using Xunit;

namespace DeadVault.Tests;

public class AttributionServiceTests
{
    [Fact]
    public void AppendCommitTrailers_RoundTripsSummary()
    {
        var service = new AttributionService();
        var summary = new SnapshotAttributionSummary
        {
            PrimaryAuthor = AttributionAuthorKinds.AI,
            AiFiles = 3,
            HumanFiles = 1,
            WatermarkedFiles = 3,
            ProcessedTextBytes = 2048,
        };

        var message = service.AppendCommitTrailers("[v1.2.3] [patch] test", summary);
        var parsed = service.ParseCommitTrailers(message);

        Assert.Equal(AttributionAuthorKinds.AI, parsed.PrimaryAuthor);
        Assert.Equal(3, parsed.AiFiles);
        Assert.Equal(1, parsed.HumanFiles);
        Assert.Equal(3, parsed.WatermarkedFiles);
        Assert.Equal(2048, parsed.ProcessedTextBytes);
    }

    [Theory]
    [InlineData("human", AttributionAuthorKinds.Human)]
    [InlineData("AI", AttributionAuthorKinds.AI)]
    [InlineData(" mixed ", AttributionAuthorKinds.Mixed)]
    [InlineData("who-knows", AttributionAuthorKinds.Unknown)]
    public void NormalizeAuthor_ProducesExpectedValue(string input, string expected)
    {
        Assert.Equal(expected, AttributionAuthorKinds.Normalize(input));
    }

    [Fact]
    public void ProjectConfig_Normalize_AppliesReasonableDefaults()
    {
        var project = new ProjectConfig
        {
            DebounceSeconds = 0,
            SessionTimeoutMinutes = 0,
            AttributionAuthor = "mystery",
            AttributionTextBudgetBytes = 0,
        };

        project.Normalize();

        Assert.Equal(60, project.DebounceSeconds);
        Assert.Equal(120, project.SessionTimeoutMinutes);
        Assert.Equal(AttributionAuthorKinds.Human, project.AttributionAuthor);
        Assert.Equal(10 * 1024 * 1024, project.AttributionTextBudgetBytes);
    }
}
