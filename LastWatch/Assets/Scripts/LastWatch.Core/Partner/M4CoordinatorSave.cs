using System;

namespace LastWatch.Core.Partner
{
    // Persisted coordinator state is kept independent of journal-consumption behavior.
    [Serializable]
    public sealed class M4CoordinatorSave
    {
        public int EvidenceSchemaVersion = 2;
        public int NightOrdinal=1; public ulong RunFactCursor; public int MaximumActiveEnemies;
        // Existing scalar fields remain cumulative across Nights.
        public float PlayerDamage; public float PartnerDamage; public float BarrierDamage; public float SupportDamage;
        public bool PlayerDeath; public bool PartnerDeath; public bool BarrierPermanentLoss; public bool SupportPermanentLoss;
        public string LastDirectorStateHash; public string LastQueueHash; public bool CurrentNightCommitted;
        public M4LossTotals PerNightLosses = new M4LossTotals();

        internal M4CoordinatorSave Copy()
        {
            var copy = (M4CoordinatorSave)MemberwiseClone();
            copy.PerNightLosses = PerNightLosses?.Copy();
            return copy;
        }

        internal M4LossTotals AggregateLosses() => new M4LossTotals
        {
            PlayerDamage = PlayerDamage, PartnerDamage = PartnerDamage,
            BarrierDamage = BarrierDamage, SupportDamage = SupportDamage,
            PlayerDeath = PlayerDeath, PartnerDeath = PartnerDeath,
            BarrierPermanentLoss = BarrierPermanentLoss, SupportPermanentLoss = SupportPermanentLoss
        };
    }
}
