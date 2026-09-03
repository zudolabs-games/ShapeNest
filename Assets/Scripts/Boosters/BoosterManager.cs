using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates boosters. Does not move blocks, match, or own occupancy.
/// Magnet gameplay stays on <see cref="MagnetBooster"/> / <see cref="BlockMover"/>.
/// Hammer gameplay stays on <see cref="HammerBooster"/>.
/// Shuffle gameplay stays on <see cref="ShuffleBooster"/>.
/// Undo gameplay stays on <see cref="UndoBooster"/> / <see cref="BoardUndoHistory"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class BoosterManager : MonoBehaviour
{
    private readonly List<IBooster> boosters = new List<IBooster>();
    private bool registryReady;

    public bool IsAnySelecting
    {
        get
        {
            EnsureRegistry();
            for (int i = 0; i < boosters.Count; i++)
            {
                if (boosters[i] != null && boosters[i].IsSelecting)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool IsAnyExecuting
    {
        get
        {
            EnsureRegistry();
            for (int i = 0; i < boosters.Count; i++)
            {
                IBooster booster = boosters[i];
                if (booster != null && booster.IsBusy && !booster.IsSelecting)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool IsAnyBusy => IsAnySelecting || IsAnyExecuting;

    private void Awake()
    {
        EnsureMagnet();
        EnsureHammer();
        EnsureShuffle();
        EnsureUndo();
        RefreshRegistry();
    }

    private void OnEnable()
    {
        RefreshRegistry();
    }

    /// <summary>Finds a registered booster, or null if that type is not present.</summary>
    public IBooster GetBooster(BoosterType type)
    {
        EnsureRegistry();
        for (int i = 0; i < boosters.Count; i++)
        {
            IBooster booster = boosters[i];
            if (booster != null && booster.Type == type)
            {
                return booster;
            }
        }

        RefreshRegistry();
        for (int i = 0; i < boosters.Count; i++)
        {
            IBooster booster = boosters[i];
            if (booster != null && booster.Type == type)
            {
                return booster;
            }
        }

        return null;
    }

    public int GetCharges(BoosterType type)
    {
        IBooster booster = GetBooster(type);
        return booster != null ? booster.Charges : 0;
    }

    public bool CanActivate(BoosterType type)
    {
        IBooster booster = GetBooster(type);
        if (booster == null || !booster.CanActivate)
        {
            return false;
        }

        return !IsOtherBoosterBusy(booster);
    }

    /// <summary>
    /// Activates the booster if it can run and no other booster is busy.
    /// Same-booster toggle (Magnet cancel-while-selecting) is allowed.
    /// Returns false when activation does not begin (charges, busy, no target, etc.).
    /// </summary>
    public bool TryActivate(BoosterType type)
    {
        return TryActivate(type, out _);
    }

    /// <summary>
    /// Same as <see cref="TryActivate(BoosterType)"/> with a presentation-friendly failure reason.
    /// Does not change gameplay eligibility; reason mirrors existing early-outs.
    /// </summary>
    public bool TryActivate(BoosterType type, out BoosterFailureReason reason)
    {
        reason = BoosterFailureReason.None;
        IBooster booster = GetBooster(type);
        if (booster == null)
        {
            reason = BoosterFailureReason.Unavailable;
            return false;
        }

        if (IsOtherBoosterBusy(booster))
        {
            reason = BoosterFailureReason.Busy;
            return false;
        }

        switch (type)
        {
            case BoosterType.Magnet:
            {
                MagnetBooster magnet = booster as MagnetBooster;
                if (magnet == null)
                {
                    reason = BoosterFailureReason.Unavailable;
                    return false;
                }

                return magnet.TryBeginActivation(out reason);
            }
            case BoosterType.Hammer:
            {
                HammerBooster hammer = booster as HammerBooster;
                if (hammer == null)
                {
                    reason = BoosterFailureReason.Unavailable;
                    return false;
                }

                return hammer.TryBeginActivation(out reason);
            }
            case BoosterType.Shuffle:
            {
                ShuffleBooster shuffle = booster as ShuffleBooster;
                if (shuffle == null)
                {
                    reason = BoosterFailureReason.Unavailable;
                    return false;
                }

                return shuffle.TryBeginActivation(out reason);
            }
            case BoosterType.Undo:
            {
                UndoBooster undo = booster as UndoBooster;
                if (undo == null)
                {
                    reason = BoosterFailureReason.Unavailable;
                    return false;
                }

                return undo.TryBeginActivation(out reason);
            }
            default:
                if (!booster.CanActivate)
                {
                    reason = BoosterFailureReason.Unavailable;
                    return false;
                }

                booster.Activate();
                return true;
        }
    }

    public void Cancel(BoosterType type)
    {
        IBooster booster = GetBooster(type);
        if (booster != null)
        {
            booster.Cancel();
        }
    }

    /// <summary>Clears busy/selecting state. Does not change charge inventories.</summary>
    public void ResetAll(string reason = null)
    {
        RefreshRegistry();
        for (int i = 0; i < boosters.Count; i++)
        {
            if (boosters[i] != null)
            {
                boosters[i].ResetState(reason);
            }
        }

        BoosterFeedbackMessage.HideExisting(true);
    }

    /// <summary>Routes a board tap to the booster currently in targeting mode.</summary>
    public bool TryHandleSelectionPress(Block block)
    {
        EnsureRegistry();
        for (int i = 0; i < boosters.Count; i++)
        {
            IBooster booster = boosters[i];
            if (booster != null && booster.IsSelecting)
            {
                return booster.TryHandleBlockSelection(block);
            }
        }

        return false;
    }

    private void EnsureRegistry()
    {
        if (!registryReady)
        {
            RefreshRegistry();
        }
    }

    private bool IsOtherBoosterBusy(IBooster self)
    {
        EnsureRegistry();
        for (int i = 0; i < boosters.Count; i++)
        {
            IBooster booster = boosters[i];
            if (booster != null && booster != self && booster.IsBusy)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureMagnet()
    {
        if (GetComponent<MagnetBooster>() != null)
        {
            return;
        }

        if (FindFirstObjectByType<MagnetBooster>() != null)
        {
            return;
        }

        gameObject.AddComponent<MagnetBooster>();
    }

    private void EnsureHammer()
    {
        if (GetComponent<HammerBooster>() != null)
        {
            return;
        }

        if (FindFirstObjectByType<HammerBooster>() != null)
        {
            return;
        }

        gameObject.AddComponent<HammerBooster>();
    }

    private void EnsureShuffle()
    {
        if (GetComponent<ShuffleBooster>() != null)
        {
            return;
        }

        if (FindFirstObjectByType<ShuffleBooster>() != null)
        {
            return;
        }

        gameObject.AddComponent<ShuffleBooster>();
    }

    private void EnsureUndo()
    {
        if (GetComponent<BoardUndoHistory>() == null && FindFirstObjectByType<BoardUndoHistory>() == null)
        {
            gameObject.AddComponent<BoardUndoHistory>();
        }

        if (GetComponent<UndoBooster>() != null)
        {
            return;
        }

        if (FindFirstObjectByType<UndoBooster>() != null)
        {
            return;
        }

        gameObject.AddComponent<UndoBooster>();
    }

    private void RefreshRegistry()
    {
        boosters.Clear();
        MonoBehaviour[] found = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] is IBooster booster)
            {
                boosters.Add(booster);
            }
        }

        registryReady = true;
    }
}
