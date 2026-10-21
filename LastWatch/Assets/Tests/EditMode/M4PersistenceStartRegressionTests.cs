using LastWatch.Core.Combat;
using LastWatch.Core.Identity;
using LastWatch.Core.Partner;
using LastWatch.Core.Persistence;
using LastWatch.Core.Space;
using LastWatch.Core.State;
using NUnit.Framework;

namespace LastWatch.Tests.M4
{
    [TestFixture]
    public sealed class M4PersistenceStartRegressionTests
    {
        [Test]
        public void CanonicalDefenseEntry_RoundTripsThroughInMemoryStore()
        {
            var session = M4Session.CreateNew(
                new M4RunMetadata(new RunId(0x4d340001UL), 1234UL, LastWatchDifficulty.Hard),
                "0.1.0-m4-static",
                new M4TopologySnapshot(M4BridgeProfile.ContentVersion, 0,
                    new[] { FaceId.FaceA, FaceId.FaceB, FaceId.FaceC },
                    new[]
                    {
                        new M4EntityBinding(new EntityId(0x4d340101UL), M4BindingRole.Player, null),
                        new M4EntityBinding(new EntityId(0x4d340102UL), M4BindingRole.Partner, null)
                    }),
                new[]
                {
                    new DamageableFixture { EntityValue = 0x4d340101UL, Kind = DamageableKind.Player, HitPoints = 100f },
                    new DamageableFixture { EntityValue = 0x4d340102UL, Kind = DamageableKind.Partner, HitPoints = 100f }
                },
                new PartnerRuntimeState
                {
                    Role = PartnerRole.CoverFace,
                    DesignatedFaceId = "A",
                    CurrentNodeId = "FaceA.Cover",
                    HitPoints = 100f,
                    Life = PartnerLifeState.Healthy
                });

            M4CommandResult result = session.EnterDefense(new M4InMemorySessionStore());

            Assert.That(result.Status, Is.EqualTo(M4CommandStatus.Committed), result.Reason);
            Assert.That(session.Stage, Is.EqualTo(M4SessionStage.Defense));
        }

        [Test]
        public void DefenseTickWithoutEvidenceChange_DoesNotPersistFullSession()
        {
            var store = new M4InMemorySessionStore();
            M4Session session = CreateHardSession();
            Assert.That(session.EnterDefense(store).Status, Is.EqualTo(M4CommandStatus.Committed));

            M4CommandResult result = session.ObserveCompletedTick(store, session.BoundDirector);

            Assert.That(result.Status, Is.EqualTo(M4CommandStatus.NoChange), result.Reason);
            Assert.That(session.Stage, Is.EqualTo(M4SessionStage.Defense));
        }

        static M4Session CreateHardSession()
        {
            return M4Session.CreateNew(
                new M4RunMetadata(new RunId(0x4d340001UL), 1234UL, LastWatchDifficulty.Hard),
                "0.1.0-m4-static",
                new M4TopologySnapshot(M4BridgeProfile.ContentVersion, 0,
                    new[] { FaceId.FaceA, FaceId.FaceB, FaceId.FaceC },
                    new[]
                    {
                        new M4EntityBinding(new EntityId(0x4d340101UL), M4BindingRole.Player, null),
                        new M4EntityBinding(new EntityId(0x4d340102UL), M4BindingRole.Partner, null)
                    }),
                new[]
                {
                    new DamageableFixture { EntityValue = 0x4d340101UL, Kind = DamageableKind.Player, HitPoints = 100f },
                    new DamageableFixture { EntityValue = 0x4d340102UL, Kind = DamageableKind.Partner, HitPoints = 100f }
                },
                new PartnerRuntimeState
                {
                    Role = PartnerRole.CoverFace,
                    DesignatedFaceId = "A",
                    CurrentNodeId = "FaceA.Cover",
                    HitPoints = 100f,
                    Life = PartnerLifeState.Healthy
                });
        }
    }
}
