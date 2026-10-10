using System;

namespace FrostArk.Core.World
{
    /// <summary>
    /// Case-sensitive persistent simulation identity. IDs come from authored
    /// data or a future explicit ID allocator, never Unity instance IDs.
    /// The default value is invalid and must not be persisted as a real ID.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        private readonly string _value;

        public bool IsValid => !string.IsNullOrEmpty(_value);
        public string Value => _value ?? string.Empty;

        private EntityId(string value) => _value = value;

        public static EntityId Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() != value)
                throw new ArgumentException("An entity ID must be non-empty and have no leading/trailing whitespace.", nameof(value));
            return new EntityId(value);
        }

        public static bool TryParse(string value, out EntityId id)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() != value)
            {
                id = default;
                return false;
            }
            id = new EntityId(value);
            return true;
        }

        public bool Equals(EntityId other) => string.Equals(_value, other._value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(EntityId a, EntityId b) => a.Equals(b);
        public static bool operator !=(EntityId a, EntityId b) => !a.Equals(b);
    }
}
