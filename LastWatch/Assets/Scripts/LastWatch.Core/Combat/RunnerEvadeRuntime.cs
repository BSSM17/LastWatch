using System;
using LastWatch.Core.Identity;

namespace LastWatch.Core.Combat
{
    // Runtime state machine for one already-authorized Runner evade decision.
    public readonly struct RunnerEvadeTransition
    {
        public EntityId RunnerId{get;} public FireSequenceId FireSequenceId{get;} public int FixedTick{get;} public RunnerEvadePhase Previous{get;} public RunnerEvadePhase Current{get;}
        internal RunnerEvadeTransition(EntityId runner,FireSequenceId fire,int tick,RunnerEvadePhase previous,RunnerEvadePhase current){RunnerId=runner;FireSequenceId=fire;FixedTick=tick;Previous=previous;Current=current;}
    }
    public sealed class RunnerEvadeRuntime
    {
        private readonly EntityId runner;private readonly FireSequenceId fire;private readonly RunnerReactionDecision decision;private RunnerEvadePhase phase;private int endTick;
        public RunnerEvadeRuntime(EntityId runnerId,FireSequenceId fireSequence,RunnerReactionDecision scheduledDecision){if(!runnerId.IsValid||!fireSequence.IsValid||scheduledDecision.Phase!=RunnerEvadePhase.Scheduled)throw new ArgumentException("A scheduled authoritative decision is required.");runner=runnerId;fire=fireSequence;decision=scheduledDecision;phase=RunnerEvadePhase.Scheduled;endTick=checked(decision.ScheduledTick+decision.MovementDurationTicks);}
        public RunnerEvadePhase Phase=>phase;
        public EntityId RunnerId=>runner;
        public FireSequenceId FireSequenceId=>fire;
        public bool IsMovingAt(int fixedTick)=>phase==RunnerEvadePhase.Moving&&fixedTick>=decision.ScheduledTick&&fixedTick<endTick;
        public bool TryAdvance(int fixedTick,EnemyMovementState currentState,out RunnerEvadeTransition transition)
        {
            if(phase==RunnerEvadePhase.Scheduled&&fixedTick>=decision.ScheduledTick){RunnerEvadePhase next=(currentState==EnemyMovementState.Approach||currentState==EnemyMovementState.TargetActorApproach)?RunnerEvadePhase.Moving:RunnerEvadePhase.Skipped;transition=new RunnerEvadeTransition(runner,fire,fixedTick,phase,next);phase=next;return true;}
            if(phase==RunnerEvadePhase.Moving&&fixedTick>=endTick){transition=new RunnerEvadeTransition(runner,fire,fixedTick,phase,RunnerEvadePhase.Completed);phase=RunnerEvadePhase.Completed;return true;}
            transition=default;return false;
        }
    }
}
