using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class SettingsManagerTests
{
    [Theory]
    [InlineData("", 1.0)]
    [InlineData("not-a-number", 1.0)]
    [InlineData("-0.1", 1.0)]
    [InlineData("2.1", 1.0)]
    [InlineData("0.5", 0.5)]
    [InlineData("2.0", 2.0)]
    public void ParseTemperature_returns_default_or_valid_temperature(string value, double expected)
    {
        double? parsed = LittleAgentsSettingsManager.ParseTemperature(value);

        Assert.Equal(expected, parsed);
    }
}
