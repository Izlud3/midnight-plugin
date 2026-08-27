namespace MidnightPlugin.Core;

public static class EncounterPullStartPolicy
{
    public static bool ShouldStart(
        bool isDancingMad,
        bool isDutyStarted,
        bool isInCombat,
        bool isDutyRecorderPlayback,
        bool hasEncounterSignal)
    {
        if (!isDancingMad) return false;
        if (isDutyStarted && isInCombat) return true;
        // Duty Recorder playback does not consistently expose either the duty,
        // combat, or playback condition flags. A caller-supplied encounter signal
        // must therefore be independently verified as a boss/encounter action.
        var liveFlagsAreIncomplete = !isDutyStarted || !isInCombat;
        return hasEncounterSignal && (isDutyRecorderPlayback || liveFlagsAreIncomplete);
    }
}
