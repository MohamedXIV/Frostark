using System;
using System.Globalization;

namespace FrostArk.Core.World
{
    /// <summary>
    /// Non-negative, whole minutes since the beginning of a game run.
    /// This is simulation time, not Unity frame time or a wall-clock timestamp.
    /// The default value (zero) is a valid starting minute.
    /// </summary>
    public readonly struct WorldMinute : IEquatable<WorldMinute>, IComparable<WorldMinute>
    {
        public static WorldMinute Zero => default;

        public long Value { get; }

        public WorldMinute(long value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "World time cannot be negative.");
            Value = value;
        }

        public WorldMinute Add(long minutes)
        {
            if (minutes < 0)
                throw new ArgumentOutOfRangeException(nameof(minutes), "Use Subtract to rewind time.");
            return new WorldMinute(checked(Value + minutes));
        }

        public WorldMinute Subtract(long minutes)
        {
            if (minutes < 0)
                throw new ArgumentOutOfRangeException(nameof(minutes), "Use Add to advance time.");
            if (minutes > Value)
                throw new ArgumentOutOfRangeException(nameof(minutes), "Cannot go before the start of a run.");
            return new WorldMinute(Value - minutes);
        }

        public long ElapsedSince(WorldMinute earlier)
        {
            if (earlier.Value > Value)
                throw new ArgumentOutOfRangeException(nameof(earlier), "Earlier time must not be in the future.");
            return Value - earlier.Value;
        }

        public int CompareTo(WorldMinute other) => Value.CompareTo(other.Value);
        public bool Equals(WorldMinute other) => Value == other.Value;
        public override bool Equals(object obj) => obj is WorldMinute other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        public static bool operator ==(WorldMinute a, WorldMinute b) => a.Equals(b);
        public static bool operator !=(WorldMinute a, WorldMinute b) => !a.Equals(b);
        public static bool operator <(WorldMinute a, WorldMinute b) => a.Value < b.Value;
        public static bool operator >(WorldMinute a, WorldMinute b) => a.Value > b.Value;
        public static bool operator <=(WorldMinute a, WorldMinute b) => a.Value <= b.Value;
        public static bool operator >=(WorldMinute a, WorldMinute b) => a.Value >= b.Value;
    }
}
