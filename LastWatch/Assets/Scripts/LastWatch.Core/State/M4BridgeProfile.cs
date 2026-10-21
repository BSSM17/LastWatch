using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LastWatch.Core.State
{
    public static class M4BridgeProfile
    {
        public const string Id = "M3LegacyStoryBridgeV1";
        public const string SeedAlgorithm = "LW.NIGHTSEED.SHA256.LE64.v1";
        public const string QueueAlgorithm = "LW.M3.STORY.31";
        public const int RulesetVersion = 31;
        public const string ContentVersion = "LW.M2.TowerGeometryFixture.v1";
        // Version-controlled manifest of the actual bounded generator and geometry, not a display-version hash.
        public const string Manifest = "LW.M4.RULESET.v1|M3LegacyStoryBridgeV1|LW.NIGHTSEED.SHA256.LE64.v1|LW.M3.STORY.31|ruleset=31|tick=50|StoryNight=1..3|seed=1234|total=18+6*(day-1);round-half-up|active=min(14,8+(day-1)/2+bonus)|density=min(6,(2.6+.30*(day-1))*mult);round1-away|Normal=1,0,1|Hard=1.25,2,1.15|archetypes=70,15,10,5|pressure=4,5,7,12;64,28,28|face-cap=6|breaker=2|windup=2|window=500|face-share=60|group=4;gap=20..35|revision=M3-v0.7+D3|geometry=LW.M2.TowerGeometryFixture.v1";
        public static string RulesetHash { get { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(Manifest))); } }
        public static ulong NightSeed(ulong runSeed, int night)
        {
            if (night < 1 || night > 3) throw new ArgumentOutOfRangeException(nameof(night));
            string input = runSeed.ToString(CultureInfo.InvariantCulture) + "|Story|" + night.ToString(CultureInfo.InvariantCulture);
            using (var sha = SHA256.Create())
            { byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input)); ulong value = 0; for (int n = 0; n < 8; n++) value |= (ulong)bytes[n] << (n * 8); return value; }
        }
        internal static string Hex(byte[] bytes) { var b = new StringBuilder(bytes.Length * 2); foreach (byte x in bytes) b.Append(x.ToString("x2", CultureInfo.InvariantCulture)); return b.ToString(); }
    }
}
