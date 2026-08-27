namespace MidnightPlugin.Core;

public static class ActionTimingClassifier
{
    public const uint SpellCategoryId = 2;
    public const uint WeaponskillCategoryId = 3;
    public const uint AbilityCategoryId = 4;

    /// <summary>
    /// Classifies a locally captured action by its Lumina action category.
    /// </summary>
    /// <remarks>
    /// The action-effect capture path has already verified that the effect came
    /// from the local player. Lumina's IsPlayerAction flag is not reliable for
    /// generated or unassignable player actions such as Paladin's Blade and
    /// Sword Oath combo actions, so it must not be used to reject a supported
    /// category here.
    /// </remarks>
    public static ActionTimingClass Classify(uint actionCategoryId) => actionCategoryId switch
        {
            SpellCategoryId or WeaponskillCategoryId => ActionTimingClass.Gcd,
            AbilityCategoryId => ActionTimingClass.Ogcd,
            _ => ActionTimingClass.Unknown,
        };
}
