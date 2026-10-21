using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Partner;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LastWatch.Editor.Validation
{
    /// <summary>Writes the Core-owned same-run capture bundle without clipboard or manual hashing.</summary>
    [InitializeOnLoad]
    public static class M4CaptureBundleCollector
    {
        const string MenuPath = "LastWatch/QA/Export Frozen M4 Capture Bundle";
        const string ProvenanceSchemaVersion = "0.2";
        const ulong ExpectedRunSeed = 1234UL;
        const string CompilationFingerprintPrefix = "LastWatch.M4CaptureBundleCollector.CompilationFingerprint.";

        static M4CaptureBundleCollector()
        {
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationNotRequired += OnAssemblyCompilationNotRequired;
            EditorApplication.delayCall += RefreshLoadedCoreFingerprint;
        }

        [MenuItem(MenuPath, true)]
        static bool ValidateMenu() => EditorApplication.isPlaying && !EditorApplication.isCompiling;

        [MenuItem(MenuPath)]
        static void ExportFrozenCaptureBundle()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
            {
                Debug.LogError("M4 capture export requires an active, non-compiling Play Mode session.");
                return;
            }

            M4NightOneGrayboxController controller = FindController();
            if (controller == null)
            {
                Debug.LogError("No active scene M4NightOneGrayboxController was found.");
                return;
            }

            // Read the Core seam exactly once. All later checks operate on this detached snapshot.
            if (!controller.TryGetFrozenCaptureBundle(out M4QaCaptureBundle bundle, out string reason))
            {
                Debug.LogError("M4 capture bundle is unavailable: " + reason);
                return;
            }

            string runId = bundle == null ? string.Empty : bundle.CaptureRunId;
            string folderId = IsNGuid(runId) ? runId : "invalid-capture-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
            string repoRoot = FindRepositoryRoot();
            if (string.IsNullOrEmpty(repoRoot))
            {
                Debug.LogError("M4 capture bundle is valid, but the repository evidence destination could not be located.");
                return;
            }

            string outputDirectory = Path.Combine(repoRoot, "tests", "LastWatch", "evidence", "M4", folderId);
            try
            {
                Directory.CreateDirectory(outputDirectory);
                ExportResult result = ExportDetachedBundle(bundle, outputDirectory, repoRoot);
                WriteDriverReceipt(bundle, result, outputDirectory, repoRoot);
                Debug.Log((result.ManifestComplete ? "M4 capture exported with source fingerprint: " : "M4 capture exported INCOMPLETE; replay remains blocked: ") + outputDirectory + "\n" + result.Message);
            }
            catch (Exception exception)
            {
                WriteFailureRecord(outputDirectory, runId, "COLLECTOR_FAILURE", exception.GetType().Name + ": " + exception.Message);
                WriteDriverReceipt(bundle, new ExportResult(false, exception.GetType().Name + ": " + exception.Message), outputDirectory, repoRoot);
                Debug.LogError("M4 capture bundle export failed; an incomplete status record was written. " + exception);
            }
        }

        static M4NightOneGrayboxController FindController()
        {
            M4NightOneGrayboxController[] all = Resources.FindObjectsOfTypeAll<M4NightOneGrayboxController>();
            for (int i = 0; i < all.Length; i++)
            {
                M4NightOneGrayboxController candidate = all[i];
                if (candidate != null && candidate.gameObject.scene.IsValid() && candidate.gameObject.activeInHierarchy)
                    return candidate;
            }
            return null;
        }

        static ExportResult ExportDetachedBundle(M4QaCaptureBundle bundle, string outputDirectory, string repoRoot)
        {
            if (!TryValidateBundle(bundle, out M4FrozenEvidence evidence, out M4ReplayTape tape, out string validationFailure))
            {
                WriteFailureRecord(outputDirectory, bundle == null ? string.Empty : bundle.CaptureRunId, "INVALID_CORE_BUNDLE", validationFailure);
                return new ExportResult(false, validationFailure);
            }

            InputPlanDependency inputPlan = null;
            if (bundle.InputKind == M4QaCaptureInputKind.DeterministicInputPlan)
            {
                if (!TryReadInputPlanDependency(bundle, repoRoot, out inputPlan, out validationFailure))
                {
                    WriteFailureRecord(outputDirectory, bundle.CaptureRunId, "INPUT_PLAN_PROVENANCE_INVALID", validationFailure);
                    return new ExportResult(false, validationFailure);
                }
            }

            string difficulty = bundle.Difficulty == 0 ? "normal" : "hard";
            string tapeName = difficulty + "-replay-input.json";
            string evidenceName = difficulty + "-m4-frozen.json";
            string manifestName = difficulty + "-provenance-v0.2.json";
            string transcriptName = "capture-transcript-" + bundle.CaptureRunId + ".log";
            string runtimeName = "runtime-identity-" + bundle.CaptureRunId + ".log";

            byte[] tapeBytes = new UTF8Encoding(false).GetBytes(bundle.ReplayTape.RawJson);
            byte[] evidenceBytes = new UTF8Encoding(false).GetBytes(bundle.FrozenEvidence.RawJson);
            WriteNewBytes(Path.Combine(outputDirectory, tapeName), tapeBytes);
            WriteNewBytes(Path.Combine(outputDirectory, evidenceName), evidenceBytes);
            if (inputPlan != null)
            {
                WriteNewBytes(Path.Combine(outputDirectory, inputPlan.PlanFile), inputPlan.PlanBytes);
                WriteNewBytes(Path.Combine(outputDirectory, inputPlan.LineageFile), inputPlan.LineageBytes);
                WriteNewText(Path.Combine(outputDirectory, inputPlan.ReceiptFile), inputPlan.ReceiptJson);
                if (inputPlan.DecisionBytes != null)
                    WriteNewBytes(Path.Combine(outputDirectory, inputPlan.DecisionFile), inputPlan.DecisionBytes);
                if (inputPlan.PolicySourceBytes != null)
                    WriteNewBytes(Path.Combine(outputDirectory, inputPlan.PolicySourceFile), inputPlan.PolicySourceBytes);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string projectVersion = ReadProjectVersion(projectRoot);
            string transcript = "M4 same-run capture export\r\nCaptureRunId: " + bundle.CaptureRunId +
                "\r\nDifficulty: " + difficulty + "\r\nRunSeed: " + bundle.RunSeed.ToString(CultureInfo.InvariantCulture) +
                "\r\nCore getter: TryGetFrozenCaptureBundle = true (one call)\r\n" +
                "ExportedUtc: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\r\n";
            string runtimeRecord = "Generated from the detached Core M4QaCaptureBundle.RuntimeIdentity snapshot.\r\n" +
                "Raw credential-bearing Unity Editor logs are intentionally excluded.\r\n" +
                "UnityVersion: " + bundle.RuntimeIdentity.UnityVersion + "\r\n" +
                "ApplicationVersion: " + bundle.RuntimeIdentity.ApplicationVersion + "\r\n" +
                "UnityBuildGuid: " + (bundle.RuntimeIdentity.UnityBuildGuidAvailable ? bundle.RuntimeIdentity.UnityBuildGuid : "UNAVAILABLE") + "\r\n" +
                "Platform: " + bundle.RuntimeIdentity.Platform + "\r\n" +
                "IsEditor: " + bundle.RuntimeIdentity.IsEditor.ToString() + "\r\n" +
                "ProjectVersion: " + projectVersion + "\r\n" +
                "CaptureRunId: " + bundle.CaptureRunId + "\r\n";
            byte[] transcriptBytes = new UTF8Encoding(false).GetBytes(transcript);
            byte[] runtimeBytes = new UTF8Encoding(false).GetBytes(runtimeRecord);
            WriteNewBytes(Path.Combine(outputDirectory, transcriptName), transcriptBytes);
            WriteNewBytes(Path.Combine(outputDirectory, runtimeName), runtimeBytes);

            SourceFingerprint fingerprint = TryCreateSourceDelivery(bundle, projectRoot, repoRoot, outputDirectory, out string sourceFailure);
            ReplayProvenanceManifest manifest = CreateManifest(bundle, difficulty, tapeName, evidenceName, transcriptName, runtimeName,
                tapeBytes, evidenceBytes, transcriptBytes, runtimeBytes, projectVersion, fingerprint);
            WriteNewText(Path.Combine(outputDirectory, manifestName), JsonUtility.ToJson(manifest, true) + "\n");

            string classification = fingerprint == null ? "INCOMPLETE_SOURCE_IDENTITY_UNAVAILABLE" : "CAPTURE_SOURCE_FINGERPRINTED";
            CaptureStatus status = new CaptureStatus
            {
                Classification = classification,
                CaptureRunId = bundle.CaptureRunId,
                Difficulty = difficulty,
                RunSeed = bundle.RunSeed,
                InputKind = bundle.InputKind.ToString(),
                InputPlanId = bundle.InputPlanId ?? string.Empty,
                InputPlanFile = inputPlan == null ? string.Empty : inputPlan.PlanFile,
                InputPlanSha256 = inputPlan == null ? string.Empty : inputPlan.PlanSha256,
                InputPlanReceiptFile = inputPlan == null ? string.Empty : inputPlan.ReceiptFile,
                InputPlanReceiptSha256 = inputPlan == null ? string.Empty : inputPlan.ReceiptSha256,
                InputPlanLineageFile = inputPlan == null ? string.Empty : inputPlan.LineageFile,
                InputPlanLineageSha256 = inputPlan == null ? string.Empty : inputPlan.LineageSha256,
                BundleSchemaVersion = bundle.SchemaVersion,
                FrozenEvidenceSchemaVersion = evidence.EvidenceSchemaVersion,
                ReplayTapeSchemaVersion = tape.schemaVersion,
                ReplayTickCount = tape.ticks.Length,
                TapeSha256 = Sha256(tapeBytes),
                FrozenEvidenceSha256 = Sha256(evidenceBytes),
                SourceBuildId = fingerprint == null ? string.Empty : fingerprint.SourceBuildId,
                SourceDeliverySha256 = fingerprint == null ? string.Empty : fingerprint.SourceDeliverySha256,
                SourceFailure = fingerprint == null ? sourceFailure : string.Empty,
                ReplayPreflightReady = false,
                Note = fingerprint == null
                    ? "The v0.2 manifest intentionally has blank source identity/link fields and must fail schema/preflight."
                    : "This is one source-backed capture input. Pairing and the existing replay preflight are still required."
            };
            WriteNewText(Path.Combine(outputDirectory, "capture-status.json"), JsonUtility.ToJson(status, true) + "\n");
            if (fingerprint != null)
            {
                string pairResult = TryCreatePairedInput(repoRoot, outputDirectory, bundle, manifest, fingerprint);
                if (!string.IsNullOrEmpty(pairResult)) Debug.Log("M4 source-backed pair: " + pairResult);
            }
            return new ExportResult(fingerprint != null, status.Classification + ": " + (fingerprint == null ? sourceFailure : fingerprint.SourceBuildId),
                fingerprint == null ? string.Empty : fingerprint.SourceBuildId,
                fingerprint == null ? string.Empty : fingerprint.SourceDeliverySha256,
                inputPlan == null ? string.Empty : inputPlan.PlanSha256);
        }

        static bool TryValidateBundle(M4QaCaptureBundle bundle, out M4FrozenEvidence evidence, out M4ReplayTape tape, out string reason)
        {
            evidence = null;
            tape = null;
            if (bundle == null) return Fail("Core returned a null bundle.", out reason);
            if (bundle.SchemaVersion != M4QaCaptureBundle.CurrentSchemaVersion) return Fail("Unsupported capture-bundle schema.", out reason);
            if (!IsNGuid(bundle.CaptureRunId) || bundle.FrozenEvidence == null || bundle.ReplayTape == null ||
                !string.Equals(bundle.CaptureRunId, bundle.FrozenEvidence.CaptureRunId, StringComparison.Ordinal) ||
                !string.Equals(bundle.CaptureRunId, bundle.ReplayTape.CaptureRunId, StringComparison.Ordinal))
                return Fail("Root, frozen-evidence, and replay-tape CaptureRunId values do not exactly match.", out reason);
            if (bundle.RunSeed != ExpectedRunSeed || (bundle.Difficulty != 0 && bundle.Difficulty != 1) ||
                !bundle.Frozen || bundle.CompletedNights != 3 || bundle.ReplayTickCount <= 0 ||
                string.IsNullOrWhiteSpace(bundle.FinalGameplayHash))
                return Fail("Bundle seed/difficulty/frozen/Night/tick/hash identity is invalid.", out reason);
            if (bundle.InputKind != M4QaCaptureInputKind.OwnerLiveInput && bundle.InputKind != M4QaCaptureInputKind.DeterministicInputPlan)
                return Fail("Bundle input kind is unsupported.", out reason);
            if (bundle.InputKind == M4QaCaptureInputKind.DeterministicInputPlan && string.IsNullOrWhiteSpace(bundle.InputPlanId))
                return Fail("Deterministic input-plan capture bundle has no InputPlanId.", out reason);
            if (bundle.InputKind == M4QaCaptureInputKind.OwnerLiveInput && !string.IsNullOrEmpty(bundle.InputPlanId))
                return Fail("Owner-live capture bundle unexpectedly contains an InputPlanId.", out reason);
            if (string.IsNullOrWhiteSpace(bundle.FrozenEvidence.RawJson) || string.IsNullOrWhiteSpace(bundle.ReplayTape.RawJson))
                return Fail("Core raw JSON payload is empty.", out reason);
            if (bundle.RuntimeIdentity == null || !bundle.RuntimeIdentity.UnityVersionAvailable ||
                string.IsNullOrWhiteSpace(bundle.RuntimeIdentity.UnityVersion) || !bundle.RuntimeIdentity.ApplicationVersionAvailable ||
                string.IsNullOrWhiteSpace(bundle.RuntimeIdentity.ApplicationVersion) || string.IsNullOrWhiteSpace(bundle.RuntimeIdentity.Platform))
                return Fail("Required Core runtime identity fields are unavailable.", out reason);

            try { evidence = JsonUtility.FromJson<M4FrozenEvidence>(bundle.FrozenEvidence.RawJson); }
            catch (Exception exception) { return Fail("Frozen evidence JSON parse failed: " + exception.Message, out reason); }
            if (evidence == null || evidence.EvidenceSchemaVersion != 2 || evidence.RunSeed != ExpectedRunSeed ||
                (int)evidence.Difficulty != bundle.Difficulty || evidence.CompletedNights != 3 || !evidence.Frozen ||
                evidence.Nights == null || evidence.Nights.Length != 3 ||
                !string.Equals(evidence.FinalGameplayHash, bundle.FinalGameplayHash, StringComparison.Ordinal) ||
                evidence.Nights[0] == null || evidence.Nights[1] == null || evidence.Nights[2] == null ||
                evidence.Nights[0].NightOrdinal != 1 || evidence.Nights[1].NightOrdinal != 2 || evidence.Nights[2].NightOrdinal != 3 ||
                !string.Equals(evidence.Nights[2].GameplayHash, evidence.FinalGameplayHash, StringComparison.Ordinal))
                return Fail("Frozen evidence schema/identity/final hash does not match the Core bundle.", out reason);

            if (!M4ReplayTape.TryParse(bundle.ReplayTape.RawJson, out tape, out string tapeReason))
                return Fail("Replay tape JSON/schema validation failed: " + tapeReason, out reason);
            if (tape == null || tape.schemaVersion != M4ReplayTape.CurrentSchemaVersion || tape.runSeed != ExpectedRunSeed ||
                tape.difficulty != bundle.Difficulty || !tape.frozen || tape.ticks == null ||
                tape.ticks.Length != bundle.ReplayTickCount || tape.ticks.Length == 0)
                return Fail("Replay tape schema/identity/tick count does not match the Core bundle.", out reason);
            reason = string.Empty;
            return true;
        }

        static bool TryReadInputPlanDependency(M4QaCaptureBundle bundle, string repoRoot,
            out InputPlanDependency dependency, out string reason)
        {
            if (bundle.InputPlanId != null && bundle.InputPlanId.StartsWith("m4-adaptive-", StringComparison.Ordinal))
                return TryReadAdaptiveInputDependency(bundle, repoRoot, out dependency, out reason);
            dependency = null;
            string difficulty = bundle.Difficulty == 0 ? "normal" : "hard";
            string planFile = difficulty + "-input-plan.json";
            string receiptFile = difficulty + "-input-plan-provenance-v0.1.json";
            string lineageFile = "M4_INPUT_PLAN_DEPENDENCIES_v0.1.json";
            string sourceRelativePath = "tests/LastWatch/input-plans/" + difficulty + "-m4-input-plan-v0.1.json";
            string expectedPlanPath = Path.GetFullPath(Path.Combine(repoRoot, sourceRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            string configuredPlanPath = Environment.GetEnvironmentVariable("LASTWATCH_M4_INPUT_PLAN_PATH");
            if (string.IsNullOrWhiteSpace(configuredPlanPath))
                return Fail("LASTWATCH_M4_INPUT_PLAN_PATH is required for deterministic capture export.", out reason);

            string sourcePlanPath;
            try { sourcePlanPath = Path.GetFullPath(configuredPlanPath); }
            catch (Exception exception) { return Fail("Input plan path is invalid: " + exception.Message, out reason); }
            if (!PathsEqual(sourcePlanPath, expectedPlanPath) || !IsWithin(sourcePlanPath, repoRoot) || !File.Exists(sourcePlanPath))
                return Fail("Input plan must be the checked-in difficulty-specific plan under tests/LastWatch/input-plans.", out reason);

            byte[] planBytes;
            M4QaInputPlan plan;
            try
            {
                planBytes = File.ReadAllBytes(sourcePlanPath);
                string rawPlanJson = new UTF8Encoding(false, true).GetString(planBytes);
                if (!M4QaInputPlan.TryParse(rawPlanJson, out plan, out string planReason))
                    return Fail("Checked-in input plan failed Core schema validation: " + planReason, out reason);
            }
            catch (Exception exception) { return Fail("Input plan bytes could not be read or parsed: " + exception.GetType().Name + ": " + exception.Message, out reason); }

            if (plan == null || !string.Equals(plan.planId, bundle.InputPlanId, StringComparison.Ordinal) ||
                plan.runSeed != bundle.RunSeed || plan.difficulty != bundle.Difficulty || plan.ticks.Length != bundle.ReplayTickCount)
                return Fail("Checked-in input plan id/seed/difficulty/command count does not match the frozen Core bundle.", out reason);

            string lineagePath = Path.GetFullPath(Path.Combine(repoRoot, "tests", "LastWatch", "input-plans", lineageFile));
            if (!File.Exists(lineagePath)) return Fail("Checked-in input-plan lineage manifest is missing.", out reason);
            byte[] lineageBytes;
            InputPlanDependencyManifest lineage;
            try
            {
                lineageBytes = File.ReadAllBytes(lineagePath);
                lineage = JsonUtility.FromJson<InputPlanDependencyManifest>(new UTF8Encoding(false, true).GetString(lineageBytes));
            }
            catch (Exception exception) { return Fail("Input-plan lineage manifest could not be read: " + exception.GetType().Name + ": " + exception.Message, out reason); }

            string planSha = Sha256(planBytes);
            InputPlanLineageEntry matched = lineage == null || lineage.dependencies == null ? null :
                lineage.dependencies.FirstOrDefault(item => item != null && string.Equals(item.planId, plan.planId, StringComparison.Ordinal));
            if (lineage == null || lineage.schemaVersion != 1 || matched == null ||
                !string.Equals(matched.planPath, sourceRelativePath, StringComparison.Ordinal) ||
                !string.Equals(matched.planSha256, planSha, StringComparison.OrdinalIgnoreCase) ||
                matched.commandCount != plan.ticks.Length ||
                !string.Equals(matched.sourceUse, "COMMAND_STIMULUS_ONLY_NOT_EVIDENCE", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(matched.sourceTapePath) || matched.sourceTapeSha256 == null || matched.sourceTapeSha256.Length != 64 ||
                string.IsNullOrWhiteSpace(matched.sourceCaptureProvenance))
                return Fail("Input-plan lineage entry does not bind the checked-in plan bytes and stimulus-only source classification.", out reason);

            var receipt = new InputPlanProvenanceReceipt
            {
                SchemaVersion = 1,
                CaptureRunId = bundle.CaptureRunId,
                InputKind = bundle.InputKind.ToString(),
                InputPlanId = plan.planId,
                RunSeed = plan.runSeed,
                Difficulty = plan.difficulty,
                CommandCount = plan.ticks.Length,
                PlanFile = planFile,
                PlanSha256 = planSha,
                SourcePlanPath = sourceRelativePath,
                SourcePlanSha256 = planSha,
                LineageFile = lineageFile,
                LineageSha256 = Sha256(lineageBytes),
                SourceTapePath = matched.sourceTapePath,
                SourceTapeSha256 = matched.sourceTapeSha256,
                SourceUse = matched.sourceUse,
                SourceCaptureProvenance = matched.sourceCaptureProvenance
            };
            string receiptJson = JsonUtility.ToJson(receipt, true) + "\n";
            byte[] receiptBytes = new UTF8Encoding(false).GetBytes(receiptJson);
            dependency = new InputPlanDependency
            {
                PlanFile = planFile,
                PlanBytes = planBytes,
                PlanSha256 = planSha,
                ReceiptFile = receiptFile,
                ReceiptJson = receiptJson,
                ReceiptSha256 = Sha256(receiptBytes),
                LineageFile = lineageFile,
                LineageBytes = lineageBytes,
                LineageSha256 = Sha256(lineageBytes)
            };
            reason = string.Empty;
            return true;
        }

        static bool TryReadAdaptiveInputDependency(M4QaCaptureBundle bundle, string repoRoot,
            out InputPlanDependency dependency, out string reason)
        {
            dependency = null;
            const string lineageFile = "M4_ADAPTIVE_POLICY_DEPENDENCIES_v0.1.json";
            string difficulty = bundle.Difficulty == 0 ? "normal" : "hard";
            string planFile = difficulty + "-input-plan.json";
            string decisionFile = difficulty + "-adaptive-decisions-v0.1.json";
            string receiptFile = difficulty + "-input-plan-provenance-v0.1.json";
            string planPath = Environment.GetEnvironmentVariable("LASTWATCH_M4_ADAPTIVE_PLAN_PATH");
            string decisionPath = Environment.GetEnvironmentVariable("LASTWATCH_M4_ADAPTIVE_DECISION_PATH");
            if (string.IsNullOrWhiteSpace(planPath) || string.IsNullOrWhiteSpace(decisionPath))
                return Fail("Adaptive plan and decision-log paths are required.", out reason);
            try { planPath = Path.GetFullPath(planPath); decisionPath = Path.GetFullPath(decisionPath); }
            catch (Exception exception) { return Fail("Adaptive stimulus path is invalid: " + exception.Message, out reason); }
            string stimulusRoot = Path.Combine(repoRoot, "tests", "LastWatch", "evidence", "m4-adaptive-stimulus");
            if (!IsWithin(planPath, stimulusRoot) || !IsWithin(decisionPath, stimulusRoot) ||
                !File.Exists(planPath) || !File.Exists(decisionPath))
                return Fail("Adaptive stimulus files must exist under tests/LastWatch/evidence/m4-adaptive-stimulus.", out reason);

            byte[] planBytes; byte[] decisionBytes; byte[] lineageBytes;
            M4QaInputPlan plan; AdaptiveDecisionLog log; AdaptivePolicyLineage lineage;
            try
            {
                planBytes = File.ReadAllBytes(planPath);
                decisionBytes = File.ReadAllBytes(decisionPath);
                string planJson = new UTF8Encoding(false, true).GetString(planBytes);
                if (!M4QaInputPlan.TryParse(planJson, out plan, out string planFailure))
                    return Fail("Adaptive plan failed Core validation: " + planFailure, out reason);
                log = JsonUtility.FromJson<AdaptiveDecisionLog>(new UTF8Encoding(false, true).GetString(decisionBytes));
                string lineagePath = Path.Combine(repoRoot, "tests", "LastWatch", "input-plans", lineageFile);
                lineageBytes = File.ReadAllBytes(lineagePath);
                lineage = JsonUtility.FromJson<AdaptivePolicyLineage>(new UTF8Encoding(false, true).GetString(lineageBytes));
            }
            catch (Exception exception) { return Fail("Adaptive provenance is unreadable: " + exception.GetType().Name + ": " + exception.Message, out reason); }
            if (plan == null || plan.planId != bundle.InputPlanId || plan.runSeed != bundle.RunSeed ||
                plan.difficulty != bundle.Difficulty || plan.ticks.Length != bundle.ReplayTickCount ||
                log == null || log.schemaVersion != 1 || log.policyId != "m4-adaptive-visible-actor-v0.1" ||
                log.stimulusId != plan.planId || log.difficulty != plan.difficulty ||
                log.decisions == null || log.decisions.Length != plan.ticks.Length ||
                lineage == null || lineage.schemaVersion != 1 || lineage.policyId != log.policyId)
                return Fail("Adaptive plan/decision identity or command count differs from the frozen Core bundle.", out reason);
            string baselineName = difficulty + "-m4-input-plan-v0.1.json";
            string baselinePath = Path.Combine(repoRoot, "tests", "LastWatch", "input-plans", baselineName);
            if (!File.Exists(baselinePath) ||
                !string.Equals(Sha256(File.ReadAllBytes(baselinePath)), log.baselinePlanSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(difficulty == "normal" ? lineage.normalBaselineSha256 : lineage.hardBaselineSha256,
                    log.baselinePlanSha256, StringComparison.OrdinalIgnoreCase))
                return Fail("Adaptive decision log does not bind the declared baseline stimulus bytes.", out reason);
            string policySourcePath = Path.Combine(repoRoot, "LastWatch", "Assets", "Tests", "PlayMode", "M4AdaptiveQaCaptureTests.cs");
            byte[] policySourceBytes = File.ReadAllBytes(policySourcePath);
            if (!string.Equals(Sha256(policySourceBytes), lineage.policySourceSha256, StringComparison.OrdinalIgnoreCase))
                return Fail("Adaptive policy source does not match the policy lineage hash.", out reason);
            if (!M4ReplayTape.TryParse(bundle.ReplayTape.RawJson, out M4ReplayTape tape, out string tapeFailure))
                return Fail("Core tape could not be parsed for adaptive stimulus comparison: " + tapeFailure, out reason);
            for (int index = 0; index < plan.ticks.Length; index++)
            {
                AdaptiveDecision decision = log.decisions[index];
                if (decision == null || decision.command == null ||
                    decision.runFixedTick != index || decision.nightOrdinal != plan.ticks[index].nightOrdinal ||
                    !string.Equals(JsonUtility.ToJson(decision.command), JsonUtility.ToJson(plan.ticks[index]), StringComparison.Ordinal) ||
                    !AppliedAdaptiveCommandMatches(plan.ticks[index], tape.ticks[index]))
                    return Fail("Adaptive decision, stimulus, and Core applied tape differ at tick " + index + ".", out reason);
            }
            string planSha = Sha256(planBytes);
            string decisionSha = Sha256(decisionBytes);
            string lineageSha = Sha256(lineageBytes);
            string sourcePlanPath = planPath.Substring(repoRoot.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
            var receipt = new InputPlanProvenanceReceipt
            {
                SchemaVersion = 1, CaptureRunId = bundle.CaptureRunId,
                InputKind = bundle.InputKind.ToString(), InputPlanId = plan.planId,
                RunSeed = plan.runSeed, Difficulty = plan.difficulty, CommandCount = plan.ticks.Length,
                PlanFile = planFile, PlanSha256 = planSha, SourcePlanPath = sourcePlanPath,
                SourcePlanSha256 = planSha, LineageFile = lineageFile, LineageSha256 = lineageSha,
                SourceTapePath = decisionFile, SourceTapeSha256 = decisionSha,
                SourceUse = "ADAPTIVE_COMMAND_STIMULUS_ONLY_NOT_EVIDENCE",
                SourceCaptureProvenance = "CORE_APPLIED_TAPE_MATCHED_TO_TOOLS_DECISIONS; FROZEN_EXPORT_IS_SEPARATE",
                DecisionFile = decisionFile, DecisionSha256 = decisionSha
            };
            string receiptJson = JsonUtility.ToJson(receipt, true) + "\n";
            dependency = new InputPlanDependency
            {
                PlanFile = planFile, PlanBytes = planBytes, PlanSha256 = planSha,
                ReceiptFile = receiptFile, ReceiptJson = receiptJson,
                ReceiptSha256 = Sha256(new UTF8Encoding(false).GetBytes(receiptJson)),
                LineageFile = lineageFile, LineageBytes = lineageBytes, LineageSha256 = lineageSha,
                DecisionFile = decisionFile, DecisionBytes = decisionBytes, DecisionSha256 = decisionSha,
                PolicySourceFile = "m4-adaptive-policy-source-v0.1.cs", PolicySourceBytes = policySourceBytes
            };
            reason = string.Empty;
            return true;
        }

        static bool AppliedAdaptiveCommandMatches(M4ReplayTick requested, M4ReplayTick applied)
        {
            return requested != null && applied != null &&
                requested.runFixedTick == applied.runFixedTick && requested.nightOrdinal == applied.nightOrdinal &&
                Vector3.Distance(requested.Position, applied.Position) < 0.001f &&
                Mathf.Abs(Mathf.DeltaAngle(requested.bodyYaw, applied.bodyYaw)) < 0.01f &&
                Mathf.Abs(requested.cameraPitch - applied.cameraPitch) < 0.01f &&
                requested.weapon == applied.weapon && requested.firePressed == applied.firePressed &&
                requested.reloadPressed == applied.reloadPressed;
        }

        static void WriteDriverReceipt(M4QaCaptureBundle bundle, ExportResult result, string outputDirectory, string repoRoot)
        {
            string receiptPath = Environment.GetEnvironmentVariable("LASTWATCH_M4_CAPTURE_RESULT_PATH");
            if (string.IsNullOrWhiteSpace(receiptPath)) return;
            try
            {
                receiptPath = Path.GetFullPath(receiptPath);
                if (!IsWithin(receiptPath, repoRoot)) throw new InvalidOperationException("Capture result path must remain under the repository root.");
                var receipt = new CaptureDriverReceipt
                {
                    SchemaVersion = 1,
                    Classification = result.ManifestComplete ? "CAPTURE_SOURCE_FINGERPRINTED" : "CAPTURE_INCOMPLETE",
                    CaptureRunId = bundle == null ? string.Empty : bundle.CaptureRunId,
                    Difficulty = bundle == null ? -1 : bundle.Difficulty,
                    InputKind = bundle == null ? string.Empty : bundle.InputKind.ToString(),
                    InputPlanId = bundle == null ? string.Empty : bundle.InputPlanId,
                    InputPlanSha256 = result.InputPlanSha256,
                    SourceBuildId = result.SourceBuildId,
                    SourceDeliverySha256 = result.SourceDeliverySha256,
                    CaptureDirectory = RelativePath(repoRoot, outputDirectory).Replace('\\', '/'),
                    Message = result.Message
                };
                WriteNewText(receiptPath, JsonUtility.ToJson(receipt, true) + "\n");
            }
            catch (Exception exception)
            {
                Debug.LogError("M4 capture driver receipt could not be written: " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        static SourceFingerprint TryCreateSourceDelivery(M4QaCaptureBundle bundle, string projectRoot, string repoRoot, string outputDirectory, out string reason)
        {
            reason = string.Empty;
            try
            {
                if (EditorApplication.isCompiling) throw new InvalidOperationException("Unity is compiling scripts.");
                System.Reflection.Assembly loaded = typeof(M4NightOneGrayboxController).Assembly;
                string loadedPath = Path.GetFullPath(loaded.Location);
                UnityEditor.Compilation.Assembly compiled = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                    .FirstOrDefault(item => string.Equals(item.name, loaded.GetName().Name, StringComparison.Ordinal));
                if (string.IsNullOrWhiteSpace(compiled.outputPath))
                    throw new InvalidOperationException("CompilationPipeline did not expose the loaded Core assembly.");
                string outputPath = Path.GetFullPath(Path.IsPathRooted(compiled.outputPath)
                    ? compiled.outputPath : Path.Combine(projectRoot, compiled.outputPath));
                if (!PathsEqual(loadedPath, outputPath) || !File.Exists(outputPath))
                    throw new InvalidOperationException("Loaded Core Assembly.Location does not match CompilationPipeline outputPath.");

                byte[] assemblyBytes = File.ReadAllBytes(outputPath);
                string assemblySha = Sha256(assemblyBytes);
                string assemblyMvid = loaded.ManifestModule.ModuleVersionId.ToString("N");
                if (assemblyBytes.Length == 0 || string.IsNullOrWhiteSpace(assemblyMvid))
                    throw new InvalidOperationException("Loaded Core assembly fingerprint is unavailable.");
                if (compiled.sourceFiles == null || compiled.sourceFiles.Length == 0)
                    throw new InvalidOperationException("CompilationPipeline returned no Core sourceFiles.");

                var sourceEntries = new List<SourceFileReceipt>();
                var archiveEntries = new List<ArchiveBytes>();
                foreach (string sourcePathValue in compiled.sourceFiles.OrderBy(path => path, StringComparer.Ordinal))
                {
                    string sourcePath = Path.GetFullPath(Path.IsPathRooted(sourcePathValue)
                        ? sourcePathValue : Path.Combine(projectRoot, sourcePathValue));
                    if (!IsWithin(sourcePath, projectRoot))
                        throw new InvalidOperationException("Core source file is outside the Unity project: " + sourcePathValue);
                    if (!File.Exists(sourcePath)) throw new FileNotFoundException("Core compilation source is missing.", sourcePath);
                    DateTime sourceWriteUtc = File.GetLastWriteTimeUtc(sourcePath);
                    DateTime outputWriteUtc = File.GetLastWriteTimeUtc(outputPath);
                    if (sourceWriteUtc > outputWriteUtc)
                        throw new InvalidOperationException("Core source changed after the compiled assembly output: " + sourcePathValue);
                    byte[] bytes = File.ReadAllBytes(sourcePath);
                    string relativeProjectPath = RelativePath(projectRoot, sourcePath);
                    string archivePath = "source/" + relativeProjectPath.Replace('\\', '/');
                    sourceEntries.Add(new SourceFileReceipt { Path = relativeProjectPath.Replace('\\', '/'), Sha256 = Sha256(bytes), Length = bytes.Length });
                    archiveEntries.Add(new ArchiveBytes { Path = archivePath, Bytes = bytes });
                }

                string cachedFingerprintJson = SessionState.GetString(CompilationFingerprintKey(outputPath), string.Empty);
                if (string.IsNullOrWhiteSpace(cachedFingerprintJson))
                    throw new InvalidOperationException("No same-session compilation fingerprint exists for the loaded Core assembly.");
                CompilationFingerprintReceipt cachedFingerprint = JsonUtility.FromJson<CompilationFingerprintReceipt>(cachedFingerprintJson);
                if (cachedFingerprint == null || !string.Equals(cachedFingerprint.AssemblySha256, assemblySha, StringComparison.Ordinal) ||
                    !SameSourceFingerprints(cachedFingerprint.SourceFiles, sourceEntries))
                    throw new InvalidOperationException("Loaded Core assembly/sourceFiles differ from the same-session compilation fingerprint.");

                string asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(loaded.GetName().Name);
                if (string.IsNullOrWhiteSpace(asmdefPath)) throw new InvalidOperationException("Core asmdef path is unavailable from CompilationPipeline.");
                asmdefPath = Path.GetFullPath(Path.IsPathRooted(asmdefPath) ? asmdefPath : Path.Combine(projectRoot, asmdefPath));
                if (!IsWithin(asmdefPath, projectRoot) || !File.Exists(asmdefPath)) throw new InvalidOperationException("Core asmdef source is unavailable in the project.");
                byte[] asmdefBytes = File.ReadAllBytes(asmdefPath);
                archiveEntries.Add(new ArchiveBytes { Path = "build/" + RelativePath(projectRoot, asmdefPath).Replace('\\', '/'), Bytes = asmdefBytes });

                string projectVersionPath = Path.Combine(projectRoot, "ProjectSettings", "ProjectVersion.txt");
                if (!File.Exists(projectVersionPath)) throw new FileNotFoundException("Unity ProjectVersion.txt is missing.", projectVersionPath);
                byte[] projectVersionBytes = File.ReadAllBytes(projectVersionPath);
                string projectVersion = ReadProjectVersion(projectRoot);
                if (!string.Equals(projectVersion, bundle.RuntimeIdentity.UnityVersion, StringComparison.Ordinal))
                    throw new InvalidOperationException("Core runtime UnityVersion differs from ProjectSettings/ProjectVersion.txt.");
                archiveEntries.Add(new ArchiveBytes { Path = "build/ProjectVersion.txt", Bytes = projectVersionBytes });
                archiveEntries.Add(new ArchiveBytes { Path = "build/LastWatch.Core.dll", Bytes = assemblyBytes });

                SourceFingerprintReceipt receipt = new SourceFingerprintReceipt
                {
                    SchemaVersion = 1,
                    CaptureRunId = bundle.CaptureRunId,
                    AssemblyName = loaded.GetName().Name,
                    AssemblyMvid = assemblyMvid,
                    AssemblyOutputRelativePath = RelativePath(projectRoot, outputPath).Replace('\\', '/'),
                    AssemblySha256 = assemblySha,
                    UnityVersion = bundle.RuntimeIdentity.UnityVersion,
                    ApplicationVersion = bundle.RuntimeIdentity.ApplicationVersion,
                    ProjectVersion = projectVersion,
                    SourceFiles = sourceEntries.ToArray()
                };
                byte[] receiptBytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(receipt, true) + "\n");
                archiveEntries.Add(new ArchiveBytes { Path = "source-build-fingerprint.json", Bytes = receiptBytes });
                string sourceDeliveryName = "source-delivery-core-" + bundle.CaptureRunId + ".zip";
                string sourceDeliveryPath = Path.Combine(outputDirectory, sourceDeliveryName);
                WriteZipNew(sourceDeliveryPath, archiveEntries);
                string sourceDeliverySha = Sha256(File.ReadAllBytes(sourceDeliveryPath));
                VerifyArchive(sourceDeliveryPath, archiveEntries);
                string sourceBuildId = "MVID-" + assemblyMvid + "-SHA256-" + assemblySha;
                return new SourceFingerprint
                {
                    SourceBuildId = sourceBuildId,
                    SourceDeliveryFile = sourceDeliveryName,
                    SourceDeliverySha256 = sourceDeliverySha,
                    AssemblySha256 = assemblySha,
                    AssemblyMvid = assemblyMvid,
                    SourceFiles = sourceEntries.ToArray()
                };
            }
            catch (Exception exception)
            {
                reason = exception.GetType().Name + ": " + exception.Message;
                return null;
            }
        }

        static ReplayProvenanceManifest CreateManifest(M4QaCaptureBundle bundle, string difficulty, string tapeName,
            string evidenceName, string transcriptName, string runtimeName, byte[] tapeBytes, byte[] evidenceBytes,
            byte[] transcriptBytes, byte[] runtimeBytes, string projectVersion, SourceFingerprint fingerprint)
        {
            string platformKind = bundle.RuntimeIdentity.IsEditor ? "Editor" :
                bundle.RuntimeIdentity.Platform.IndexOf("WebGL", StringComparison.OrdinalIgnoreCase) >= 0 ? "WebGL" :
                bundle.RuntimeIdentity.Platform.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0 ? "Windows" :
                throw new InvalidOperationException("Runtime platform cannot be mapped to the strict v0.2 schema.");
            return new ReplayProvenanceManifest
            {
                SchemaVersion = ProvenanceSchemaVersion,
                RunId = bundle.CaptureRunId,
                RunSeed = bundle.RunSeed,
                Difficulty = bundle.Difficulty,
                RuntimeExecuted = true,
                OriginalUnityEvidenceExport = true,
                ReplayInputFile = tapeName,
                ReplayInputSha256 = Sha256(tapeBytes),
                FrozenEvidenceFile = evidenceName,
                FrozenEvidenceSha256 = Sha256(evidenceBytes),
                RawUnityEvidenceExport = new LinkedFile { Path = evidenceName, Sha256 = Sha256(evidenceBytes) },
                FinalGameplayHash = bundle.FinalGameplayHash,
                UnityVersion = bundle.RuntimeIdentity.UnityVersion,
                ProjectVersion = projectVersion,
                SourceBuildId = fingerprint == null ? string.Empty : fingerprint.SourceBuildId,
                SourceDeliverySha256 = fingerprint == null ? string.Empty : fingerprint.SourceDeliverySha256,
                SourceDeliveryArtifact = new LinkedFile
                {
                    Path = fingerprint == null ? string.Empty : fingerprint.SourceDeliveryFile,
                    Sha256 = fingerprint == null ? string.Empty : fingerprint.SourceDeliverySha256
                },
                Platform = new ReplayPlatform { Kind = platformKind, Version = bundle.RuntimeIdentity.ApplicationVersion, Device = string.Empty },
                CommandTranscript = new LinkedFile { Path = transcriptName, Sha256 = Sha256(transcriptBytes) },
                RuntimeLog = new LinkedFile { Path = runtimeName, Sha256 = Sha256(runtimeBytes) }
            };
        }

        static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages != null && messages.Any(message => message.type == CompilerMessageType.Error)) return;
            RecordCompilationFingerprint(assemblyPath);
        }

        static void OnAssemblyCompilationNotRequired(string assemblyPath) => RecordCompilationFingerprint(assemblyPath);

        static void RefreshLoadedCoreFingerprint()
        {
            if (EditorApplication.isCompiling) return;
            try { RecordCompilationFingerprint(typeof(M4NightOneGrayboxController).Assembly.Location); }
            catch (Exception exception)
            {
                Debug.LogWarning("M4 loaded Core compilation fingerprint was not refreshed: " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        static void RecordCompilationFingerprint(string assemblyPath)
        {
            try
            {
                string fullOutputPath = Path.GetFullPath(assemblyPath);
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                UnityEditor.Compilation.Assembly compiled = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                    .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.outputPath) && PathsEqual(
                        Path.IsPathRooted(item.outputPath) ? item.outputPath : Path.Combine(projectRoot, item.outputPath), fullOutputPath));
                if (string.IsNullOrWhiteSpace(compiled.outputPath) || compiled.sourceFiles == null || compiled.sourceFiles.Length == 0) return;
                var files = new List<SourceFileReceipt>();
                foreach (string sourcePathValue in compiled.sourceFiles.OrderBy(path => path, StringComparer.Ordinal))
                {
                    string sourcePath = Path.GetFullPath(Path.IsPathRooted(sourcePathValue) ? sourcePathValue : Path.Combine(projectRoot, sourcePathValue));
                    if (!IsWithin(sourcePath, projectRoot) || !File.Exists(sourcePath)) return;
                    byte[] bytes = File.ReadAllBytes(sourcePath);
                    files.Add(new SourceFileReceipt { Path = RelativePath(projectRoot, sourcePath).Replace('\\', '/'), Sha256 = Sha256(bytes), Length = bytes.Length });
                }
                CompilationFingerprintReceipt receipt = new CompilationFingerprintReceipt
                {
                    AssemblyOutputPath = fullOutputPath,
                    AssemblySha256 = Sha256(File.ReadAllBytes(fullOutputPath)),
                    SourceFiles = files.ToArray()
                };
                SessionState.SetString(CompilationFingerprintKey(fullOutputPath), JsonUtility.ToJson(receipt));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("M4 same-session Core compilation fingerprint was not recorded: " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        static bool SameSourceFingerprints(SourceFileReceipt[] compiled, IList<SourceFileReceipt> current)
        {
            if (compiled == null || compiled.Length != current.Count) return false;
            for (int i = 0; i < compiled.Length; i++)
                if (!string.Equals(compiled[i].Path, current[i].Path, StringComparison.Ordinal) ||
                    !string.Equals(compiled[i].Sha256, current[i].Sha256, StringComparison.Ordinal) || compiled[i].Length != current[i].Length)
                    return false;
            return true;
        }

        static string CompilationFingerprintKey(string outputPath) =>
            CompilationFingerprintPrefix + Sha256(new UTF8Encoding(false).GetBytes(Path.GetFullPath(outputPath)));

        static string TryCreatePairedInput(string repoRoot, string currentDirectory, M4QaCaptureBundle currentBundle,
            ReplayProvenanceManifest currentManifest, SourceFingerprint currentFingerprint)
        {
            string evidenceRoot = Path.Combine(repoRoot, "tests", "LastWatch", "evidence", "M4");
            string currentDifficulty = currentBundle.Difficulty == 0 ? "normal" : "hard";
            CaptureStatus currentStatus = ReadCaptureStatus(currentDirectory);
            if (currentStatus == null || !ValidateCapturePlanDependency(currentDirectory, currentStatus))
                return "current deterministic input-plan dependency is missing or invalid; no pair was published";
            CaptureStatus match = null;
            string matchDirectory = null;
            DateTime newest = DateTime.MinValue;
            foreach (string directory in Directory.GetDirectories(evidenceRoot))
            {
                if (PathsEqual(directory, currentDirectory) || !IsNGuid(Path.GetFileName(directory))) continue;
                string statusPath = Path.Combine(directory, "capture-status.json");
                if (!File.Exists(statusPath)) continue;
                CaptureStatus candidate;
                try { candidate = JsonUtility.FromJson<CaptureStatus>(File.ReadAllText(statusPath, Encoding.UTF8)); }
                catch { continue; }
                if (candidate == null || candidate.Difficulty == currentDifficulty ||
                    !string.Equals(candidate.Classification, "CAPTURE_SOURCE_FINGERPRINTED", StringComparison.Ordinal) ||
                    !string.Equals(candidate.SourceBuildId, currentFingerprint.SourceBuildId, StringComparison.Ordinal) ||
                    !string.Equals(candidate.InputKind, M4QaCaptureInputKind.DeterministicInputPlan.ToString(), StringComparison.Ordinal) ||
                    !ValidateCapturePlanDependency(directory, candidate)) continue;
                DateTime modified = File.GetLastWriteTimeUtc(statusPath);
                if (modified > newest) { newest = modified; match = candidate; matchDirectory = directory; }
            }
            if (match == null || string.IsNullOrEmpty(matchDirectory)) return "waiting for a same-source capture at the other difficulty";

            string normalId = currentDifficulty == "normal" ? currentBundle.CaptureRunId : match.CaptureRunId;
            string hardId = currentDifficulty == "hard" ? currentBundle.CaptureRunId : match.CaptureRunId;
            string pairName = normalId + "_" + hardId;
            string pairRoot = Path.Combine(evidenceRoot, "automated-capture-pairs");
            string pairDirectory = Path.Combine(pairRoot, pairName);

            string otherDifficulty = currentDifficulty == "normal" ? "hard" : "normal";
            string otherManifestPath = Path.Combine(matchDirectory, otherDifficulty + "-provenance-v0.2.json");
            ReplayProvenanceManifest otherManifest;
            try { otherManifest = JsonUtility.FromJson<ReplayProvenanceManifest>(File.ReadAllText(otherManifestPath, Encoding.UTF8)); }
            catch { return "opposite capture manifest is unreadable; no pair was published"; }
            if (otherManifest == null || !string.Equals(otherManifest.SourceBuildId, currentFingerprint.SourceBuildId, StringComparison.Ordinal))
                return "opposite capture source build does not match; no pair was published";
            if (!string.Equals(match.InputPlanLineageFile, currentStatus.InputPlanLineageFile, StringComparison.Ordinal) ||
                !string.Equals(match.InputPlanLineageSha256, currentStatus.InputPlanLineageSha256, StringComparison.Ordinal))
                return "Normal/Hard input-plan lineage manifest differs; no pair was published";
            if (!string.Equals(otherManifest.UnityVersion, currentManifest.UnityVersion, StringComparison.Ordinal) ||
                !string.Equals(otherManifest.ProjectVersion, currentManifest.ProjectVersion, StringComparison.Ordinal))
                return "opposite capture Unity/project version differs; no pair was published";
            if (!ValidateManifestLinks(currentDirectory, currentManifest) || !ValidateManifestLinks(matchDirectory, otherManifest))
                return "one capture has a linked-file/hash mismatch; no pair was published";

            ReplayProvenanceManifest normalManifest = currentDifficulty == "normal" ? currentManifest : otherManifest;
            ReplayProvenanceManifest hardManifest = currentDifficulty == "hard" ? currentManifest : otherManifest;
            if (Directory.Exists(pairDirectory) && HasCompletePublishedPair(pairDirectory, normalId, hardId,
                currentFingerprint.SourceBuildId, normalManifest, hardManifest, currentStatus, match))
                return pairDirectory + " (existing complete pair preserved)";

            string stagingDirectory = Path.Combine(pairRoot, ".staging-" + Guid.NewGuid().ToString("N"));
            string publishedDirectory = Directory.Exists(pairDirectory)
                ? Path.Combine(pairRoot, pairName + "_retry_" + Guid.NewGuid().ToString("N"))
                : pairDirectory;

            try
            {
                Directory.CreateDirectory(pairRoot);
                Directory.CreateDirectory(stagingDirectory);
                CopyLinkedCapture(currentDirectory, stagingDirectory, currentDifficulty, currentManifest);
                CopyLinkedCapture(matchDirectory, stagingDirectory, otherDifficulty, otherManifest);
                CopyRequired(currentDirectory, stagingDirectory, currentStatus.InputPlanLineageFile);
                if (currentStatus.InputPlanId.StartsWith("m4-adaptive-", StringComparison.Ordinal))
                    CopyRequired(currentDirectory, stagingDirectory, "m4-adaptive-policy-source-v0.1.cs");
                string expectedLineageHash = currentStatus.InputPlanLineageSha256;
                if (!string.Equals(Sha256(File.ReadAllBytes(Path.Combine(stagingDirectory, currentStatus.InputPlanLineageFile))), expectedLineageHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Staged input-plan lineage manifest hash mismatch.");
                if (!ValidateManifestLinks(stagingDirectory, normalManifest) || !ValidateManifestLinks(stagingDirectory, hardManifest))
                    throw new InvalidDataException("Staged pair failed linked-file/hash verification.");
                if (!ValidatePairedPlanDependency(stagingDirectory, "normal", normalId,
                    currentDifficulty == "normal" ? currentStatus : match) ||
                    !ValidatePairedPlanDependency(stagingDirectory, "hard", hardId,
                    currentDifficulty == "hard" ? currentStatus : match))
                    throw new InvalidDataException("Staged pair failed deterministic input-plan provenance verification.");
                PairedInputStatus pairStatus = new PairedInputStatus
                {
                    Classification = "SOURCE_BACKED_PAIR_READY_FOR_PREFLIGHT",
                    NormalCaptureRunId = normalId,
                    HardCaptureRunId = hardId,
                    SourceBuildId = currentFingerprint.SourceBuildId,
                    SourceDeliverySha256Normal = currentDifficulty == "normal" ? currentFingerprint.SourceDeliverySha256 : otherManifest.SourceDeliverySha256,
                    SourceDeliverySha256Hard = currentDifficulty == "hard" ? currentFingerprint.SourceDeliverySha256 : otherManifest.SourceDeliverySha256,
                    NormalInputPlanId = currentDifficulty == "normal" ? currentStatus.InputPlanId : match.InputPlanId,
                    NormalInputPlanSha256 = currentDifficulty == "normal" ? currentStatus.InputPlanSha256 : match.InputPlanSha256,
                    NormalInputPlanReceiptSha256 = currentDifficulty == "normal" ? currentStatus.InputPlanReceiptSha256 : match.InputPlanReceiptSha256,
                    HardInputPlanId = currentDifficulty == "hard" ? currentStatus.InputPlanId : match.InputPlanId,
                    HardInputPlanSha256 = currentDifficulty == "hard" ? currentStatus.InputPlanSha256 : match.InputPlanSha256,
                    HardInputPlanReceiptSha256 = currentDifficulty == "hard" ? currentStatus.InputPlanReceiptSha256 : match.InputPlanReceiptSha256,
                    InputPlanLineageSha256 = currentStatus.InputPlanLineageSha256,
                    ReplayPreflightReady = false,
                    Note = "Pair assembled from explicit same-source Normal and Hard Core capture bundles. Run M4CandidateReplayRunner preflight before Unity."
                };
                WriteNewText(Path.Combine(stagingDirectory, "capture-pair-status.json"), JsonUtility.ToJson(pairStatus, true) + "\n");
                Directory.Move(stagingDirectory, publishedDirectory);
                return publishedDirectory;
            }
            catch (Exception exception)
            {
                if (Directory.Exists(stagingDirectory))
                {
                    string statusPath = Path.Combine(stagingDirectory, "capture-pair-status.json");
                    if (!File.Exists(statusPath))
                        WriteNewText(statusPath, JsonUtility.ToJson(new PairedInputStatus
                        {
                            Classification = "INCOMPLETE_PAIR_COPY",
                            NormalCaptureRunId = normalId,
                            HardCaptureRunId = hardId,
                            SourceBuildId = currentFingerprint.SourceBuildId,
                            ReplayPreflightReady = false,
                            Note = exception.GetType().Name + ": " + exception.Message
                        }, true) + "\n");
                    string incompleteDirectory = Path.Combine(pairRoot, pairName + "_incomplete_" + Guid.NewGuid().ToString("N"));
                    try { Directory.Move(stagingDirectory, incompleteDirectory); }
                    catch { /* Preserve the unique staging directory; the runner ignores non-ready entries. */ }
                }
                return "pair copy incomplete; replay remains blocked and a later export can retry: " + exception.Message;
            }
        }

        static bool HasCompletePublishedPair(string directory, string normalId, string hardId, string sourceBuildId,
            ReplayProvenanceManifest normalManifest, ReplayProvenanceManifest hardManifest,
            CaptureStatus currentStatus, CaptureStatus otherStatus)
        {
            try
            {
                string statusPath = Path.Combine(directory, "capture-pair-status.json");
                if (!File.Exists(statusPath)) return false;
                PairedInputStatus status = JsonUtility.FromJson<PairedInputStatus>(File.ReadAllText(statusPath, Encoding.UTF8));
                CaptureStatus normalStatus = string.Equals(currentStatus.Difficulty, "normal", StringComparison.Ordinal) ? currentStatus : otherStatus;
                CaptureStatus hardStatus = string.Equals(currentStatus.Difficulty, "hard", StringComparison.Ordinal) ? currentStatus : otherStatus;
                return status != null && string.Equals(status.Classification, "SOURCE_BACKED_PAIR_READY_FOR_PREFLIGHT", StringComparison.Ordinal) &&
                    string.Equals(status.NormalCaptureRunId, normalId, StringComparison.Ordinal) &&
                    string.Equals(status.HardCaptureRunId, hardId, StringComparison.Ordinal) &&
                    string.Equals(status.SourceBuildId, sourceBuildId, StringComparison.Ordinal) &&
                    string.Equals(status.NormalInputPlanId, normalStatus.InputPlanId, StringComparison.Ordinal) &&
                    string.Equals(status.NormalInputPlanSha256, normalStatus.InputPlanSha256, StringComparison.Ordinal) &&
                    string.Equals(status.NormalInputPlanReceiptSha256, normalStatus.InputPlanReceiptSha256, StringComparison.Ordinal) &&
                    string.Equals(status.HardInputPlanId, hardStatus.InputPlanId, StringComparison.Ordinal) &&
                    string.Equals(status.HardInputPlanSha256, hardStatus.InputPlanSha256, StringComparison.Ordinal) &&
                    string.Equals(status.HardInputPlanReceiptSha256, hardStatus.InputPlanReceiptSha256, StringComparison.Ordinal) &&
                    string.Equals(status.InputPlanLineageSha256, normalStatus.InputPlanLineageSha256, StringComparison.Ordinal) &&
                    string.Equals(status.InputPlanLineageSha256, hardStatus.InputPlanLineageSha256, StringComparison.Ordinal) &&
                    ValidateManifestLinks(directory, normalManifest) && ValidateManifestLinks(directory, hardManifest) &&
                    ValidatePairedPlanDependency(directory, "normal", normalId, normalStatus) &&
                    ValidatePairedPlanDependency(directory, "hard", hardId, hardStatus);
            }
            catch { return false; }
        }

        static void CopyLinkedCapture(string sourceDirectory, string pairDirectory, string difficulty, ReplayProvenanceManifest manifest)
        {
            string manifestName = difficulty + "-provenance-v0.2.json";
            CopyRequired(sourceDirectory, pairDirectory, manifest.ReplayInputFile);
            CopyRequired(sourceDirectory, pairDirectory, manifest.FrozenEvidenceFile);
            CopyRequired(sourceDirectory, pairDirectory, manifest.SourceDeliveryArtifact == null ? null : manifest.SourceDeliveryArtifact.Path);
            CopyRequired(sourceDirectory, pairDirectory, manifest.CommandTranscript == null ? null : manifest.CommandTranscript.Path);
            CopyRequired(sourceDirectory, pairDirectory, manifest.RuntimeLog == null ? null : manifest.RuntimeLog.Path);
            CopyRequired(sourceDirectory, pairDirectory, manifestName);
            CaptureStatus status = ReadCaptureStatus(sourceDirectory);
            if (status == null || !ValidateCapturePlanDependency(sourceDirectory, status))
                throw new InvalidDataException("Capture input-plan provenance is invalid before pair copy.");
            CopyRequired(sourceDirectory, pairDirectory, status.InputPlanFile);
            CopyRequired(sourceDirectory, pairDirectory, status.InputPlanReceiptFile);
            if (status.InputPlanId.StartsWith("m4-adaptive-", StringComparison.Ordinal))
            {
                CopyRequired(sourceDirectory, pairDirectory, difficulty + "-adaptive-decisions-v0.1.json");
            }
        }

        static CaptureStatus ReadCaptureStatus(string directory)
        {
            string path = Path.Combine(directory, "capture-status.json");
            try { return File.Exists(path) ? JsonUtility.FromJson<CaptureStatus>(File.ReadAllText(path, Encoding.UTF8)) : null; }
            catch { return null; }
        }

        static bool ValidateCapturePlanDependency(string directory, CaptureStatus status)
        {
            return status != null && string.Equals(status.InputKind, M4QaCaptureInputKind.DeterministicInputPlan.ToString(), StringComparison.Ordinal) &&
                ValidatePlanDependencyFiles(directory, status.InputPlanFile, status.InputPlanSha256,
                    status.InputPlanReceiptFile, status.InputPlanReceiptSha256, status.InputPlanLineageFile,
                    status.InputPlanLineageSha256, status.CaptureRunId, status.InputPlanId, status.RunSeed,
                    status.Difficulty == "normal" ? 0 : status.Difficulty == "hard" ? 1 : -1);
        }

        static bool ValidatePairedPlanDependency(string directory, string difficulty, string captureRunId, CaptureStatus status)
        {
            return status != null && string.Equals(status.Difficulty, difficulty, StringComparison.Ordinal) &&
                string.Equals(status.CaptureRunId, captureRunId, StringComparison.Ordinal) &&
                ValidatePlanDependencyFiles(directory, difficulty + "-input-plan.json", status.InputPlanSha256,
                    difficulty + "-input-plan-provenance-v0.1.json", status.InputPlanReceiptSha256,
                    status.InputPlanLineageFile, status.InputPlanLineageSha256, captureRunId,
                    status.InputPlanId, status.RunSeed, difficulty == "normal" ? 0 : 1);
        }

        static bool ValidatePlanDependencyFiles(string directory, string planFile, string planSha,
            string receiptFile, string receiptSha, string lineageFile, string lineageSha,
            string captureRunId, string planId, ulong runSeed, int difficulty)
        {
            try
            {
                if (!MatchesFile(directory, planFile, planSha) || !MatchesFile(directory, receiptFile, receiptSha) ||
                    !MatchesFile(directory, lineageFile, lineageSha) || difficulty < 0) return false;
                string planJson = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(Path.Combine(directory, planFile)));
                if (!M4QaInputPlan.TryParse(planJson, out M4QaInputPlan plan, out _) || plan == null ||
                    !string.Equals(plan.planId, planId, StringComparison.Ordinal) || plan.runSeed != runSeed || plan.difficulty != difficulty)
                    return false;
                InputPlanProvenanceReceipt receipt = JsonUtility.FromJson<InputPlanProvenanceReceipt>(File.ReadAllText(Path.Combine(directory, receiptFile), Encoding.UTF8));
                if (receipt == null || receipt.SchemaVersion != 1 ||
                    !string.Equals(receipt.CaptureRunId, captureRunId, StringComparison.Ordinal) ||
                    !string.Equals(receipt.InputKind, M4QaCaptureInputKind.DeterministicInputPlan.ToString(), StringComparison.Ordinal) ||
                    !string.Equals(receipt.InputPlanId, planId, StringComparison.Ordinal) ||
                    !string.Equals(receipt.PlanFile, planFile, StringComparison.Ordinal) ||
                    !string.Equals(receipt.PlanSha256, planSha, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(receipt.SourcePlanSha256, planSha, StringComparison.OrdinalIgnoreCase) ||
                    receipt.RunSeed != runSeed || receipt.Difficulty != difficulty || receipt.CommandCount != plan.ticks.Length ||
                    !string.Equals(receipt.LineageFile, lineageFile, StringComparison.Ordinal) ||
                    !string.Equals(receipt.LineageSha256, lineageSha, StringComparison.OrdinalIgnoreCase)) return false;
                if (planId.StartsWith("m4-adaptive-", StringComparison.Ordinal))
                {
                    string decisionFile = (difficulty == 0 ? "normal" : "hard") + "-adaptive-decisions-v0.1.json";
                    if (receipt.SourceUse != "ADAPTIVE_COMMAND_STIMULUS_ONLY_NOT_EVIDENCE" ||
                        receipt.DecisionFile != decisionFile || receipt.SourceTapePath != decisionFile ||
                        !receipt.SourcePlanPath.StartsWith("tests/LastWatch/evidence/m4-adaptive-stimulus/", StringComparison.Ordinal) ||
                        !MatchesFile(directory, decisionFile, receipt.DecisionSha256) ||
                        !string.Equals(receipt.DecisionSha256, receipt.SourceTapeSha256, StringComparison.OrdinalIgnoreCase)) return false;
                    AdaptivePolicyLineage adaptiveLineage = JsonUtility.FromJson<AdaptivePolicyLineage>(File.ReadAllText(Path.Combine(directory, lineageFile), Encoding.UTF8));
                    AdaptiveDecisionLog log = JsonUtility.FromJson<AdaptiveDecisionLog>(File.ReadAllText(Path.Combine(directory, decisionFile), Encoding.UTF8));
                    if (adaptiveLineage == null || adaptiveLineage.schemaVersion != 1 ||
                        adaptiveLineage.policyId != "m4-adaptive-visible-actor-v0.1" || log == null ||
                        log.schemaVersion != 1 || log.policyId != adaptiveLineage.policyId ||
                        log.stimulusId != planId || log.difficulty != difficulty ||
                        log.decisions == null || log.decisions.Length != plan.ticks.Length ||
                        !string.Equals(difficulty == 0 ? adaptiveLineage.normalBaselineSha256 : adaptiveLineage.hardBaselineSha256,
                            log.baselinePlanSha256, StringComparison.OrdinalIgnoreCase)) return false;
                    if (!MatchesFile(directory, "m4-adaptive-policy-source-v0.1.cs", adaptiveLineage.policySourceSha256)) return false;
                    for (int i = 0; i < plan.ticks.Length; i++)
                        if (log.decisions[i] == null || log.decisions[i].runFixedTick != i ||
                            log.decisions[i].nightOrdinal != plan.ticks[i].nightOrdinal ||
                            !string.Equals(JsonUtility.ToJson(log.decisions[i].command), JsonUtility.ToJson(plan.ticks[i]), StringComparison.Ordinal)) return false;
                    return true;
                }
                if (!string.Equals(receipt.SourcePlanPath, "tests/LastWatch/input-plans/" + (difficulty == 0 ? "normal" : "hard") + "-m4-input-plan-v0.1.json", StringComparison.Ordinal) ||
                    !string.Equals(receipt.SourceUse, "COMMAND_STIMULUS_ONLY_NOT_EVIDENCE", StringComparison.Ordinal)) return false;
                InputPlanDependencyManifest lineage = JsonUtility.FromJson<InputPlanDependencyManifest>(File.ReadAllText(Path.Combine(directory, lineageFile), Encoding.UTF8));
                InputPlanLineageEntry entry = lineage == null || lineage.dependencies == null ? null :
                    lineage.dependencies.FirstOrDefault(value => value != null && string.Equals(value.planId, planId, StringComparison.Ordinal));
                return lineage != null && lineage.schemaVersion == 1 && entry != null &&
                    string.Equals(entry.planSha256, planSha, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.sourceTapeSha256, receipt.SourceTapeSha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.sourceTapePath, receipt.SourceTapePath, StringComparison.Ordinal) &&
                    string.Equals(entry.sourceUse, receipt.SourceUse, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        static void CopyRequired(string sourceDirectory, string destinationDirectory, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || !string.Equals(Path.GetFileName(relativePath), relativePath, StringComparison.Ordinal))
                throw new InvalidDataException("Capture manifest contains an unsafe or empty linked path.");
            string sourcePath = Path.Combine(sourceDirectory, relativePath);
            string destinationPath = Path.Combine(destinationDirectory, relativePath);
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Capture pair source file is missing.", sourcePath);
            if (File.Exists(destinationPath)) throw new IOException("Capture pair destination already exists: " + relativePath);
            File.Copy(sourcePath, destinationPath, false);
            if (!string.Equals(Sha256(File.ReadAllBytes(sourcePath)), Sha256(File.ReadAllBytes(destinationPath)), StringComparison.Ordinal))
                throw new InvalidDataException("Capture pair copy hash mismatch: " + relativePath);
        }

        static bool ValidateManifestLinks(string directory, ReplayProvenanceManifest manifest)
        {
            return MatchesFile(directory, manifest.ReplayInputFile, manifest.ReplayInputSha256) &&
                MatchesFile(directory, manifest.FrozenEvidenceFile, manifest.FrozenEvidenceSha256) &&
                manifest.RawUnityEvidenceExport != null &&
                MatchesFile(directory, manifest.RawUnityEvidenceExport.Path, manifest.RawUnityEvidenceExport.Sha256) &&
                manifest.SourceDeliveryArtifact != null &&
                MatchesFile(directory, manifest.SourceDeliveryArtifact.Path, manifest.SourceDeliveryArtifact.Sha256) &&
                manifest.CommandTranscript != null &&
                MatchesFile(directory, manifest.CommandTranscript.Path, manifest.CommandTranscript.Sha256) &&
                manifest.RuntimeLog != null && MatchesFile(directory, manifest.RuntimeLog.Path, manifest.RuntimeLog.Sha256);
        }

        static bool MatchesFile(string directory, string relativePath, string expectedSha)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || !string.Equals(Path.GetFileName(relativePath), relativePath, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(expectedSha)) return false;
            string path = Path.Combine(directory, relativePath);
            return File.Exists(path) && string.Equals(Sha256(File.ReadAllBytes(path)), expectedSha, StringComparison.OrdinalIgnoreCase);
        }

        static string FindRepositoryRoot()
        {
            string candidate = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            for (int depth = 0; depth < 5 && !string.IsNullOrEmpty(candidate); depth++)
            {
                if (File.Exists(Path.Combine(candidate, "tests", "LastWatch", "M4_REPLAY_INPUT_PROVENANCE_SCHEMA_v0.2.json"))) return candidate;
                DirectoryInfo parent = Directory.GetParent(candidate);
                candidate = parent == null ? string.Empty : parent.FullName;
            }
            return string.Empty;
        }

        static string ReadProjectVersion(string projectRoot)
        {
            string path = Path.Combine(projectRoot, "ProjectSettings", "ProjectVersion.txt");
            if (!File.Exists(path)) throw new FileNotFoundException("Unity project version file is missing.", path);
            string line = File.ReadAllLines(path).FirstOrDefault(value => value.StartsWith("m_EditorVersion:", StringComparison.Ordinal));
            return line == null ? string.Empty : line.Substring("m_EditorVersion:".Length).Trim();
        }

        static void WriteFailureRecord(string outputDirectory, string runId, string classification, string reason)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory)) return;
            Directory.CreateDirectory(outputDirectory);
            CaptureStatus status = new CaptureStatus
            {
                Classification = classification,
                CaptureRunId = runId ?? string.Empty,
                SourceFailure = reason ?? string.Empty,
                ReplayPreflightReady = false,
                Note = "No valid v0.2 provenance is asserted. Unity replay must remain blocked."
            };
            string path = Path.Combine(outputDirectory, "capture-status.json");
            if (!File.Exists(path)) WriteNewText(path, JsonUtility.ToJson(status, true) + "\n");
        }

        static void WriteNewBytes(string path, byte[] bytes)
        {
            if (File.Exists(path)) throw new IOException("Refusing to overwrite an existing capture artifact: " + Path.GetFileName(path));
            File.WriteAllBytes(path, bytes);
        }

        static void WriteNewText(string path, string text) => WriteNewBytes(path, new UTF8Encoding(false).GetBytes(text));

        static void WriteZipNew(string path, IList<ArchiveBytes> entries)
        {
            if (File.Exists(path)) throw new IOException("Refusing to overwrite an existing Source Delivery artifact.");
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                foreach (ArchiveBytes item in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
                {
                    ZipArchiveEntry entry = archive.CreateEntry(item.Path, System.IO.Compression.CompressionLevel.Optimal);
                    using (Stream output = entry.Open()) output.Write(item.Bytes, 0, item.Bytes.Length);
                }
            }
        }

        static void VerifyArchive(string path, IList<ArchiveBytes> expected)
        {
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read, false, Encoding.UTF8))
            {
                foreach (ArchiveBytes item in expected)
                {
                    ZipArchiveEntry entry = archive.GetEntry(item.Path);
                    if (entry == null) throw new InvalidDataException("Source Delivery ZIP is missing " + item.Path);
                    using (Stream input = entry.Open())
                    using (MemoryStream output = new MemoryStream())
                    {
                        input.CopyTo(output);
                        if (!BytesEqual(output.ToArray(), item.Bytes)) throw new InvalidDataException("Source Delivery ZIP entry bytes differ: " + item.Path);
                    }
                }
                if (archive.Entries.Count != expected.Count) throw new InvalidDataException("Source Delivery ZIP contains an unexpected entry count.");
            }
        }

        static bool IsNGuid(string value) => Guid.TryParseExact(value, "N", out _);
        static bool Fail(string message, out string reason) { reason = message; return false; }
        static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        static string RelativePath(string root, string path) => Uri.UnescapeDataString(new Uri(AppendSeparator(Path.GetFullPath(root))).MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString());
        static string AppendSeparator(string value) => value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? value : value + Path.DirectorySeparatorChar;
        static bool IsWithin(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = AppendSeparator(Path.GetFullPath(root));
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
        }
        static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        [Serializable] sealed class ReplayProvenanceManifest
        {
            public string SchemaVersion; public string RunId; public ulong RunSeed; public int Difficulty;
            public bool RuntimeExecuted; public bool OriginalUnityEvidenceExport; public string ReplayInputFile;
            public string ReplayInputSha256; public string FrozenEvidenceFile; public string FrozenEvidenceSha256;
            public LinkedFile RawUnityEvidenceExport; public string FinalGameplayHash; public string UnityVersion;
            public string ProjectVersion; public string SourceBuildId; public string SourceDeliverySha256;
            public LinkedFile SourceDeliveryArtifact; public ReplayPlatform Platform; public LinkedFile CommandTranscript; public LinkedFile RuntimeLog;
        }
        [Serializable] sealed class LinkedFile { public string Path; public string Sha256; }
        [Serializable] sealed class ReplayPlatform { public string Kind; public string Version; public string Device; }
        [Serializable] sealed class CaptureStatus
        {
            public string Classification; public string CaptureRunId; public string Difficulty; public ulong RunSeed;
            public string InputKind; public string InputPlanId; public string InputPlanFile; public string InputPlanSha256;
            public string InputPlanReceiptFile; public string InputPlanReceiptSha256; public string InputPlanLineageFile; public string InputPlanLineageSha256;
            public int BundleSchemaVersion; public int FrozenEvidenceSchemaVersion; public int ReplayTapeSchemaVersion;
            public int ReplayTickCount; public string TapeSha256; public string FrozenEvidenceSha256;
            public string SourceBuildId; public string SourceDeliverySha256; public string SourceFailure;
            public bool ReplayPreflightReady; public string Note;
        }
        [Serializable] sealed class PairedInputStatus
        {
            public string Classification; public string NormalCaptureRunId; public string HardCaptureRunId;
            public string SourceBuildId; public string SourceDeliverySha256Normal; public string SourceDeliverySha256Hard;
            public string NormalInputPlanId; public string NormalInputPlanSha256; public string HardInputPlanId; public string HardInputPlanSha256;
            public string NormalInputPlanReceiptSha256; public string HardInputPlanReceiptSha256;
            public string InputPlanLineageSha256;
            public bool ReplayPreflightReady; public string Note;
        }
        [Serializable] sealed class InputPlanDependencyManifest
        {
            public int schemaVersion; public string purpose; public string evidencePolicy;
            public bool commandsPreservedVerbatim; public bool frozenMarkerRemovedForPlanType;
            public InputPlanLineageEntry[] dependencies;
        }
        [Serializable] sealed class InputPlanLineageEntry
        {
            public string difficulty; public string planId; public string planPath; public string planSha256; public int commandCount;
            public string sourceTapePath; public string sourceTapeSha256; public string sourceUse; public string sourceCaptureProvenance;
        }
        [Serializable] sealed class InputPlanProvenanceReceipt
        {
            public int SchemaVersion; public string CaptureRunId; public string InputKind; public string InputPlanId;
            public ulong RunSeed; public int Difficulty; public int CommandCount; public string PlanFile; public string PlanSha256;
            public string SourcePlanPath; public string SourcePlanSha256; public string LineageFile; public string LineageSha256;
            public string SourceTapePath; public string SourceTapeSha256; public string SourceUse; public string SourceCaptureProvenance;
            public string DecisionFile; public string DecisionSha256;
        }
        [Serializable] sealed class AdaptivePolicyLineage
        {
            public int schemaVersion; public string policyId;
            public string policySourceSha256;
            public string normalBaselineSha256; public string hardBaselineSha256;
        }
        [Serializable] sealed class AdaptiveDecisionLog
        {
            public int schemaVersion; public string policyId; public string stimulusId;
            public string baselinePlanSha256; public int difficulty; public AdaptiveDecision[] decisions;
        }
        [Serializable] sealed class AdaptiveDecision
        {
            public int runFixedTick; public int nightOrdinal; public M4ReplayTick command;
        }
        [Serializable] sealed class CaptureDriverReceipt
        {
            public int SchemaVersion; public string Classification; public string CaptureRunId; public int Difficulty;
            public string InputKind; public string InputPlanId; public string InputPlanSha256; public string SourceBuildId;
            public string SourceDeliverySha256; public string CaptureDirectory; public string Message;
        }
        [Serializable] sealed class SourceFingerprintReceipt
        {
            public int SchemaVersion; public string CaptureRunId; public string AssemblyName; public string AssemblyMvid;
            public string AssemblyOutputRelativePath; public string AssemblySha256; public string UnityVersion;
            public string ApplicationVersion; public string ProjectVersion; public SourceFileReceipt[] SourceFiles;
        }
        [Serializable] sealed class SourceFileReceipt { public string Path; public string Sha256; public long Length; }
        [Serializable] sealed class CompilationFingerprintReceipt { public string AssemblyOutputPath; public string AssemblySha256; public SourceFileReceipt[] SourceFiles; }
        sealed class SourceFingerprint
        {
            public string SourceBuildId; public string SourceDeliveryFile; public string SourceDeliverySha256;
            public string AssemblySha256; public string AssemblyMvid; public SourceFileReceipt[] SourceFiles;
        }
        sealed class ArchiveBytes { public string Path; public byte[] Bytes; }
        sealed class InputPlanDependency
        {
            public string PlanFile; public byte[] PlanBytes; public string PlanSha256; public string ReceiptFile;
            public string ReceiptJson; public string ReceiptSha256; public string LineageFile; public byte[] LineageBytes; public string LineageSha256;
            public string DecisionFile; public byte[] DecisionBytes; public string DecisionSha256;
            public string PolicySourceFile; public byte[] PolicySourceBytes;
        }
        sealed class ExportResult
        {
            public bool ManifestComplete; public string Message; public string SourceBuildId; public string SourceDeliverySha256; public string InputPlanSha256;
            public ExportResult(bool complete, string message, string sourceBuildId = "", string sourceDeliverySha256 = "", string inputPlanSha256 = "")
            {
                ManifestComplete = complete; Message = message; SourceBuildId = sourceBuildId;
                SourceDeliverySha256 = sourceDeliverySha256; InputPlanSha256 = inputPlanSha256;
            }
        }
    }
}
