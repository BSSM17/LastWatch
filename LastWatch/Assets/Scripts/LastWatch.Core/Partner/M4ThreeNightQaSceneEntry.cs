using System;
using LastWatch.Core.Combat;
using LastWatch.Core.Configuration;
using LastWatch.Core.Persistence;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    // Implement this only in the Core-owned runtime components assigned to the bounded QA
    // entry. The host invokes processors in inspector order before the canonical completion
    // callback; it never supplies an alternate M3 simulation or completion condition.
    internal interface IM4ThreeNightQaFixedTickProcessor
    {
        void ProcessCompletedCoreTick(M4ThreeNightQaFixedTickContext context);
        void RestorePlayerWeaponActionStateFromSession(M4Session session);
    }

    // A per-tick view of the session-owned M3 objects. It is internal so Tools and scene UI
    // cannot mutate Core state through this diagnostic entry.
    internal sealed class M4ThreeNightQaFixedTickContext
    {
        // M3 ticket scheduling is local to the current Night. RunFixedTick remains available
        // for canonical run facts, but processors must use NightFixedTick for Director calls.
        internal int RunFixedTick { get; }
        internal int NightFixedTick { get; }
        internal RunState Run { get; }
        internal EnemyDirectorRuntime Director { get; }
        internal EnemyPool Pool { get; }
        internal RunnerEvadeCoordinator Runners { get; }

        internal M4ThreeNightQaFixedTickContext(int runFixedTick, int nightFixedTick, RunState run, EnemyDirectorRuntime director, EnemyPool pool, RunnerEvadeCoordinator runners)
        {
            RunFixedTick = runFixedTick;
            NightFixedTick = nightFixedTick;
            Run = run;
            Director = director;
            Pool = pool;
            Runners = runners;
        }
    }

    // Scene entry label required by the bounded-slice contract: LastWatch_M2M3M4_3Night_QA.
    // No scene/prefab existed when this was introduced, so this component is the minimal
    // project-owned entry target to attach to that future scene. It requires explicit Start.
    public sealed class M4ThreeNightQaSceneEntry : MonoBehaviour
    {
        public const string EntryLabel = "LastWatch_M2M3M4_3Night_QA";
        public const ulong EvidenceRunSeed = M4ThreeNightHarness.EvidenceRunSeed;

        [SerializeField] M4DiagnosticBootstrap bootstrap;
        [SerializeField] LastWatchDifficulty selectedDifficulty = LastWatchDifficulty.Normal;
        [SerializeField] MonoBehaviour[] fixedTickProcessors = Array.Empty<MonoBehaviour>();

        bool fixedTickRunning;
        M4CanonicalCompletedTickSnapshot completedTick;
        M4Session completedTickSession;
        M4SessionStage completedTickStage;
        string completedTickUnavailableReason = "NO_CANONICAL_COMPLETION";
        ulong contextGeneration;
        ulong completionAttempt;

        void InvalidateCompletedTick(string reason)
        {
            completedTick = null;
            completedTickSession = null;
            completedTickUnavailableReason = reason;
        }

        // Passive main-thread read. Retained DTOs remain historical after invalidation;
        // callers must reacquire for each immediate post-completion comparison.
        public bool TryGetLastCanonicalCompletedTick(out M4CanonicalCompletedTickSnapshot snapshot)
        {
            M4Session current = bootstrap == null ? null : bootstrap.CanonicalSession;
            string reason = completedTickUnavailableReason;
            if (completedTick != null)
            {
                if (ReferenceEquals(current, completedTickSession) && current != null &&
                    current.Stage == completedTickStage && current.ActiveEntry != null &&
                    current.ActiveEntry.Key.NightOrdinal == completedTick.NightOrdinal &&
                    current.Metadata.RunId.ToString() == completedTick.RunId &&
                    current.RuntimeRun.FixedTick == completedTick.AdvancedRunFixedTick &&
                    current.NightElapsedTick == completedTick.AdvancedNightFixedTick)
                {
                    snapshot = completedTick;
                    return true;
                }
                reason = "CANONICAL_CONTEXT_CHANGED";
            }
            snapshot = new M4CanonicalCompletedTickSnapshot(false, reason, null,
                contextGeneration, completionAttempt, -1, -1, 0, -1, -1, false);
            return false;
        }
        public bool Started => bootstrap != null && bootstrap.Started;
        public bool FixedTickRunning => fixedTickRunning;
        bool ownerPaused;
        public bool OwnerPaused => ownerPaused;
        public void SetOwnerPause(bool paused)
        {
            if (!Started || CurrentStage != M4SessionStage.Defense || (!fixedTickRunning && !ownerPaused)) return;
            ownerPaused = paused; fixedTickRunning = !paused;
            InvalidateCompletedTick(paused ? "OWNER_PAUSED" : "OWNER_RESUMED");
        }
        public LastWatchDifficulty SelectedDifficulty => selectedDifficulty;
        public M4SessionStage CurrentStage => bootstrap == null || bootstrap.CanonicalSession == null ? M4SessionStage.New : bootstrap.CanonicalSession.Stage;
        public int CompletedNights => bootstrap == null || bootstrap.CanonicalSession == null ? 0 : bootstrap.CanonicalSession.CompletedNights;
        public int ActiveNightOrdinal => CurrentStage == M4SessionStage.Frozen ? 3 : Math.Min(3, CompletedNights + 1);
        public bool CanEnterNextNight => CurrentStage == M4SessionStage.NightCompleted && CompletedNights < 3;
        public bool EvidenceFrozen => CurrentStage == M4SessionStage.Frozen;
        public M4CommandResult LastTickResult { get; private set; }
        internal M4Session CanonicalSessionForPlayerAction => bootstrap == null ? null : bootstrap.CanonicalSession;

        // A malformed/mismatched replay must stop the canonical clock at the exact
        // divergence point instead of silently advancing without its recorded command.
        internal void HaltFixedTicksForReplayFailure()
        {
            HaltFixedTicksForQaInputFailure("replay divergence");
        }

        internal void HaltFixedTicksForQaInputFailure(string reason)
        {
            InvalidateCompletedTick("QA_INPUT_HALTED");
            fixedTickRunning = false;
            LastTickResult = new M4CommandResult
            {
                Status = M4CommandStatus.InvalidPhase,
                Reason = "The canonical fixed tick was halted at a QA input failure before clock advancement: " + (reason ?? "unknown input failure")
            };
        }

        public void SelectDifficulty(LastWatchDifficulty difficulty)
        {
            if (Started) throw new InvalidOperationException("Difficulty is immutable after explicit Start.");
            if (!Enum.IsDefined(typeof(LastWatchDifficulty), difficulty)) throw new ArgumentOutOfRangeException(nameof(difficulty));
            selectedDifficulty = difficulty;
        }

        // Scene/UI code must call this after the operator presses its explicit Start control.
        // Topology and damageables are supplied by the scene owner; this entry never chooses
        // Faces, stable IDs, hit points, or a replacement M3 queue.
        public M4CommandResult StartExplicit(M4RunMetadata metadata, M4TopologySnapshot topology, DamageableFixture[] setup, PartnerRuntimeState initialPartner, IM4SessionStore store = null, M4PlayerWeaponActionSaveState initialPlayerWeaponAction = null)
        {
            InvalidateCompletedTick("START_ATTEMPT");
            unchecked { contextGeneration++; }
            if (bootstrap == null) throw new InvalidOperationException("M4DiagnosticBootstrap is required.");
            if (metadata == null || metadata.RunSeed != EvidenceRunSeed || metadata.Difficulty != selectedDifficulty)
                throw new ArgumentException("The bounded entry requires RunSeed 1234 and the selected Normal/Hard difficulty.");
            if (Started) throw new InvalidOperationException("The bounded entry is already started.");

            bootstrap.SelectDifficulty((M4HarnessDifficulty)selectedDifficulty);
            bootstrap.StartCanonicalDiagnostic(metadata, topology, setup, initialPartner, store, initialPlayerWeaponAction);
            M4CommandResult entry = bootstrap.EnterCanonicalDefense();
            fixedTickRunning = entry.Status == M4CommandStatus.Committed || entry.Status == M4CommandStatus.AlreadyCommitted;
            LastTickResult = entry;
            return entry;
        }

        public M4LoadResult RestoreCanonicalDiagnostic(string json, IM4SessionStore store = null)
        {
            InvalidateCompletedTick("RESTORE_ATTEMPT");
            unchecked { contextGeneration++; }
            if (bootstrap == null) throw new InvalidOperationException("M4DiagnosticBootstrap is required.");
            if (Started) throw new InvalidOperationException("A diagnostic is already started.");
            M4LoadResult restored = bootstrap.RestoreCanonicalDiagnostic(json, store);
            if (restored.Session == null) return restored;

            bool reconnected = false;
            foreach (MonoBehaviour component in fixedTickProcessors)
            {
                if (component == null) throw new InvalidOperationException("Fixed-tick processor binding is missing.");
                if (!(component is IM4ThreeNightQaFixedTickProcessor processor))
                    throw new InvalidOperationException("Fixed-tick processor is not a Core QA processor.");
                processor.RestorePlayerWeaponActionStateFromSession(restored.Session);
                reconnected = true;
            }
            if (!reconnected) throw new InvalidOperationException("No Core processor is bound to restore player weapon/action state.");
            fixedTickRunning = false; // Bounded restore does not restore actor transforms or a playable world.
            LastTickResult = new M4CommandResult
            {
                Status = M4CommandStatus.Committed,
                Reason = "Canonical M4 bounded session and player weapon/action state restored; actor continuation is unavailable."
            };
            return restored;
        }

        // This is the only between-Night transition. It retains the canonical two-flush
        // EnterDefense transaction and does not create a new director or queue itself.
        public M4CommandResult EnterNextCanonicalDefense()
        {
            InvalidateCompletedTick("DEFENSE_ENTRY_ATTEMPT");
            unchecked { contextGeneration++; }
            if (!Started) throw new InvalidOperationException("Explicit Start is required.");
            M4CommandResult entry = bootstrap.EnterCanonicalDefense();
            fixedTickRunning = entry.Status == M4CommandStatus.Committed || entry.Status == M4CommandStatus.AlreadyCommitted;
            LastTickResult = entry;
            return entry;
        }

        public string ExportFrozenEvidenceJson()
        {
            if (!EvidenceFrozen) throw new InvalidOperationException("Frozen Night-3 evidence is available only after canonical completion.");
            return bootstrap.FreezeAndExport();
        }

        void FixedUpdate()
        {
            if (fixedTickRunning) CompleteOneFixedTick();
        }

        void Update()
        {
            InvalidateCompletedTick("UPDATE_WINDOW_ENDED");
        }

        void OnDisable()
        {
            InvalidateCompletedTick("ENTRY_DISABLED");
            unchecked { contextGeneration++; }
        }

        // A Core host may call this directly when it owns a manually driven fixed clock. The
        // callback occurs after every assigned Core processor has finished this tick.
        public M4CommandResult CompleteOneFixedTick()
        {
            InvalidateCompletedTick("COMPLETION_ATTEMPT_NOT_SUCCESSFUL");
            unchecked { completionAttempt++; }
            if (!Started || !fixedTickRunning) throw new InvalidOperationException("A canonical Defense tick is not running.");
            M4Session session = bootstrap.CanonicalSession;
            if (session == null || session.Stage != M4SessionStage.Defense)
            {
                fixedTickRunning = false;
                return LastTickResult = new M4CommandResult { Status = M4CommandStatus.InvalidPhase, Reason = "Canonical session is not in Defense." };
            }

            // Read the session-owned instance at this boundary. ObserveCompletedTick commits a
            // detached candidate, so retaining a prior boundary's reference would be stale.
            EnemyDirectorRuntime director = session.BoundDirector;
            if (director == null) throw new InvalidOperationException("Defense has no session-owned M3 director.");
            int runFixedTick = session.RuntimeRun.FixedTick;
            int nightFixedTick = session.NightElapsedTick.GetValueOrDefault(-1);
            if (nightFixedTick < 0) throw new InvalidOperationException("Defense has no canonical Night tick.");
            int nightOrdinal = session.ActiveEntry.Key.NightOrdinal;
            string runId = session.Metadata.RunId.ToString();
            var context = new M4ThreeNightQaFixedTickContext(runFixedTick, nightFixedTick, session.RuntimeRun, director, session.RuntimePool, session.RuntimeRunners);
            foreach (MonoBehaviour component in fixedTickProcessors)
            {
                if (component == null) throw new InvalidOperationException("Fixed-tick processor binding is missing.");
                if (!(component is IM4ThreeNightQaFixedTickProcessor processor)) throw new InvalidOperationException("Fixed-tick processor is not a Core QA processor.");
                processor.ProcessCompletedCoreTick(context);
                // Replay divergence can halt from inside a processor. Do not invoke another
                // processor, advance this RunState tick, or observe completion afterward.
                if (!fixedTickRunning) return LastTickResult;
            }

            // Advance the canonical run clock only after the current local M3 Night tick has
            // completed. This gives every Night its required first M3 tick of zero while
            // retaining the global RunState clock across Night boundaries.
            session.RuntimeRun.AdvanceOneTick();
            // This must remain after all Core M3 work for the tick and must use the exact
            // session-owned director read above. It preserves M3 request/queue determinism.
            M4CommandResult completion = bootstrap.ObserveCanonicalCompletedTick(director);
            LastTickResult = completion;
            if (bootstrap.CanonicalSession.Stage != M4SessionStage.Defense) fixedTickRunning = false;
            // Night/life/safety facts are canonical before any exchange can commit.
            if (completion.Status == M4CommandStatus.NoChange || completion.Status == M4CommandStatus.Committed)
                foreach (MonoBehaviour component in fixedTickProcessors)
                    if (component is IM4PartnerExchangeCompletionSink exchangeSink)
                        exchangeSink.ProcessExchangeCompletionBoundary(runFixedTick);
            // NoChange is normal successful observation, not a persistence failure.
            // Stamp only after the unchanged canonical advance/observe sequence returns.
            if (completion.Status == M4CommandStatus.NoChange || completion.Status == M4CommandStatus.Committed)
            {
                M4Session current = bootstrap.CanonicalSession;
                completedTickSession = current;
                completedTickStage = current.Stage;
                completedTick = new M4CanonicalCompletedTickSnapshot(true, null, runId,
                    contextGeneration, completionAttempt, runFixedTick, nightFixedTick, nightOrdinal,
                    current.RuntimeRun.FixedTick, current.NightElapsedTick.GetValueOrDefault(-1),
                    current.Stage == M4SessionStage.NightCompleted || current.Stage == M4SessionStage.Frozen);
            }
            return completion;
        }
    }
}
