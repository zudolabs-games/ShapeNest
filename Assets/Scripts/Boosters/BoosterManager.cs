using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates boosters. Does not move blocks, match, or own occupancy.
/// Magnet gameplay stays on <see cref="MagnetBooster"/> / <see cref="BlockMover"/>.
/// Hammer gameplay stays on <see cref="HammerBooster"/>.
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
    /// </summary>
    public bool TryActivate(BoosterType type)
    {
        IBooster booster = GetBooster(type);
        if (booster == null || !booster.CanActivate || IsOtherBoosterBusy(booster))
        {
            return false;
        }

        booster.Activate();
        return true;
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
