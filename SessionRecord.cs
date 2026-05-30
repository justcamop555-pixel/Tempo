using System;

namespace AutoClicker.Models
{
    /// <summary>
    /// A summary of a single completed clicking run, kept in the session history
    /// so the user can review past activity.
    /// </summary>
    public sealed class SessionRecord
    {
        public DateTime WhenUtc { get; set; } = DateTime.UtcNow;
        public long Clicks { get; set; }
        public double DurationSeconds { get; set; }
        public double AverageCps { get; set; }
        public double PeakCps { get; set; }

        /// <summary>The profile that was active for the run, if known.</summary>
        public string Profile { get; set; } = "";
    }
}
