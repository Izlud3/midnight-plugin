using Dalamud.Interface.Textures;
using ClassJobSheet = Lumina.Excel.Sheets.ClassJob;

namespace MidnightPlugin;

public sealed class JobIconResolver
{
    private const uint ClassJobIconBase = 62000;
    private readonly Dictionary<string, ISharedImmediateTexture?> textures = new(StringComparer.OrdinalIgnoreCase);

    public bool TryResolve(string abbreviation, out ISharedImmediateTexture? texture)
    {
        if (textures.TryGetValue(abbreviation, out texture)) return texture is not null;

        var job = Plugin.DataManager.GetExcelSheet<ClassJobSheet>()
            .FirstOrDefault(row => row.Abbreviation.ToString().Equals(abbreviation, StringComparison.OrdinalIgnoreCase));
        texture = job.RowId == 0
            ? null
            : Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(ClassJobIconBase + job.RowId));
        textures[abbreviation] = texture;
        return texture is not null;
    }

    public void Clear() => textures.Clear();
}
