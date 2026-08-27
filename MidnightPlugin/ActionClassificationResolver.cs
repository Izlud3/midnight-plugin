using MidnightPlugin.Core;
using ActionSheet = Lumina.Excel.Sheets.Action;

namespace MidnightPlugin;

public sealed class ActionClassificationResolver
{
    private readonly Dictionary<uint, ActionClassificationInfo> values = [];

    public ActionClassificationInfo Resolve(uint actionId)
    {
        if (values.TryGetValue(actionId, out var value))
        {
            return value;
        }

        var sheet = Plugin.DataManager.GetExcelSheet<ActionSheet>();
        if (!sheet.TryGetRow(actionId, out var action))
        {
            value = new ActionClassificationInfo(
                actionId,
                $"Action {actionId}",
                0,
                0,
                false,
                ActionTimingClass.Unknown,
                false);
        }
        else
        {
            var name = string.IsNullOrWhiteSpace(action.Name.ToString())
                ? $"Action {actionId}"
                : action.Name.ToString();
            var categoryId = action.ActionCategory.RowId;

            value = new ActionClassificationInfo(
                actionId,
                name,
                action.Icon,
                categoryId,
                action.IsPlayerAction,
                ActionTimingClassifier.Classify(categoryId),
                true);
        }

        values[actionId] = value;
        return value;
    }

    public void Clear()
    {
        values.Clear();
    }
}

public readonly record struct ActionClassificationInfo(
    uint ActionId,
    string Name,
    uint IconId,
    uint ActionCategoryId,
    bool IsPlayerAction,
    ActionTimingClass TimingClass,
    bool IsResolved);
