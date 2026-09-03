/// <summary>
/// Presentation-friendly failure categories for booster activation / targeting.
/// Mapped to player-facing copy in <see cref="BoosterFeedbackMessage"/>.
/// Does not change gameplay eligibility rules.
/// </summary>
public enum BoosterFailureReason
{
    None = 0,
    NoCharges = 1,
    Busy = 2,
    NoValidTarget = 3,
    InvalidTarget = 4,
    NoUndoAvailable = 5,
    NoShufflePlan = 6,
    Unavailable = 7
}
