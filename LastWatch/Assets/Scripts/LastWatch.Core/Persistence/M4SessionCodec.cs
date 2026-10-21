using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Partner;
using LastWatch.Core.Space;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Persistence
{
    public enum M4LoadStatus { LoadedBoundedContract, LegacyPartialSave, UnsupportedSchema, UnsupportedRuleset, UnsupportedRuntimeState, CorruptOrInconsistent }
    public enum M4RestorePurpose { BoundedContract, PlayableContinuation }
    public sealed class M4LoadResult
    {
        public M4LoadStatus Status { get; internal set; }
        public string Reason { get; internal set; }
        public M4Session Session { get; internal set; }
        public string OriginalJson { get; internal set; }
    }
    [Serializable] internal sealed class M4BindingDto { public ulong id; public M4BindingRole role; public FaceId? face; }
    [Serializable] internal sealed class M4TopologyDto { public string version; public int lossSequence; public FaceId[] validFaces; public M4BindingDto[] bindings; }
    [Serializable] internal sealed class M4EntryDto
    {
        public int schema; public ulong runId; public M4GameMode mode; public int ordinal; public ulong nightSeed;
        public int preparedTick; public RunPhase priorPhase; public bool spotlight; public bool motor; public PartnerRole role; public FaceId face; public StationId station;
        public M4TopologyDto topology; public M4TicketDto[] originals; public string queueHash; public string digest;
    }
    [Serializable] internal sealed class M4ContextStampDto { public M4CursorNamespace producer; public ulong cursor; public int ordinal; public string entryDigest; }
    // Exact shape of pre-player-action M4 session payloads. M4ValueCodec validates field
    // counts, so retain this DTO for verified read/migration of envelope versions 2–4.
    [Serializable] internal sealed class M4SessionDtoV2
    {
        public string format; public int schema; public int hashVersion; public string profile; public string seedAlgorithm; public string queueAlgorithm; public int ruleset;
        public ulong runId; public ulong runSeed; public LastWatchDifficulty difficulty; public string rulesetHash; public string contentVersion;
        public ulong generation; public M4SessionStage stage; public int completedNights; public int snapshotTick; public int? nightStartTick;
        public string runtimeCoverage; public RunStateV2Dto run; public PartnerRuntimeState partner;
        public M4EntryDto[] entries; public M4TopologyDto topology; public M4CombatDto combat;
        public M4CoordinatorSave coordinator; public M4HarnessSaveSnapshot harness; public M4ContextStampDto[] contexts;
        public ulong[] completionRunFactCursors; public string gameplayHash; public string resumeHash; public string integrityHash;
    }
    [Serializable] internal sealed class M4SessionDto
    {
        public string format; public int schema; public int hashVersion; public string profile; public string seedAlgorithm; public string queueAlgorithm; public int ruleset;
        public ulong runId; public ulong runSeed; public LastWatchDifficulty difficulty; public string rulesetHash; public string contentVersion;
        public ulong generation; public M4SessionStage stage; public int completedNights; public int snapshotTick; public int? nightStartTick;
        public string runtimeCoverage; public RunStateV2Dto run; public PartnerRuntimeState partner; public M4PlayerWeaponActionSaveState playerWeaponAction;
        public M4EntryDto[] entries; public M4TopologyDto topology; public M4CombatDto combat;
        public M4CoordinatorSave coordinator; public M4HarnessSaveSnapshot harness; public M4ContextStampDto[] contexts;
        public ulong[] completionRunFactCursors; public string gameplayHash; public string resumeHash; public string integrityHash;
    }
    [Serializable] internal sealed class M4WireDto
    {
        public string format;
        public int schemaVersion;
        // v2 was recursive and cannot represent a full M4 session through JsonUtility.
        // Keep it only for bounded legacy reads; v3 writes payloadFlat.
        public M4ValueDto payload;
        public M4FlatValueDto[] payloadFlat;
    }
    // Do not use M4WireDto for writes: JsonUtility still walks the recursive payload field
    // even when it is null. This type intentionally contains no recursive M4ValueDto member.
    [Serializable] internal sealed class M4FlatWireDto
    {
        public string format;
        public int schemaVersion;
        public M4FlatValueDto[] payloadFlat;
    }
    // Read the discriminator without presenting a recursive field to JsonUtility. The legacy
    // recursive DTO is parsed only after this header explicitly identifies an envelope-v2 save.
    [Serializable] internal sealed class M4WireHeaderDto
    {
        public string format;
        public int schemaVersion;
    }
    [Serializable] internal sealed class M4LegacyHeader { public int schemaVersion; public int SchemaVersion; public int EvidenceSchemaVersion; public string runStateJson; public string partnerStateJson; public int NightOrdinal; }

    public static class M4SessionJson
    {
        public const string Format = "LW.M4.BOUNDED.SESSION";
        public const string RuntimeCoverage = "CORE_CONTRACT_PLAYER_WEAPON_ACTION_PARTNER_MOTION_EXCHANGE_INTENT_RECEIPTS_NO_WHOLE_ACTOR_OR_WEAPON_PATH_CONTINUATION";
        const string BeforeExchangeRuntimeCoverage = "CORE_CONTRACT_PLAYER_WEAPON_ACTION_PARTNER_MOTION_RECEIPTS_NO_WHOLE_ACTOR_OR_WEAPON_PATH_CONTINUATION";
        const string BeforePartnerMovementRuntimeCoverage = "CORE_CONTRACT_ONLY_PLAYER_WEAPON_ACTION_NO_ACTOR_TRANSFORMS_OR_WEAPON_PATH_CONTINUATION";
        const string LegacyRuntimeCoverage = "CORE_CONTRACT_ONLY_NO_ACTOR_TRANSFORMS_WEAPON_PATH_CONTINUATION";
        public static string Serialize(M4Session session, bool pretty = false)
        { if(session==null)throw new ArgumentNullException(nameof(session));return Encode(session.CaptureDto(),pretty); }
        internal static string Encode(M4SessionDto dto,bool pretty=false)
        { Seal(dto);return JsonUtility.ToJson(new M4FlatWireDto{format=Format,schemaVersion=7,payloadFlat=M4ValueCodec.Flatten(M4ValueCodec.Encode(dto))},pretty); }
        public static M4LoadResult TryDeserialize(string json, M4RestorePurpose purpose = M4RestorePurpose.PlayableContinuation)
        {
            var result=new M4LoadResult{OriginalJson=json};
            try
            {
                if(!Enum.IsDefined(typeof(M4RestorePurpose),purpose))throw new FormatException("Unknown restore purpose.");
                M4StrictJson.Validate(json);M4WireHeaderDto header=JsonUtility.FromJson<M4WireHeaderDto>(json);
                if(header.format!=Format)
                {
                    M4LegacyHeader legacy=JsonUtility.FromJson<M4LegacyHeader>(json);
                    if(legacy.schemaVersion==1||legacy.schemaVersion==2||legacy.SchemaVersion==1||legacy.EvidenceSchemaVersion==2||legacy.NightOrdinal>0)
                    {result.Status=M4LoadStatus.LegacyPartialSave;result.Reason="Legacy Run/Partner/Harness/coordinator fragments cannot synthesize canonical continuation.";return result;}
                    throw new FormatException("Unknown session format.");
                }
                M4SessionDto d;
                bool movementMigrated = false;
                if (header.schemaVersion == 2)
                {
                    M4WireDto legacy=JsonUtility.FromJson<M4WireDto>(json);
                    movementMigrated = M4ValueCodec.HasBeforePartnerMovementShape(legacy.payload);
                    d=MigrateLegacy(movementMigrated ? M4ValueCodec.DecodeBeforePartnerMovement<M4SessionDtoV2>(legacy.payload) :
                        M4ValueCodec.DecodeBeforePartnerExchange<M4SessionDtoV2>(legacy.payload), movementMigrated);
                }
                else if (header.schemaVersion == 3)
                {
                    M4FlatWireDto flat=JsonUtility.FromJson<M4FlatWireDto>(json);
                    M4ValueDto raw = M4ValueCodec.InflateLegacyV3(flat.payloadFlat);
                    movementMigrated = M4ValueCodec.HasBeforePartnerMovementShape(raw);
                    d=MigrateLegacy(movementMigrated ? M4ValueCodec.DecodeBeforePartnerMovement<M4SessionDtoV2>(raw) : M4ValueCodec.DecodeBeforePartnerExchange<M4SessionDtoV2>(raw), movementMigrated);
                }
                else if (header.schemaVersion == 4)
                {
                    M4FlatWireDto flat=JsonUtility.FromJson<M4FlatWireDto>(json);
                    M4ValueDto raw = M4ValueCodec.Inflate(flat.payloadFlat);
                    movementMigrated = M4ValueCodec.HasBeforePartnerMovementShape(raw);
                    d=MigrateLegacy(movementMigrated ? M4ValueCodec.DecodeBeforePartnerMovement<M4SessionDtoV2>(raw) : M4ValueCodec.DecodeBeforePartnerExchange<M4SessionDtoV2>(raw), movementMigrated);
                }
                else if (header.schemaVersion == 5)
                {
                    M4FlatWireDto flat=JsonUtility.FromJson<M4FlatWireDto>(json);
                    M4ValueDto raw = M4ValueCodec.Inflate(flat.payloadFlat);
                    movementMigrated = M4ValueCodec.HasBeforePartnerMovementShape(raw);
                    if (!movementMigrated) throw new FormatException("Envelope5 requires the exact pre-navigation Partner layout.");
                    d = M4ValueCodec.DecodeBeforePartnerMovement<M4SessionDto>(raw);
                    VerifyBeforePartnerMovementHashes(d);
                    d.runtimeCoverage = RuntimeCoverage; Seal(d);
                }
                else if (header.schemaVersion == 6)
                {
                    M4FlatWireDto flat=JsonUtility.FromJson<M4FlatWireDto>(json);
                    d=M4ValueCodec.DecodeBeforePartnerExchange<M4SessionDto>(M4ValueCodec.Inflate(flat.payloadFlat));
                    VerifyBeforePartnerExchangeHashes(d);
                    d.runtimeCoverage = RuntimeCoverage; Seal(d);
                }
                else if (header.schemaVersion == 7)
                {
                    M4FlatWireDto flat=JsonUtility.FromJson<M4FlatWireDto>(json);
                    d=M4ValueCodec.Decode<M4SessionDto>(M4ValueCodec.Inflate(flat.payloadFlat));
                }
                else {result.Status=M4LoadStatus.UnsupportedSchema;result.Reason="Unsupported M4 envelope version.";return result;}
                if(d.schema!=3||d.hashVersion!=1){result.Status=M4LoadStatus.UnsupportedSchema;return result;}
                if(d.profile!=M4BridgeProfile.Id||d.seedAlgorithm!=M4BridgeProfile.SeedAlgorithm||d.queueAlgorithm!=M4BridgeProfile.QueueAlgorithm||d.ruleset!=31||d.rulesetHash!=M4BridgeProfile.RulesetHash||d.contentVersion!=M4BridgeProfile.ContentVersion)
                {result.Status=M4LoadStatus.UnsupportedRuleset;result.Reason="Unsupported manifest/profile/content; no queue regeneration.";return result;}
                VerifyHashes(d);M4Session restored=M4Session.RestoreDto(d);
                if(purpose==M4RestorePurpose.PlayableContinuation){result.Status=M4LoadStatus.UnsupportedRuntimeState;result.Reason=RuntimeCoverage;return result;}
                result.Status=M4LoadStatus.LoadedBoundedContract;result.Session=restored;
                result.Reason=movementMigrated ? RuntimeCoverage + "; verified pre-navigation source migrated with null motion/cursor1; original historical evidence retained, no motion evidence." : RuntimeCoverage;
                return result;
            }
            catch(Exception error) when(!(error is OutOfMemoryException)) { result.Status=M4LoadStatus.CorruptOrInconsistent;result.Reason=error.Message;return result; }
        }
        internal static void Seal(M4SessionDto d)
        {
            if(d==null||d.schema!=3||d.runtimeCoverage!=RuntimeCoverage)throw new FormatException("Unsupported M4 session schema/coverage.");
            if(d.playerWeaponAction!=null)d.playerWeaponAction.Validate();
            d.gameplayHash=M4SessionHashes.Gameplay(d);d.resumeHash=M4SessionHashes.Resume(d);d.integrityHash=M4SessionHashes.Integrity(d);
        }
        internal static void VerifyHashes(M4SessionDto d)
        {if(d.format!=Format||d.runtimeCoverage!=RuntimeCoverage||d.gameplayHash!=M4SessionHashes.Gameplay(d)||d.resumeHash!=M4SessionHashes.Resume(d)||d.integrityHash!=M4SessionHashes.Integrity(d))throw new FormatException("Session hash/coverage mismatch.");}
        static void VerifyBeforePartnerExchangeHashes(M4SessionDto d)
        {
            if (d.format != Format || d.schema != 3 || d.hashVersion != 1 || d.runtimeCoverage != BeforeExchangeRuntimeCoverage ||
                d.partner == null || d.partner.ExchangeFlow != null ||
                d.gameplayHash != M4SessionHashes.Gameplay(d, false, true) || d.resumeHash != M4SessionHashes.Resume(d, false, true) ||
                d.integrityHash != M4SessionHashes.Integrity(d, false, true)) throw new FormatException("Pre-exchange original session hash/coverage mismatch.");
        }
        static void VerifyBeforePartnerMovementHashes(M4SessionDto d)
        {
            if (d.format != Format || d.schema != 3 || d.hashVersion != 1 || d.runtimeCoverage != BeforePartnerMovementRuntimeCoverage || d.partner == null ||
                d.partner.MovementProgress != null || d.partner.NextMovementPlanId != 1 ||
                d.gameplayHash != M4SessionHashes.Gameplay(d, true) || d.resumeHash != M4SessionHashes.Resume(d, true) ||
                d.integrityHash != M4SessionHashes.Integrity(d, true)) throw new FormatException("Pre-navigation session original hash/coverage mismatch.");
        }
        internal static bool MatchesVerifiedOriginalReceipt(string json, M4StoreReceipt receipt, RunId expectedRunId)
        {
            // The migrated digest differs. Bind storage to the verified original source,
            // including its original generation/stage/digest, before accepting recovery.
            M4StrictJson.Validate(json);
            var header = JsonUtility.FromJson<M4WireHeaderDto>(json);
            if (header.format != Format) return false;
            if (header.schemaVersion >= 2 && header.schemaVersion <= 4)
            {
                M4ValueDto legacyRaw;
                if (header.schemaVersion == 2) legacyRaw = JsonUtility.FromJson<M4WireDto>(json).payload;
                else
                {
                    var legacyFlat = JsonUtility.FromJson<M4FlatWireDto>(json);
                    legacyRaw = header.schemaVersion == 3 ? M4ValueCodec.InflateLegacyV3(legacyFlat.payloadFlat) : M4ValueCodec.Inflate(legacyFlat.payloadFlat);
                }
                bool oldPartner = M4ValueCodec.HasBeforePartnerMovementShape(legacyRaw);
                var legacy = oldPartner ? M4ValueCodec.DecodeBeforePartnerMovement<M4SessionDtoV2>(legacyRaw) : M4ValueCodec.DecodeBeforePartnerExchange<M4SessionDtoV2>(legacyRaw);
                MigrateLegacy(legacy, oldPartner); // Verifies original hashes before creating a current DTO.
                return receipt != null && legacy.runId == expectedRunId.Value && receipt.RunId.Equals(expectedRunId) &&
                    receipt.Generation == legacy.generation && receipt.Stage == legacy.stage && receipt.Digest == legacy.integrityHash;
            }
            if (header.schemaVersion != 5 && header.schemaVersion != 6 && header.schemaVersion != 7) return false;
            var flat = JsonUtility.FromJson<M4FlatWireDto>(json); var raw = M4ValueCodec.Inflate(flat.payloadFlat);
            bool old = header.schemaVersion == 5 && M4ValueCodec.HasBeforePartnerMovementShape(raw);
            if (header.schemaVersion == 5 && !old) throw new FormatException("Envelope5 requires the exact pre-navigation Partner layout.");
            var source = old ? M4ValueCodec.DecodeBeforePartnerMovement<M4SessionDto>(raw) : header.schemaVersion == 6 ? M4ValueCodec.DecodeBeforePartnerExchange<M4SessionDto>(raw) : M4ValueCodec.Decode<M4SessionDto>(raw);
            if (old) VerifyBeforePartnerMovementHashes(source); else if (header.schemaVersion == 6) VerifyBeforePartnerExchangeHashes(source); else VerifyHashes(source);
            return receipt != null && source.runId == expectedRunId.Value && receipt.Generation == source.generation &&
                receipt.RunId.Equals(expectedRunId) && receipt.Stage == source.stage && receipt.Digest == source.integrityHash;
        }
        static M4SessionDto MigrateLegacy(M4SessionDtoV2 old, bool beforePartnerMovement = false)
        {
            if(old==null||old.format!=Format||old.schema!=2||old.hashVersion!=1||old.runtimeCoverage!=LegacyRuntimeCoverage)
                throw new FormatException("Unsupported legacy M4 session payload.");
            if(old.gameplayHash!=M4SessionHashes.Gameplay(old,beforePartnerMovement,true)||old.resumeHash!=M4SessionHashes.Resume(old,beforePartnerMovement,true)||old.integrityHash!=M4SessionHashes.Integrity(old,beforePartnerMovement,true))
                throw new FormatException("Legacy M4 session hash mismatch.");
            var current=new M4SessionDto
            {
                format=old.format,schema=3,hashVersion=old.hashVersion,profile=old.profile,seedAlgorithm=old.seedAlgorithm,
                queueAlgorithm=old.queueAlgorithm,ruleset=old.ruleset,runId=old.runId,runSeed=old.runSeed,
                difficulty=old.difficulty,rulesetHash=old.rulesetHash,contentVersion=old.contentVersion,
                generation=old.generation,stage=old.stage,completedNights=old.completedNights,
                snapshotTick=old.snapshotTick,nightStartTick=old.nightStartTick,runtimeCoverage=RuntimeCoverage,
                // Earlier envelopes never persisted this player state; do not infer ammo or reload progress.
                run=old.run,partner=old.partner,playerWeaponAction=null,entries=old.entries,topology=old.topology,
                combat=old.combat,coordinator=old.coordinator,harness=old.harness,contexts=old.contexts,
                completionRunFactCursors=old.completionRunFactCursors
            };
            Seal(current);
            return current;
        }
        internal static M4EntryDto Entry(DefenseEntrySnapshot e)
        { return new M4EntryDto{schema=1,runId=e.Key.RunId.Value,mode=e.Key.GameMode,ordinal=e.Key.NightOrdinal,nightSeed=e.NightSeed,preparedTick=e.PreparedAtFixedTick,priorPhase=e.PriorPhase,spotlight=e.Allocation.SpotlightOn,motor=e.Allocation.StructureMotorOn,role=e.Allocation.Role,face=e.Allocation.Face,station=e.Allocation.Station,topology=Topology(e.Topology),originals=Array.ConvertAll(e.OriginalTickets,M4CombatCodec.Ticket),queueHash=e.QueueHash}; }
        internal static DefenseEntrySnapshot Entry(M4EntryDto e,M4RunMetadata m)
        {
            if(e==null||e.schema!=1||e.runId!=m.RunId.Value||e.mode!=M4GameMode.Story||e.nightSeed!=M4BridgeProfile.NightSeed(m.RunSeed,e.ordinal))throw new FormatException("Entry identity mismatch.");
            StoryQuantity qty=EnemyDirector.GetStoryQuantity(e.ordinal,m.Difficulty);
            var q=M4CombatCodec.OriginalQueue(new M4CombatDto{schema=1,runSeed=m.RunSeed,difficulty=m.Difficulty,day=e.ordinal,ruleset=31,total=qty.Total,activeCap=qty.ActiveCap,density=qty.DensityPer10Seconds,originals=e.originals,queueHash=e.queueHash});
            var entry=new DefenseEntrySnapshot(m,e.ordinal,e.preparedTick,e.priorPhase,new M4CommittedAllocation(e.spotlight,e.motor,e.role,e.face,e.station),Topology(e.topology),q);
            if(entry.Digest!=e.digest)throw new FormatException("Entry digest mismatch.");return entry;
        }
        internal static M4TopologyDto Topology(M4TopologySnapshot t)=>new M4TopologyDto{version=t.Version,lossSequence=t.FaceLossSequence,validFaces=t.ValidFaces,bindings=Array.ConvertAll(t.Bindings,x=>new M4BindingDto{id=x.EntityId.Value,role=x.Role,face=x.Face})};
        internal static M4TopologySnapshot Topology(M4TopologyDto t)
        {if(t==null||t.bindings==null)throw new FormatException("Missing topology.");return new M4TopologySnapshot(t.version,t.lossSequence,t.validFaces,Array.ConvertAll(t.bindings,x=>new M4EntityBinding(new EntityId(x.id),x.role,x.face)));}
    }

    public static class M4SessionHashes
    {
        public static string Entry(DefenseEntrySnapshot entry)
        {
            M4EntryDto d=M4SessionJson.Entry(entry);d.digest=null;
            // Metadata is bound explicitly, including original seed/difficulty/algorithm versions.
            return M4ValueCodec.Hash("LW.M4.EntrySnapshot.v1|"+entry.Metadata.RunSeed.ToString("x16")+"|"+entry.Metadata.Difficulty+"|"+entry.Metadata.RulesetHash,M4ValueCodec.Encode(d));
        }
        public static string Gameplay(M4Session session)=>Gameplay(session.CaptureDto());
        public static string Resume(M4Session session)=>Resume(session.CaptureDto());
        internal static string Gameplay(M4SessionDto source, bool beforePartnerMovement = false, bool beforePartnerExchange = false)
        {
            M4SessionDto d=Base(source);d.generation=0;d.harness=null;d.coordinator=null;d.contexts=null;d.completionRunFactCursors=null;
            d.run.committedFacts=null;d.run.committedFactAcknowledgedCursor=null;d.run.cosmeticRng=null;
            d.partner.Data40Facts=null;d.partner.Data40AcknowledgedCursor=0;d.partner.NextData40Cursor=0;
            if(d.combat!=null){d.combat.directorFacts=null;d.combat.projectionFacts=null;d.combat.directorAck=0;d.combat.projectionAck=0;d.combat.directorNext=0;d.combat.projectionNext=0;d.combat.corpses=0;d.combat.poolEvents=null;}
            return M4ValueCodec.Hash("LW.M4.SessionGameplay.v1", beforePartnerMovement ? M4ValueCodec.EncodeBeforePartnerMovement(d) : beforePartnerExchange ? M4ValueCodec.EncodeBeforePartnerExchange(d) : M4ValueCodec.Encode(d));
        }
        internal static string Resume(M4SessionDto source, bool beforePartnerMovement = false, bool beforePartnerExchange = false)
        {
            M4SessionDto d=Base(source);d.generation=0;d.run.committedFactAcknowledgedCursor="0000000000000000";d.partner.Data40AcknowledgedCursor=0;
            if(d.combat!=null){d.combat.directorAck=0;d.combat.projectionAck=0;}
            return M4ValueCodec.Hash("LW.M4.SessionResume.v1", beforePartnerMovement ? M4ValueCodec.EncodeBeforePartnerMovement(d) : beforePartnerExchange ? M4ValueCodec.EncodeBeforePartnerExchange(d) : M4ValueCodec.Encode(d));
        }
        internal static string Integrity(M4SessionDto source, bool beforePartnerMovement = false, bool beforePartnerExchange = false)
        {M4SessionDto d=M4ValueCodec.Copy(source);d.integrityHash=null;return M4ValueCodec.Hash("LW.M4.SaveIntegrity.v1", beforePartnerMovement ? M4ValueCodec.EncodeBeforePartnerMovement(d) : beforePartnerExchange ? M4ValueCodec.EncodeBeforePartnerExchange(d) : M4ValueCodec.Encode(d));}
        static M4SessionDto Base(M4SessionDto source)
        {M4SessionDto d=M4ValueCodec.Copy(source);d.gameplayHash=null;d.resumeHash=null;d.integrityHash=null;return d;}

        internal static string Gameplay(M4SessionDtoV2 source, bool beforePartnerMovement = false, bool beforePartnerExchange = false)
        {
            M4SessionDtoV2 d=Base(source);d.generation=0;d.harness=null;d.coordinator=null;d.contexts=null;d.completionRunFactCursors=null;
            d.run.committedFacts=null;d.run.committedFactAcknowledgedCursor=null;d.run.cosmeticRng=null;
            d.partner.Data40Facts=null;d.partner.Data40AcknowledgedCursor=0;d.partner.NextData40Cursor=0;
            if(d.combat!=null){d.combat.directorFacts=null;d.combat.projectionFacts=null;d.combat.directorAck=0;d.combat.projectionAck=0;d.combat.directorNext=0;d.combat.projectionNext=0;d.combat.corpses=0;d.combat.poolEvents=null;}
            return M4ValueCodec.Hash("LW.M4.SessionGameplay.v1", beforePartnerMovement ? M4ValueCodec.EncodeBeforePartnerMovement(d) : beforePartnerExchange ? M4ValueCodec.EncodeBeforePartnerExchange(d) : M4ValueCodec.Encode(d));
        }
        internal static string Resume(M4SessionDtoV2 source, bool beforePartnerMovement = false, bool beforePartnerExchange = false)
        {
            M4SessionDtoV2 d=Base(source);d.generation=0;d.run.committedFactAcknowledgedCursor="0000000000000000";d.partner.Data40AcknowledgedCursor=0;
            if(d.combat!=null){d.combat.directorAck=0;d.combat.projectionAck=0;}
            return M4ValueCodec.Hash("LW.M4.SessionResume.v1", beforePartnerMovement ? M4ValueCodec.EncodeBeforePartnerMovement(d) : beforePartnerExchange ? M4ValueCodec.EncodeBeforePartnerExchange(d) : M4ValueCodec.Encode(d));
        }
        internal static string Integrity(M4SessionDtoV2 source, bool beforePartnerMovement = false, bool beforePartnerExchange = false)
        {M4SessionDtoV2 d=M4ValueCodec.Copy(source);d.integrityHash=null;return M4ValueCodec.Hash("LW.M4.SaveIntegrity.v1", beforePartnerMovement ? M4ValueCodec.EncodeBeforePartnerMovement(d) : beforePartnerExchange ? M4ValueCodec.EncodeBeforePartnerExchange(d) : M4ValueCodec.Encode(d));}
        static M4SessionDtoV2 Base(M4SessionDtoV2 source)
        {M4SessionDtoV2 d=M4ValueCodec.Copy(source);d.gameplayHash=null;d.resumeHash=null;d.integrityHash=null;return d;}
    }
}
