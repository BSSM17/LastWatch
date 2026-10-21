using System;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Simulation;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    public sealed class PartnerController
    {
        public const int TicksPerSecond = 60;
        public const int AcknowledgementTicks = 30;
        public const int OrdinaryMoveTargetTicks = 120;
        public const int ExchangeTicks = 90;
        public const int PlayerLineRecheckTicks = 12;
        public const int ExecutionSeparationTicks = 21;
        readonly PartnerRuntimeState state;

        public PartnerController(PartnerRuntimeState state) { this.state = state ?? throw new ArgumentNullException(nameof(state)); }
        public PartnerObservation Observe() => state.Observe();
        // This controller's SelectCoverTarget/CreatePartnerShot/NextSpreadDirection are
        // the fixed Core AR contract. A host must separately report its actual pipeline.
        public M4PartnerCoverEquipmentObservation ObserveCoverEquipment(bool hostFireReloadPipelineAvailable)
            => new M4PartnerCoverEquipmentObservation(state.ArAmmo >= 0, hostFireReloadPipelineAvailable,
                state.ArAmmo < 0 ? "Core AR inventory unknown" : hostFireReloadPipelineAvailable ? "Core AR and host pipeline available" : "Core AR known; host fire/reload pipeline UNAVAILABLE");

        public SafetyOverrideResult ActivateSafetyOverride(string safeNodeId,int nowTick)
        {
            PartnerRole interrupted=state.Role; CancelExchange(nowTick, "safety override"); Evacuate(safeNodeId);
            return new SafetyOverrideResult(true,interrupted,safeNodeId,nowTick);
        }

        public PartnerPathResult RequestPath(IPartnerStationGraph graph,string targetNodeId,string nearestSafeNodeId,int nowTick)
        {
            if(graph==null)throw new ArgumentNullException(nameof(graph));
            PartnerPathResult result=graph.Request(state.CurrentNodeId,targetNodeId,nearestSafeNodeId,nowTick);
            if(result.Status==PartnerPathStatus.NoPathRecoveredToSafeNode)RecoverPath(result.ResolvedNodeId);
            else { state.ReservedNodeId=Required(result.ResolvedNodeId);state.Movement=PartnerMovementState.Reserved;state.Decision=PartnerDecisionState.Moving; }
            return result;
        }

        public void RequestRole(in PartnerRoleRequest request, int nowTick)
        {
            if (request.Role == PartnerRole.None) throw new ArgumentException("A concrete role is required.");
            if (state.Life == PartnerLifeState.Dead || state.Life == PartnerLifeState.Downed || state.Life == PartnerLifeState.Stabilized)
                throw new InvalidOperationException("Partner cannot accept a role in the current life state.");
            string faceId = request.Role == PartnerRole.CoverFace ? Required(request.FaceId) : null;
            string stationNodeId = Required(request.StationNodeId);
            var flow = M4PartnerExchangePersistence.Ensure(state);
            if (flow.intentRevision == ulong.MaxValue) throw new InvalidOperationException("Partner intent cursor exhausted.");
            CancelExchange(nowTick, "role replaced");
            flow.intentRevision++; flow.suspended = false; flow.returnReason = "newer explicit role wins";
            state.Role = request.Role; state.DesignatedFaceId = faceId;
            state.ReservedNodeId = stationNodeId; state.Decision = PartnerDecisionState.Acknowledging;
            state.Movement = PartnerMovementState.Reserved; state.Fire = PartnerFireState.Idle;
            state.LastAcknowledgementTick = nowTick; state.LastSignal = PartnerSignalKind.RequestAcknowledged;
        }

        public void Evacuate(string safeNodeId)
        {
            var flow = M4PartnerExchangePersistence.Ensure(state);
            if (flow.intentRevision != ulong.MaxValue) flow.intentRevision++;
            flow.suspended = false; flow.returnReason = "safety evacuation overrides prior role";
            state.Role = PartnerRole.None; state.DesignatedFaceId = null; state.ReservedNodeId = Required(safeNodeId);
            state.Decision = PartnerDecisionState.Moving; state.Movement = PartnerMovementState.Reserved; state.Fire = PartnerFireState.Idle;
        }

        public void ArriveAtReservedNode()
        {
            if (string.IsNullOrWhiteSpace(state.ReservedNodeId)) throw new InvalidOperationException("No reserved node.");
            state.CurrentNodeId = state.ReservedNodeId; state.ReservedNodeId = null; state.Movement = PartnerMovementState.Stationary;
            state.Decision = state.Role == PartnerRole.None ? PartnerDecisionState.Idle : PartnerDecisionState.Working;
        }

        public void RecoverPath(string nearestSafeNodeId)
        {
            // Technical failure changes navigation only; it never applies damage or life transitions.
            state.ReservedNodeId = Required(nearestSafeNodeId); state.Movement = PartnerMovementState.Recovering;
            state.Decision = PartnerDecisionState.Recovering; state.Fire = PartnerFireState.Idle; state.LastSignal = PartnerSignalKind.PathRecovered;
        }

        public PartnerTargetObservation? SelectCoverTarget(PartnerTargetObservation[] candidates)
        {
            if (state.Role != PartnerRole.CoverFace || state.ArAmmo <= 0) return null;
            PartnerTargetObservation? best = null;
            foreach (var x in candidates ?? Array.Empty<PartnerTargetObservation>())
            {
                if (!x.EntityId.IsValid || !string.Equals(x.FaceId, state.DesignatedFaceId, StringComparison.Ordinal)) continue;
                if (!best.HasValue || Compare(x, best.Value) < 0) best = x;
            }
            return best;
        }

        public bool RecheckPlayerLine(bool playerIntersectsMuzzleToTarget)
        {
            if (playerIntersectsMuzzleToTarget) { state.Fire = PartnerFireState.HoldingForPlayer; state.LastSignal = PartnerSignalKind.FireHeld; return false; }
            state.Fire = PartnerFireState.Firing; return true;
        }

        public Vector3 NextSpreadDirection(Vector3 forward)
        {
            if (state.ArAmmo <= 0) throw new InvalidOperationException("Partner ammunition is empty.");
            uint a = NextUInt(); uint b = NextUInt();
            float radius = Mathf.Sqrt((a & 0xFFFFFFu) / 16777216f) * Mathf.Tan(0.65f * Mathf.Deg2Rad);
            float angle = ((b & 0xFFFFFFu) / 16777216f) * Mathf.PI * 2f;
            Vector3 f = forward.normalized; Vector3 right = Vector3.Cross(Mathf.Abs(f.y) > .99f ? Vector3.forward : Vector3.up, f).normalized;
            Vector3 up = Vector3.Cross(f, right); return (f + right * (radius * Mathf.Cos(angle)) + up * (radius * Mathf.Sin(angle))).normalized;
        }

        public DamagePacket CreatePartnerShot(RunState run, EntityId partnerId, EntityId targetId, Vector3 origin, Vector3 hitPoint, FireSequenceId fireSequence)
        {
            if (state.ArAmmo <= 0) throw new InvalidOperationException("Partner ammunition is empty.");
            state.ArAmmo--; return new DamagePacket(run.IssueEventSequence(), run.FixedTick, partnerId, partnerId, targetId,
                DamageKind.Ballistic, 12f, hitPoint, origin, fireSequence);
        }

        public PartnerResolvedFireFact ResolveFireObservation(in DamagePacket shot,bool heldForPlayerLine)
        { return new PartnerResolvedFireFact(shot.FixedTick,shot.EventSequence,shot.FireSequence,state.DesignatedFaceId,shot.Target,state.ArAmmo,heldForPlayerLine); }

        public PartnerLifeTransitionResult Stabilize(int nowTick)
        {
            var before=state.Life;if(before!=PartnerLifeState.Downed)return new PartnerLifeTransitionResult(before,before,PartnerDeathCause.None,false);
            state.Life=PartnerLifeState.Stabilized;state.BleedoutTick=null;return new PartnerLifeTransitionResult(before,state.Life,PartnerDeathCause.None,true);
        }

        public PartnerLifeTransitionResult ApplyBleedoutOrHazard(PartnerDeathCause cause,int nowTick,ulong eventSequence)
        {
            var before=state.Life;bool valid=(cause==PartnerDeathCause.Bleedout&&before==PartnerLifeState.Downed)||cause==PartnerDeathCause.LostFaceFloorHazard||cause==PartnerDeathCause.CollapseImpact||cause==PartnerDeathCause.AfterfallTimeoutHazard;
            if(!valid||before==PartnerLifeState.Dead)return new PartnerLifeTransitionResult(before,before,state.DeathCause,false);
            state.Life=PartnerLifeState.Dead;state.DeathCause=cause;state.DeathTick=nowTick;state.DeathEventSequence=eventSequence;
            state.Role=PartnerRole.None;state.Fire=PartnerFireState.Idle;return new PartnerLifeTransitionResult(before,state.Life,cause,true);
        }

        public void EndCoverIfEmpty(string centralSafeNodeId)
        {
            if (state.ArAmmo > 0) return;
            state.Role = PartnerRole.None; state.Fire = PartnerFireState.Idle; state.LastSignal = PartnerSignalKind.AmmoEmpty;
            state.ReservedNodeId = Required(centralSafeNodeId); state.Movement = PartnerMovementState.Reserved; state.Decision = PartnerDecisionState.Moving;
        }

        public void BeginExchange(ulong id, ExchangeResource resource, ExchangeDirection direction, int requestedAmount, int nowTick,
            int playerAmmo, int playerParts, int ticksPerSecond = TicksPerSecond, ulong targetPartnerEntityValue = 0,
            string runIdentity = null, int nightOrdinal = 0, string stationDescriptorId = null,
            ulong selectionEpisode = 0, string policyIdentity = null)
        {
            var flow = M4PartnerExchangePersistence.Ensure(state);
            if (selectionEpisode != 0 && selectionEpisode >= flow.nextSelectionEpisode) throw new ArgumentException("Unissued selection episode.");
            if (selectionEpisode != 0 && (selectionEpisode < flow.lastSelectionEpisode ||
                (selectionEpisode == flow.lastSelectionEpisode && !M4PartnerExchangePersistence.Active(state)))) return;
            if (M4PartnerExchangePersistence.Active(state))
            {
                var active = state.PendingExchange;
                if (active.Resource == resource && active.Direction == direction && active.TargetPartnerEntityValue == targetPartnerEntityValue &&
                    active.RunIdentity == runIdentity && active.NightOrdinal == nightOrdinal)
                { if (selectionEpisode != 0) flow.lastSelectionEpisode = selectionEpisode; return; }
            }
            if (nightOrdinal != 0 && state.Life != PartnerLifeState.Healthy)
                throw new InvalidOperationException("Partner cannot exchange in this bound QA life domain.");
            if (id == 0 || id == ulong.MaxValue || id < state.NextExchangeTransactionId || nowTick < 0 || ticksPerSecond <= 0 ||
                requestedAmount <= 0 || playerAmmo < 0 || playerParts < 0 || state.ArAmmo < 0 || state.Parts < 0 ||
                !Enum.IsDefined(typeof(ExchangeResource), resource) || !Enum.IsDefined(typeof(ExchangeDirection), direction))
                throw new ArgumentException("Invalid exchange identity/resource/clock input.");
            if (nightOrdinal != 0 && (nightOrdinal < 1 || nightOrdinal > 3 || targetPartnerEntityValue == 0 ||
                string.IsNullOrWhiteSpace(runIdentity) || string.IsNullOrWhiteSpace(stationDescriptorId)))
                throw new ArgumentException("Missing bound exchange target/context.");
            int available = direction == ExchangeDirection.PlayerToPartner
                ? (resource == ExchangeResource.ArAmmo ? playerAmmo : playerParts)
                : (resource == ExchangeResource.ArAmmo ? state.ArAmmo : state.Parts);
            int unit = resource == ExchangeResource.ArAmmo ? 30 : 1;
            int amount = Math.Min(available, Math.Min(requestedAmount, unit));
            if (amount <= 0) throw new InvalidOperationException("No resource available.");
            // Approved 1.5 seconds, rounded upward only if this explicit clock needs it.
            int duration = checked((checked(ticksPerSecond * 3) + 1) / 2);
            int completion = checked(nowTick + duration);
            if (flow.intentRevision == ulong.MaxValue) throw new InvalidOperationException("Partner intent cursor exhausted.");
            CancelExchange(nowTick, "explicit replacement exchange");
            // A replacement keeps the original interrupted Cover intent, never RallyExchange.
            if (!flow.suspended)
            {
                flow.priorRole = state.Role; flow.priorFaceId = state.DesignatedFaceId;
                flow.priorReservedNodeId = state.ReservedNodeId; flow.priorDecision = state.Decision;
                flow.priorMovement = state.Movement; flow.priorMovementProgress = state.MovementProgress;
                flow.suspendedTick = nowTick;
            }
            flow.intentRevision++; flow.transactionIntentRevision = flow.intentRevision;
            flow.lastSelectionEpisode = selectionEpisode; flow.policyIdentity = policyIdentity;
            flow.occludedSinceTick = -1;
            flow.suspended = nightOrdinal != 0 && !string.IsNullOrWhiteSpace(policyIdentity);
            state.PendingExchange = new ExchangeTransaction { TransactionId=id, Resource=resource, Direction=direction, Amount=amount,
                StartTick=nowTick, CompletionTick=completion, TicksPerSecond=ticksPerSecond,
                TargetPartnerEntityValue=targetPartnerEntityValue, RunIdentity=runIdentity, NightOrdinal=nightOrdinal,
                StationDescriptorId=stationDescriptorId };
            state.NextExchangeTransactionId = id + 1;
            state.Role = PartnerRole.RallyExchange; state.Decision = PartnerDecisionState.Working;
            if (flow.suspended) { state.ReservedNodeId = null; state.Movement = PartnerMovementState.Stationary; }
            state.Fire = PartnerFireState.Idle;
            state.LastAcknowledgementTick = nowTick; state.LastSignal = PartnerSignalKind.RequestAcknowledged;
        }

        public bool ResumePriorCover(int nowTick, bool validCoverIntent, string unavailableReason)
        {
            var f = state.ExchangeFlow;
            if (f == null || !f.suspended || M4PartnerExchangePersistence.Active(state)) return false;
            f.suspended = false;
            if (f.transactionIntentRevision != f.intentRevision) return false;
            if (!validCoverIntent || f.priorRole != PartnerRole.CoverFace || state.Life != PartnerLifeState.Healthy || state.ArAmmo <= 0)
            {
                state.Role = PartnerRole.None; state.DesignatedFaceId = null; state.ReservedNodeId = null;
                state.Movement = PartnerMovementState.Stationary; state.Decision = PartnerDecisionState.Idle; state.Fire = PartnerFireState.Idle;
                if (state.MovementProgress != null && !state.MovementProgress.completed && !state.MovementProgress.cancelled)
                    M4PartnerMovementPersistence.Cancel(state, nowTick, unavailableReason);
                f.returnReason = unavailableReason ?? "previous Cover intent unavailable"; return false;
            }
            state.Role = PartnerRole.CoverFace; state.DesignatedFaceId = f.priorFaceId; state.ReservedNodeId = f.priorReservedNodeId;
            state.Decision = f.priorDecision; state.Movement = f.priorMovement; state.Fire = PartnerFireState.Idle;
            state.MovementProgress = f.priorMovementProgress;
            if (state.MovementProgress != null && !state.MovementProgress.completed && !state.MovementProgress.cancelled)
            {
                // Exclude the suspended duration from no-progress accounting; preserve path/cursor/plan.
                int held = checked(Math.Max(0, nowTick - f.suspendedTick) + 1); // Both boundary ticks skipped motor advancement.
                state.MovementProgress.lastProgressTick = (int)Math.Min(nowTick, (long)state.MovementProgress.lastProgressTick + held);
                state.MovementProgress.lastTick = nowTick;
            }
            f.returnReason = "Cover intent restored; actual host coverfire UNAVAILABLE"; return true;
        }

        public bool CompleteExchange(int nowTick, ref int playerAmmo, ref int playerParts)
        {
            var x=state.PendingExchange; if (x==null || x.Completed || x.Cancelled || nowTick<x.CompletionTick) return false;
            if (x.NightOrdinal != 0 && (state.Life != PartnerLifeState.Healthy || state.Role != PartnerRole.RallyExchange))
            { CancelExchange(nowTick, "life/role changed"); return false; }
            int sender = x.Direction == ExchangeDirection.PlayerToPartner
                ? (x.Resource == ExchangeResource.ArAmmo ? playerAmmo : playerParts)
                : (x.Resource == ExchangeResource.ArAmmo ? state.ArAmmo : state.Parts);
            int recipient = x.Direction == ExchangeDirection.PlayerToPartner
                ? (x.Resource == ExchangeResource.ArAmmo ? state.ArAmmo : state.Parts)
                : (x.Resource == ExchangeResource.ArAmmo ? playerAmmo : playerParts);
            if (sender < x.Amount || recipient < 0 || (long)recipient + x.Amount > int.MaxValue)
            { CancelExchange(nowTick, "sender stock changed or recipient capacity unavailable"); return false; }
            if (x.Resource==ExchangeResource.ArAmmo) { if (x.Direction==ExchangeDirection.PlayerToPartner) { playerAmmo-=x.Amount; state.ArAmmo+=x.Amount; } else { state.ArAmmo-=x.Amount; playerAmmo+=x.Amount; } }
            else { if (x.Direction==ExchangeDirection.PlayerToPartner) { playerParts-=x.Amount; state.Parts+=x.Amount; } else { state.Parts-=x.Amount; playerParts+=x.Amount; } }
            x.Completed=true; x.TerminalTick=nowTick; state.Role=PartnerRole.None; state.Decision=PartnerDecisionState.Idle; return true;
        }

        public bool CancelExchange(int nowTick, string reason)
        {
            var x = state.PendingExchange;
            if (x == null || x.Completed || x.Cancelled) return false;
            x.Cancelled = true; x.TerminalTick = nowTick; x.CancellationReason = reason;
            if (state.Role == PartnerRole.RallyExchange)
            { state.Role = PartnerRole.None; state.Decision = PartnerDecisionState.Idle; state.Fire = PartnerFireState.Idle; }
            return true;
        }

        static int Compare(PartnerTargetObservation a, PartnerTargetObservation b)
        {
            int c=b.PredictedStructureDamage.CompareTo(a.PredictedStructureDamage); if(c!=0)return c;
            c=a.Distance.CompareTo(b.Distance); return c!=0?c:a.EntityId.CompareTo(b.EntityId);
        }
        uint NextUInt() { ulong s0=state.PartnerCombatRngState0,s1=state.PartnerCombatRngState1; ulong result=s0+s1; s1^=s0; state.PartnerCombatRngState0=Rotl(s0,55)^s1^(s1<<14); state.PartnerCombatRngState1=Rotl(s1,36); return (uint)(result>>32); }
        public void RestoreCombatRng(RngStreamState rng){state.PartnerCombatRngState0=rng.State0;state.PartnerCombatRngState1=rng.State1;}
        public RngStreamState ObserveCombatRng()=>new RngStreamState(state.PartnerCombatRngState0,state.PartnerCombatRngState1);
        static ulong Rotl(ulong x,int k)=>(x<<k)|(x>>(64-k));
        static string Required(string value)=>!string.IsNullOrWhiteSpace(value)?value:throw new ArgumentException("Required identifier is missing.");
    }
}
