using Dalamud.Interface.Textures;
using MidnightPlugin.Core;
using ActionSheet = Lumina.Excel.Sheets.Action;

namespace MidnightPlugin;

public sealed class ActionIconResolver
{
    private static readonly IReadOnlyDictionary<string, uint> KnownReferenceActionIds = new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        // The reference export predates the action-ID enrichment for this action.
        ["expiacion"] = 25747,
    };

    private readonly Dictionary<uint, ActionMetadata> metadata = [];
    private readonly Dictionary<string, ActionMetadata> referenceMetadata = [];
    private readonly ActionClassificationResolver classificationResolver = new();
    private Dictionary<string, uint>? nameIndex;

    public bool TryResolve(uint actionId, out ActionMetadata actionMetadata)
    {
        if (metadata.TryGetValue(actionId, out actionMetadata))
        {
            return actionMetadata.IsResolved;
        }

        var classification = classificationResolver.Resolve(actionId);
        if (!classification.IsResolved)
        {
            actionMetadata = new ActionMetadata(
                classification.Name,
                null,
                classification.TimingClass,
                false,
                classification.ActionCategoryId,
                classification.IsPlayerAction);
            metadata[actionId] = actionMetadata;
            return false;
        }

        ISharedImmediateTexture? texture = null;
        if (classification.IconId != 0)
        {
            texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(classification.IconId));
        }

        actionMetadata = new ActionMetadata(
            classification.Name,
            texture,
            classification.TimingClass,
            true,
            classification.ActionCategoryId,
            classification.IsPlayerAction);
        metadata[actionId] = actionMetadata;
        return true;
    }

    public ActionMetadata ResolveReference(PracticeReferenceAction reference)
    {
        var key = ActionNameNormalizer.Normalize(reference.ActionName);
        if (reference.ActionId != 0 && TryResolve(reference.ActionId, out var byId))
        {
            return byId;
        }

        if (KnownReferenceActionIds.TryGetValue(key, out var knownActionId) &&
            TryResolve(knownActionId, out var byKnownId))
        {
            referenceMetadata[key] = byKnownId;
            return byKnownId;
        }

        if (referenceMetadata.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (NameIndex().TryGetValue(key, out var rowId) &&
            TryResolve(rowId, out var resolved))
        {
            referenceMetadata[key] = resolved;
            return resolved;
        }

        var fallback = new ActionMetadata(
            reference.ActionName,
            null,
            reference.TimingClass,
            false,
            0,
            true);
        referenceMetadata[key] = fallback;
        return fallback;
    }

    private Dictionary<string, uint> NameIndex()
    {
        if (nameIndex is not null)
        {
            return nameIndex;
        }

        var index = new Dictionary<string, uint>(StringComparer.Ordinal);
        var sheet = Plugin.DataManager.GetExcelSheet<ActionSheet>();
        foreach (var action in sheet)
        {
            var name = ActionNameNormalizer.Normalize(action.Name.ToString());
            if (name.Length == 0 || index.ContainsKey(name))
            {
                continue;
            }

            index[name] = action.RowId;
        }

        nameIndex = index;
        return index;
    }

    public void Clear()
    {
        metadata.Clear();
        referenceMetadata.Clear();
        classificationResolver.Clear();
        nameIndex = null;
    }
}

public readonly record struct ActionMetadata(
    string Name,
    ISharedImmediateTexture? Texture,
    ActionTimingClass TimingClass,
    bool IsResolved,
    uint ActionCategoryId,
    bool IsPlayerAction)
{
    public static ActionMetadata Unknown(uint actionId) =>
        new($"Action {actionId}", null, ActionTimingClass.Unknown, false, 0, false);
}
