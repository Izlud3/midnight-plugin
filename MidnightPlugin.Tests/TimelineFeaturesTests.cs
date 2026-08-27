using MidnightPlugin.Core;
using Xunit;

namespace MidnightPlugin.Tests;

public sealed class TimelineFeaturesTests
{
    [Theory]
    [InlineData(ActionTimingClassifier.SpellCategoryId, ActionTimingClass.Gcd)]
    [InlineData(ActionTimingClassifier.WeaponskillCategoryId, ActionTimingClass.Gcd)]
    [InlineData(ActionTimingClassifier.AbilityCategoryId, ActionTimingClass.Ogcd)]
    [InlineData(0, ActionTimingClass.Unknown)]
    [InlineData(1, ActionTimingClass.Unknown)]
    [InlineData(99, ActionTimingClass.Unknown)]
    public void ActionCategoriesAreClassifiedSafely(uint categoryId, ActionTimingClass expected)
    {
        Assert.Equal(expected, ActionTimingClassifier.Classify(categoryId));
    }

    [Theory]
    [InlineData(25748, ActionTimingClass.Gcd)]
    [InlineData(25749, ActionTimingClass.Gcd)]
    [InlineData(25750, ActionTimingClass.Gcd)]
    [InlineData(36922, ActionTimingClass.Ogcd)]
    [InlineData(36918, ActionTimingClass.Gcd)]
    [InlineData(36919, ActionTimingClass.Gcd)]
    public void GeneratedPlayerActionsUseTheirSupportedLuminaCategory(uint actionId, ActionTimingClass expected)
    {
        var categoryId = actionId switch
        {
            25748 or 25749 or 25750 => ActionTimingClassifier.SpellCategoryId,
            36922 => ActionTimingClassifier.AbilityCategoryId,
            36918 or 36919 => ActionTimingClassifier.WeaponskillCategoryId,
            _ => 0u,
        };

        Assert.Equal(expected, ActionTimingClassifier.Classify(categoryId));
    }

    [Fact]
    public void UnknownTimingClassHasNoTimelineLane()
    {
        Assert.False(TimelineLaneResolver.TryResolve(ActionTimingClass.Unknown, out _));
    }
}
