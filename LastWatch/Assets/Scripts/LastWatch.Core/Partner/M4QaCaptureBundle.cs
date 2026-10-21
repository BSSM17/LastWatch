using System;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    public enum M4QaCaptureInputKind
    {
        OwnerLiveInput = 0,
        DeterministicInputPlan = 1
    }

#if UNITY_EDITOR
    public enum M4QaInputPlanCaptureStatus
    {
        NotStarted,
        Running,
        Completed,
        Failed
    }
#endif

    // A payload reference binds Core's unchanged JSON output to the capture run that produced it.
    [Serializable]
    public sealed class M4QaCapturePayload
    {
        public string CaptureRunId;
        public string RawJson;
    }

    [Serializable]
    public sealed class M4QaCaptureRuntimeIdentity
    {
        public string UnityVersion;
        public bool UnityVersionAvailable;
        public string ApplicationVersion;
        public bool ApplicationVersionAvailable;
        public string UnityBuildGuid;
        public bool UnityBuildGuidAvailable;
        public string Platform;
        public bool IsEditor;
        public bool SourceBuildIdentityAvailable;
        public string SourceBuildIdentity;
        public string SourceBuildIdentityUnavailableReason;

        internal static M4QaCaptureRuntimeIdentity CaptureFromRuntime()
        {
            string unityVersion = Application.unityVersion;
            string applicationVersion = Application.version;
            string buildGuid = Application.buildGUID;
            return new M4QaCaptureRuntimeIdentity
            {
                UnityVersion = unityVersion,
                UnityVersionAvailable = !string.IsNullOrWhiteSpace(unityVersion),
                ApplicationVersion = applicationVersion,
                ApplicationVersionAvailable = !string.IsNullOrWhiteSpace(applicationVersion),
                UnityBuildGuid = buildGuid,
                UnityBuildGuidAvailable = !string.IsNullOrWhiteSpace(buildGuid),
                Platform = Application.platform.ToString(),
                IsEditor = Application.isEditor,
                SourceBuildIdentityAvailable = false,
                SourceBuildIdentity = string.Empty,
                SourceBuildIdentityUnavailableReason = "Core runtime does not expose a verifiable source revision or SourceDelivery artifact identity."
            };
        }

        internal M4QaCaptureRuntimeIdentity Copy() => (M4QaCaptureRuntimeIdentity)MemberwiseClone();
    }

    [Serializable]
    public sealed class M4QaCaptureBundle
    {
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion = CurrentSchemaVersion;
        public string CaptureRunId;
        public ulong RunSeed;
        public int Difficulty;
        public bool Frozen;
        public int CompletedNights;
        public string FinalGameplayHash;
        public int ReplayTickCount;
        public M4QaCaptureInputKind InputKind;
        // Populated only for deterministic-plan captures. Core does not hash the plan;
        // DEV-TOOLS preserves and fingerprints its original JSON as a separate dependency.
        public string InputPlanId;
        public M4QaCapturePayload FrozenEvidence;
        public M4QaCapturePayload ReplayTape;
        public M4QaCaptureRuntimeIdentity RuntimeIdentity;
    }
}
