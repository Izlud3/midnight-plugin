using Dalamud.Hooking;
using Dalamud.Plugin.Services;

namespace MidnightPlugin;

public sealed class TargetIconSource : IDisposable
{
    private const uint TargetIconCategory = 0x22;
    private const string Signature = "E8 ?? ?? ?? ?? 0F B7 0B 83 E9 64";

    private readonly IPluginLog log;
    private readonly Hook<ProcessActorControl>? hook;

    private delegate void ProcessActorControl(
        uint entityId,
        uint category,
        uint param1,
        uint param2,
        uint param3,
        uint param4,
        uint param5,
        uint param6,
        uint param7,
        uint param8,
        ulong targetId,
        byte param9);

    public TargetIconSource(
        IGameInteropProvider gameInteropProvider,
        IPluginLog log)
    {
        this.log = log;
        try
        {
            hook = gameInteropProvider.HookFromSignature<ProcessActorControl>(Signature, OnActorControl);
            hook.Enable();
            log.Information("TargetIcon ActorControl hook initialized.");
        }
        catch (Exception exception)
        {
            hook = null;
            log.Warning(exception, "Could not initialize the TargetIcon hook; Limit Cut number assignments are unavailable.");
        }
    }

    public event Action<TargetIconEvent>? TargetIcon;
    public bool IsAvailable => hook is not null;

    public void Dispose()
    {
        hook?.Dispose();
        TargetIcon = null;
    }

    private void OnActorControl(
        uint entityId,
        uint category,
        uint param1,
        uint param2,
        uint param3,
        uint param4,
        uint param5,
        uint param6,
        uint param7,
        uint param8,
        ulong targetId,
        byte param9)
    {
        hook!.Original(entityId, category, param1, param2, param3, param4, param5, param6, param7, param8, targetId, param9);
        if (category != TargetIconCategory || param1 == 0) return;

        try
        {
            foreach (Action<TargetIconEvent> subscriber in TargetIcon?.GetInvocationList() ?? [])
            {
                try
                {
                    subscriber(new(entityId, param1));
                }
                catch (Exception exception)
                {
                    log.Error(exception, "A TargetIcon subscriber failed.");
                }
            }
        }
        catch (Exception exception)
        {
            log.Error(exception, "Unable to process a TargetIcon event.");
        }
    }
}

public readonly record struct TargetIconEvent(ulong ActorId, uint MarkerId);
