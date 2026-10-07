using System;

namespace AlightenInk.Core
{
    /// <summary>Timing for fading ink: fully visible for the hold time, then a smooth fade to nothing.</summary>
    public static class FadeMath
    {
        public const double FadeOutSeconds = 0.8;

        /// <summary>Opacity multiplier (0..1) for an item that is <paramref name="elapsed"/> seconds old.</summary>
        public static double Opacity(double elapsed, double hold, double fade = FadeOutSeconds)
        {
            if (double.IsNaN(elapsed) || elapsed <= hold) return 1.0;
            if (fade <= 0 || elapsed >= hold + fade) return 0.0;
            double t = (elapsed - hold) / fade;          // 0..1 through the fade
            double eased = t * t * (3 - 2 * t);           // smoothstep
            return 1.0 - eased;
        }

        public static bool IsExpired(double elapsed, double hold, double fade = FadeOutSeconds) =>
            elapsed >= hold + Math.Max(0, fade);

        /// <summary>Next hold choice after <paramref name="current"/>, wrapping round (2 → 3 → 5 → 8 → 2).</summary>
        public static double NextHold(double current, double[] choices)
        {
            if (choices == null || choices.Length == 0) throw new ArgumentException("No choices", nameof(choices));
            foreach (double c in choices)
            {
                if (c > current + 1e-9) return c;
            }
            return choices[0];
        }
    }
}
