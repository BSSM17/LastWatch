using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    // Diagnostic input tape for reproducing the bounded M4 graybox through the same Core
    // fixed-tick combat path. This stores effective per-tick pose/action, not raw device input.
    [Serializable]
    public sealed class M4ReplayTape
    {
        public const int CurrentSchemaVersion = 2;
        public int schemaVersion = CurrentSchemaVersion;
        public ulong runSeed;
        public int difficulty;
        public M4ReplayTick[] ticks = Array.Empty<M4ReplayTick>();
        public bool frozen;

        public M4ReplayTape(ulong seed, int selectedDifficulty)
        {
            runSeed = seed;
            difficulty = selectedDifficulty;
        }

        public M4ReplayTape() { }

        public void Append(M4ReplayTick tick)
        {
            if (frozen) throw new InvalidOperationException("A frozen replay tape cannot be extended.");
            if (!TryValidateTick(tick, out string invalidTickReason))
                throw new ArgumentException(invalidTickReason, nameof(tick));
            int expectedTick = ticks == null ? 0 : ticks.Length;
            if (!TryValidateAppendOrder(tick, expectedTick, ticks, out string orderReason))
                throw new InvalidOperationException(orderReason);

            AppendValidated(tick, expectedTick);
        }

        // Capture is a best-effort QA sidecar. Callers on the canonical fixed-tick path use
        // this non-throwing result so malformed commands can invalidate the tape without
        // aborting the actual gameplay tick.
        public bool TryAppend(M4ReplayTick tick, out string reason)
        {
            if (frozen) return Fail("A frozen replay tape cannot be extended.", out reason);
            if (!TryValidateTick(tick, out reason)) return false;
            int expectedTick = ticks == null ? 0 : ticks.Length;
            if (!TryValidateAppendOrder(tick, expectedTick, ticks, out reason)) return false;

            AppendValidated(tick, expectedTick);
            reason = string.Empty;
            return true;
        }

        public bool TryValidate(out string reason)
        {
            if (schemaVersion != CurrentSchemaVersion) return Fail("Unsupported replay schema.", out reason);
            if (runSeed != M4ThreeNightQaSceneEntry.EvidenceRunSeed) return Fail("Replay RunSeed is not the approved bounded seed.", out reason);
            if (difficulty != 0 && difficulty != 1) return Fail("Replay difficulty must be Normal (0) or Hard (1).", out reason);
            if (ticks == null || ticks.Length == 0) return Fail("Replay contains no fixed-tick commands.", out reason);
            int previousNight = 0;
            for (int i = 0; i < ticks.Length; i++)
            {
                M4ReplayTick tick = ticks[i];
                if (!TryValidateTick(tick, out string invalidTickReason)) return Fail(invalidTickReason, out reason);
                if (tick.runFixedTick != i)
                    return Fail("Replay tick at " + TickLocation(tick) + " has invalid runFixedTick ordering; expected " + i.ToString(CultureInfo.InvariantCulture) + ".", out reason);
                if (tick.nightOrdinal < previousNight || tick.nightOrdinal > previousNight + 1)
                    return Fail("Replay tick at " + TickLocation(tick) + " has invalid nightOrdinal ordering; it must be monotonic and cannot skip a Night.", out reason);
                previousNight = tick.nightOrdinal;
            }
            if (frozen && previousNight != 3)
                return Fail("A frozen replay tape must contain commands through Night 3.", out reason);
            reason = string.Empty;
            return true;
        }

        public string ToJson()
        {
            if (!TryValidate(out string reason)) throw new InvalidOperationException(reason);
            return JsonUtility.ToJson(this, false);
        }

        public string ComputeSha256()
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(ToJson()));
                var result = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) result.Append(digest[i].ToString("x2"));
                return result.ToString();
            }
        }

        public static bool TryParse(string json, out M4ReplayTape tape, out string reason)
        {
            tape = null;
            if (string.IsNullOrWhiteSpace(json)) return Fail("Clipboard is empty.", out reason);
            try
            {
                tape = JsonUtility.FromJson<M4ReplayTape>(json);
            }
            catch (Exception exception)
            {
                return Fail("Replay JSON could not be parsed: " + exception.Message, out reason);
            }
            if (tape == null)
                return Fail("Replay JSON did not contain a tape object.", out reason);
            if (!tape.TryValidate(out reason))
            {
                tape = null;
                return false;
            }
            return true;
        }

        static bool Fail(string message, out string reason)
        {
            reason = message;
            return false;
        }

        internal static bool TryValidateTick(M4ReplayTick tick, out string reason)
        {
            if (tick == null)
                return Fail("Replay tick has invalid field tick=<null>; runFixedTick and nightOrdinal are unavailable.", out reason);

            if (tick.runFixedTick < 0)
                return InvalidField(tick, "runFixedTick", tick.runFixedTick.ToString(CultureInfo.InvariantCulture), "must be >= 0", out reason);
            if (tick.nightOrdinal < 1 || tick.nightOrdinal > 3)
                return InvalidField(tick, "nightOrdinal", tick.nightOrdinal.ToString(CultureInfo.InvariantCulture), "must be in 1..3", out reason);
            if (!IsFinite(tick.positionX))
                return InvalidField(tick, "positionX", FormatFloat(tick.positionX), "must be finite", out reason);
            if (!IsFinite(tick.positionY))
                return InvalidField(tick, "positionY", FormatFloat(tick.positionY), "must be finite", out reason);
            if (!IsFinite(tick.positionZ))
                return InvalidField(tick, "positionZ", FormatFloat(tick.positionZ), "must be finite", out reason);
            if (!IsFinite(tick.bodyYaw))
                return InvalidField(tick, "bodyYaw", FormatFloat(tick.bodyYaw), "must be finite", out reason);
            if (!IsFinite(tick.cameraPitch) || tick.cameraPitch < -80f || tick.cameraPitch > 80f)
                return InvalidField(tick, "cameraPitch", FormatFloat(tick.cameraPitch), "must be finite and in -80..80 degrees", out reason);
            if (tick.weapon < 0 || tick.weapon > 2)
                return InvalidField(tick, "weapon", tick.weapon.ToString(CultureInfo.InvariantCulture), "must be in 0..2", out reason);
            if (!tick.triggerStateRecorded || (tick.triggerHeld && tick.weapon != 1) ||
                (tick.triggerHeld && !tick.firePressed))
                return Fail("Replay command lacks supported explicit trigger state (schema2 required).", out reason);

            reason = string.Empty;
            return true;
        }

        static bool TryValidateAppendOrder(M4ReplayTick tick, int expectedTick, M4ReplayTick[] existingTicks, out string reason)
        {
            if (tick.runFixedTick != expectedTick)
                return Fail("Replay tick at " + TickLocation(tick) + " has invalid runFixedTick; expected " + expectedTick.ToString(CultureInfo.InvariantCulture) + ".", out reason);
            if (expectedTick > 0)
            {
                M4ReplayTick previous = existingTicks[expectedTick - 1];
                if (previous == null)
                    return Fail("Replay tape already contains a null command at runFixedTick=" + (expectedTick - 1).ToString(CultureInfo.InvariantCulture) + ".", out reason);
                if (tick.nightOrdinal < previous.nightOrdinal || tick.nightOrdinal > previous.nightOrdinal + 1)
                    return Fail("Replay tick at " + TickLocation(tick) + " has invalid nightOrdinal progression from " + previous.nightOrdinal.ToString(CultureInfo.InvariantCulture) + "; Night ordinals must be monotonic and cannot skip a Night.", out reason);
            }

            reason = string.Empty;
            return true;
        }

        void AppendValidated(M4ReplayTick tick, int expectedTick)
        {
            var next = new M4ReplayTick[expectedTick + 1];
            if (expectedTick > 0) Array.Copy(ticks, next, expectedTick);
            next[expectedTick] = tick;
            ticks = next;
        }

        static bool InvalidField(M4ReplayTick tick, string field, string value, string constraint, out string reason)
        {
            return Fail("Replay tick at " + TickLocation(tick) + " has invalid field " + field + "=" + value + "; " + constraint + ".", out reason);
        }

        static string TickLocation(M4ReplayTick tick) =>
            "runFixedTick=" + tick.runFixedTick.ToString(CultureInfo.InvariantCulture) + ", nightOrdinal=" + tick.nightOrdinal.ToString(CultureInfo.InvariantCulture);

        static string FormatFloat(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [Serializable]
    public sealed class M4ReplayTick
    {
        public int runFixedTick;
        public int nightOrdinal;
        public float positionX;
        public float positionY;
        public float positionZ;
        public float bodyYaw;
        public float cameraPitch;
        public int weapon;
        public bool firePressed;
        public bool triggerStateRecorded;
        public bool triggerHeld;
        public bool triggerReleased;
        public bool reloadPressed;

        public static M4ReplayTick Capture(int runTick, int night, Vector3 position, float yaw, float pitch, int selectedWeapon, bool fire, bool reload,
            bool held = false, bool released = true)
        {
            return new M4ReplayTick
            {
                runFixedTick = runTick,
                nightOrdinal = night,
                positionX = position.x,
                positionY = position.y,
                positionZ = position.z,
                bodyYaw = yaw,
                cameraPitch = pitch,
                weapon = selectedWeapon,
                firePressed = fire,
                triggerStateRecorded = true,
                triggerHeld = held,
                triggerReleased = released,
                reloadPressed = reload
            };
        }

        public Vector3 Position => new Vector3(positionX, positionY, positionZ);

        internal M4ReplayTick DetachedCopy()
        {
            return new M4ReplayTick
            {
                runFixedTick = runFixedTick,
                nightOrdinal = nightOrdinal,
                positionX = positionX,
                positionY = positionY,
                positionZ = positionZ,
                bodyYaw = bodyYaw,
                cameraPitch = cameraPitch,
                weapon = weapon,
                firePressed = firePressed,
                triggerStateRecorded = triggerStateRecorded,
                triggerHeld = triggerHeld,
                triggerReleased = triggerReleased,
                reloadPressed = reloadPressed
            };
        }
    }

#if UNITY_EDITOR
    // Editor-only stimulus for an unattended fresh capture. This is intentionally a
    // different schema from M4ReplayTape: it describes a plan to execute, not evidence
    // copied from a previously completed run.
    [Serializable]
    public sealed class M4QaInputPlan
    {
        public const int CurrentSchemaVersion = 2;
        public int schemaVersion = CurrentSchemaVersion;
        public string planId;
        public ulong runSeed;
        public int difficulty;
        public M4ReplayTick[] ticks = Array.Empty<M4ReplayTick>();

        public bool TryValidate(out string reason)
        {
            if (schemaVersion != CurrentSchemaVersion) return Fail("Unsupported QA input-plan schema.", out reason);
            if (!IsValidPlanId(planId)) return Fail("QA input plan requires a 1..80 character ASCII planId using letters, digits, '.', '_' or '-'.", out reason);
            if (runSeed != M4ThreeNightQaSceneEntry.EvidenceRunSeed) return Fail("QA input plan RunSeed is not the approved bounded seed.", out reason);
            if (difficulty != 0 && difficulty != 1) return Fail("QA input-plan difficulty must be Normal (0) or Hard (1).", out reason);
            if (ticks == null || ticks.Length == 0) return Fail("QA input plan contains no fixed-tick commands.", out reason);

            // Reuse the replay command validator for the exact effective command bounds,
            // contiguous canonical tick sequence, and complete three-Night coverage. The
            // temporary tape is validation-only and is never exported as the input plan.
            var validationTape = new M4ReplayTape(runSeed, difficulty) { ticks = ticks, frozen = true };
            if (!validationTape.TryValidate(out reason)) return false;
            reason = string.Empty;
            return true;
        }

        public static bool TryParse(string json, out M4QaInputPlan plan, out string reason)
        {
            plan = null;
            if (string.IsNullOrWhiteSpace(json)) return Fail("QA input plan is empty.", out reason);
            try { plan = JsonUtility.FromJson<M4QaInputPlan>(json); }
            catch (Exception exception) { return Fail("QA input-plan JSON could not be parsed: " + exception.Message, out reason); }
            if (plan == null) return Fail("QA input-plan JSON did not contain a plan object.", out reason);
            if (!plan.TryValidate(out reason))
            {
                plan = null;
                return false;
            }
            return true;
        }

        static bool IsValidPlanId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 80) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-')) return false;
            }
            return true;
        }

        static bool Fail(string message, out string reason)
        {
            reason = message;
            return false;
        }
    }
#endif

    public enum M4ReplayConsumeStatus
    {
        Matched,
        RunTickMismatch,
        NightMismatch,
        Exhausted
    }

    // Pure Core-owned cursor seam. It consumes only validated commands and has no access to
    // Unity input, RunState, Director, session clocks, or gameplay resolution.
    public sealed class M4ReplayConsumer
    {
        readonly M4ReplayTick[] commands;
        readonly bool tapeFrozen;
        readonly int finalNightOrdinal;
        readonly bool inputPlanSource;

        public int Cursor { get; private set; }
        public int Count => commands.Length;
        public bool IsExhausted => Cursor >= commands.Length;

        public M4ReplayConsumer(M4ReplayTape tape)
        {
            if (tape == null) throw new ArgumentNullException(nameof(tape));
            if (!tape.TryValidate(out string reason)) throw new ArgumentException(reason, nameof(tape));
            commands = CopyCommands(tape.ticks);
            tapeFrozen = tape.frozen;
            finalNightOrdinal = commands[commands.Length - 1].nightOrdinal;
        }

#if UNITY_EDITOR
        public M4ReplayConsumer(M4QaInputPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.TryValidate(out string reason)) throw new ArgumentException(reason, nameof(plan));
            commands = CopyCommands(plan.ticks);
            tapeFrozen = false;
            inputPlanSource = true;
            finalNightOrdinal = commands[commands.Length - 1].nightOrdinal;
        }
#endif

        public M4ReplayConsumeStatus TryConsume(int expectedRunTick, int expectedNightOrdinal, out M4ReplayTick command)
        {
            command = null;
            if (IsExhausted) return M4ReplayConsumeStatus.Exhausted;
            M4ReplayTick next = commands[Cursor];
            if (next.runFixedTick != expectedRunTick) return M4ReplayConsumeStatus.RunTickMismatch;
            if (next.nightOrdinal != expectedNightOrdinal) return M4ReplayConsumeStatus.NightMismatch;
            command = next.DetachedCopy();
            Cursor++;
            return M4ReplayConsumeStatus.Matched;
        }

        // frozen is the recorder's declaration that capture stopped after canonical Night 3.
        // Completion additionally requires the session itself to be frozen and every command
        // to have been consumed; neither flag alone proves deterministic equivalence.
        public bool TryComplete(bool canonicalSessionFrozen, out string reason)
        {
            if (inputPlanSource) return Fail("A deterministic QA input plan is a stimulus, not a frozen replay tape.", out reason);
            if (!canonicalSessionFrozen) return Fail("Canonical session has not frozen at Night 3.", out reason);
            if (!tapeFrozen || finalNightOrdinal != 3) return Fail("Input tape is not a frozen three-Night capture.", out reason);
            if (!IsExhausted) return Fail("Canonical session froze with unconsumed replay commands.", out reason);
            reason = string.Empty;
            return true;
        }

        static M4ReplayTick[] CopyCommands(M4ReplayTick[] source)
        {
            var copy = new M4ReplayTick[source.Length];
            for (int i = 0; i < source.Length; i++) copy[i] = source[i].DetachedCopy();
            return copy;
        }

        static bool Fail(string message, out string reason)
        {
            reason = message;
            return false;
        }
    }
}
