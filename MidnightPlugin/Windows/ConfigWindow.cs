using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MidnightPlugin.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("Configuración de Midnight Timeline###With a constant ID")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(520, 380);
        SizeCondition = ImGuiCond.Always;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.Button("Ver diagnóstico"))
        {
            plugin.ToggleDiagnosticsUi();
        }

        Section("Referencias de práctica");
        ImGui.TextDisabled($"{plugin.References.References.Count} cargados desde archivos JSON incluidos y del usuario.");
        if (plugin.References.Errors.Count > 0)
        {
            ImGui.TextDisabled($"{plugin.References.Errors.Count} error(es) de archivo; consulta Diagnóstico.");
        }
        if (ImGui.Button("Recargar referencias"))
        {
            plugin.ReloadPracticeReferences();
        }
        ImGui.SameLine();
        if (ImGui.Button("Copiar carpeta de referencias"))
        {
            ImGui.SetClipboardText(plugin.References.UserDirectory);
        }

        Section("Timeline");
        var stopOnMistake = configuration.StopOnMistake;
        if (ImGui.Checkbox("Detener el timeline tras 3 errores", ref stopOnMistake))
        {
            plugin.SetStopOnMistake(stopOnMistake);
        }

        var opacity = configuration.TimelineOpacity;
        if (ImGui.SliderFloat("Opacidad", ref opacity, 0.1f, 1f, "%.2f"))
        {
            configuration.TimelineOpacity = opacity;
            configuration.Save();
        }

        Section("DMU Review");
        var failureCards = configuration.ForsakenFailureCardsEnabled;
        if (ImGui.Checkbox("Mostrar tarjetas de fallos tras la resolución", ref failureCards))
        {
            configuration.ForsakenFailureCardsEnabled = failureCards;
            configuration.Save();
        }
        if (ImGui.Button("Abrir DMU Review")) plugin.ToggleForsakenUi();
    }

    private static void Section(string label)
    {
        ImGui.Separator();
        ImGui.Text(label);
    }
}
