using PerDeviceMixer.App;

namespace PerDeviceMixer.App.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.0.0", "1.0.0-preview.9", 1)]
    [InlineData("1.0.1-preview.1", "1.0.0", 1)]
    [InlineData("1.0.0-preview.2", "1.0.0-preview.10", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    public void CompareToUsesSemanticVersionOrdering(string leftText, string rightText, int expectedSign)
    {
        Assert.True(ReleaseVersion.TryParse(leftText, out var left));
        Assert.True(ReleaseVersion.TryParse(rightText, out var right));

        Assert.Equal(expectedSign, Math.Sign(left!.CompareTo(right)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("version-one")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-alpha..1")]
    [InlineData("1.0.0-01")]
    public void TryParseRejectsMalformedVersions(string value)
    {
        Assert.False(ReleaseVersion.TryParse(value, out _));
    }
}
