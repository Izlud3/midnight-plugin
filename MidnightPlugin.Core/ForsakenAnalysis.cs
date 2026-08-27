namespace MidnightPlugin.Core;

public enum MechanicVerdict
{
    InsufficientEvidence,
    Success,
    Failure,
}

public sealed record ForsakenParticipant(
    ulong ActorId,
    string Name,
    string Job,
    bool TookTower,
    bool Died,
    EncounterPosition? Position,
    IReadOnlyList<string>? Effects = null,
    uint? HpAtPairStart = null,
    uint? HpAtResolution = null,
    uint? MaxHp = null,
    IReadOnlySet<uint>? Statuses = null);

public sealed record ArenaTower(float X, float Y, int SoakerCount, IReadOnlyList<ulong>? SoakerIds = null)
{
    public const int ExpectedSoakers = 2;
    public const float Radius = 4;
    public bool IsResolvedCorrectly => SoakerCount == ExpectedSoakers;
}
public sealed record StackResolution(int PlayerCount, IReadOnlyList<ulong>? PlayerIds = null, ulong SourceId = 0)
{
    public const int ExpectedPlayers = 3;
    public bool IsResolvedCorrectly => PlayerCount == ExpectedPlayers;
}
public sealed record ConeResolution(int PlayerCount, IReadOnlyList<ulong>? PlayerIds = null, ulong SourceId = 0)
{
    // BossMod aims Spellwave at the cone owner's closest other player.
    // Exactly that one other player should be inside the cone.
    public const int ExpectedPlayers = 1;
    public bool IsResolvedCorrectly => PlayerCount == ExpectedPlayers;
}
public sealed record ArenaSnapshot(
    TimeSpan Elapsed,
    IReadOnlyList<ForsakenParticipant> Players,
    IReadOnlyList<ArenaTower> Towers,
    IReadOnlyList<StackResolution> Stacks,
    IReadOnlyList<ConeResolution> Cones);

public sealed record ForsakenPairResult(
    int PairNumber,
    MechanicVerdict Verdict,
    TimeSpan Elapsed,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<ForsakenParticipant> Participants,
    ArenaSnapshot? Snapshot);

public static class ForsakenAnalyzer
{
    public static ForsakenPairResult Analyze(
        int pairNumber,
        TimeSpan elapsed,
        IReadOnlyList<ForsakenParticipant> participants,
        IReadOnlyList<ArenaTower> towers,
        IReadOnlyList<StackResolution> stacks,
        bool evidenceComplete,
        IReadOnlyList<ConeResolution>? cones = null)
    {
        if (!evidenceComplete || participants.Count != 8 || towers.Count != 2)
        {
            return new(pairNumber, MechanicVerdict.InsufficientEvidence, elapsed,
                ["No se capturó la evidencia requerida del grupo o de las torres."], participants, null);
        }

        var reasons = new List<string>();
        foreach (var (tower, index) in towers.Select((tower, index) => (tower, index + 1)))
            if (!tower.IsResolvedCorrectly) reasons.Add($"Torre {index} tuvo {tower.SoakerCount} soakers; se esperaban {ArenaTower.ExpectedSoakers}.");

        foreach (var (stack, index) in stacks.Select((stack, index) => (stack, index + 1)))
            if (!stack.IsResolvedCorrectly) reasons.Add($"Stack {index} tuvo {stack.PlayerCount} jugadores; se esperaban {StackResolution.ExpectedPlayers}.");

        var coneResults = cones ?? [];
        foreach (var (cone, index) in coneResults.Select((cone, index) => (cone, index + 1)))
            if (!cone.IsResolvedCorrectly) reasons.Add($"Cono {index} alcanzó a {cone.PlayerCount} otros jugadores; se esperaban {ConeResolution.ExpectedPlayers}.");

        foreach (var player in participants)
        {
            if (player.Died) reasons.Add($"{player.Name} murió durante el par de torres.");
        }

        var snapshot = new ArenaSnapshot(elapsed, participants, towers, stacks, coneResults);
        return new(pairNumber, reasons.Count == 0 ? MechanicVerdict.Success : MechanicVerdict.Failure,
            elapsed, reasons, participants, snapshot);
    }
}
