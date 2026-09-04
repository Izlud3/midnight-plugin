using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MidnightPlugin.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly ActionIconResolver metadataResolver = new();

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("Configuración###With a constant ID")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoCollapse;

        Size = new Vector2(560, 620);
        SizeCondition = ImGuiCond.FirstUseEver;

        configuration = plugin.Configuration;
    }

    public void Dispose() => metadataResolver.Clear();

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
        var opacity = configuration.TimelineOpacity;
        if (ImGui.SliderFloat("Opacidad", ref opacity, 0.1f, 1f, "%.2f"))
        {
            configuration.TimelineOpacity = opacity;
            configuration.Save();
        }

        DrawReferenceAlerts();
    }

    private void DrawReferenceAlerts()
    {
        Section("Alertas de acciones de referencia");
        var scope = configuration.ReferenceAlertScope;
        var scopeLabel = scope switch
        {
            ReferenceAlertScope.DancingMad => "Solo Dancing Mad",
            ReferenceAlertScope.AnyCombat => "Cualquier combate (práctica)",
            _ => "Desactivadas",
        };
        if (ImGui.BeginCombo("Activación", scopeLabel))
        {
            DrawScopeChoice("Desactivadas", ReferenceAlertScope.Off, ref scope);
            DrawScopeChoice("Solo Dancing Mad", ReferenceAlertScope.DancingMad, ref scope);
            DrawScopeChoice("Cualquier combate (práctica)", ReferenceAlertScope.AnyCombat, ref scope);
            ImGui.EndCombo();
        }

        var lead = configuration.ReferenceAlertLeadSeconds;
        if (ImGui.SliderFloat("Avisar antes", ref lead, 1f, 15f, "%.0f s"))
        {
            configuration.ReferenceAlertLeadSeconds = lead;
            configuration.Save();
        }
        var lockPosition = configuration.LockReferenceAlertPosition;
        if (ImGui.Checkbox("Bloquear posición", ref lockPosition))
        {
            configuration.LockReferenceAlertPosition = lockPosition;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Vista previa")) plugin.PreviewReferenceAlert();

        if (plugin.SelectedReference.Rotation is not { } rotation)
        {
            ImGui.TextDisabled("Cambia a un trabajo con referencia para elegir acciones.");
            return;
        }

        ImGui.TextDisabled($"Acciones para {rotation.Job} (cada uso en la referencia)");
        if (!configuration.ReferenceAlertActionsByJob.TryGetValue(rotation.Job, out var selected))
        {
            selected = [];
            configuration.ReferenceAlertActionsByJob[rotation.Job] = selected;
        }

        var selectedKeys = selected.ToHashSet(StringComparer.Ordinal);
        var uniqueActions = rotation.AlertActions
            .GroupBy(Plugin.ReferenceAlertKey)
            .Select(group => group.First())
            .ToArray();
        if (ImGui.Button("Seleccionar todas"))
        {
            selected.Clear();
            selected.AddRange(uniqueActions.Select(Plugin.ReferenceAlertKey));
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Limpiar"))
        {
            selected.Clear();
            configuration.Save();
        }

        var drawList = ImGui.BeginChild("ReferenceAlertActionList", new Vector2(0, 245), true);
        if (drawList)
        {
            foreach (var action in uniqueActions)
            {
                var key = Plugin.ReferenceAlertKey(action);
                var enabled = selectedKeys.Contains(key);
                var metadata = metadataResolver.ResolveReference(action);
                if (metadata.Texture is not null)
                {
                    ImGui.Image(metadata.Texture.GetWrapOrEmpty().Handle, new Vector2(24));
                    ImGui.SameLine();
                }
                if (ImGui.Checkbox($"{metadata.Name}##alert-{key}", ref enabled))
                {
                    if (enabled && !selected.Contains(key, StringComparer.Ordinal)) selected.Add(key);
                    if (!enabled) selected.RemoveAll(item => string.Equals(item, key, StringComparison.Ordinal));
                    configuration.Save();
                }
            }
        }
        ImGui.EndChild();
    }

    private void DrawScopeChoice(string label, ReferenceAlertScope value, ref ReferenceAlertScope selected)
    {
        var isSelected = selected == value;
        if (ImGui.Selectable(label, isSelected))
        {
            selected = value;
            configuration.ReferenceAlertScope = value;
            configuration.Save();
        }
        if (isSelected) ImGui.SetItemDefaultFocus();
    }

    private static void Section(string label)
    {
        ImGui.Separator();
        ImGui.Text(label);
    }
}
