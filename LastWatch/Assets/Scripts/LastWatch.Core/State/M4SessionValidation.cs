using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Partner;

namespace LastWatch.Core.State
{
    internal static class M4LossReducer
    {
        internal static void Apply(M4LossTotals total,CommittedFactRecord record,EntityId[] barriers,EntityId[] supports)
        {
            if(record.Kind==CommittedFactKind.Damage)
            {
                var f=record.Damage;
                if(f.TargetKind==DamageableKind.Player)total.PlayerDamage+=f.Amount;
                else if(f.TargetKind==DamageableKind.Partner)total.PartnerDamage+=f.Amount;
                else if(f.TargetKind==DamageableKind.Structure)
                {
                    if(Contains(barriers,f.TargetEntityId)){total.BarrierDamage+=f.Amount;total.BarrierPermanentLoss|=f.BecameResolved;}
                    else if(Contains(supports,f.TargetEntityId)){total.SupportDamage+=f.Amount;total.SupportPermanentLoss|=f.BecameResolved;}
                }
            }
            else
            {var f=record.ActorTransition;if(f.ActorKind==DamageableKind.Player&&f.CurrentLifeState==ActorLifeStateCode.PlayerDead)total.PlayerDeath=true;if(f.ActorKind==DamageableKind.Partner&&f.CurrentLifeState==ActorLifeStateCode.PartnerDead)total.PartnerDeath=true;}
        }
        static bool Contains(EntityId[] ids,EntityId id){foreach(var x in ids)if(x==id)return true;return false;}
        internal static bool Equal(M4LossTotals a,M4LossTotals b)=>a!=null&&b!=null&&a.Contains(b)&&b.Contains(a);
    }
    internal static class M4SessionValidation
    {
        internal static void ValidatePartner(RunState run,PartnerRuntimeState p,M4TopologySnapshot topology)
        {
            M4PartnerMovementPersistence.Validate(p);
            if (p.MovementProgress != null && (p.MovementProgress.runIdentity != run.RunId.ToString() ||
                p.MovementProgress.lastTick > run.FixedTick)) throw new FormatException("Partner movement run/tick mismatch.");
            if (p.MovementProgress != null && !p.MovementProgress.completed && !p.MovementProgress.cancelled &&
                (run.M4ActiveEntryContext == null || p.MovementProgress.nightOrdinal != run.M4ActiveEntryContext.Key.NightOrdinal ||
                p.MovementProgress.startTick < run.M4ActiveEntryContext.NightStartFixedTick || run.Phase != RunPhase.Defense ||
                (p.Life != PartnerLifeState.Healthy && p.Life != PartnerLifeState.Injured)))
                throw new FormatException("Partner active movement entry/phase/life mismatch.");
            if(p.FriendlyIncidents==null||p.Data40Facts==null||p.NextFriendlyIncidentId==0||p.NextData40Cursor==0||p.ArAmmo<0||p.Parts<0||p.HitPoints<0||float.IsNaN(p.HitPoints)||float.IsInfinity(p.HitPoints)||(p.PartnerCombatRngState0|p.PartnerCombatRngState1)==0||p.TechnicalDamageOrDeathCount!=0)
                throw new FormatException("Invalid Partner resources/RNG/technical loss.");
            if(p.Life!=run.PartnerLife||(p.Life==PartnerLifeState.Dead)!=p.DeathTick.HasValue||p.DeathTick!=run.PartnerDeath?.Tick||p.DeathEventSequence!=run.PartnerDeath?.EventSequence||p.ConfirmedPartnerExecution!=run.ConfirmedPartnerExecution)throw new FormatException("Run/Partner life/death mirrors disagree.");
            foreach(var b in topology.Bindings)
            {
                run.TryGetDamageable(b.EntityId,out DamageableSnapshot d);
                if(b.Role==M4BindingRole.Partner&&(p.HitPoints!=d.HitPoints||d.IsResolved!=(p.Life==PartnerLifeState.Dead)))throw new FormatException("Partner damageable mirror differs.");
                if(b.Role==M4BindingRole.Player&&d.IsResolved!=(run.PlayerLife==PlayerLifeState.Dead))throw new FormatException("Player damageable mirror differs.");
            }
            ulong last=0;var fires=new HashSet<ulong>();bool reactionFound=!p.LastReactionIncidentId.HasValue;
            foreach(var x in p.FriendlyIncidents)
            {
                if(x==null||x.IncidentId!=last+1||x.FireSequenceId==0||!fires.Add(x.FireSequenceId)||x.HitCount<1||x.FirstHitTick<0||x.LastHitTick<x.FirstHitTick||x.LastHitTick>run.FixedTick||x.TotalDamage<=0||float.IsNaN(x.TotalDamage)||float.IsInfinity(x.TotalDamage))throw new FormatException("Friendly incident sequence/value mismatch.");
                last=x.IncidentId;if(p.LastReactionIncidentId==last)reactionFound=true;
            }
            if(p.NextFriendlyIncidentId!=last+1||!reactionFound)throw new FormatException("Friendly incident/reaction cursor mismatch.");
            ulong next=1;foreach(var f in p.Data40Facts)
            {if(f==null||f.Cursor!=next++||f.EventSequence==0||f.EventSequence>=run.NextEventSequence||f.FixedTick<0||f.FixedTick>run.FixedTick)throw new FormatException("DATA-40 sequence/tick mismatch.");}
            if(p.NextData40Cursor!=next||p.Data40AcknowledgedCursor>=next)throw new FormatException("DATA-40 cursor mismatch.");
            if(p.PendingExchange!=null&&(p.PendingExchange.TransactionId==0||p.PendingExchange.Amount<=0||p.PendingExchange.CompletionTick<0))throw new FormatException("Invalid exchange transaction.");
            if (p.NextExchangeTransactionId == 0) throw new FormatException("Invalid exchange identity cursor.");
            M4PartnerExchangePersistence.Validate(p);
            if (p.ExchangeFlow != null && (p.ExchangeFlow.suspendedTick > run.FixedTick || p.ExchangeFlow.occludedSinceTick > run.FixedTick))
                throw new FormatException("Exchange suspension/occlusion tick exceeds canonical clock.");
            var exchange = p.PendingExchange;
            if (exchange != null && exchange.TicksPerSecond > 0)
            {
                if (!Enum.IsDefined(typeof(ExchangeResource), exchange.Resource) || !Enum.IsDefined(typeof(ExchangeDirection), exchange.Direction) ||
                    exchange.TransactionId >= p.NextExchangeTransactionId || exchange.StartTick < 0 || exchange.StartTick > run.FixedTick ||
                    (long)exchange.CompletionTick - exchange.StartTick != ((long)exchange.TicksPerSecond * 3 + 1) / 2 ||
                    exchange.Amount > (exchange.Resource == ExchangeResource.ArAmmo ? 30 : 1) || (exchange.Completed && exchange.Cancelled) ||
                    ((exchange.Completed || exchange.Cancelled) != exchange.TerminalTick.HasValue) ||
                    (exchange.TerminalTick.HasValue && (exchange.TerminalTick.Value < exchange.StartTick || exchange.TerminalTick.Value > run.FixedTick)) ||
                    (exchange.Completed && exchange.TerminalTick.Value < exchange.CompletionTick))
                    throw new FormatException("Exchange clock/amount/terminal/cursor mismatch.");
                if (exchange.NightOrdinal != 0)
                {
                    bool targetFound = false;
                    foreach (var binding in topology.Bindings)
                        if (binding.Role == M4BindingRole.Partner && binding.EntityId.Value == exchange.TargetPartnerEntityValue) targetFound = true;
                    if (!targetFound || exchange.RunIdentity != run.RunId.ToString() || exchange.NightOrdinal < 1 || exchange.NightOrdinal > 3 ||
                        string.IsNullOrWhiteSpace(exchange.StationDescriptorId) || exchange.Resource != ExchangeResource.ArAmmo || exchange.TicksPerSecond != 50 ||
                        exchange.PlayerHitPointsAtBegin < 0f || float.IsNaN(exchange.PlayerHitPointsAtBegin) || float.IsInfinity(exchange.PlayerHitPointsAtBegin) ||
                        exchange.PartnerHitPointsAtBegin < 0f || float.IsNaN(exchange.PartnerHitPointsAtBegin) || float.IsInfinity(exchange.PartnerHitPointsAtBegin))
                        throw new FormatException("Bound QA exchange target/context/domain mismatch.");
                }
            }
        }
        internal static void ValidateLosses(RunState run,PartnerRuntimeState partner,M4TopologySnapshot topology,M4CoordinatorSave coordinator,M4HarnessSaveSnapshot harness,ulong[] boundaries)
        {
            var journal=CommittedFactJournal.ReadAfter(run,0);ulong previous=0;
            foreach(ulong cursor in boundaries){if(cursor<previous||cursor>coordinator.RunFactCursor)throw new FormatException("Completion loss cursor mismatch.");previous=cursor;}
            if(coordinator.RunFactCursor>journal.LastCommittedCursor||coordinator.MaximumActiveEnemies<0||coordinator.MaximumActiveEnemies>14||harness.Nights.Length!=boundaries.Length)throw new FormatException("Loss consumer cursor/history mismatch.");
            var aggregate=new M4LossTotals();var interval=new M4LossTotals();EntityId[] barriers=topology.Ids(M4BindingRole.Barrier),supports=topology.Ids(M4BindingRole.Support);
            int index=0;
            for(int night=0;night<boundaries.Length;night++)
            {
                interval=new M4LossTotals();while(index<journal.Records.Length&&journal.Records[index].Cursor<=boundaries[night]){M4LossReducer.Apply(aggregate,journal.Records[index],barriers,supports);M4LossReducer.Apply(interval,journal.Records[index],barriers,supports);index++;}
                if(!M4LossReducer.Equal(aggregate,harness.Nights[night].AggregateLosses)||!M4LossReducer.Equal(interval,harness.Nights[night].PerNightLosses))throw new FormatException("History losses differ from retained facts.");
            }
            if(!coordinator.CurrentNightCommitted)interval=new M4LossTotals();
            while(index<journal.Records.Length&&journal.Records[index].Cursor<=coordinator.RunFactCursor){M4LossReducer.Apply(aggregate,journal.Records[index],barriers,supports);M4LossReducer.Apply(interval,journal.Records[index],barriers,supports);index++;}
            if(!M4LossReducer.Equal(aggregate,coordinator.AggregateLosses())||!M4LossReducer.Equal(interval,coordinator.PerNightLosses))throw new FormatException("Coordinator losses differ from retained facts.");
        }
    }
}
