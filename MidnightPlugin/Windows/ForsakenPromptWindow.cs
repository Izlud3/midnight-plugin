using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MidnightPlugin.Windows;

public sealed class ForsakenPromptWindow : Window, IDisposable
{
    private static readonly TimeSpan PromptLifetime = TimeSpan.FromSeconds(30);
    private readonly Plugin plugin;
    private Guid? pullId;
    private DateTimeOffset? expiresAt;

    public ForsakenPromptWindow(Plugin plugin)
        : base(
            "DMU Review###MidnightForsakenPrompt",
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        Size = new Vector2(240, 82);
        SizeCondition = ImGuiCond.Always;
        Position = GetDefaultPosition();
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public void Show(Guid reviewPullId)
    {
        pullId = reviewPullId;
        expiresAt = DateTimeOffset.UtcNow + PromptLifetime;
        IsOpen = true;
    }

    public void Dismiss()
    {
        pullId = null;
        expiresAt = null;
        IsOpen = false;
    }

    public override void OnClose()
    {
        pullId = null;
        expiresAt = null;
    }

    public override void Draw()
    {
        if (plugin.IsForsakenUiOpen ||
            pullId is not { } reviewPullId ||
            expiresAt is not { } expiration)
        {
            Dismiss();
            return;
        }

        var remaining = expiration - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            Dismiss();
            return;
        }

        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        if (!ImGui.Button($"Abrir DMU Review ({seconds}s)###open-dmu-review", new Vector2(-1, -1))) return;

        plugin.OpenForsakenReview(reviewPullId);
        Dismiss();
    }

    private static Vector2 GetDefaultPosition()
    {
        ref var workPosition = ref ImGui.GetMainViewport().WorkPos;
        ref var workSize = ref ImGui.GetMainViewport().WorkSize;
        return workPosition + (workSize - new Vector2(240, 82)) / 2;
    }
}
