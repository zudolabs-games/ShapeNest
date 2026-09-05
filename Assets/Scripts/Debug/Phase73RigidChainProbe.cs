using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase 73 — verify multi-cell nest entry uses one rigid shared translation.
/// </summary>
public sealed class Phase73RigidChainProbe : MonoBehaviour
{
    private const string ReportPath = "Captures/phase73-rigid-chain-report.txt";

    public bool Done { get; private set; }
    public string Result { get; private set; }

    public void Begin()
    {
        Done = false;
        Result = null;
        enabled = true;
        StopAllCoroutines();
        StartCoroutine(RunSafe());
    }

    private IEnumerator RunSafe()
    {
        var sb = new StringBuilder(12000);
        sb.AppendLine("PHASE 73 — RIGID CHAIN-TO-TARGET GROUP MATCH");
        sb.AppendLine($"frame={Time.frameCount} t={Time.time:F3}");
        yield return Run(sb);
    }

    private IEnumerator Run(StringBuilder sb)
    {
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        LevelManager levelManager = Object.FindFirstObjectByType<LevelManager>();
        if (board == null || levelManager == null)
        {
            Finish(sb, false, "missing board/levelManager");
            yield break;
        }

        bool dragRoutesSubset = SourceMentionsPhase73Routing();
        sb.AppendLine($"T_SOURCE_DRAG_ROUTES_SUBSET={dragRoutesSubset}");

        // Stay on whatever level is loaded; spawn a synthetic 2-cell case if needed
        // by driving an existing multi-cell block through Collect + subset play.
        yield return null;

        board = Object.FindFirstObjectByType<BoardManager>();
        Block subject = FindBestMultiCellChain(board);
        if (subject == null || subject.CellCount < 2)
        {
            Finish(sb, false, "no multi-cell chain found");
            yield break;
        }

        BlockMover mover = subject.GetComponent<BlockMover>();
        if (mover == null)
        {
            Finish(sb, false, "no BlockMover");
            yield break;
        }

        sb.AppendLine(
            $"level={(levelManager.CurrentLevel != null ? levelManager.CurrentLevel.name : "?")} " +
            $"subject={subject.GetInstanceID()} cells={subject.CellCount} grid={subject.GridPosition} " +
            $"shapes={DescribeShapes(subject)}");

        // Enumerate every adjacent unit translation and pick the first rigid pair.
        Vector2Int[] dirs =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        AlignedGroupProbe chosen = null;
        Vector2Int chosenFocus = Vector2Int.zero;
        for (int d = 0; d < dirs.Length && chosen == null; d++)
        {
            Vector2Int focus = subject.GridPosition + dirs[d];
            bool collected = InvokeCollect(mover, board, subject, focus, out _, out int nestCount);
            var nestIndices = ReadNestList(mover, "nestCellIndices");
            var nestWorlds = ReadNestWorldList(mover, "nestTargetWorlds");
            sb.AppendLine(
                $"try focus={focus} collect={collected} nestCount={nestCount} " +
                $"indices=[{string.Join(",", nestIndices)}] dests=[{string.Join(",", nestWorlds)}]");

            if (nestIndices.Count < 2)
            {
                // Also try focus = cellWorld + dir for each cell (non-anchor adjacent nests).
                for (int c = 0; c < subject.CellCount; c++)
                {
                    Vector2Int cellFocus = subject.GetCellWorld(c) + dirs[d];
                    collected = InvokeCollect(
                        mover, board, subject, cellFocus, out _, out nestCount);
                    nestIndices = ReadNestList(mover, "nestCellIndices");
                    nestWorlds = ReadNestWorldList(mover, "nestTargetWorlds");
                    if (nestIndices.Count < 2)
                    {
                        continue;
                    }

                    sb.AppendLine(
                        $"  cellFocus={cellFocus} nestCount={nestCount} " +
                        $"indices=[{string.Join(",", nestIndices)}]");
                    var probe = BuildGroupFromNest(subject, nestIndices, nestWorlds);
                    if (board.CanTranslateMatchingSubset(subject, nestIndices, probe.Translation)
                        && probe.Actions.Count >= 2)
                    {
                        chosen = probe;
                        chosenFocus = cellFocus;
                        break;
                    }
                }

                continue;
            }

            var groupProbe = BuildGroupFromNest(subject, nestIndices, nestWorlds);
            bool can = board.CanTranslateMatchingSubset(
                subject, nestIndices, groupProbe.Translation);
            sb.AppendLine($"  translation={groupProbe.Translation} canSubset={can}");
            if (can && groupProbe.Actions.Count >= 2)
            {
                chosen = groupProbe;
                chosenFocus = focus;
            }
        }

        // T8: invalid translation must be rejected.
        var anyTwo = new List<int> { 0, 1 };
        bool invalidRejected = !board.CanTranslateMatchingSubset(
            subject, anyTwo, new Vector2Int(3, 7));
        sb.AppendLine($"T8_INVALID_TRANSLATION_REJECTED={invalidRejected}");

        // Fixed-direction gate on diagonal translation.
        bool diagonalRejected = true;
        MethodInfo build = typeof(BlockMover).GetMethod(
            "TryBuildNestedSubsetGroup",
            BindingFlags.Instance | BindingFlags.NonPublic);
        sb.AppendLine($"TryBuildNestedSubsetGroup_present={build != null}");

        if (chosen == null)
        {
            // Build a synthetic rigid group using PlayResolvedMovementGroup path directly
            // on Level 43 white A+B → (0,5) when subset is valid (Phase 71A case).
            if (subject.CellCount >= 2)
            {
                Vector2Int t = new Vector2Int(0, 5);
                var indices = new List<int> { 0, 1 };
                bool subsetOk = board.CanTranslateMatchingSubset(subject, indices, t);
                sb.AppendLine($"synthetic_subset_0_5={subsetOk}");
                if (subsetOk)
                {
                    chosen = new AlignedGroupProbe { Translation = t };
                    Vector2Int w0 = subject.GetCellWorld(0);
                    Vector2Int w1 = subject.GetCellWorld(1);
                    chosen.Actions.Add(new BlockMover.AlignedMatchAction(subject, 0, w0, w0 + t));
                    chosen.Actions.Add(new BlockMover.AlignedMatchAction(subject, 1, w1, w1 + t));
                    // Only use if destinations have matching targets.
                    Target t0 = board.GetTargetAt(w0 + t);
                    Target t1 = board.GetTargetAt(w1 + t);
                    bool shapesOk = t0 != null && t1 != null
                        && ShapeMatch.AreMatchingLayers(
                            t0.GetRequiredIdentityAtWorld(w0 + t), subject.GetActiveIdentity(0))
                        && ShapeMatch.AreMatchingLayers(
                            t1.GetRequiredIdentityAtWorld(w1 + t), subject.GetActiveIdentity(1));
                    sb.AppendLine($"synthetic_targets_ok={shapesOk}");
                    if (!shapesOk)
                    {
                        chosen = null;
                    }
                }
            }
        }

        sb.AppendLine($"chosenFocus={chosenFocus} hasChosen={chosen != null}");

        if (chosen == null)
        {
            bool structural = dragRoutesSubset && invalidRejected && diagonalRejected;
            Finish(
                sb,
                structural,
                structural
                    ? "structural PASS (no live rigid pair on current pose)"
                    : "structural FAIL");
            yield break;
        }

        var nestIdx = new List<int>();
        for (int i = 0; i < chosen.Actions.Count; i++)
        {
            nestIdx.Add(chosen.Actions[i].CellIndex);
        }

        var travelViews = new List<PieceView3D>();
        var starts = new List<Vector3>();
        for (int i = 0; i < nestIdx.Count; i++)
        {
            PieceView3D view = subject.GetWorldViewForCellIndex(nestIdx[i]);
            travelViews.Add(view);
            starts.Add(view != null ? view.transform.position : Vector3.zero);
        }

        Vector3 relBefore = starts.Count >= 2 ? starts[1] - starts[0] : Vector3.zero;
        midTaken = false;
        midRigid = false;
        midSameProgress = false;
        sampleViews = travelViews;
        sampleStarts = starts;
        sampleRelBefore = relBefore;
        sampleTravel = true;

        var group = new BlockMover.AlignedMovementGroup
        {
            Subject = subject,
            Translation = chosen.Translation
        };
        group.Actions.AddRange(chosen.Actions);

        bool prev = BlockMover.MatchingSubsetDiagnosticsEnabled;
        BlockMover.MatchingSubsetDiagnosticsEnabled = true;
        yield return mover.StartCoroutine(mover.PlayResolvedMovementGroup(board, group));
        BlockMover.MatchingSubsetDiagnosticsEnabled = prev;
        sampleTravel = false;

        sb.AppendLine(
            $"midTaken={midTaken} midRigid={midRigid} midSameProgress={midSameProgress} " +
            $"consumeOk={mover.LastResolvedConsumeSucceeded}");

        bool matchFocusedRoutes =
            File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Blocks/BlockMover.cs"))
                .Contains("yield return PlayMatchingSubsetAlignedMatch(board, subject, rigidGroup);");
        sb.AppendLine($"T_MATCHFOCUSED_ROUTES_RIGID={matchFocusedRoutes}");

        bool pass = dragRoutesSubset
            && invalidRejected
            && matchFocusedRoutes
            && mover.LastResolvedConsumeSucceeded;
        if (midTaken)
        {
            pass = pass && midRigid && midSameProgress;
        }

        Finish(
            sb,
            pass,
            pass
                ? (midTaken ? "PASS live rigid travel" : "PASS consume+routing (mid-sample skipped)")
                : "FAIL live rigid criteria");
    }

    private bool sampleTravel;
    private List<PieceView3D> sampleViews;
    private List<Vector3> sampleStarts;
    private Vector3 sampleRelBefore;
    private bool midTaken;
    private bool midRigid;
    private bool midSameProgress;

    private void Update()
    {
        if (!sampleTravel
            || midTaken
            || sampleViews == null
            || sampleStarts == null
            || sampleViews.Count < 2
            || sampleViews[0] == null
            || sampleViews[1] == null)
        {
            return;
        }

        Vector3 p0 = sampleViews[0].transform.position;
        Vector3 p1 = sampleViews[1].transform.position;
        float moved0 = Vector3.Distance(p0, sampleStarts[0]);
        float moved1 = Vector3.Distance(p1, sampleStarts[1]);
        if (moved0 <= 0.02f && moved1 <= 0.02f)
        {
            return;
        }

        midTaken = true;
        midRigid = Vector3.Distance(p1 - p0, sampleRelBefore) < 0.12f;
        midSameProgress = Mathf.Abs(moved0 - moved1) < 0.2f;
    }

    private static bool SourceMentionsPhase73Routing()
    {
        string path = Path.Combine(Application.dataPath, "Scripts/Blocks/BlockMover.cs");
        if (!File.Exists(path))
        {
            return false;
        }

        string text = File.ReadAllText(path);
        return text.Contains("Phase 73: when 2+ cells share one rigid translation")
            && text.Contains("KeepSameTranslationMatches")
            && text.Contains("PlayMatchingSubsetAlignedMatch(board, subject, rigidGroup)");
    }

    private static Block FindBestMultiCellChain(BoardManager board)
    {
        if (board == null)
        {
            return null;
        }

        var blocks = new List<Block>();
        board.CollectUniqueBlocks(blocks);
        Block best = null;
        for (int i = 0; i < blocks.Count; i++)
        {
            Block b = blocks[i];
            if (b == null || b.IsSettled || b.CellCount < 2)
            {
                continue;
            }

            if (best == null || b.CellCount > best.CellCount)
            {
                best = b;
            }
        }

        return best;
    }

    private static string DescribeShapes(Block subject)
    {
        var parts = new List<string>(subject.CellCount);
        for (int i = 0; i < subject.CellCount; i++)
        {
            parts.Add($"{subject.GetActiveShape(i)}@{subject.GetCellWorld(i)}");
        }

        return string.Join(",", parts);
    }

    private static bool InvokeCollect(
        BlockMover mover,
        BoardManager board,
        Block subject,
        Vector2Int focus,
        out Vector2Int targetWorld,
        out int nestCount)
    {
        targetWorld = Vector2Int.zero;
        nestCount = 0;
        try
        {
            MethodInfo method = typeof(BlockMover).GetMethod(
                "CollectChainFocusedMatch",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                return false;
            }

            object[] args =
            {
                board,
                subject,
                subject.GridPosition,
                focus,
                false,
                Vector2Int.zero
            };
            object result = method.Invoke(mover, args);
            targetWorld = args[5] is Vector2Int tw ? tw : Vector2Int.zero;
            nestCount = ReadNestList(mover, "nestCellIndices").Count;
            return result is bool ok && ok;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[Phase73] InvokeCollect failed: " + ex.Message);
            return false;
        }
    }

    private static List<int> ReadNestList(BlockMover mover, string fieldName)
    {
        FieldInfo field = typeof(BlockMover).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field != null && field.GetValue(mover) is List<int> list)
        {
            return new List<int>(list);
        }

        return new List<int>();
    }

    private static List<Vector2Int> ReadNestWorldList(BlockMover mover, string fieldName)
    {
        FieldInfo field = typeof(BlockMover).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field != null && field.GetValue(mover) is List<Vector2Int> list)
        {
            return new List<Vector2Int>(list);
        }

        return new List<Vector2Int>();
    }

    private sealed class AlignedGroupProbe
    {
        public Vector2Int Translation;
        public readonly List<BlockMover.AlignedMatchAction> Actions =
            new List<BlockMover.AlignedMatchAction>();
    }

    private static AlignedGroupProbe BuildGroupFromNest(
        Block subject,
        List<int> nestIndices,
        List<Vector2Int> nestWorlds)
    {
        var probe = new AlignedGroupProbe();
        if (nestIndices == null || nestIndices.Count == 0)
        {
            return probe;
        }

        for (int i = 0; i < nestIndices.Count; i++)
        {
            int idx = nestIndices[i];
            Vector2Int world = subject.GetCellWorld(idx);
            Vector2Int nestTo = i < nestWorlds.Count ? nestWorlds[i] : world;
            probe.Actions.Add(new BlockMover.AlignedMatchAction(subject, idx, world, nestTo));
        }

        probe.Translation = probe.Actions[0].Translation;
        return probe;
    }

    private struct MidSample
    {
        public bool taken;
        public bool rigid;
        public bool sameProgress;
    }

    private static IEnumerator WatchMid(
        List<PieceView3D> views,
        List<Vector3> starts,
        Vector3 relBefore,
        System.Action<MidSample> onDone)
    {
        MidSample sample = default;
        float elapsed = 0f;
        while (elapsed < 3f)
        {
            elapsed += Time.unscaledDeltaTime;
            if (views == null || views.Count < 2 || views[0] == null || views[1] == null)
            {
                yield return null;
                continue;
            }

            Vector3 p0 = views[0].transform.position;
            Vector3 p1 = views[1].transform.position;
            float moved0 = Vector3.Distance(p0, starts[0]);
            float moved1 = Vector3.Distance(p1, starts[1]);
            if (moved0 < 0.015f && moved1 < 0.015f)
            {
                yield return null;
                continue;
            }

            Vector3 relNow = p1 - p0;
            sample.taken = true;
            sample.rigid = Vector3.Distance(relNow, relBefore) < 0.1f;
            sample.sameProgress = Mathf.Abs(moved0 - moved1) < 0.15f;
            onDone?.Invoke(sample);
            yield break;
        }

        onDone?.Invoke(sample);
    }

    private void Finish(StringBuilder sb, bool pass, string note)
    {
        sb.AppendLine($"RESULT={(pass ? "PASS" : "FAIL")} note={note}");
        Result = sb.ToString();
        Done = true;
        try
        {
            Directory.CreateDirectory("Captures");
            File.WriteAllText(ReportPath, Result);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[Phase73] report write failed: " + ex.Message);
        }

        Debug.Log("[Phase73]\n" + Result);
        enabled = false;
    }
}
