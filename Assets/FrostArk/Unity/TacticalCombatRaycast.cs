using System;
using UnityEngine;

namespace FrostArk.Unity
{
    public enum CombatTargetingStatus
    {
        Clear = 0,
        OutOfRange = 1,
        Obstructed = 2,
        InvalidTarget = 3
    }

    /// <summary>
    /// Evaluates line-of-fire physics queries for combat weapons.
    /// Distinguishes camera clicks from ballistic/line-of-fire raycasts,
    /// eliminates shooter self-collision, and validates obstructions.
    /// </summary>
    public static class TacticalCombatRaycast
    {
        public static CombatTargetingStatus EvaluateLineOfFire(
            Vector3 muzzlePosition,
            TacticalCombatTargetProxy target,
            float maxRange,
            LayerMask blockingMask,
            Collider shooterCollider,
            out RaycastHit hit)
        {
            hit = default;

            if (target == null || target.IsDead || !target.IsHostile)
                return CombatTargetingStatus.InvalidTarget;

            Vector3 targetPoint = target.AimPoint != null
                ? target.AimPoint.position
                : target.transform.position + Vector3.up * 1.0f;

            Vector3 delta = targetPoint - muzzlePosition;
            float distance = delta.magnitude;

            if (distance > maxRange)
                return CombatTargetingStatus.OutOfRange;

            if (distance < 0.001f)
                return CombatTargetingStatus.Clear;

            Vector3 direction = delta / distance;

            // Perform raycast query ignoring triggers
            var hits = Physics.RaycastAll(muzzlePosition, direction, distance, blockingMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
            {
                // No geometry between muzzle and target point
                return CombatTargetingStatus.Clear;
            }

            // Sort hits by distance
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var candidate in hits)
            {
                // Skip self-collision
                if (shooterCollider != null && (candidate.collider == shooterCollider || candidate.collider.transform.IsChildOf(shooterCollider.transform)))
                    continue;

                // Did we hit the target itself or its hitbox?
                if (candidate.collider == target.Hitbox ||
                    candidate.collider.transform == target.transform ||
                    candidate.collider.transform.IsChildOf(target.transform))
                {
                    hit = candidate;
                    return CombatTargetingStatus.Clear;
                }

                // Any other physical geometry (wall, obstacle, or friendly actor) obstructs
                hit = candidate;
                return CombatTargetingStatus.Obstructed;
            }

            return CombatTargetingStatus.Clear;
        }
    }
}
