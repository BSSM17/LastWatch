using System;

namespace LastWatch.Core.Configuration
{
    public sealed class RunContextDefinition
    {
        public const string UnavailableToken = "unavailable.pre_milestone";
        public const string UnavailableRulesetHash =
            "0000000000000000000000000000000000000000000000000000000000000000";

        public string RulesetHash { get; }
        public string ContentVersion { get; }
        public string ModeToken { get; }
        public string DifficultyToken { get; }
        public int? Night { get; }
        public bool IsPreMilestoneUnavailable => RulesetHash == UnavailableRulesetHash;

        public static RunContextDefinition PreMilestoneUnavailable { get; } =
            new RunContextDefinition(UnavailableRulesetHash, UnavailableToken, UnavailableToken, UnavailableToken, null);

        private RunContextDefinition(string rulesetHash, string contentVersion, string modeToken,
            string difficultyToken, int? night, bool boundedActual = false)
        {
            RulesetHash = RequireSha256(rulesetHash);
            ContentVersion = RequireToken(contentVersion, nameof(contentVersion));
            ModeToken = RequireToken(modeToken, nameof(modeToken));
            DifficultyToken = RequireToken(difficultyToken, nameof(difficultyToken));
            if (!boundedActual && (rulesetHash != UnavailableRulesetHash || contentVersion != UnavailableToken ||
                modeToken != UnavailableToken || difficultyToken != UnavailableToken || night.HasValue))
                throw new ArgumentException("Only the approved pre-milestone context is available in this milestone.");
            Night = night;
        }

        // Only the versioned Core manifest factory may create actual run metadata.
        // Night belongs to the immutable event-time entry context, not this run-wide value.
        internal static RunContextDefinition ForM4(State.M4RunMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            return new RunContextDefinition(metadata.RulesetHash, metadata.ContentVersion,
                "Story", metadata.Difficulty.ToString(), null, true);
        }

        internal static RunContextDefinition Restore(string rulesetHash, string contentVersion,
            string modeToken, string difficultyToken, int? night)
        {
            if (rulesetHash == UnavailableRulesetHash && contentVersion == UnavailableToken &&
                modeToken == UnavailableToken && difficultyToken == UnavailableToken && !night.HasValue)
                return PreMilestoneUnavailable;
            throw new FormatException("Run context values are not approved for this milestone.");
        }

        private static string RequireToken(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '\\', '/', '\r', '\n' }) >= 0)
                throw new ArgumentException(field + " must be a non-path token.", field);
            return value;
        }

        private static string RequireSha256(string value)
        {
            if (value == null || value.Length != 64)
                throw new ArgumentException("RulesetHash must be a lowercase SHA-256.", nameof(value));
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    throw new ArgumentException("RulesetHash must be a lowercase SHA-256.", nameof(value));
            }
            return value;
        }
    }
}
