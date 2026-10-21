using System;
using LastWatch.Core.State;

namespace LastWatch.Core.Partner
{
    public sealed class FriendlyFireLedger
    {
        readonly PartnerRuntimeState state;
        public FriendlyFireLedger(PartnerRuntimeState state) { this.state=state??throw new ArgumentNullException(nameof(state)); }

        public FriendlyHitIncident RecordHit(ulong fireSequenceId, int fixedTick, float damage,
            PartnerLifeState before, PartnerLifeState after, bool lethalPlayerFire)
        {
            if (fireSequenceId==0 || fixedTick<0 || damage<=0) throw new ArgumentException("Invalid friendly-hit fact.");
            FriendlyHitIncident incident=null;
            for(int i=state.FriendlyIncidents.Count-1;i>=0;i--) if(state.FriendlyIncidents[i].FireSequenceId==fireSequenceId){incident=state.FriendlyIncidents[i];break;}
            bool newSequence=incident==null;
            if(newSequence) incident=new FriendlyHitIncident { IncidentId=state.NextFriendlyIncidentId++, FireSequenceId=fireSequenceId,
                FirstHitTick=fixedTick, LastHitTick=fixedTick, StateBefore=before, StateAfter=before };
            incident.LastHitTick=fixedTick; incident.HitCount++; incident.TotalDamage+=damage; incident.StateAfter=after;
            incident.PlayerCausedDowning |= before!=PartnerLifeState.Downed && before!=PartnerLifeState.Stabilized && after==PartnerLifeState.Downed;
            incident.PlayerCausedDeath |= lethalPlayerFire && after==PartnerLifeState.Dead;
            bool wasAlreadyHelpless=before==PartnerLifeState.Downed||before==PartnerLifeState.Stabilized;
            bool priorSequenceEndedLongEnough=WasPriorPlayerDowningAtLeastTicksAgo(fireSequenceId,fixedTick,PartnerController.ExecutionSeparationTicks);
            incident.ConfirmedPartnerExecution |= newSequence && wasAlreadyHelpless && priorSequenceEndedLongEnough && lethalPlayerFire && after==PartnerLifeState.Dead;
            incident.ReactionStage=Stage(incident,wasAlreadyHelpless);
            if(newSequence) state.FriendlyIncidents.Add(incident);
            state.LastReactionIncidentId=incident.IncidentId;
            if(incident.ConfirmedPartnerExecution) state.ConfirmedPartnerExecution=true;
            if(after==PartnerLifeState.Dead){state.DeathCause=lethalPlayerFire?PartnerDeathCause.PlayerFire:PartnerDeathCause.OtherValidDamage;state.DeathTick=fixedTick;}
            state.Life=after; return incident;
        }

        bool WasPriorPlayerDowningAtLeastTicksAgo(ulong currentSequence,int tick,int separation)
        {
            foreach(var x in state.FriendlyIncidents)
                if(x.FireSequenceId!=currentSequence && x.PlayerCausedDowning && tick-x.LastHitTick>=separation) return true;
            return false;
        }
        PartnerReactionStage Stage(FriendlyHitIncident x,bool helpless)
        {
            if(x.StateAfter==PartnerLifeState.Dead)return PartnerReactionStage.Dead;
            if(helpless)return PartnerReactionStage.DownedOrStabilizedHit;
            int recent=0; foreach(var i in state.FriendlyIncidents) if(x.FirstHitTick-i.LastHitTick<=400)recent++;
            return recent>0?PartnerReactionStage.RepeatedIncident:PartnerReactionStage.FirstIncident;
        }
    }
}
