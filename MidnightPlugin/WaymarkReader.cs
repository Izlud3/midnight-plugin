using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using MidnightPlugin.Core;

namespace MidnightPlugin;

internal static class WaymarkReader
{
    private static readonly string[] Labels = ["A", "B", "C", "D", "1", "2", "3", "4"];

    public static unsafe IReadOnlyList<LimitCutWaymark> Snapshot()
    {
        var controller = MarkingController.Instance();
        if (controller is null) return [];

        var markers = controller->FieldMarkers;
        var result = new List<LimitCutWaymark>(Labels.Length);
        for (var index = 0; index < Math.Min(Labels.Length, markers.Length); index++)
        {
            ref var marker = ref markers[index];
            if (!marker.Active) continue;
            result.Add(new(Labels[index], new Vector2(marker.X / 1000f, marker.Z / 1000f)));
        }

        return result;
    }
}
