using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ConfigStructuredStringCodecTests
{
    [Fact]
    public void ParseIds_AcceptsTheExactGameWhitespaceSeparatorsAndRoundTripsCanonically()
    {
        var result = ConfigStructuredStringCodec.ParseIds(" 4012\t4001\r\n385 ");

        Assert.True(result.IsValid);
        Assert.Equal([4012, 4001, 385], result.Items);
        Assert.Equal("4012 4001 385", ConfigStructuredStringCodec.FormatIds(result.Items));
    }

    [Fact]
    public void ParseIds_ReportsInvalidTokensWithoutDiscardingValidIds()
    {
        var result = ConfigStructuredStringCodec.ParseIds("12 nope 34");

        Assert.False(result.IsValid);
        Assert.Equal([12, 34], result.Items);
        Assert.Contains(result.Errors, error => error.Contains("nope"));
    }

    [Fact]
    public void WeightedConditions_RequirePairsAndUseInvariantFiniteNumbers()
    {
        var valid = ConfigStructuredStringCodec.ParseWeightedConditions("1260 1.25\n1400 -2");
        var invalid = ConfigStructuredStringCodec.ParseWeightedConditions("1260 NaN 1400");

        Assert.True(valid.IsValid);
        Assert.Equal(
            [new WeightedConditionValue(1260, 1.25f), new WeightedConditionValue(1400, -2f)],
            valid.Items);
        Assert.Equal("1260 1.25 1400 -2", ConfigStructuredStringCodec.FormatWeightedConditions(valid.Items));
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, error => error.Contains("成对"));
        Assert.Contains(invalid.Errors, error => error.Contains("有限权重"));
    }

    [Fact]
    public void ConditionExpression_ParsesAndFormatsSemanticGroups()
    {
        var result = ConfigStructuredStringCodec.ParseConditionExpression("10 996 20 997 30");

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(
            [new ConditionExpressionTermValue(10, false), new ConditionExpressionTermValue(20, true)],
            result.Items[0].Terms);
        Assert.Equal([new ConditionExpressionTermValue(30, false)], result.Items[1].Terms);
        Assert.Equal("10 996 20 997 30", ConfigStructuredStringCodec.FormatConditionExpression(result.Items));
    }

    [Theory]
    [InlineData("997 10")]
    [InlineData("10 997")]
    [InlineData("10 997 997 20")]
    [InlineData("10 996")]
    [InlineData("10 996 997 20")]
    [InlineData("10 996 996 20")]
    public void ConditionExpression_RejectsEmptyGroupsAndDanglingOperators(string value)
    {
        var result = ConfigStructuredStringCodec.ParseConditionExpression(value);

        Assert.False(result.IsValid);
        Assert.Empty(result.Items);
    }
}
