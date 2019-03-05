using System;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    /// <summary>
    /// Provides common retention policies
    /// </summary>
    public static class RetentionPolicy
    {
        private const float DefaultFactor = 0.9F;

        /// <summary>
        /// The default retention policy
        /// </summary>
        public static Func<long, long, long> Default { get; } = Decay(DefaultFactor);

        /// <summary>
        /// Retain the space required by the previous operation (trim to the size of the last usage)
        /// </summary>
        public static Func<long, long, long> Recent => (old, current) => current;

        /// <summary>
        /// Retain nothing (trim aggressively)
        /// </summary>
        public static Func<long, long, long> Nothing => (old, current) => 0;

        /// <summary>
        /// Retain everything (grow only)
        /// </summary>
        public static Func<long, long, long> Everything => (old, current) => Math.Max(old, current);

        /// <summary>
        /// When the required usage drops, decay the retained amount exponentially; growth is instant
        /// </summary>
        public static Func<long, long, long> Decay(float factor)
        {
            if (factor <= 0) return Recent;
            if (factor >= 1) return Everything;
            if (factor == DefaultFactor & Default != null) return Default;
            return (old, current) => Math.Max((long)(old * factor), current);
        }
    }
}
