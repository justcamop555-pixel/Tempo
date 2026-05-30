using System;

namespace AutoClicker.Engine
{
    /// <summary>
    /// Provides repeatable, thread-confined randomness for jittering click
    /// intervals and positions. A single instance is intended to be used from the
    /// engine's worker thread.
    /// </summary>
    public sealed class RandomizationHelper
    {
        private readonly Random _random;

        public RandomizationHelper()
        {
            _random = new Random();
        }

        public RandomizationHelper(int seed)
        {
            _random = new Random(seed);
        }

        /// <summary>
        /// Returns the base interval adjusted by a random offset in the range
        /// [-jitter, +jitter]. The offset follows a triangular distribution centred
        /// on zero, so most clicks land near the configured interval and large
        /// deviations are rare — closer to natural human timing than a flat
        /// (uniform) spread. The result is never less than 1.
        /// </summary>
        public long JitterInterval(long baseMs, int jitterMs)
        {
            if (jitterMs <= 0)
            {
                return baseMs < 1 ? 1 : baseMs;
            }

            long result = baseMs + TriangularOffset(jitterMs);
            return result < 1 ? 1 : result;
        }

        /// <summary>
        /// Applies a random pixel offset in the range [-jitter, +jitter] to the
        /// supplied coordinates, using a triangular distribution so the cursor tends
        /// to stay near the intended point.
        /// </summary>
        public void JitterPosition(ref int x, ref int y, int jitterPixels)
        {
            if (jitterPixels <= 0)
            {
                return;
            }

            x += TriangularOffset(jitterPixels);
            y += TriangularOffset(jitterPixels);
        }

        /// <summary>Returns a random integer in [minInclusive, maxExclusive).</summary>
        public int Next(int minInclusive, int maxExclusive)
        {
            return _random.Next(minInclusive, maxExclusive);
        }

        /// <summary>
        /// Produces an integer offset in [-magnitude, +magnitude] with a triangular
        /// distribution centred on zero. Implemented as the average of two
        /// independent uniform draws, which is the standard, cheap way to obtain a
        /// symmetric triangular spread.
        /// </summary>
        private int TriangularOffset(int magnitude)
        {
            if (magnitude <= 0)
            {
                return 0;
            }

            int a = _random.Next(-magnitude, magnitude + 1);
            int b = _random.Next(-magnitude, magnitude + 1);

            // Rounding the average keeps the full [-magnitude, +magnitude] range.
            double avg = (a + b) / 2.0;
            return (int)Math.Round(avg, MidpointRounding.AwayFromZero);
        }
    }
}
