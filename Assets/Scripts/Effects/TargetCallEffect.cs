using UnityEngine;

/// <summary>
/// Phase 79K/79L: presentation-only directional target hint when a valid matching block is
/// exactly one logical cell away. Does NOT modify nest Mesh / SocketCavity / target root.
/// Drives <see cref="TargetDirectionHintEffect"/> (3 looping curved strokes).
/// Coverage uses gameplay CollectNestMatches cells — no shape hard-coding.
/// </summary>
[DisallowMultipleComponent]
public sealed class TargetCallEffect : MonoBehaviour
{
    public enum CallState
    {
        Inactive,
        Calling,
        Completed
    }

    private Target target;
    private Vector2Int activeTowardBlock;
    private Vector2Int activeMatchWorldCell;
    private bool hasMatchWorldCell;

    public CallState State { get; private set; } = CallState.Inactive;

    public Vector2Int ActiveTowardBlock => activeTowardBlock;

    public bool IsPlaying => State == CallState.Calling;

    private readonly System.Collections.Generic.Dictionary<
    string,
    TargetDirectionHintEffect> pairHints =
    new System.Collections.Generic.Dictionary<
        string,
        TargetDirectionHintEffect>();

    private readonly System.Collections.Generic.HashSet<string> activePairKeys =
    new System.Collections.Generic.HashSet<string>();

    public static TargetCallEffect Ensure(Target nest)
    {
        if (nest == null)
        {
            return null;
        }

        TargetCallEffect effect = nest.GetComponent<TargetCallEffect>();
        if (effect == null)
        {
            effect = nest.gameObject.AddComponent<TargetCallEffect>();
        }

        effect.target = nest;
        return effect;
    }

    /// <summary>
    /// Shows or refreshes the looping directional hint. Safe to call every frame while eligible.
    /// <paramref name="gridDirectionTowardBlock"/> is the cardinal from nest toward block.
    /// </summary>
    public void TryPlay(Vector2Int gridDirectionTowardBlock)
    {
        TryPlay(
            gridDirectionTowardBlock,
            null,
            default,
            default,
            false,
            null);
    }
    public void TryPlay(Vector2Int gridDirectionTowardBlock, Block block)
    {
        Vector2Int matchCell = target != null ? target.GridPosition : default;
        Vector2Int sourceCell = block != null ? block.GridPosition : default;

        TryPlay(
            gridDirectionTowardBlock,
            block,
            matchCell,
            sourceCell,
            target != null,
            block);
    }

    /// <summary>
    /// Phase 79L: <paramref name="matchWorldCell"/> is the board cell CollectNestMatches
    /// reported as matching — used for World3D placement and accent (not Canvas transform).
    /// </summary>
    public void TryPlay(Vector2Int gridDirectionTowardBlock, Block block, Vector2Int matchWorldCell)
    {
        Vector2Int sourceCell = block != null ? block.GridPosition : default;

        TryPlay(
            gridDirectionTowardBlock,
            block,
            matchWorldCell,
            sourceCell,
            true,
            block);
    }

    /// <summary>
    /// Presentation-only overload for multi-block hint sessions. <paramref name="block"/>
    /// is the block whose matching cell is being visualized; <paramref name="sessionOwner"/>
    /// is the block whose active drag owns the hint lifetime.
    /// </summary>
    public void TryPlay(
    Vector2Int gridDirectionTowardBlock,
    Block block,
    Vector2Int matchWorldCell,
    Vector2Int sourceWorldCell,
    Block sessionOwner)
    {
        TryPlay(
            gridDirectionTowardBlock,
            block,
            matchWorldCell,
            sourceWorldCell,
            true,
            sessionOwner);
    }

    private void TryPlay(
    Vector2Int gridDirectionTowardBlock,
    Block block,
    Vector2Int matchWorldCell,
    Vector2Int sourceWorldCell,
    bool hasMatchCell,
    Block sessionOwner)
    {
        if (target == null)
        {
            target = GetComponent<Target>();
        }

        if (target == null
            || !isActiveAndEnabled
            || target.IsMatched
            || !target.HasLiveNestCells
            || target.IsMatchPresentationActive)
        {

            return;
        }

        if (gridDirectionTowardBlock == Vector2Int.zero
            || (Mathf.Abs(gridDirectionTowardBlock.x) + Mathf.Abs(gridDirectionTowardBlock.y)) != 1)
        {
            return;
        }

        activeTowardBlock = gridDirectionTowardBlock;
        activeMatchWorldCell = hasMatchCell ? matchWorldCell : target.GridPosition;
        hasMatchWorldCell = true;
        Color accent = ResolveAccentColor(target, activeMatchWorldCell);

        ClearLegacyCavityOffsets();

        string pairKey =
    sourceWorldCell.x + ":" +
    sourceWorldCell.y + ":" +
    matchWorldCell.x + ":" +
    matchWorldCell.y;

        activePairKeys.Add(pairKey);

        TargetDirectionHintEffect hint = GetOrCreateHint(sourceWorldCell, matchWorldCell);
        if (hint == null)
        {
            return;
        }

        Block owner = sessionOwner != null ? sessionOwner : block;

        State = CallState.Calling;

        if (hint.IsVisible)
        {
            hint.Refresh(
                block,
                gridDirectionTowardBlock,
                accent,
                matchWorldCell,
                owner);
        }
        else
        {
            hint.Show(
                block,
                gridDirectionTowardBlock,
                accent,
                matchWorldCell,
                owner);
        }
    }

    public void BeginPairPresentationUpdate()
    {
        activePairKeys.Clear();
    }

    public void EndPairPresentationUpdate()
    {
        var staleKeys = new System.Collections.Generic.List<string>();

        foreach (var pair in pairHints)
        {
            if (!activePairKeys.Contains(pair.Key))
            {
                if (pair.Value != null)
                {
                    pair.Value.Hide();
                }

                staleKeys.Add(pair.Key);
            }
        }

        for (int i = 0; i < staleKeys.Count; i++)
        {
            pairHints.Remove(staleKeys[i]);
        }
    }

    public void ResetCall(string reason = "reset")
    {
        ClearLegacyCavityOffsets();
        foreach (var pair in pairHints)
        {
            if (pair.Value != null)
            {
                pair.Value.Hide();
            }
        }

        pairHints.Clear();
        activePairKeys.Clear();

        activeTowardBlock = Vector2Int.zero;
        activeMatchWorldCell = Vector2Int.zero;
        hasMatchWorldCell = false;
        State = CallState.Inactive;
    }

    public void StopForMatch()
    {
        ResetCall("match");
    }

    private void Awake()
    {
        target = GetComponent<Target>();
    }

    private void OnDisable()
    {
        ResetCall("disable");
    }

    private void OnDestroy()
    {
        ResetCall("destroy");
    }

    private void ClearLegacyCavityOffsets()
    {
        if (target == null)
        {
            return;
        }

        if (target.WorldView != null)
        {
            target.WorldView.ClearCallBeckonOffset();
        }

        var extras = target.ExtraWorldViews;
        if (extras == null)
        {
            return;
        }

        for (int i = 0; i < extras.Count; i++)
        {
            if (extras[i] != null)
            {
                extras[i].ClearCallBeckonOffset();
            }
        }
    }

    private static Color ResolveAccentColor(Target nest, Vector2Int matchWorldCell)
    {
        if (nest == null)
        {
            return Color.white;
        }

        MatchIdentity identity = nest.GetRequiredIdentityAtWorld(matchWorldCell);
        return ShapeVisuals3D.AccentColor(identity.Shape, identity.Color);
    }

    private TargetDirectionHintEffect GetOrCreateHint(
    Vector2Int sourceWorldCell,
    Vector2Int matchWorldCell)
    {
        string key =
            sourceWorldCell.x + ":" +
            sourceWorldCell.y + ":" +
            matchWorldCell.x + ":" +
            matchWorldCell.y;

        if (pairHints.TryGetValue(key, out TargetDirectionHintEffect existing)
            && existing != null)
        {
            return existing;
        }

        GameObject hintObject = new GameObject(
            $"TargetDirectionHint_{sourceWorldCell.x}_{sourceWorldCell.y}_{matchWorldCell.x}_{matchWorldCell.y}");

        hintObject.transform.SetParent(transform, false);

        TargetDirectionHintEffect created =
            hintObject.AddComponent<TargetDirectionHintEffect>();

        pairHints[key] = created;

        return created;
    }
}
