using System;

namespace FrostArk.Core.World
{
    /// <summary>
    /// Non-authoritative, transient on-foot input sample.
    /// This never owns world time, positions, collision, camera projection,
    /// gameplay command provenance or Unity Input System bindings.
    ///
    /// MoveX/MoveY are axes in the local planar input frame; the Unity adapter
    /// converts them into camera-relative world movement. AimX/AimY represent
    /// the requested planar aiming direction, not a screen-space cursor.
    /// </summary>
    public readonly struct PlayerInputIntent
    {
        public float MoveX { get; }
        public float MoveY { get; }
        public float AimX { get; }
        public float AimY { get; }
        public bool FireHeld { get; }
        public bool InteractPressed { get; }
        public bool HasAimDirection => AimX != 0f || AimY != 0f;

        private PlayerInputIntent(float moveX, float moveY, float aimX, float aimY,
            bool fireHeld, bool interactPressed)
        {
            MoveX = moveX;
            MoveY = moveY;
            AimX = aimX;
            AimY = aimY;
            FireHeld = fireHeld;
            InteractPressed = interactPressed;
        }

        /// <summary>
        /// Normalizes only inputs whose magnitude exceeds one, preserving
        /// sub-unit analog stick pressure. Zero aim stays zero: callers decide
        /// whether an existing facing direction is appropriate. This method
        /// does not resolve mouse rays or advance world state.
        /// </summary>
        public static PlayerInputIntent FromAxes(
            float moveX, float moveY, float aimX, float aimY,
            bool fireHeld, bool interactPressed)
        {
            ValidateFinite(moveX, nameof(moveX));
            ValidateFinite(moveY, nameof(moveY));
            ValidateFinite(aimX, nameof(aimX));
            ValidateFinite(aimY, nameof(aimY));

            ClampMagnitude(ref moveX, ref moveY);
            ClampMagnitude(ref aimX, ref aimY);
            return new PlayerInputIntent(moveX, moveY, aimX, aimY,
                fireHeld, interactPressed);
        }

        private static void ValidateFinite(float value, string parameter)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameter,
                    "Input axes must be finite.");
        }

        private static void ClampMagnitude(ref float x, ref float y)
        {
            // Compute in double so even finite float extremes cannot overflow.
            var lengthSquared = (double)x * x + (double)y * y;
            if (lengthSquared <= 1d)
                return;

            var length = Math.Sqrt(lengthSquared);
            x = (float)(x / length);
            y = (float)(y / length);
        }
    }
}
