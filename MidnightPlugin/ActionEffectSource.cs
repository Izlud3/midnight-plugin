using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace MidnightPlugin;

/// <summary>
/// Lightweight local action-effect hook adapted from ECommons 3.2.1.17.
/// Payloads are copied into managed memory before subscribers are invoked.
/// </summary>
public sealed unsafe class ActionEffectSource : IDisposable
{
    private const string Signature =
        "40 55 53 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 70 4C 8B BD";

    private const int EffectsPerTarget = 8;
    private const byte MaxEffectTargets = 32;

    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly Hook<ProcessActionEffect>? hook;

    private delegate void ProcessActionEffect(
        uint sourceId,
        Character* sourceCharacter,
        Vector3* position,
        ActionEffectHeader* header,
        ActionEffectEntry* entries,
        ulong* targetIds);

    public ActionEffectSource(
        ISigScanner sigScanner,
        IGameInteropProvider gameInteropProvider,
        IObjectTable objectTable,
        IPluginLog log)
    {
        this.objectTable = objectTable;
        this.log = log;

        if (!sigScanner.TryScanText(Signature, out var address))
        {
            log.Error("Could not find the action-effect signature; action timeline capture is unavailable.");
            return;
        }

        hook = gameInteropProvider.HookFromAddress<ProcessActionEffect>(address, OnActionEffect);
        hook.Enable();
        log.Information("Action-effect hook initialized.");
    }

    public event Action<ActionEffectSet>? ActionEffect;
    public bool IsAvailable => hook is not null;

    public void Dispose()
    {
        hook?.Dispose();
        ActionEffect = null;
    }

    private void OnActionEffect(
        uint sourceId,
        Character* sourceCharacter,
        Vector3* position,
        ActionEffectHeader* header,
        ActionEffectEntry* entries,
        ulong* targetIds)
    {
        try
        {
            if (header is null || entries is null || targetIds is null) return;

            var captured = new ActionEffectSet(
                *header,
                objectTable.SearchById(sourceId),
                position is null ? default : *position,
                CopyTargets(header->TargetCount, entries, targetIds));

            foreach (Action<ActionEffectSet> subscriber in ActionEffect?.GetInvocationList() ?? [])
            {
                try
                {
                    subscriber(captured);
                }
                catch (Exception exception)
                {
                    log.Error(exception, "An action-effect subscriber failed.");
                }
            }
        }
        catch (Exception exception)
        {
            log.Error(exception, "Unable to process an action effect.");
        }
        finally
        {
            hook!.Original(sourceId, sourceCharacter, position, header, entries, targetIds);
        }
    }

    private static ActionEffectTarget[] CopyTargets(byte targetCount, ActionEffectEntry* entries, ulong* targetIds)
    {
        if (targetCount > MaxEffectTargets)
        {
            targetCount = MaxEffectTargets;
        }

        var targets = new ActionEffectTarget[targetCount];
        for (var targetIndex = 0; targetIndex < targetCount; targetIndex++)
        {
            var hasEffect = false;
            for (var entryIndex = 0; entryIndex < EffectsPerTarget; entryIndex++)
            {
                if (entries[(targetIndex * EffectsPerTarget) + entryIndex].Type != 0)
                {
                    hasEffect = true;
                    break;
                }
            }

            targets[targetIndex] = new ActionEffectTarget(targetIds[targetIndex], hasEffect);
        }

        return targets;
    }
}

public readonly record struct ActionEffectSet(
    ActionEffectHeader Header,
    IGameObject? Source,
    Vector3 Position,
    IReadOnlyList<ActionEffectTarget> TargetEffects);

public readonly record struct ActionEffectTarget(ulong TargetId, bool HasEffect);

[StructLayout(LayoutKind.Explicit)]
public struct ActionEffectHeader
{
    [FieldOffset(0)] public ulong AnimationTargetId;
    [FieldOffset(8)] public uint ActionId;
    [FieldOffset(12)] public uint GlobalEffectCounter;
    [FieldOffset(16)] public float AnimationLockTime;
    [FieldOffset(20)] public uint SomeTargetId;
    [FieldOffset(24)] public ushort SourceSequence;
    [FieldOffset(26)] public ushort Rotation;
    [FieldOffset(28)] public ushort AnimationId;
    [FieldOffset(30)] public byte Variation;
    [FieldOffset(31)] public byte ActionType;
    [FieldOffset(33)] public byte TargetCount;
}

[StructLayout(LayoutKind.Sequential)]
public struct ActionEffectEntry
{
    public byte Type;
    public byte Parameter0;
    public byte Parameter1;
    public byte Parameter2;
    public byte Multiplier;
    public byte Flags;
    public ushort Value;
}
