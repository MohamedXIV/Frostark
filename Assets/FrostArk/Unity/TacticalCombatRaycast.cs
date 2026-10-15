using System;
using UnityEngine;

namespace FrostArk.Unity
{
    public enum CombatTargetingStatus
    {
        Clear = 0,
        OutOfRange = 1,
        Obstructed = 2,
        InvalidTarget = 3,
        Missed = 4
    }

    /// <summary>
    /// Evaluates line-of-fire physics queries for combat weapons.
    /// Distinguishes camera clicks from ballistic/line-of-fire raycasts,
    /// eliminates shooter self-collision, and validates obstructions.
    /// </summary>
    public static class TacticalCombatRaycast
    {
        /// <summary>
        /// Evaluates whether the line-of-sight between muzzle and target aim point is free
        /// of physical obstructions (cover, obstacles, friendly squad members).
        /// </summary>
        public static bool HasClearLineOfSight(
            Vector3 muzzlePosition,
            TacticalCombatTargetProxy target,
            float maxRange,
            LayerMask blockingMask,
            Collider shooterCollider)
        {
            if (target == null || target.IsDead || !target.IsHostile)
                return false;

            Vector3 targetPoint = target.AimPoint != null
                ? target.AimPoint.position
                : target.transform.position + Vector3.up * 1.0f;

            Vector3 delta = targetPoint - muzzlePosition;
            float distance = delta.magnitude;
            if (distance > maxRange || distance < 0.001f)
                return false;

            Vector3 direction = delta / distance;
            var hits = Physics.RaycastAll(muzzlePosition, direction, distance, blockingMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
                return true;

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var candidate in hits)
            {
                if (shooterCollider != null && (candidate.collider == shooterCollider || candidate.collider.transform.IsChildOf(shooterCollider.transform)))
                    continue;

                if (candidate.collider == target.Hitbox ||
                    candidate.collider.transform == target.transform ||
                    candidate.collider.transform.IsChildOf(target.transform))
                {
                    return true;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Evaluates ballistic line of fire and requires actual target hitbox contact
        /// to report CombatTargetingStatus.Clear. If blocked by intermediate geometry,
        /// returns Obstructed. If the ray encounters no obstruction but fails to strike
        /// the target hitbox (e.g. aim point misaligned/outside collider), returns Missed.
        /// </summary>
        public static CombatTargetingStatus EvaluateLineOfFire(
            Vector3 muzzlePosition,
            TacticalCombatTargetProxy target,
            float maxRange,
            LayerMask blockingMask,
            Collider shooterCollider,
            out RaycastHit hit)
        {
            hit = default;

            if (target == null || target.IsDead || !target.IsHostile || target.Hitbox == null || !target.Hitbox.enabled)
                return CombatTargetingStatus.InvalidTarget;

            Vector3 targetPoint = target.AimPoint != null
                ? target.AimPoint.position
                : target.transform.position + Vector3.up * 1.0f;

            Vector3 delta = targetPoint - muzzlePosition;
            float distance = delta.magnitude;

            if (distance > maxRange)
                return CombatTargetingStatus.OutOfRange;

            if (distance < 0.001f)
                return CombatTargetingStatus.InvalidTarget;

            Vector3 direction = delta / distance;

            // Perform raycast query ignoring triggers
            var hits = Physics.RaycastAll(muzzlePosition, direction, distance, blockingMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
            {
                // No geometry between muzzle and target point hit; missed target hitbox
                return CombatTargetingStatus.Missed;
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

            return CombatTargetingStatus.Missed;
        }

        /// <summary>
        /// Explicitly verifies whether a ballistic discharge strikes the target's hitbox.
        /// Distinguishes unobstructed line of sight from a confirmed physical hit.
        /// </summary>
        public static bool TryVerifyProjectileHit(
            Vector3 muzzlePosition,
            TacticalCombatTargetProxy target,
            float maxRange,
            LayerMask blockingMask,
            Collider shooterCollider,
            out RaycastHit hit)
        {
            var status = EvaluateLineOfFire(muzzlePosition, target, maxRange, blockingMask, shooterCollider, out hit);
            return status == CombatTargetingStatus.Clear &&
                   hit.collider != null &&
                   (hit.collider == target.Hitbox ||
                    hit.collider.transform == target.transform ||
                    hit.collider.transform.IsChildOf(target.transform));
        }
    }
}
