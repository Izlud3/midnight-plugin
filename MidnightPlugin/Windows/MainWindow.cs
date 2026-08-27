using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace MidnightPlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base(
            "Midnight Timeline##MidnightTimelineMain",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(240, 105),
            MaximumSize = new Vector2(650, 105),
        };
        Size = new Vector2(260, 105);
        SizeCondition = ImGuiCond.Always;

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (plugin.BrandIcon.TryGetWrap(out var brandIcon, out _))
        {
            ImGui.Image(brandIcon.Handle, new Vector2(36) * ImGuiHelpers.GlobalScale);
            ImGui.SameLine();
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Midnight Timeline");

        ImGui.Spacing();

        if (IconButtonHelper.IconButton(FontAwesomeIcon.Cog, "Ajustes", "###main-settings"))
        {
            plugin.ToggleConfigUi();
        }

        ImGui.SameLine();
        if (IconButtonHelper.IconButton(FontAwesomeIcon.History, "Timeline", "###main-timeline"))
        {
            plugin.ToggleTimelineUi();
        }

        ImGui.SameLine();
        if (IconButtonHelper.IconButton(FontAwesomeIcon.LocationCrosshairs, "DMU Review", "###main-dmu"))
        {
            plugin.ToggleForsakenUi();
        }
    }
}
