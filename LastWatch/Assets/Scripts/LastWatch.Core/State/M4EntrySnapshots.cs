using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Partner;
using LastWatch.Core.Space;

namespace LastWatch.Core.State
{
    // Immutable allocation, topology, and entry evidence captured for one Night.
    public sealed class M4CommittedAllocation
    {
        public bool SpotlightOn { get; }
        public bool StructureMotorOn { get; }
        public PartnerRole Role { get; }
        public FaceId Face { get; }
        public StationId Station { get; }
        public static M4CommittedAllocation Initial => new M4CommittedAllocation(true, true, PartnerRole.CoverFace, FaceId.FaceA, StationId.S_A);
        public M4CommittedAllocation(bool spotlight, bool motor, PartnerRole role, FaceId face, StationId station)
        {
            if (!Enum.IsDefined(typeof(PartnerRole), role) || role == PartnerRole.None || !Enum.IsDefined(typeof(FaceId), face) ||
                !Enum.IsDefined(typeof(StationId), station) || (station != StationId.S_0 && (int)station != (int)face + 1))
                throw new ArgumentException("Invalid allocation role/Face/Station binding.");
            SpotlightOn = spotlight; StructureMotorOn = motor; Role = role; Face = face; Station = station;
        }
    }

    public readonly struct M4EntityBinding
    {
        public EntityId EntityId { get; }
        public M4BindingRole Role { get; }
        public FaceId? Face { get; }
        public M4EntityBinding(EntityId id, M4BindingRole role, FaceId? face)
        {
            if (!id.IsValid || !Enum.IsDefined(typeof(M4BindingRole), role) || (face.HasValue && !Enum.IsDefined(typeof(FaceId), face.Value)) ||
                ((role == M4BindingRole.Barrier || role == M4BindingRole.Support || role == M4BindingRole.NightBrace) != face.HasValue)) throw new ArgumentException("Invalid stable binding.");
            EntityId = id; Role = role; Face = face;
        }
    }

    public sealed class M4TopologySnapshot
    {
        readonly FaceId[] faces;
        readonly M4EntityBinding[] bindings;
        public string Version { get; }
        public int FaceLossSequence { get; }
        public FaceId[] ValidFaces => (FaceId[])faces.Clone();
        public M4EntityBinding[] Bindings => (M4EntityBinding[])bindings.Clone();
        public M4TopologySnapshot(string version, int lossSequence, FaceId[] validFaces, M4EntityBinding[] entityBindings)
        {
            if (version != M4BridgeProfile.ContentVersion || lossSequence < 0 || validFaces == null || entityBindings == null) throw new ArgumentException("Unsupported topology identity.");
            faces = (FaceId[])validFaces.Clone(); Array.Sort(faces);
            if (faces.Length < 1 || faces.Length > 3) throw new ArgumentException("At least one authoritative Face is required.");
            for (int n = 0; n < faces.Length; n++) if (!Enum.IsDefined(typeof(FaceId), faces[n]) || (n > 0 && faces[n] == faces[n - 1])) throw new ArgumentException("Unknown/duplicate Face.");
            bindings = (M4EntityBinding[])entityBindings.Clone(); Array.Sort(bindings, (a, b) => a.EntityId.CompareTo(b.EntityId));
            int players = 0, partners = 0;
            for (int n = 0; n < bindings.Length; n++)
            {
                M4EntityBinding b = bindings[n]; if (!b.EntityId.IsValid || (n > 0 && b.EntityId == bindings[n - 1].EntityId)) throw new ArgumentException("Duplicate/invalid binding.");
                if (b.Role == M4BindingRole.Player) players++; if (b.Role == M4BindingRole.Partner) partners++;
            }
            if (players != 1 || partners != 1) throw new ArgumentException("Exactly one Player and Partner binding required.");
            Version = version; FaceLossSequence = lossSequence;
        }
        internal EntityId[] Ids(M4BindingRole role) { var ids = new List<EntityId>(); foreach (var b in bindings) if (b.Role == role) ids.Add(b.EntityId); return ids.ToArray(); }
        internal void ValidateRun(RunState run)
        {
            foreach (M4EntityBinding b in bindings)
            {
                DamageableKind kind = b.Role == M4BindingRole.Player ? DamageableKind.Player : b.Role == M4BindingRole.Partner ? DamageableKind.Partner : DamageableKind.Structure;
                if (!run.TryGetDamageable(b.EntityId, out DamageableSnapshot d) || d.Kind != kind) throw new FormatException("Stable binding does not match Core damageable.");
            }
        }
    }

    public sealed class DefenseEntrySnapshot
    {
        public EnterDefenseKey Key { get; }
        public M4RunMetadata Metadata { get; }
        public ulong NightSeed { get; }
        public int PreparedAtFixedTick { get; }
        public RunPhase PriorPhase { get; }
        public M4CommittedAllocation Allocation { get; }
        public M4TopologySnapshot Topology { get; }
        internal EnemyDirectorQueue OriginalQueue { get; }
        public SpawnTicket[] OriginalTickets { get { var result = new SpawnTicket[OriginalQueue.Tickets.Count]; OriginalQueue.Tickets.CopyToArray(result); return result; } }
        public string QueueHash => OriginalQueue.CanonicalHash();
        public string Digest => Persistence.M4SessionHashes.Entry(this);
        internal DefenseEntrySnapshot(M4RunMetadata metadata, int ordinal, int tick, RunPhase prior, M4CommittedAllocation allocation, M4TopologySnapshot topology, EnemyDirectorQueue queue)
        {
            if (metadata == null || allocation == null || topology == null || queue == null || tick < 0 || !Enum.IsDefined(typeof(RunPhase), prior)) throw new ArgumentException("Missing entry input.");
            Metadata = metadata; Key = new EnterDefenseKey(metadata.RunId, M4GameMode.Story, ordinal); NightSeed = M4BridgeProfile.NightSeed(metadata.RunSeed, ordinal);
            PreparedAtFixedTick = tick; PriorPhase = prior; Allocation = allocation; Topology = topology; OriginalQueue = queue;
            EnemyDirectorRequest r = queue.Request;
            if (r.RunSeed != metadata.RunSeed || r.Difficulty != metadata.Difficulty || r.Day != ordinal || r.RulesetVersion != M4BridgeProfile.RulesetVersion) throw new FormatException("Entry/request mapping mismatch.");
        }
    }

    internal static class M4CopyExtensions
    { internal static void CopyToArray<T>(this IReadOnlyList<T> source, T[] target) { for (int n = 0; n < target.Length; n++) target[n] = source[n]; } }
}
