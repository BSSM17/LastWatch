#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using LastWatch.Core.Combat;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    // Detached values only. A policy cannot obtain the live Director, actor, or RunState.
    public sealed class M4QaAdaptiveSnapshot
    {
        public int RunFixedTick { get; }
        public int NightOrdinal { get; }
        public M4SessionStage SessionStage { get; }
        public int CompletedNights { get; }
        public Vector3 PlayerPosition { get; }
        public Vector3 PlayerCameraPosition { get; }
        public float PlayerYaw { get; }
        public float CameraPitch { get; }
        public int SelectedWeapon { get; }
        public bool PlayerDead { get; }
        public bool ReloadActive { get; }
        public int ReloadCompleteRunTick { get; }
        public int ReloadShotBlockedThroughRunTick { get; }
        public IReadOnlyList<M4QaWeaponSnapshot> Weapons { get; }
        public IReadOnlyList<M4QaTicketSnapshot> Tickets { get; }

        internal M4QaAdaptiveSnapshot(int runTick, int night, M4SessionStage stage, int completedNights,
            Vector3 playerPosition, Vector3 cameraPosition, float yaw, float pitch, int selectedWeapon,
            bool playerDead, bool reloadActive, int reloadCompleteRunTick, int reloadShotBlockedThroughRunTick,
            M4QaWeaponSnapshot[] weapons, M4QaTicketSnapshot[] tickets)
        {
            RunFixedTick = runTick;
            NightOrdinal = night;
            SessionStage = stage;
            CompletedNights = completedNights;
            PlayerPosition = playerPosition;
            PlayerCameraPosition = cameraPosition;
            PlayerYaw = yaw;
            CameraPitch = pitch;
            SelectedWeapon = selectedWeapon;
            PlayerDead = playerDead;
            ReloadActive = reloadActive;
            ReloadCompleteRunTick = reloadCompleteRunTick;
            ReloadShotBlockedThroughRunTick = reloadShotBlockedThroughRunTick;
            Weapons = Array.AsReadOnly(weapons);
            Tickets = Array.AsReadOnly(tickets);
        }

        internal static bool IsValidStimulusId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 80) return false;
            foreach (char c in value)
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-')) return false;
            return true;
        }
    }

    public readonly struct M4QaWeaponSnapshot
    {
        public int Weapon { get; }
        public int Magazine { get; }
        public int Reserve { get; }
        public int NextLegalShotRunTick { get; }
        public M4QaWeaponSnapshot(int weapon, int magazine, int reserve, int nextLegalShotRunTick)
        { Weapon = weapon; Magazine = magazine; Reserve = reserve; NextLegalShotRunTick = nextLegalShotRunTick; }
    }

    public readonly struct M4QaTicketSnapshot
    {
        public ulong TicketId { get; }
        public SpawnTicketLifecycle Lifecycle { get; }
        public int Face { get; }
        public ulong? ActiveEntityId { get; }
        public EnemyMovementState MovementState { get; }
        public bool ActorPresent { get; }
        public Vector3 ActorPosition { get; }
        public Vector3 ActorAimPoint { get; }
        public M4QaTicketSnapshot(ulong ticketId, SpawnTicketLifecycle lifecycle, int face,
            ulong? activeEntityId, EnemyMovementState movementState, bool actorPresent,
            Vector3 actorPosition, Vector3 actorAimPoint)
        {
            TicketId = ticketId;
            Lifecycle = lifecycle;
            Face = face;
            ActiveEntityId = activeEntityId;
            MovementState = movementState;
            ActorPresent = actorPresent;
            ActorPosition = actorPosition;
            ActorAimPoint = actorAimPoint;
        }
    }
}
#endif
