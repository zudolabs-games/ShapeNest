using System;

/// <summary>
/// Identity for registered boosters. Add values when a new booster is implemented.
/// </summary>
public enum BoosterType
{
    Magnet = 0,
    Hammer = 1
}

/// <summary>
/// Shared booster lifecycle. Magnet's existing Idle / Selecting / Executing map 1:1.
/// </summary>
public enum BoosterState
{
    Idle = 0,
    Selecting = 1,
    Executing = 2
}

/// <summary>
/// Minimal contract so future boosters can share activation, charges, targeting,
/// and level-reset without duplicating InputManager or LevelManager wiring.
/// Boosters must not move pieces themselves; they drive existing gameplay systems
/// (Magnet uses <see cref="BlockMover"/>; Hammer uses Board occupancy + Block settle).
/// </summary>
public interface IBooster
{
    BoosterType Type { get; }
    BoosterState State { get; }
    int Charges { get; }
    bool IsBusy { get; }
    bool IsSelecting { get; }
    bool CanActivate { get; }

    event Action<int> OnChargesChanged;
    event Action OnStateChanged;

    void Activate();
    void Cancel();
    void ResetState(string reason = null);
    bool TryHandleBlockSelection(Block block);
}
