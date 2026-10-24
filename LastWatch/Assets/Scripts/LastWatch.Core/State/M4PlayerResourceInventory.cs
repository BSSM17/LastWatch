using System;

namespace LastWatch.Core.State
{
    // Null means unavailable, not empty. Receipt identifies lineage only; setup
    // callers must independently obtain approval for the supplied inventory.
    [Serializable]
    public sealed class M4PlayerResourceInventory
    {
        public int schemaVersion = 1;
        public int parts;
        public string sourceReceiptId;

        public void Validate()
        {
            if (schemaVersion != 1 || parts < 0 || string.IsNullOrWhiteSpace(sourceReceiptId))
                throw new FormatException("Invalid Player Parts inventory or missing setup lineage.");
        }
        public M4PlayerResourceInventory Copy()
        {
            Validate();
            return new M4PlayerResourceInventory { schemaVersion = schemaVersion, parts = parts,
                sourceReceiptId = sourceReceiptId };
        }
    }
}
