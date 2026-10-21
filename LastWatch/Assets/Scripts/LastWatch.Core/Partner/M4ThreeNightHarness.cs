using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    public enum M4HarnessDifficulty { Normal=0, Hard=1 }
    public enum LossTarget { Player=0, Partner=1, Barrier=2, Support=3 }
    // Mutable serialization values. Every public capture/export boundary returns detached copies.
    [Serializable] public sealed class M4LossTotals
    {
        public float PlayerDamage; public float PartnerDamage; public float BarrierDamage; public float SupportDamage;
        public bool PlayerDeath; public bool PartnerDeath; public bool BarrierPermanentLoss; public bool SupportPermanentLoss;

        internal M4LossTotals Copy() => (M4LossTotals)MemberwiseClone();
        internal void Validate()
        {
            ValidateDamage(PlayerDamage); ValidateDamage(PartnerDamage);
            ValidateDamage(BarrierDamage); ValidateDamage(SupportDamage);
        }
        internal bool Contains(M4LossTotals earlier) =>
            PlayerDamage >= earlier.PlayerDamage && PartnerDamage >= earlier.PartnerDamage &&
            BarrierDamage >= earlier.BarrierDamage && SupportDamage >= earlier.SupportDamage &&
            (!earlier.PlayerDeath || PlayerDeath) && (!earlier.PartnerDeath || PartnerDeath) &&
            (!earlier.BarrierPermanentLoss || BarrierPermanentLoss) && (!earlier.SupportPermanentLoss || SupportPermanentLoss);
        internal static void ValidateDamage(float value)
        {
            if (value < 0 || float.IsNaN(value) || float.IsInfinity(value))
                throw new FormatException("Damage must be finite and nonnegative.");
        }
    }
    [Serializable] public sealed class NightLossObservation
    {
        public int NightOrdinal; public ulong NightSeed; public int ResolvedTicketCount; public int TotalTicketCount;
        // Compatibility fields retain their v0.4 cumulative meaning. Do not sum them over Nights.
        public int MaximumActiveEnemies; public float PlayerDamage; public float PartnerDamage; public float BarrierDamage; public float SupportDamage;
        public bool PlayerDeath; public bool PartnerDeath; public bool BarrierPermanentLoss; public bool SupportPermanentLoss;
        public string GameplayHash;
        public M4LossTotals PerNightLosses;
        public M4LossTotals AggregateLosses;

        internal NightLossObservation Copy()
        {
            var copy = (NightLossObservation)MemberwiseClone();
            copy.PerNightLosses = PerNightLosses?.Copy();
            copy.AggregateLosses = AggregateLosses?.Copy();
            return copy;
        }
        internal void ValidateLosses()
        {
            if (PerNightLosses == null || AggregateLosses == null)
                throw new FormatException("Explicit per-Night and aggregate loss observations are required.");
            PerNightLosses.Validate(); AggregateLosses.Validate();
            if (!AggregateLosses.Contains(PerNightLosses) ||
                PlayerDamage != AggregateLosses.PlayerDamage || PartnerDamage != AggregateLosses.PartnerDamage ||
                BarrierDamage != AggregateLosses.BarrierDamage || SupportDamage != AggregateLosses.SupportDamage ||
                PlayerDeath != AggregateLosses.PlayerDeath || PartnerDeath != AggregateLosses.PartnerDeath ||
                BarrierPermanentLoss != AggregateLosses.BarrierPermanentLoss || SupportPermanentLoss != AggregateLosses.SupportPermanentLoss)
                throw new FormatException("Loss observation fields disagree.");
        }
    }
    [Serializable] public sealed class M4FrozenEvidence
    {
        public int EvidenceSchemaVersion = 2;
        public ulong RunSeed; public M4HarnessDifficulty Difficulty; public int CompletedNights; public bool Frozen;
        public int MaximumActiveEnemies; public NightLossObservation[] Nights; public string FinalGameplayHash;
        public M4LossTotals AggregateLosses;
    }
    // Harness-owned evidence history only. This is not a complete canonical Night/session save.
    [Serializable] public sealed class M4HarnessSaveSnapshot
    {
        public int SchemaVersion = 1;
        public ulong RunSeed;
        public M4HarnessDifficulty Difficulty;
        public int CompletedNights;
        public bool Frozen;
        public NightLossObservation[] Nights;
        public string FinalGameplayHash;
    }
    public sealed class M4ThreeNightHarness
    {
        public const ulong EvidenceRunSeed=1234;
        readonly List<NightLossObservation> nights=new List<NightLossObservation>(3);
        public M4HarnessDifficulty Difficulty { get; }
        public int CompletedNights => nights.Count;
        public int CurrentNightOrdinal=>nights.Count+1;
        public bool IsFrozen { get; private set; }
        public M4ThreeNightHarness(M4HarnessDifficulty difficulty)
        {
            if (!Enum.IsDefined(typeof(M4HarnessDifficulty), difficulty))
                throw new ArgumentOutOfRangeException(nameof(difficulty));
            Difficulty=difficulty;
        }
        public ulong CurrentNightSeed=>DeriveNightSeed(EvidenceRunSeed,"Story",CurrentNightOrdinal);
        internal void CommitNight(NightLossObservation observation,bool nightCompletedThisTick)
        {
            if(IsFrozen)throw new InvalidOperationException("Evidence is frozen.");if(!nightCompletedThisTick)throw new InvalidOperationException("Canonical Night completion is required.");
            if(observation==null||observation.NightOrdinal!=CurrentNightOrdinal||observation.NightSeed!=CurrentNightSeed)throw new ArgumentException("Night observation does not match canonical identity.");
            if(observation.TotalTicketCount<=0||observation.ResolvedTicketCount!=observation.TotalTicketCount||observation.MaximumActiveEnemies<0)throw new ArgumentException("Night is not fully resolved.");
            if (string.IsNullOrWhiteSpace(observation.GameplayHash)) throw new ArgumentException("Night hash is missing.");
            observation.ValidateLosses();
            if (nights.Count > 0 && !observation.AggregateLosses.Contains(nights[nights.Count-1].AggregateLosses))
                throw new ArgumentException("Aggregate losses cannot decrease between Nights.");
            nights.Add(observation.Copy());if(nights.Count==3)IsFrozen=true;
        }
        public M4HarnessSaveSnapshot CaptureSave() => new M4HarnessSaveSnapshot
        {
            RunSeed = EvidenceRunSeed, Difficulty = Difficulty, CompletedNights = nights.Count,
            Frozen = IsFrozen, Nights = CopyNights(),
            FinalGameplayHash = IsFrozen ? nights[2].GameplayHash : null
        };
        public static M4ThreeNightHarness Restore(M4HarnessSaveSnapshot saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            if (saved.SchemaVersion != 1 || saved.RunSeed != EvidenceRunSeed || saved.Nights == null ||
                saved.CompletedNights != saved.Nights.Length || saved.CompletedNights < 0 || saved.CompletedNights > 3 ||
                saved.Frozen != (saved.CompletedNights == 3))
                throw new FormatException("Invalid Harness schema, identity, history or freeze state.");
            var restored = new M4ThreeNightHarness(saved.Difficulty);
            foreach (NightLossObservation observation in saved.Nights) restored.CommitNight(observation, true);
            string expectedFinalHash = restored.IsFrozen ? restored.nights[2].GameplayHash : null;
            if (saved.FinalGameplayHash != expectedFinalHash) throw new FormatException("Harness final hash mismatch.");
            return restored;
        }
        internal M4LossTotals LastCommittedLosses() => nights.Count == 0
            ? new M4LossTotals() : nights[nights.Count-1].AggregateLosses.Copy();
        internal NightLossObservation LastCommittedObservation() => nights.Count == 0
            ? null : nights[nights.Count-1].Copy();
        internal void BindLastCanonicalHash(string hash)
        { if(nights.Count==0||string.IsNullOrEmpty(hash))throw new InvalidOperationException("Missing committed observation/hash.");nights[nights.Count-1].GameplayHash=hash; }
        NightLossObservation[] CopyNights()
        {
            var copies = new NightLossObservation[nights.Count];
            for (int index = 0; index < copies.Length; index++) copies[index] = nights[index].Copy();
            return copies;
        }
        public M4FrozenEvidence ExportFrozen()
        {
            if(!IsFrozen||nights.Count!=3)throw new InvalidOperationException("Evidence freezes only after Night 3.");int max=0;foreach(var n in nights)if(n.MaximumActiveEnemies>max)max=n.MaximumActiveEnemies;
            return new M4FrozenEvidence{RunSeed=EvidenceRunSeed,Difficulty=Difficulty,CompletedNights=3,Frozen=true,MaximumActiveEnemies=max,Nights=CopyNights(),FinalGameplayHash=nights[2].GameplayHash,AggregateLosses=nights[2].AggregateLosses.Copy()};
        }
        public string ExportFrozenJson(bool pretty=false)=>JsonUtility.ToJson(ExportFrozen(),pretty);
        public static ulong DeriveNightSeed(ulong runSeed,string gameMode,int nightOrdinal)
        {
            if(runSeed==0||string.IsNullOrWhiteSpace(gameMode)||nightOrdinal<1)throw new ArgumentException("Invalid NightSeed input.");
            using(var sha=SHA256.Create()){byte[] bytes=sha.ComputeHash(Encoding.UTF8.GetBytes(runSeed.ToString(System.Globalization.CultureInfo.InvariantCulture)+"|"+gameMode+"|"+nightOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)));ulong value=0;for(int n=0;n<8;n++)value|=(ulong)bytes[n]<<(8*n);return value;}
        }
    }
}
