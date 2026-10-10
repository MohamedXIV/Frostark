using System;
using System.Text;

namespace FrostArk.Core.World
{
    /// <summary>
    /// Stable FNV-1a 32-bit hash of UTF-8 bytes, with a seeded offset basis.
    /// Not cryptographic and not a gameplay RNG. Future event streams can
    /// derive reproducible subsystem seeds from persistent labels.
    /// Never replace this algorithm without versioning affected save fixtures.
    /// </summary>
    public static class StableHash32
    {
        public static uint Of(string label, uint seed = 0)
        {
            if (label == null)
                throw new ArgumentNullException(nameof(label));

            unchecked
            {
                uint hash = 2166136261u ^ seed;
                var bytes = Encoding.UTF8.GetBytes(label);
                foreach (byte value in bytes)
                {
                    hash ^= value;
                    hash *= 16777619u;
                }
                return hash;
            }
        }
    }
}
