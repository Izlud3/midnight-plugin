using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MidnightPlugin.Core;

namespace MidnightPlugin.Windows;

public sealed class ReferenceAlertWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly ActionIconResolver metadataResolver = new();
    private DateTimeOffset previewUntil;

    public ReferenceAlertWindow(Plugin plugin)
        : base("Reference Action Alert###MidnightReferenceAlert",
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        this.plugin = plugin;
        PositionCondition = ImGuiCond.FirstUseEver;
        Position = new Vector2(760, 360);
        RespectCloseHotkey = false;
    }

    public void Dispose() => metadataResolver.Clear();

    public void ShowPreview()
    {
        previewUntil = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(4);
        IsOpen = true;
        BringToFront();
    }

    public override void OnClose() => IsOpen = true;

    public override bool DrawConditions()
    {
        IsOpen = true;
        Flags = plugin.Configuration.LockReferenceAlertPosition
            ? Flags | ImGuiWindowFlags.NoMove
            : Flags & ~ImGuiWindowFlags.NoMove;
        return previewUntil > DateTimeOffset.UtcNow || plugin.TryGetReferenceAlerts(out _);
    }

    public override void Draw()
    {
        IReadOnlyList<ReferenceAlertDisplay> alerts;
        if (previewUntil > DateTimeOffset.UtcNow)
        {
            var action = plugin.SelectedReference.Rotation?.AlertActions.FirstOrDefault();
            if (action is not { } previewAction) return;
            alerts = [new ReferenceAlertDisplay(previewAction, plugin.Configuration.ReferenceAlertLeadSeconds)];
        }
        else if (!plugin.TryGetReferenceAlerts(out alerts)) return;

        foreach (var alert in alerts)
        {
            var metadata = metadataResolver.ResolveReference(alert.Action);
            if (metadata.Texture is not null)
            {
                var icon = metadata.Texture.GetWrapOrEmpty();
                ImGui.Image(icon.Handle, new Vector2(42));
                ImGui.SameLine();
            }

            ImGui.BeginGroup();
            ImGui.TextUnformatted(metadata.Name);
            ImGui.TextDisabled(alert.SecondsUntil > 0.05
                ? $"en {alert.SecondsUntil:0.0}s"
                : "¡ahora!");
            ImGui.EndGroup();
        }
    }
}

public readonly record struct ReferenceAlertDisplay(PracticeReferenceAction Action, double SecondsUntil);
