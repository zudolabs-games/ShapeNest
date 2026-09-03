using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase 71B Play Mode probe — nested inner stays at SOURCE while outer travels.
/// Diagnostic only. Does not change matching, grouping, or Level 43 data.
/// </summary>
public sealed class Phase71BPlayProbe : MonoBehaviour
{
    private const string ReportPath = "Captures/phase71b-play-report.txt";

    public bool Done { get; private set; }
    public string Result { get; private set; }

    public void BeginOrangeMixed()
    {
        Done = false;
        Result = null;
        enabled = true;
        DontDestroyOnLoad(gameObject);
        StopAllCoroutines();
        StartCoroutine(RunOrangeMixedOnly());
    }

    private IEnumerator RunOrangeMixedOnly()
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine("PHASE 71B — ORANGE MIXED PLAY");
        LevelManager lm = Object.FindFirstObjectByType<LevelManager>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (lm == null || board == null)
        {
            Finish(sb, false, "missing lm/board");
            yield break;
        }

        yield return RunOrangeMixedMatch(sb, lm, board);
    }

    private IEnumerator Run()
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine("PHASE 71B — PLAY MODE NESTED EXTRACTION");
        sb.AppendLine("======================================");

        LevelManager lm = Object.FindFirstObjectByType<LevelManager>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (lm == null || board == null)
        {
            Finish(sb, false, "missing lm/board");
            yield break;
        }

        if (lm.CurrentLevel == null || lm.CurrentLevel.name != "Campaign_43_Reference")
        {
            lm.LoadLevel(42);
            yield return null;
            yield return null;
        }

        Block nested = FindNested(ShapeType.Pentagon, ShapeColor.Orange);
        if (nested == null)
        {
            Finish(sb, false, "orange nested pentagon not found");
            yield break;
        }

        int cellIndex = FirstInnerCell(nested);
        PieceView3D outer = nested.GetWorldViewForCellIndex(cellIndex);
        if (outer == null || cellIndex < 0)
        {
            Finish(sb, false, "no outer view");
            yield break;
        }

        Vector2Int source = nested.GetCellWorld(cellIndex);
        Transform nestedBefore = outer.transform.Find("NestedInner3D");
        int innerIdBefore = nestedBefore != null ? nestedBefore.GetInstanceID() : 0;
        Vector3 innerStart = nestedBefore != null ? nestedBefore.position : Vector3.zero;
        Vector3 outerStart = outer.transform.position;

        sb.AppendLine(
            $"block={nested.GetInstanceID()} cell={cellIndex} source={source} " +
            $"outer={outer.GetInstanceID()} innerChild={innerIdBefore}");
        sb.AppendLine($"before innerPos={innerStart:F3} outerPos={outerStart:F3}");

        Transform residual = BoardPresentationController.DetachAndAnchorNestedInner(nested, cellIndex);
        outer.BeginMotionLock();
        BoardPresentationController.BeginChainCellTravel(nested, outer, cellIndex);
        yield return null;

        bool detached = residual != null;
        bool notChild = outer.transform.Find("NestedInner3D") == null;
        int innerIdAfter = residual != null ? residual.GetInstanceID() : 0;
        bool sameObject = innerIdBefore != 0 && innerIdBefore == innerIdAfter;

        Vector3 outerMoved = outerStart + new Vector3(1.5f, 0f, 2.5f);
        outer.transform.position = outerMoved;
        yield return null;
        yield return null;

        Vector3 innerDuring = residual != null ? residual.position : Vector3.positiveInfinity;
        Vector3 innerIfFollowed = innerStart + (outerMoved - outerStart);
        float followError = Vector3.Distance(
            new Vector3(innerDuring.x, 0f, innerDuring.z),
            new Vector3(innerIfFollowed.x, 0f, innerIfFollowed.z));
        float xzDrift = Vector2.Distance(
            new Vector2(innerDuring.x, innerDuring.z),
            new Vector2(innerStart.x, innerStart.z));
        float outerMovedDist = Vector3.Distance(outer.transform.position, outerStart);
        bool innerStayed = detached && followError > 0.75f && xzDrift < 0.35f;
        bool outerMovedOk = outerMovedDist > 0.5f;

        sb.AppendLine($"detached={detached} sameObject={sameObject} notChildOfOuter={notChild}");
        sb.AppendLine(
            $"during innerPos={innerDuring:F3} outerPos={outer.transform.position:F3} " +
            $"innerXZDrift={xzDrift:F4} followError={followError:F3} outerMoved={outerMovedOk}");

        bool pass = detached && notChild && sameObject && innerStayed && outerMovedOk;
        sb.AppendLine(
            pass
                ? "INNER stayed at SOURCE while OUTER world position changed."
                : "FAIL: inner did not remain independent at source.");

        BoardPresentationController.NotifyChainCellTravelCleared(nested);
        outer.EndMotionLock();

        if (!pass)
        {
            Finish(sb, false, "FAIL detach");
            yield break;
        }

        yield return RunOrangeMixedMatch(sb, lm, board);
    }

    private IEnumerator RunOrangeMixedMatch(StringBuilder sb, LevelManager lm, BoardManager board)
    {
        sb.AppendLine();
        sb.AppendLine("--- ORANGE MIXED SUBSET ---");
        lm.LoadLevel(42);
        yield return null;
        yield return null;

        Block orange = FindNested(ShapeType.Pentagon, ShapeColor.Orange);
        if (orange == null || orange.CellCount != 3)
        {
            Finish(sb, false, "orange 3-cell missing after reload");
            yield break;
        }

        Block[] all = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Block other = all[i];
            if (other == null || other == orange || other.IsSettled)
            {
                continue;
            }

            board.UnregisterBlock(other);
        }

        Vector2Int sourceAnchor = new Vector2Int(4, 3);
        board.UnregisterBlock(orange);
        orange.SetGridPosition(sourceAnchor);
        board.TryRegisterBlock(orange, sourceAnchor);
        yield return null;

        Vector2Int innerA = orange.GetCellWorld(1);
        Vector2Int innerB = orange.GetCellWorld(2);
        Vector2Int targetA = new Vector2Int(5, 4);
        Vector2Int targetB = new Vector2Int(5, 5);
        sb.AppendLine("sourceAnchor=" + sourceAnchor + " innerCells=" + innerA + "," + innerB);

        BlockMover mover = orange.GetComponent<BlockMover>();
        mover.StartCoroutine(mover.PlayResolvedAutoMatch(board, new Vector2Int(5, 3)));

        int frames = 0;
        while (frames < 400 && (mover.IsMoving || orange.HasPendingLayerExtraction))
        {
            frames++;
            yield return null;
        }

        yield return null;
        yield return null;
        sb.AppendLine("waitFrames=" + frames);

        sb.AppendLine(
            "after cells=" + orange.CellCount + " grid=" + orange.GridPosition +
            " settled=" + orange.IsSettled + " pending=" + orange.HasPendingLayerExtraction);

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        IGridSpace space = presenter != null ? presenter.GridSpace : null;

        bool anyInnerAtTarget = false;
        bool cyanAtSource = false;
        bool stillNestedOrange = false;
        foreach (Block live in Object.FindObjectsByType<Block>(FindObjectsSortMode.None))
        {
            if (live == null || live.IsSettled)
            {
                continue;
            }

            for (int c = 0; c < live.CellCount; c++)
            {
                MatchIdentity id = live.GetActiveIdentity(c);
                Vector2Int world = live.GetCellWorld(c);
                PieceView3D view = live.GetWorldViewForCellIndex(c);
                Vector2Int viewCell = world;
                if (view != null && space != null)
                {
                    viewCell = space.WorldToGrid(view.transform.position);
                }

                sb.AppendLine(
                    "  live " + id + " world=" + world + " viewCell=" + viewCell +
                    " hasInner=" + live.HasInnerLayerAt(c) + " active=" +
                    (view != null && view.gameObject.activeSelf));

                if (id.Shape == ShapeType.Pentagon && id.Color == ShapeColor.Orange && live.HasInnerLayerAt(c))
                {
                    stillNestedOrange = true;
                }

                if (id.Color == ShapeColor.Cyan && (world == innerA || world == innerB))
                {
                    cyanAtSource = true;
                }

                if (view != null && (viewCell == targetA || viewCell == targetB)
                    && id.Color == ShapeColor.Cyan)
                {
                    anyInnerAtTarget = true;
                }
            }
        }

        bool targetsGone = board.GetTargetAt(new Vector2Int(5, 3)) == null
            && board.GetTargetAt(targetA) == null
            && board.GetTargetAt(targetB) == null;
        bool pass = targetsGone && cyanAtSource && !anyInnerAtTarget && !stillNestedOrange;
        sb.AppendLine(
            "targetsGone=" + targetsGone + " cyanAtSource=" + cyanAtSource +
            " innerAtTarget=" + anyInnerAtTarget + " stillNestedOrange=" + stillNestedOrange);
        Finish(sb, pass, pass ? "PASS mixed" : "FAIL mixed");
    }

    private static Block FindNested(ShapeType shape, ShapeColor color)
    {
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block b = blocks[i];
            if (b == null || b.IsSettled)
            {
                continue;
            }

            for (int c = 0; c < b.CellCount; c++)
            {
                MatchIdentity id = b.GetActiveIdentity(c);
                if (id.Shape == shape && id.Color == color && b.HasInnerLayerAt(c))
                {
                    return b;
                }
            }
        }

        return null;
    }

    private static int FirstInnerCell(Block block)
    {
        for (int i = 0; i < block.CellCount; i++)
        {
            if (block.HasInnerLayerAt(i))
            {
                return i;
            }
        }

        return -1;
    }

    private void Finish(StringBuilder sb, bool pass, string tag)
    {
        sb.AppendLine($"RESULT: {(pass ? "PASS" : "FAIL")} ({tag})");
        Result = sb.ToString();
        Done = true;
        try
        {
            File.WriteAllText(ReportPath, Result);
        }
        catch
        {
            // ignore IO
        }

        if (pass)
        {
            Debug.Log("[71B][PLAY]\n" + Result);
        }
        else
        {
            Debug.LogError("[71B][PLAY]\n" + Result);
        }
    }
}
