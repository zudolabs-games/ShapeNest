using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// TEMP Phase 70B Play Mode probe for Level 43 white-triangle matching subset.
/// Uses the same StartCoroutine nesting pattern as LevelManager.
/// </summary>
public sealed class Phase70BPlayProbe : MonoBehaviour
{
    private const string ReportPath = "Captures/phase70b-play-report.txt";

    public bool Done { get; private set; }
    public string Result { get; private set; }

    public void Begin()
    {
        Done = false;
        Result = null;
        enabled = true;
        StopAllCoroutines();
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        var sb = new StringBuilder();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        LevelManager levelManager = Object.FindFirstObjectByType<LevelManager>();
        if (board == null || levelManager == null)
        {
            Finish(sb, false, "missing board/levelManager");
            yield break;
        }

        if (levelManager.CurrentLevel == null
            || levelManager.CurrentLevel.name != "Campaign_43_Reference")
        {
            levelManager.LoadLevel(42);
            yield return null;
            yield return null;
            float wait = 0f;
            while (wait < 2f)
            {
                Block found = FindWhiteTriangle();
                if (found != null && found.CellCount == 4)
                {
                    break;
                }

                wait += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        Block whiteTri = FindWhiteTriangle();
        if (whiteTri == null || whiteTri.CellCount != 4)
        {
            Finish(sb, false, "white triangle 4-cell not found");
            yield break;
        }

        sb.AppendLine($"block cells={whiteTri.CellCount} grid={whiteTri.GridPosition}");

        var indices = new List<int> { 0, 1 };
        Vector2Int translation = new Vector2Int(0, 5);
        bool subsetOk = board.CanTranslateMatchingSubset(whiteTri, indices, translation);
        bool wholeOk = board.CanTranslateBlock(whiteTri, whiteTri.GridPosition + translation);
        sb.AppendLine($"subset01={subsetOk} whole={wholeOk}");

        if (!subsetOk || wholeOk)
        {
            Finish(sb, false, "subset/whole distinction failed");
            yield break;
        }

        PieceView3D sibling0 = whiteTri.GetWorldViewForCellIndex(2);
        PieceView3D sibling1 = whiteTri.GetWorldViewForCellIndex(3);
        Vector3 siblingWorld0 = sibling0 != null ? sibling0.transform.position : Vector3.zero;
        Vector3 siblingWorld1 = sibling1 != null ? sibling1.transform.position : Vector3.zero;
        Vector2Int siblingLogical0 = whiteTri.GetCellWorld(2);
        Vector2Int siblingLogical1 = whiteTri.GetCellWorld(3);

        PieceView3D travel0 = whiteTri.GetWorldViewForCellIndex(0);
        PieceView3D travel1 = whiteTri.GetWorldViewForCellIndex(1);
        Vector3 travelStart0 = travel0 != null ? travel0.transform.position : Vector3.zero;
        Vector3 travelStart1 = travel1 != null ? travel1.transform.position : Vector3.zero;
        Vector3 relativeBefore = travelStart1 - travelStart0;

        BlockMover mover = whiteTri.GetComponent<BlockMover>();
        if (mover == null)
        {
            Finish(sb, false, "no BlockMover");
            yield break;
        }

        bool prevDiag = BlockMover.MatchingSubsetDiagnosticsEnabled;
        BlockMover.MatchingSubsetDiagnosticsEnabled = true;

        var group = new BlockMover.AlignedMovementGroup
        {
            Subject = whiteTri,
            Translation = translation
        };
        group.Actions.Add(
            new BlockMover.AlignedMatchAction(
                whiteTri, 0, new Vector2Int(1, 4), new Vector2Int(1, 9)));
        group.Actions.Add(
            new BlockMover.AlignedMatchAction(
                whiteTri, 1, new Vector2Int(2, 4), new Vector2Int(2, 9)));

        bool midSampleTaken = false;
        bool midSiblingsStill = false;
        bool midRelativeRigid = false;
        Coroutine midWatch = StartCoroutine(WatchMid(
            travel0,
            travel1,
            sibling0,
            sibling1,
            siblingWorld0,
            siblingWorld1,
            relativeBefore,
            v =>
            {
                midSampleTaken = true;
                midSiblingsStill = v.siblingsStill;
                midRelativeRigid = v.rigid;
                sb.AppendLine(
                    $"midSample siblingsStill={v.siblingsStill} rigid={v.rigid}");
            }));

        // Production nesting: StartCoroutine on the acting BlockMover.
        yield return mover.StartCoroutine(mover.PlayResolvedMovementGroup(board, group));
        if (midWatch != null)
        {
            StopCoroutine(midWatch);
        }

        yield return null;
        yield return null;

        BlockMover.MatchingSubsetDiagnosticsEnabled = prevDiag;

        Block survivor = FindWhiteTriangle();
        bool siblingsStayLogical = false;
        if (survivor != null && survivor.CellCount == 2)
        {
            var set = new HashSet<Vector2Int>();
            for (int i = 0; i < survivor.CellCount; i++)
            {
                set.Add(survivor.GetCellWorld(i));
            }

            siblingsStayLogical =
                set.Contains(siblingLogical0) && set.Contains(siblingLogical1);
        }

        bool noReq19 = CellHasNoMatchingRequirement(board, new Vector2Int(1, 9));
        bool noReq29 = CellHasNoMatchingRequirement(board, new Vector2Int(2, 9));
        bool targetsConsumed = noReq19 && noReq29;

        sb.AppendLine(
            $"survivorCells={(survivor != null ? survivor.CellCount : -1)} " +
            $"siblingsStayLogical={siblingsStayLogical}");
        sb.AppendLine($"targetsConsumed={targetsConsumed} (19={noReq19} 29={noReq29})");
        sb.AppendLine($"consumeSucceeded={mover.LastResolvedConsumeSucceeded}");

        bool pass = subsetOk
            && !wholeOk
            && siblingsStayLogical
            && targetsConsumed
            && mover.LastResolvedConsumeSucceeded
            && (!midSampleTaken || (midSiblingsStill && midRelativeRigid));

        Finish(sb, pass, pass ? "PASS" : "FAIL");
    }

    private static IEnumerator WatchMid(
        PieceView3D travel0,
        PieceView3D travel1,
        PieceView3D sibling0,
        PieceView3D sibling1,
        Vector3 siblingWorld0,
        Vector3 siblingWorld1,
        Vector3 relativeBefore,
        System.Action<(bool siblingsStill, bool rigid)> onSample)
    {
        float t = 0f;
        while (t < 2f)
        {
            t += Time.unscaledDeltaTime;
            if (t >= 0.2f
                && travel0 != null
                && travel1 != null
                && sibling0 != null
                && sibling1 != null)
            {
                bool siblingsStill =
                    (sibling0.transform.position - siblingWorld0).sqrMagnitude < 0.0004f
                    && (sibling1.transform.position - siblingWorld1).sqrMagnitude < 0.0004f;
                Vector3 rel = travel1.transform.position - travel0.transform.position;
                bool rigid = (rel - relativeBefore).sqrMagnitude < 0.05f;
                onSample?.Invoke((siblingsStill, rigid));
                yield break;
            }

            yield return null;
        }
    }

    private static bool CellHasNoMatchingRequirement(BoardManager board, Vector2Int cell)
    {
        Target t = board.GetTargetAt(cell);
        return t == null || t.FindCellIndexAtWorld(cell) < 0;
    }

    private static Block FindWhiteTriangle()
    {
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block b = blocks[i];
            if (b == null || b.IsSettled || b.CellCount < 2)
            {
                continue;
            }

            bool ok = true;
            for (int c = 0; c < b.CellCount; c++)
            {
                MatchIdentity id = b.GetActiveIdentity(c);
                if (id.Shape != ShapeType.Triangle || id.Color != ShapeColor.White)
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                return b;
            }
        }

        return null;
    }

    private void Finish(StringBuilder sb, bool pass, string tag)
    {
        sb.AppendLine($"RESULT: {(pass ? "PASS" : "FAIL")} ({tag})");
        Result = sb.ToString();
        Done = true;
        try
        {
            string dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(ReportPath, Result);
        }
        catch
        {
            // ignore IO
        }

        if (pass)
        {
            Debug.Log("[70B][PLAY]\n" + Result);
        }
        else
        {
            Debug.LogError("[70B][PLAY]\n" + Result);
        }
    }
}
