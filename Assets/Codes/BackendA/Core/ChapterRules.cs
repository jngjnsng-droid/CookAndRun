using System;

namespace CookAndRun.Progression
{
    /// <summary>Immutable balance settings. Currency amounts are whole won.</summary>
    public sealed class ChapterRules
    {
        public int ChapterNumber { get; }
        public double DurationSeconds { get; }
        public long RevenueTarget { get; }
        public int MinimumAccuracyPercent { get; }

        public ChapterRules(int chapterNumber, double durationSeconds, long revenueTarget,
            int minimumAccuracyPercent = 0)
        {
            if (chapterNumber < 1) throw new ArgumentOutOfRangeException(nameof(chapterNumber));
            if (double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) || durationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (revenueTarget < 0) throw new ArgumentOutOfRangeException(nameof(revenueTarget));
            if (minimumAccuracyPercent < 0 || minimumAccuracyPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(minimumAccuracyPercent));

            ChapterNumber = chapterNumber;
            DurationSeconds = durationSeconds;
            RevenueTarget = revenueTarget;
            MinimumAccuracyPercent = minimumAccuracyPercent;
        }

        public static ChapterRules[] CreateDefaults()
        {
            return new[]
            {
                new ChapterRules(1, 180, 20000),
                new ChapterRules(2, 180, 30000),
                new ChapterRules(3, 180, 45000),
                new ChapterRules(4, 180, 60000),
                new ChapterRules(5, 180, 80000, 80)
            };
        }
    }
}
