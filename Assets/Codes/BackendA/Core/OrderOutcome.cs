using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CookAndRun.Progression
{
    /// <summary>B provides each violated rule, including zero-percent violations.</summary>
    public sealed class OrderViolation
    {
        public string Code { get; }
        public int PenaltyPercent { get; }

        public OrderViolation(string code, int penaltyPercent)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A rule code is required.", nameof(code));
            if (penaltyPercent < 0 || penaltyPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(penaltyPercent));
            Code = code;
            PenaltyPercent = penaltyPercent;
        }
    }

    /// <summary>
    /// A completed order only. Capture runId/chapterNumber when the order is created;
    /// do not fetch a new token when an old asynchronous callback completes.
    /// </summary>
    public sealed class OrderOutcome
    {
        public int RunId { get; }
        public int ChapterNumber { get; }
        public string OrderId { get; }
        public long BaseReward { get; }
        public IReadOnlyList<OrderViolation> Violations { get; }
        public long ChangeLoss { get; }
        public double TimePenaltySeconds { get; }

        public OrderOutcome(int runId, int chapterNumber, string orderId, long baseReward,
            IEnumerable<OrderViolation> violations = null, long changeLoss = 0,
            double timePenaltySeconds = 0)
        {
            if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
            if (chapterNumber < 1) throw new ArgumentOutOfRangeException(nameof(chapterNumber));
            if (string.IsNullOrWhiteSpace(orderId)) throw new ArgumentException("An order ID is required.", nameof(orderId));
            if (baseReward < 0) throw new ArgumentOutOfRangeException(nameof(baseReward));
            if (changeLoss < 0) throw new ArgumentOutOfRangeException(nameof(changeLoss));
            if (double.IsNaN(timePenaltySeconds) || double.IsInfinity(timePenaltySeconds) || timePenaltySeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(timePenaltySeconds));

            var copy = new List<OrderViolation>();
            if (violations != null)
            {
                foreach (var violation in violations)
                {
                    if (violation == null) throw new ArgumentException("Violation entries cannot be null.", nameof(violations));
                    copy.Add(violation);
                }
            }

            RunId = runId;
            ChapterNumber = chapterNumber;
            OrderId = orderId;
            BaseReward = baseReward;
            Violations = new ReadOnlyCollection<OrderViolation>(copy);
            ChangeLoss = changeLoss;
            TimePenaltySeconds = timePenaltySeconds;
        }
    }

    /// <summary>Immutable receipt: maximum percentage once, then a separate cash loss.</summary>
    public sealed class OrderSettlement
    {
        public OrderOutcome Outcome { get; }
        public int AppliedPenaltyPercent { get; }
        public long DiscountAmount { get; }
        public long RewardAfterDiscount { get; }
        public long NetReward { get; }
        public bool IsAccurate { get; }

        internal OrderSettlement(OrderOutcome outcome)
        {
            Outcome = outcome;
            int maximum = 0;
            foreach (var violation in outcome.Violations)
                maximum = Math.Max(maximum, violation.PenaltyPercent);

            AppliedPenaltyPercent = maximum;
            // Round the payment down to a whole won; decimal avoids floating-point currency errors.
            RewardAfterDiscount = (long)decimal.Truncate((decimal)outcome.BaseReward * (100 - maximum) / 100);
            DiscountAmount = outcome.BaseReward - RewardAfterDiscount;
            NetReward = RewardAfterDiscount - outcome.ChangeLoss;
            IsAccurate = outcome.Violations.Count == 0 && outcome.ChangeLoss == 0 && outcome.TimePenaltySeconds == 0;
        }
    }
}
