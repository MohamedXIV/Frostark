using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    /// <summary>
    /// Immutable persistent character identity independent of duty role,
    /// station assignment, theater, physical GameObject and scene.
    /// Designed for localization via an optional stable key while always
    /// carrying a meaningful display name.
    /// </summary>
    public sealed class CrewIdentity : IEquatable<CrewIdentity>
    {
        public const int MaxDisplayNameLength = 64;
        public const int MaxLocalizationKeyLength = 96;

        public EntityId Id { get; }
        public string DisplayName { get; }
        public string LocalizationKey { get; }
        public bool HasLocalizationKey => !string.IsNullOrEmpty(LocalizationKey);
        public bool IsDefaultDerived { get; }

        public CrewIdentity(EntityId id, string displayName, string localizationKey = "")
        {
            if (!id.IsValid)
                throw new ArgumentException("Persistent crew ID required.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName) ||
                displayName.Trim() != displayName ||
                displayName.Length > MaxDisplayNameLength ||
                ContainsControlChar(displayName))
                throw new ArgumentException(
                    "Crew display name must be a non-empty, trimmed string without control characters.",
                    nameof(displayName));

            string normalizedKey = localizationKey ?? string.Empty;
            if (normalizedKey.Length > 0 &&
                (normalizedKey.Length > MaxLocalizationKeyLength ||
                 !IsValidLocalizationKey(normalizedKey)))
                throw new ArgumentException(
                    "Localization key must be empty or a valid token matching [A-Za-z0-9._-] up to 96 characters.",
                    nameof(localizationKey));

            Id = id;
            DisplayName = displayName;
            LocalizationKey = normalizedKey;
            IsDefaultDerived = normalizedKey.Length == 0 &&
                string.Equals(displayName, DeriveDefaultDisplayName(id), StringComparison.Ordinal);
        }

        /// <summary>
        /// Deterministic, culture-invariant identity derived from a stable
        /// EntityId when migrating legacy saves or constructing unnamed fixtures.
        /// Never uses random values or external state.
        /// </summary>
        public static CrewIdentity CreateDefault(EntityId id)
        {
            if (!id.IsValid)
                throw new ArgumentException("Persistent crew ID required.", nameof(id));
            return new CrewIdentity(id, DeriveDefaultDisplayName(id), string.Empty);
        }

        public static string DeriveDefaultDisplayName(EntityId id)
        {
            if (!id.IsValid)
                throw new ArgumentException("Persistent crew ID required.", nameof(id));

            string[] segments = id.Value.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return TruncateDisplayName(id.Value);

            string last = segments[segments.Length - 1];
            var words = new List<string>();
            if (segments.Length >= 2 && (IsNumericOrSingleToken(last)))
            {
                AppendWords(words, segments[segments.Length - 2]);
            }
            AppendWords(words, last);

            if (words.Count == 0)
                return TruncateDisplayName(id.Value);

            var sb = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(FormatWord(words[i]));
            }

            string candidate = sb.ToString().Trim();
            return candidate.Length == 0 ? TruncateDisplayName(id.Value) : TruncateDisplayName(candidate);
        }

        private static bool IsNumericOrSingleToken(string segment)
        {
            if (segment.Length <= 1)
                return true;
            for (int i = 0; i < segment.Length; i++)
                if (!char.IsDigit(segment[i]))
                    return false;
            return true;
        }

        private static void AppendWords(List<string> target, string segment)
        {
            string[] parts = segment.Split(new[] { '-', '_', '.', '/' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                    target.Add(trimmed);
            }
        }

        private static string FormatWord(string raw)
        {
            bool hasLetter = false;
            for (int i = 0; i < raw.Length; i++)
            {
                if (char.IsLetter(raw[i]))
                {
                    hasLetter = true;
                    break;
                }
            }
            if (!hasLetter)
                return raw;

            char first = char.ToUpper(raw[0], CultureInfo.InvariantCulture);
            if (raw.Length == 1)
                return first.ToString(CultureInfo.InvariantCulture);
            return first + raw.Substring(1).ToLower(CultureInfo.InvariantCulture);
        }

        private static string TruncateDisplayName(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.Length <= MaxDisplayNameLength)
                return trimmed;
            return trimmed.Substring(0, MaxDisplayNameLength).Trim();
        }

        private static bool ContainsControlChar(string text)
        {
            for (int i = 0; i < text.Length; i++)
                if (char.IsControl(text[i]))
                    return true;
            return false;
        }

        private static bool IsValidLocalizationKey(string key)
        {
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool isTokenChar = (c >= 'a' && c <= 'z') ||
                                   (c >= 'A' && c <= 'Z') ||
                                   (c >= '0' && c <= '9') ||
                                   c == '.' || c == '_' || c == '-';
                if (!isTokenChar)
                    return false;
            }
            return true;
        }

        public bool Equals(CrewIdentity other) =>
            other != null &&
            Id == other.Id &&
            string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
            string.Equals(LocalizationKey, other.LocalizationKey, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is CrewIdentity other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Id.GetHashCode();
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(DisplayName);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(LocalizationKey);
                return hash;
            }
        }

        public override string ToString() => $"{DisplayName} ({Id})";

        public static bool operator ==(CrewIdentity left, CrewIdentity right) =>
            Equals(left, right);

        public static bool operator !=(CrewIdentity left, CrewIdentity right) =>
            !Equals(left, right);
    }
}
