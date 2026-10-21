using System;

namespace LastWatch.Core.Partner
{
    public enum M4PlayerWeaponId { ServicePistol = 0, AssaultRifle = 1, WatchRifle = 2 }
    public enum M4PlayerReloadStatus { None = 0, Active = 1, Committed = 2, Cancelled = 3 }

    // Durable player weapon/action fields for the bounded M4 canonical session save.
    // Reload remaining includes the next processing tick through its due tick (zero means the
    // due tick is deferred by a higher-priority stop); fire cooldown and shot-block counts are
    // the number of ticks still blocked from the next tick.
    [Serializable]
    public sealed class M4PlayerWeaponActionSaveState
    {
        internal const int ServicePistolMagazineCapacity = 12;
        internal const int AssaultRifleMagazineCapacity = 30;
        internal const int AssaultRifleStartingReserve = 150;
        internal const int WatchRifleMagazineCapacity = 5;
        internal const int WatchRifleStartingReserve = 10;

        public int schemaVersion = 2;
        public ulong nextFireSequence = 1;
        public ulong activeFireSequence;
        public bool triggerHeld;
        public int lastShotAgeTicks = -1;
        public M4PlayerWeaponId selectedWeapon = M4PlayerWeaponId.AssaultRifle;
        public int servicePistolMagazine = ServicePistolMagazineCapacity;
        public int servicePistolReserve = int.MaxValue;
        public int assaultRifleMagazine = AssaultRifleMagazineCapacity;
        public int assaultRifleReserve = AssaultRifleStartingReserve;
        public int watchRifleMagazine = WatchRifleMagazineCapacity;
        public int watchRifleReserve = WatchRifleStartingReserve;
        public M4PlayerReloadStatus reloadStatus;
        public M4PlayerWeaponId reloadingWeapon = M4PlayerWeaponId.AssaultRifle;
        public ulong nextReloadTransactionId = 1;
        public ulong activeReloadTransactionId;
        public int reloadRemainingTicks;
        public int reloadShotBlockedRemainingTicks;
        public int[] fireCooldownRemainingTicks = new int[3];

        public M4PlayerWeaponActionSaveState Copy()
        {
            return new M4PlayerWeaponActionSaveState
            {
                schemaVersion = schemaVersion,
                nextFireSequence = nextFireSequence,
                activeFireSequence = activeFireSequence,
                triggerHeld = triggerHeld,
                lastShotAgeTicks = lastShotAgeTicks,
                selectedWeapon = selectedWeapon,
                servicePistolMagazine = servicePistolMagazine,
                servicePistolReserve = servicePistolReserve,
                assaultRifleMagazine = assaultRifleMagazine,
                assaultRifleReserve = assaultRifleReserve,
                watchRifleMagazine = watchRifleMagazine,
                watchRifleReserve = watchRifleReserve,
                reloadStatus = reloadStatus,
                reloadingWeapon = reloadingWeapon,
                nextReloadTransactionId = nextReloadTransactionId,
                activeReloadTransactionId = activeReloadTransactionId,
                reloadRemainingTicks = reloadRemainingTicks,
                reloadShotBlockedRemainingTicks = reloadShotBlockedRemainingTicks,
                fireCooldownRemainingTicks = fireCooldownRemainingTicks == null
                    ? null
                    : (int[])fireCooldownRemainingTicks.Clone()
            };
        }

        public void Validate()
        {
            if (schemaVersion != 2 || !Enum.IsDefined(typeof(M4PlayerWeaponId), selectedWeapon) ||
                !Enum.IsDefined(typeof(M4PlayerWeaponId), reloadingWeapon) ||
                !Enum.IsDefined(typeof(M4PlayerReloadStatus), reloadStatus))
                throw new FormatException("Unsupported player weapon/action save state.");
            if (nextFireSequence == 0 || activeFireSequence >= nextFireSequence || lastShotAgeTicks < -1 ||
                (activeFireSequence != 0 && (!triggerHeld || lastShotAgeTicks < 0)))
                throw new FormatException("Invalid player FireSequence save state.");
            if (servicePistolMagazine < 0 || servicePistolMagazine > ServicePistolMagazineCapacity || servicePistolReserve != int.MaxValue ||
                assaultRifleMagazine < 0 || assaultRifleMagazine > AssaultRifleMagazineCapacity ||
                assaultRifleReserve < 0 ||
                watchRifleMagazine < 0 || watchRifleMagazine > WatchRifleMagazineCapacity ||
                watchRifleReserve < 0 || watchRifleReserve > WatchRifleStartingReserve)
                throw new FormatException("Invalid player ammunition state.");
            if (fireCooldownRemainingTicks == null || fireCooldownRemainingTicks.Length != 3)
                throw new FormatException("Player fire cooldown state must contain all three weapons.");
            foreach (int remaining in fireCooldownRemainingTicks)
                if (remaining < 0) throw new FormatException("Negative player fire cooldown.");
            if (reloadShotBlockedRemainingTicks < 0 || nextReloadTransactionId == 0)
                throw new FormatException("Invalid player reload transaction cursor.");

            bool active = reloadStatus == M4PlayerReloadStatus.Active;
            if (active)
            {
                if (activeReloadTransactionId == 0 || activeReloadTransactionId >= nextReloadTransactionId ||
                    reloadRemainingTicks < 0)
                    throw new FormatException("Active player reload is missing its transaction or remaining ticks.");
            }
            else if (activeReloadTransactionId != 0 || reloadRemainingTicks != 0)
                throw new FormatException("Terminal player reload retains pending transaction state.");
        }
    }
}
