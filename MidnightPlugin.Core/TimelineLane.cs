namespace MidnightPlugin.Core;

public enum TimelineLane
{
    Ogcd,
    Gcd,
}

public static class TimelineLaneResolver
{
    public static bool TryResolve(ActionTimingClass timingClass, out TimelineLane lane)
    {
        switch (timingClass)
        {
            case ActionTimingClass.Ogcd:
                lane = TimelineLane.Ogcd;
                return true;
            case ActionTimingClass.Gcd:
                lane = TimelineLane.Gcd;
                return true;
            default:
                lane = default;
                return false;
        }
    }
}
