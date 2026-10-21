using System;
using LastWatch.Core.Combat;
using LastWatch.Core.Configuration;
using LastWatch.Core.Identity;
using LastWatch.Core.Persistence;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    // Source component for the project-owned diagnostic entry
    // LastWatch_M2M3M4_3Night_QA. It initializes only after explicit Start.
    public sealed class M4DiagnosticBootstrap : MonoBehaviour
    {
        [SerializeField] M4HarnessDifficulty difficulty=M4HarnessDifficulty.Normal;
        public bool Started { get; private set; }
        public M4ThreeNightHarness Harness { get; private set; }
        public RunState Run { get; private set; }
        public PartnerRuntimeState Partner { get; private set; }
        public M4AuthoritativeCommitCoordinator Coordinator { get; private set; }
        public M4Session CanonicalSession { get; private set; }
        IM4SessionStore sessionStore;
        public IM4SessionStore SessionStore => sessionStore;
        public void StartCanonicalDiagnostic(M4RunMetadata metadata,M4TopologySnapshot topology,DamageableFixture[] setup,PartnerRuntimeState initialPartner,IM4SessionStore store=null,M4PlayerWeaponActionSaveState initialPlayerWeaponAction=null)
        {
            if(Started)throw new InvalidOperationException("A diagnostic is already started.");
            CanonicalSession=M4Session.CreateNew(metadata,"0.1.0-m4-static",topology,setup,initialPartner,initialPlayerWeaponAction);
            sessionStore=store??new M4InMemorySessionStore();Started=true;
        }
        public LastWatch.Core.Persistence.M4LoadResult RestoreCanonicalDiagnostic(string json)
        { return RestoreCanonicalDiagnostic(json,null); }
        public LastWatch.Core.Persistence.M4LoadResult RestoreCanonicalDiagnostic(string json,IM4SessionStore store)
        {
            if(Started)throw new InvalidOperationException("A diagnostic is already started.");
            var result=LastWatch.Core.Persistence.M4SessionJson.TryDeserialize(json,LastWatch.Core.Persistence.M4RestorePurpose.BoundedContract);
            // A resumed command host must receive the same durable store that supplied the
            // snapshot. A fresh in-memory store cannot fabricate its prior generation.
            if(result.Session!=null)
            {
                if(result.Session.ReadPlayerWeaponActionState()==null)
                    return new LastWatch.Core.Persistence.M4LoadResult
                    {
                        Status=LastWatch.Core.Persistence.M4LoadStatus.UnsupportedRuntimeState,
                        Reason="This legacy bounded session has no saved player weapon/action state; reload progress cannot be reconstructed.",
                        OriginalJson=json
                    };
                CanonicalSession=result.Session;sessionStore=store;Started=true;
            }
            return result;
        }
        public void SelectDifficulty(M4HarnessDifficulty value){if(Started)throw new InvalidOperationException("Difficulty is immutable after Start.");difficulty=value;}
        public void StartDiagnostic()
        {
            if(Started)return;Harness=new M4ThreeNightHarness(difficulty);
            Run=RunStateFactory.CreateNew(new RunId(M4ThreeNightHarness.EvidenceRunSeed),"0.1.0-m4-static","3.1",M4ThreeNightHarness.EvidenceRunSeed);
            Partner=new PartnerRuntimeState();Partner.DesignatedFaceId="A";new PartnerController(Partner).RequestRole(new PartnerRoleRequest(PartnerRole.CoverFace,"A","FaceA.Cover",0),0);
            Coordinator=new M4AuthoritativeCommitCoordinator(Harness,Run,Partner,Array.Empty<EntityId>(),Array.Empty<EntityId>());
            Started=true;
        }
        public void BindAuthoritativeStructureIds(EntityId[] barrierIds,EntityId[] supportIds)
        { if(!Started)throw new InvalidOperationException("Diagnostic has not started.");Coordinator=new M4AuthoritativeCommitCoordinator(Harness,Run,Partner,barrierIds,supportIds,Coordinator.CaptureSave()); }
        public void ObserveAuthoritativeRuntime(LastWatch.Core.Combat.EnemyDirectorRuntime director)
        {if(CanonicalSession!=null)throw new InvalidOperationException("Use CanonicalSession.ObserveCompletedTick with its store.");Coordinator.Observe(director);}
        public void BeginNextCanonicalNight()
        {if(CanonicalSession!=null)throw new InvalidOperationException("Use CanonicalSession.EnterDefense with its store.");Coordinator.BeginNextCanonicalNight();}
        public M4CommandResult EnterCanonicalDefense()
        {
            if(!Started||CanonicalSession==null||sessionStore==null)throw new InvalidOperationException("Canonical diagnostic/session store has not started.");
            return CanonicalSession.EnterDefense(sessionStore);
        }
        // The M3 integration owner calls this only after a real fixed-tick boundary. It does
        // not advance simulation or manufacture a completion observation.
        public M4CommandResult ObserveCanonicalCompletedTick(EnemyDirectorRuntime director)
        {
            if(!Started||CanonicalSession==null||sessionStore==null)throw new InvalidOperationException("Canonical diagnostic/session store has not started.");
            return CanonicalSession.ObserveCompletedTick(sessionStore,director);
        }
        public string FreezeAndExport(){if(!Started)throw new InvalidOperationException("Diagnostic has not started.");return CanonicalSession!=null?JsonUtility.ToJson(CanonicalSession.ExportFrozen(),true):Harness.ExportFrozenJson(true);}
    }
}
