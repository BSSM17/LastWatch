using LastWatch.Core.Partner;
using NUnit.Framework;
using UnityEngine;

namespace LastWatch.Tests.M4
{
    [TestFixture]
    public sealed class M4ReplayTapeContractTests
    {
        [Test]
        public void FrozenTape_RoundTripsWithStableJsonAndSha256()
        {
            M4ReplayTape original = CreateFrozenThreeNightTape();

            string json = original.ToJson();
            Assert.That(original.ToJson(), Is.EqualTo(json));
            Assert.That(M4ReplayTape.TryParse(json, out M4ReplayTape parsed, out string reason), Is.True, reason);

            Assert.That(parsed.runSeed, Is.EqualTo(original.runSeed));
            Assert.That(parsed.difficulty, Is.EqualTo(original.difficulty));
            Assert.That(parsed.frozen, Is.True);
            Assert.That(parsed.ticks.Length, Is.EqualTo(original.ticks.Length));
            Assert.That(parsed.ComputeSha256(), Is.EqualTo(original.ComputeSha256()));
            Assert.That(parsed.ToJson(), Is.EqualTo(json));
        }

        [Test]
        public void TryValidate_RejectsUnsupportedIdentityAndEmptyTape()
        {
            M4ReplayTape tape = CreateFrozenThreeNightTape();
            tape.schemaVersion++;
            AssertRejected(tape);

            tape = CreateFrozenThreeNightTape();
            tape.runSeed++;
            AssertRejected(tape);

            tape = CreateFrozenThreeNightTape();
            tape.difficulty = 2;
            AssertRejected(tape);

            tape = new M4ReplayTape(1234UL, 0) { frozen = true };
            AssertRejected(tape);
        }

        [Test]
        public void TryValidate_RejectsGappedTicksSkippedNightsAndInvalidCommandValues()
        {
            M4ReplayTape tape = CreateFrozenThreeNightTape();
            tape.ticks[1].runFixedTick = 7;
            AssertRejected(tape);

            tape = CreateFrozenThreeNightTape();
            tape.ticks[1].nightOrdinal = 3;
            AssertRejected(tape);

            tape = CreateFrozenThreeNightTape();
            tape.ticks[0].cameraPitch = 81f;
            AssertRejected(tape);

            tape = CreateFrozenThreeNightTape();
            tape.ticks[0].weapon = 3;
            AssertRejected(tape);

            tape = CreateFrozenThreeNightTape();
            tape.ticks[0].positionX = float.NaN;
            AssertRejected(tape);
        }

        [Test]
        public void TryParse_RejectsEmptyMalformedAndInvalidJson()
        {
            AssertParseRejected(string.Empty);
            AssertParseRejected("{not-json");

            M4ReplayTape invalid = CreateFrozenThreeNightTape();
            invalid.ticks[0].weapon = -1;
            AssertParseRejected(JsonUtility.ToJson(invalid));
        }

        [Test]
        public void Append_RequiresContiguousRunTicksAndNightProgression()
        {
            M4ReplayTape tape = new M4ReplayTape(1234UL, 0);
            tape.Append(CreateTick(0, 1));

            Assert.Throws<System.InvalidOperationException>(() => tape.Append(CreateTick(2, 1)));
            Assert.Throws<System.InvalidOperationException>(() => tape.Append(CreateTick(1, 3)));

            tape.Append(CreateTick(1, 2));
            tape.Append(CreateTick(2, 3));
            tape.frozen = true;
            Assert.That(tape.TryValidate(out string reason), Is.True, reason);
            Assert.Throws<System.InvalidOperationException>(() => tape.Append(CreateTick(3, 3)));
        }

        [Test]
        public void TryAppend_AcceptsInclusiveCameraPitchBounds()
        {
            var tape = new M4ReplayTape(1234UL, 0);

            Assert.That(tape.TryAppend(CreatePitchTick(0, 1, -80f), out string reason), Is.True, reason);
            Assert.That(tape.TryAppend(CreatePitchTick(1, 1, 80f), out reason), Is.True, reason);
            Assert.That(tape.ticks.Length, Is.EqualTo(2));
            Assert.That(tape.TryValidate(out reason), Is.True, reason);
        }

        [TestCase(80.00003f, "80.00003")]
        [TestCase(-80.00003f, "-80.00003")]
        public void TryAppend_RejectsOutOfBoundsCameraPitchWithFieldAndTickLocation(float pitch, string formattedPitch)
        {
            var tape = new M4ReplayTape(1234UL, 0);
            M4ReplayTick[] originalTicks = tape.ticks;
            M4ReplayTick invalidTick = CreatePitchTick(1207, 1, pitch);

            Assert.That(tape.TryAppend(invalidTick, out string reason), Is.False);
            StringAssert.Contains("cameraPitch=" + formattedPitch, reason);
            StringAssert.Contains("runFixedTick=1207, nightOrdinal=1", reason);
            StringAssert.Contains("must be finite and in -80..80 degrees", reason);
            Assert.That(tape.ticks, Is.SameAs(originalTicks), "A failed append must leave the tape array unchanged.");
            Assert.That(tape.ticks, Is.Empty);
        }

        [Test]
        public void TryAppend_InvalidCameraPitchDoesNotMutateExistingCommands()
        {
            var tape = new M4ReplayTape(1234UL, 0);
            tape.Append(CreateTick(0, 1));
            M4ReplayTick[] originalTicks = tape.ticks;
            M4ReplayTick originalCommand = tape.ticks[0];

            Assert.That(tape.TryAppend(CreatePitchTick(1, 1, 80.00003f), out string reason), Is.False);
            StringAssert.Contains("cameraPitch=80.00003", reason);
            Assert.That(tape.ticks, Is.SameAs(originalTicks));
            Assert.That(tape.ticks, Has.Length.EqualTo(1));
            Assert.That(tape.ticks[0], Is.SameAs(originalCommand));

            Assert.That(tape.TryAppend(CreateTick(1, 1), out reason), Is.True, reason,
                "A rejected command must not prevent a later valid contiguous command.");
        }

        [Test]
        public void Consumer_MatchingCommandAdvancesExactlyOnceAndReturnsDetachedCopy()
        {
            M4ReplayTape tape = CreateFrozenThreeNightTape();
            var consumer = new M4ReplayConsumer(tape);

            Assert.That(consumer.Cursor, Is.Zero);
            Assert.That(consumer.Count, Is.EqualTo(tape.ticks.Length));
            Assert.That(consumer.TryConsume(0, 1, out M4ReplayTick first), Is.EqualTo(M4ReplayConsumeStatus.Matched));
            Assert.That(consumer.Cursor, Is.EqualTo(1));
            Assert.That(first, Is.Not.Null);
            Assert.That(first.Position, Is.EqualTo(tape.ticks[0].Position));

            first.positionX = 999f;
            Assert.That(tape.ticks[0].positionX, Is.EqualTo(1f), "The consumer must return a detached command copy.");
            Assert.That(consumer.TryConsume(1, 1, out M4ReplayTick second), Is.EqualTo(M4ReplayConsumeStatus.Matched));
            Assert.That(consumer.Cursor, Is.EqualTo(2));
            Assert.That(second.Position, Is.EqualTo(tape.ticks[1].Position));

            Assert.That(consumer.TryConsume(1, 1, out M4ReplayTick repeated), Is.EqualTo(M4ReplayConsumeStatus.RunTickMismatch));
            Assert.That(repeated, Is.Null);
            Assert.That(consumer.Cursor, Is.EqualTo(2), "A previously consumed tick must not advance the cursor a second time.");
        }

        [Test]
        public void Consumer_RunTickAndNightMismatchDoNotConsumeOrReturnCommand()
        {
            M4ReplayTape tape = CreateFrozenThreeNightTape();
            var runTickConsumer = new M4ReplayConsumer(tape);
            Assert.That(runTickConsumer.TryConsume(1, 1, out M4ReplayTick wrongTick), Is.EqualTo(M4ReplayConsumeStatus.RunTickMismatch));
            Assert.That(wrongTick, Is.Null);
            Assert.That(runTickConsumer.Cursor, Is.Zero);

            var nightConsumer = new M4ReplayConsumer(tape);
            Assert.That(nightConsumer.TryConsume(0, 2, out M4ReplayTick wrongNight), Is.EqualTo(M4ReplayConsumeStatus.NightMismatch));
            Assert.That(wrongNight, Is.Null);
            Assert.That(nightConsumer.Cursor, Is.Zero);

            Assert.That(nightConsumer.TryConsume(0, 1, out _), Is.EqualTo(M4ReplayConsumeStatus.Matched));
            Assert.That(nightConsumer.Cursor, Is.EqualTo(1));
        }

        [Test]
        public void Consumer_PartialDiagnosticTapeFailsClosedAtExhaustionAndCannotComplete()
        {
            var partial = new M4ReplayTape(1234UL, 0);
            partial.Append(CreateTick(0, 1));
            var consumer = new M4ReplayConsumer(partial);

            Assert.That(consumer.TryConsume(0, 1, out _), Is.EqualTo(M4ReplayConsumeStatus.Matched));
            Assert.That(consumer.IsExhausted, Is.True);
            Assert.That(consumer.TryConsume(1, 1, out M4ReplayTick missing), Is.EqualTo(M4ReplayConsumeStatus.Exhausted));
            Assert.That(missing, Is.Null);
            Assert.That(consumer.Cursor, Is.EqualTo(consumer.Count));
            Assert.That(consumer.TryComplete(true, out string reason), Is.False);
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void Consumer_CompletionRequiresFrozenNightThreeSessionAndFullConsumption()
        {
            M4ReplayTape tape = CreateFrozenThreeNightTape();
            var consumer = new M4ReplayConsumer(tape);

            Assert.That(consumer.TryComplete(true, out string reason), Is.False, "Unconsumed commands must prevent completion.");
            Assert.That(reason, Is.Not.Empty);
            ConsumeAll(consumer, tape);

            Assert.That(consumer.TryComplete(false, out reason), Is.False, "A frozen tape cannot complete an unfrozen canonical session.");
            Assert.That(consumer.TryComplete(true, out reason), Is.True, reason);

            M4ReplayTape unfrozenFullTape = CreateFrozenThreeNightTape();
            unfrozenFullTape.frozen = false;
            var unfrozenConsumer = new M4ReplayConsumer(unfrozenFullTape);
            ConsumeAll(unfrozenConsumer, unfrozenFullTape);
            Assert.That(unfrozenConsumer.TryComplete(true, out reason), Is.False, "Diagnostic input is not a frozen capture.");
            Assert.That(reason, Is.Not.Empty);

            var frozenPartialTape = new M4ReplayTape(1234UL, 0);
            frozenPartialTape.Append(CreateTick(0, 1));
            frozenPartialTape.Append(CreateTick(1, 2));
            frozenPartialTape.frozen = true;
            Assert.That(frozenPartialTape.TryValidate(out reason), Is.False, "A frozen tape must include Night 3 commands.");
            Assert.Throws<System.ArgumentException>(() => new M4ReplayConsumer(frozenPartialTape));
        }

        static M4ReplayTape CreateFrozenThreeNightTape()
        {
            var tape = new M4ReplayTape(1234UL, 1);
            tape.Append(M4ReplayTick.Capture(0, 1, new Vector3(1f, 0f, -2f), 15f, -5f, 0, false, false));
            tape.Append(M4ReplayTick.Capture(1, 1, new Vector3(1.5f, 0f, -2f), 20f, 0f, 1, true, false));
            tape.Append(M4ReplayTick.Capture(2, 2, new Vector3(2f, 0f, -2f), 25f, 5f, 2, false, true));
            tape.Append(M4ReplayTick.Capture(3, 3, new Vector3(2.5f, 0f, -2f), 30f, 0f, 0, true, false));
            tape.frozen = true;
            return tape;
        }

        static M4ReplayTick CreateTick(int runTick, int night)
        {
            return M4ReplayTick.Capture(runTick, night, Vector3.zero, 0f, 0f, 0, false, false);
        }

        static M4ReplayTick CreatePitchTick(int runTick, int night, float pitch)
        {
            return M4ReplayTick.Capture(runTick, night, Vector3.zero, 0f, pitch, 0, false, false);
        }

        static void ConsumeAll(M4ReplayConsumer consumer, M4ReplayTape tape)
        {
            for (int i = 0; i < tape.ticks.Length; i++)
            {
                M4ReplayTick expected = tape.ticks[i];
                Assert.That(consumer.TryConsume(expected.runFixedTick, expected.nightOrdinal, out M4ReplayTick actual), Is.EqualTo(M4ReplayConsumeStatus.Matched));
                Assert.That(actual.runFixedTick, Is.EqualTo(expected.runFixedTick));
                Assert.That(actual.nightOrdinal, Is.EqualTo(expected.nightOrdinal));
            }
            Assert.That(consumer.IsExhausted, Is.True);
            Assert.That(consumer.Cursor, Is.EqualTo(consumer.Count));
        }

        static void AssertRejected(M4ReplayTape tape)
        {
            Assert.That(tape.TryValidate(out string reason), Is.False);
            Assert.That(reason, Is.Not.Empty);
        }

        static void AssertParseRejected(string json)
        {
            Assert.That(M4ReplayTape.TryParse(json, out M4ReplayTape tape, out string reason), Is.False);
            Assert.That(tape, Is.Null);
            Assert.That(reason, Is.Not.Empty);
        }
    }
}
