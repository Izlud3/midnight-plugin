using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MidnightPlugin.Windows;

public sealed class DiagnosticsWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public DiagnosticsWindow(Plugin plugin)
        : base("Diagnóstico de Midnight Timeline###MidnightTimelineDiagnostics")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(900, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 260),
            MaximumSize = new Vector2(1400, 900),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        if (ImGui.Button("Limpiar registro"))
        {
            plugin.Diagnostics.Clear();
        }

        ImGui.SameLine();
        if (ImGui.Button("Copiar registro"))
        {
            ImGui.SetClipboardText(FormatEntries(plugin.Diagnostics.Snapshot()));
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"{plugin.Diagnostics.Snapshot().Count} entradas conservadas.");
        ImGui.TextDisabled($"Archivo persistente: {plugin.Diagnostics.FilePath}");
        ImGui.Separator();

        if (!ImGui.BeginChild("MidnightTimelineDiagnosticsEntries", new Vector2(0, 0), true))
        {
            ImGui.EndChild();
            return;
        }

        var entries = plugin.Diagnostics.Snapshot();
        if (entries.Count == 0)
        {
            ImGui.TextDisabled("Aún no se han registrado entradas de diagnóstico.");
        }
        else
        {
            foreach (var entry in entries)
            {
                var action = entry.ActionId is { } actionId
                    ? $" ActionID={actionId}"
                    : string.Empty;
                ImGui.TextUnformatted(
                    $"{entry.Timestamp.ToLocalTime():HH:mm:ss.fff} [{entry.Stage}]{action} {entry.Message}");
            }
        }

        ImGui.EndChild();
    }

    private static string FormatEntries(IReadOnlyList<MidnightPlugin.Core.DiagnosticLogEntry> entries)
    {
        return string.Join(
            Environment.NewLine,
            entries.Select(entry =>
            {
                var action = entry.ActionId is { } actionId
                    ? $" ActionID={actionId}"
                    : string.Empty;
                return $"{entry.Timestamp.ToLocalTime():HH:mm:ss.fff} [{entry.Stage}]{action} {entry.Message}";
            }));
    }
}
