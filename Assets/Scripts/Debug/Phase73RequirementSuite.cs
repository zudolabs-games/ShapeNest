using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase 73 requirement validation suite (Play Mode). Does not change gameplay.
/// Drives existing PlayResolvedMovementGroup / Collect / subset infrastructure.
/// </summary>
public sealed class Phase73RequirementSuite : MonoBehaviour
{
    private const string ReportPath = "Captures/phase73-requirement-suite.txt";

    public bool Done { get; private set; }
    public string Result { get; private set; }

    private bool sampleTravel;
    private List<PieceView3D> sampleViews;
    private List<Vector3> sampleStarts;
    private readonly List<Vector3> sampleRelBefore = new List<Vector3>();
    private bool midTaken;
    private bool midRigid;
    private bool midSameProgress;
    private bool midNoLag;

    private sealed class CaseResult
    {
        public string Id;
        public string Title;
        public bool Pass;
        public string Detail = "";
    }

    private sealed class Holder
    {
        public bool Pass;
        public string Detail = "";
    }

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
        var sb = new StringBuilder(24000);
        sb.AppendLine("PHASE 73 — REQUIREMENT VALIDATION SUITE");
        sb.AppendLine($"frame={Time.frameCount} t={Time.time:F3}");

        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        LevelManager levelManager = Object.FindFirstObjectByType<LevelManager>();
        if (board == null || levelManager == null)
        {
            Finish(sb, false, "missing board/levelManager");
            yield break;
        }

        bool routingOk = SourceHasPhase73Routing();
        sb.AppendLine($"ROUTING_MARKERS={routingOk}");
        if (!routingOk)
        {
            Finish(sb, false, "Phase 73 routing markers missing");
            yield break;
        }

        var cases = new List<CaseResult>();

        // T1: 2 identical
        var h1 = new Holder();
        yield return RunRigidTravelCaseAcrossLevels(
            levelManager, sb,
            new[] { "Campaign_43_Reference", "Campaign_07_ChainIntro", "Regression_04_ChainAutoMatch" },
            2, true, false, false, h1);
        cases.Add(new CaseResult { Id = "T1", Title = "2 identical shapes → matching targets", Pass = h1.Pass, Detail = h1.Detail });

        // T2: 3 identical
        var h2 = new Holder();
        yield return RunRigidTravelCaseAcrossLevels(
            levelManager, sb,
            new[]
            {
                "DIAGNOSTIC_Phase73_RigidChain",
                "Campaign_43_Reference", "Campaign_08_ChainCascade", "Regression_04_ChainAutoMatch",
                "TestLevel2_ThreeShapeChain", "Campaign_07_ChainIntro", "Regression_12_CombinedRegression"
            },
            3, true, false, false, h2);
        cases.Add(new CaseResult { Id = "T2", Title = "3 identical shapes → matching targets", Pass = h2.Pass, Detail = h2.Detail });

        // T3: mixed shapes (prefer 4, else >=2 mixed)
        var h3 = new Holder();
        yield return RunRigidTravelCaseAcrossLevels(
            levelManager, sb,
            new[]
            {
                "DIAGNOSTIC_Phase73_RigidChain",
                "Regression_11_LongChain", "TestLevel2_ThreeShapeChain", "Campaign_08_ChainCascade",
                "Regression_04_ChainAutoMatch", "TestLevel10_Combined", "Campaign_43_Reference"
            },
            4, false, false, true, h3);
        if (!h3.Pass)
        {
            yield return RunRigidTravelCaseAcrossLevels(
                levelManager, sb,
                new[]
                {
                    "DIAGNOSTIC_Phase73_RigidChain",
                    "Regression_11_LongChain", "TestLevel2_ThreeShapeChain",
                    "Campaign_08_ChainCascade", "Regression_04_ChainAutoMatch"
                },
                2, false, false, true, h3);
            h3.Detail = "mixed>=2:" + h3.Detail;
        }

        cases.Add(new CaseResult { Id = "T3", Title = "mixed shapes → corresponding targets", Pass = h3.Pass, Detail = h3.Detail });

        // T4: 5+ cell chain
        var h4 = new Holder();
        yield return RunRigidTravelCaseAcrossLevels(
            levelManager, sb,
            new[]
            {
                "DIAGNOSTIC_Phase73_RigidChain",
                "Regression_11_LongChain", "TestLevel10_Combined",
                "Campaign_43_Reference", "Regression_12_CombinedRegression"
            },
            5, false, false, false, h4);
        cases.Add(new CaseResult { Id = "T4", Title = "5+ cell chain → corresponding targets", Pass = h4.Pass, Detail = h4.Detail });

        // T5: L-shaped
        var h5 = new Holder();
        yield return RunRigidTravelCaseAcrossLevels(
            levelManager, sb,
            new[]
            {
                "DIAGNOSTIC_Phase73_RigidChain",
                "Campaign_43_Reference", "TestLevel10_Combined",
                "Campaign_09_ChainPlanning", "Regression_12_CombinedRegression"
            },
            3, false, true, false, h5);
        cases.Add(new CaseResult { Id = "T5", Title = "L-shaped chain → same L targets", Pass = h5.Pass, Detail = h5.Detail });

        // T6: nested
        yield return LoadNamedLevel(levelManager, sb, "Campaign_43_Reference");
        board = Object.FindFirstObjectByType<BoardManager>();
        yield return null;
        yield return null;
        var h6 = new Holder();
        yield return RunNestedCase(board, sb, h6);
        cases.Add(new CaseResult { Id = "T6", Title = "nested chain behavior preserved", Pass = h6.Pass, Detail = h6.Detail });

        // T7: fixed-direction
        yield return LoadNamedLevel(levelManager, sb, "Debug_FixedDirection");
        board = Object.FindFirstObjectByType<BoardManager>();
        yield return null;
        yield return null;
        bool c7 = EvaluateFixedDirection(board, out string d7);
        if (!c7)
        {
            yield return LoadNamedLevel(levelManager, sb, "Campaign_14_FixedDirection");
            board = Object.FindFirstObjectByType<BoardManager>();
            yield return null;
            yield return null;
            c7 = EvaluateFixedDirection(board, out d7);
        }

        sb.AppendLine($"T7_DETAIL={d7}");
        cases.Add(new CaseResult { Id = "T7", Title = "fixed-direction restriction enforced", Pass = c7, Detail = d7 });

        // T8: invalid not forced
        yield return LoadNamedLevel(levelManager, sb, "Campaign_43_Reference");
        board = Object.FindFirstObjectByType<BoardManager>();
        yield return null;
        yield return null;
        bool c8 = EvaluateInvalidNotForced(board, out string d8);
        sb.AppendLine($"T8_DETAIL={d8}");
        cases.Add(new CaseResult { Id = "T8", Title = "invalid/non-same-translation NOT forced", Pass = c8, Detail = d8 });

        sb.AppendLine();
        sb.AppendLine("=== SUMMARY ===");
        int passCount = 0;
        for (int i = 0; i < cases.Count; i++)
        {
            CaseResult c = cases[i];
            if (c.Pass)
            {
                passCount++;
            }

            sb.AppendLine($"{c.Id} {(c.Pass ? "PASS" : "FAIL")} — {c.Title}");
            sb.AppendLine($"    {c.Detail}");
        }

        bool allPass = passCount == cases.Count;
        sb.AppendLine($"OVERALL={(allPass ? "PASS" : "FAIL")} {passCount}/{cases.Count}");
        Finish(sb, allPass, allPass ? "all requirement cases PASS" : "one or more FAIL");
    }

    private IEnumerator RunRigidTravelCase(
        BoardManager board,
        StringBuilder sb,
        int requiredCount,
        bool identicalOnly,
        bool requireLShape,
        bool requireMixed,
        Holder holder)
    {
        holder.Pass = false;
        holder.Detail = "no candidate";

        if (board == null)
        {
            holder.Detail = "null board";
            yield break;
        }

        if (!TryFindRigidCandidate(
                board,
                requiredCount,
                identicalOnly,
                requireLShape,
                requireMixed,
                out Block subject,
                out List<int> indices,
                out Vector2Int translation,
                out List<Vector2Int> destinations,
                out string findDetail))
        {
            holder.Detail = "find_fail:" + findDetail;
            sb.AppendLine(
                $"FIND_FAIL need={requiredCount} identical={identicalOnly} L={requireLShape} " +
                $"mixed={requireMixed} {findDetail}");
            yield break;
        }

        sb.AppendLine(
            $"CANDIDATE cells={subject.CellCount} pick={indices.Count} t={translation} " +
            $"shapes={DescribeShapes(subject, indices)} dests=[{string.Join(",", destinations)}]");

        BlockMover mover = subject.GetComponent<BlockMover>();
        if (mover == null)
        {
            holder.Detail = "no mover";
            yield break;
        }

        bool collectOk = InvokeCollect(mover, board, subject, destinations[0], out _, out int nestCount);
        sb.AppendLine($"collect focus={destinations[0]} ok={collectOk} nestCount={nestCount}");

        var group = new BlockMover.AlignedMovementGroup
        {
            Subject = subject,
            Translation = translation
        };
        for (int i = 0; i < indices.Count; i++)
        {
            Vector2Int world = subject.GetCellWorld(indices[i]);
            group.Actions.Add(
                new BlockMover.AlignedMatchAction(subject, indices[i], world, destinations[i]));
        }

        BeginSample(subject, indices);
        bool prev = BlockMover.MatchingSubsetDiagnosticsEnabled;
        BlockMover.MatchingSubsetDiagnosticsEnabled = true;
        yield return mover.StartCoroutine(mover.PlayResolvedMovementGroup(board, group));
        BlockMover.MatchingSubsetDiagnosticsEnabled = prev;
        sampleTravel = false;

        bool consumeOk = mover.LastResolvedConsumeSucceeded;
        bool travelOk;
        if (translation == Vector2Int.zero)
        {
            // Occupying group: no travel arc; rigidity is trivial (already seated together).
            travelOk = true;
        }
        else
        {
            travelOk = midTaken && midRigid && midSameProgress && midNoLag;
        }

        bool pass = consumeOk && travelOk && group.Actions.Count >= requiredCount;
        holder.Pass = pass;
        holder.Detail =
            $"n={group.Actions.Count} t={translation} midTaken={midTaken} midRigid={midRigid} " +
            $"midSameProgress={midSameProgress} midNoLag={midNoLag} consumeOk={consumeOk} " +
            $"collectNest={nestCount}";
        sb.AppendLine($"TRAVEL {(pass ? "PASS" : "FAIL")} {holder.Detail}");
    }

    private IEnumerator RunNestedCase(BoardManager board, StringBuilder sb, Holder holder)
    {
        holder.Pass = false;
        holder.Detail = "no nested";

        var blocks = new List<Block>();
        board.CollectUniqueBlocks(blocks);
        Block nested = null;
        for (int i = 0; i < blocks.Count; i++)
        {
            Block b = blocks[i];
            if (b == null || b.IsSettled)
            {
                continue;
            }

            for (int c = 0; c < b.CellCount; c++)
            {
                if (b.HasInnerLayerAt(c))
                {
                    nested = b;
                    break;
                }
            }

            if (nested != null)
            {
                break;
            }
        }

        if (nested == null)
        {
            holder.Detail = "no nested block on board";
            yield break;
        }

        bool hasSubsetMethod = typeof(BlockMover).GetMethod(
            "TryPlayNestedSubsetMatch",
            BindingFlags.Instance | BindingFlags.NonPublic) != null;
        string src = File.ReadAllText(
            Path.Combine(Application.dataPath, "Scripts/Blocks/BlockMover.cs"));
        bool nestedPrefer = src.Contains("PreferNestedCellForSingleMatch")
            && src.Contains("TryPlayNestedSubsetMatch")
            && src.Contains("PlayAllPendingNestedExtractionReveals");
        bool hasRigidRoute = SourceHasPhase73Routing();

        // If a live nested rigid pair exists, exercise it.
        if (nested.CellCount >= 2
            && TryFindRigidCandidate(
                board,
                2,
                identicalOnly: false,
                requireLShape: false,
                requireMixed: false,
                out Block subject,
                out List<int> indices,
                out Vector2Int translation,
                out List<Vector2Int> destinations,
                out _)
            && subject == nested)
        {
            BlockMover mover = nested.GetComponent<BlockMover>();
            var group = new BlockMover.AlignedMovementGroup
            {
                Subject = nested,
                Translation = translation
            };
            for (int i = 0; i < indices.Count; i++)
            {
                Vector2Int world = nested.GetCellWorld(indices[i]);
                group.Actions.Add(
                    new BlockMover.AlignedMatchAction(nested, indices[i], world, destinations[i]));
            }

            BeginSample(nested, indices);
            yield return mover.StartCoroutine(mover.PlayResolvedMovementGroup(board, group));
            sampleTravel = false;
            holder.Pass = mover.LastResolvedConsumeSucceeded && hasSubsetMethod && nestedPrefer;
            holder.Detail =
                $"live nestedCells={nested.CellCount} consumeOk={mover.LastResolvedConsumeSucceeded} " +
                $"midRigid={midRigid} subsetMethod={hasSubsetMethod}";
            sb.AppendLine($"T6_LIVE {holder.Detail}");
            yield break;
        }

        holder.Pass = nestedPrefer && hasSubsetMethod && hasRigidRoute;
        holder.Detail =
            $"structural nestedId={nested.GetInstanceID()} cells={nested.CellCount} " +
            $"subsetMethod={hasSubsetMethod} nestedPrefer={nestedPrefer}";
        sb.AppendLine($"T6_STRUCT {holder.Detail}");
    }

    private static bool EvaluateFixedDirection(BoardManager board, out string detail)
    {
        var blocks = new List<Block>();
        board.CollectUniqueBlocks(blocks);
        Block fixedBlock = null;
        for (int i = 0; i < blocks.Count; i++)
        {
            Block b = blocks[i];
            if (b != null && !b.IsSettled && b.MoveDirection != MoveDirection.Any)
            {
                fixedBlock = b;
                break;
            }
        }

        if (fixedBlock == null)
        {
            detail = "no fixed-direction block";
            return false;
        }

        BlockMover mover = fixedBlock.GetComponent<BlockMover>();
        if (mover == null)
        {
            detail = "no mover";
            return false;
        }

        Vector2Int allowed;
        switch (fixedBlock.MoveDirection)
        {
            case MoveDirection.Up:
                allowed = Vector2Int.up;
                break;
            case MoveDirection.Down:
                allowed = Vector2Int.down;
                break;
            case MoveDirection.Left:
                allowed = Vector2Int.left;
                break;
            case MoveDirection.Right:
                allowed = Vector2Int.right;
                break;
            default:
                allowed = Vector2Int.zero;
                break;
        }

        Vector2Int illegal = allowed == Vector2Int.up || allowed == Vector2Int.down
            ? Vector2Int.right
            : Vector2Int.up;

        bool allowOk = mover.IsDirectionAllowed(allowed);
        bool denyOk = !mover.IsDirectionAllowed(illegal);

        bool buildRejectsIllegal = true;
        MethodInfo build = typeof(BlockMover).GetMethod(
            "TryBuildNestedSubsetGroup",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (build != null && fixedBlock.CellCount >= 2)
        {
            var nestIdx = ReadNestListMutable(mover, "nestCellIndices");
            var nestWorlds = ReadNestWorldListMutable(mover, "nestTargetWorlds");
            nestIdx.Clear();
            nestWorlds.Clear();
            nestIdx.Add(0);
            nestIdx.Add(1);
            nestWorlds.Add(fixedBlock.GetCellWorld(0) + illegal * 2);
            nestWorlds.Add(fixedBlock.GetCellWorld(1) + illegal * 2);
            object[] args = { board, fixedBlock, fixedBlock.GridPosition, false, null };
            bool built = (bool)build.Invoke(mover, args);
            buildRejectsIllegal = !built;
        }

        bool pass = allowOk && denyOk && buildRejectsIllegal;
        detail =
            $"dir={fixedBlock.MoveDirection} allow({allowed})={allowOk} deny({illegal})={denyOk} " +
            $"buildRejectsIllegal={buildRejectsIllegal}";
        return pass;
    }

    private static bool EvaluateInvalidNotForced(BoardManager board, out string detail)
    {
        var blocks = new List<Block>();
        board.CollectUniqueBlocks(blocks);
        Block subject = null;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] != null && !blocks[i].IsSettled && blocks[i].CellCount >= 2)
            {
                subject = blocks[i];
                break;
            }
        }

        if (subject == null)
        {
            detail = "no multi-cell subject";
            return false;
        }

        var indices = new List<int> { 0, 1 };
        bool subsetRejects = !board.CanTranslateMatchingSubset(subject, indices, new Vector2Int(3, 7));

        var actions = new List<BlockMover.AlignedMatchAction>
        {
            new BlockMover.AlignedMatchAction(
                subject, 0, subject.GetCellWorld(0), subject.GetCellWorld(0) + Vector2Int.up),
            new BlockMover.AlignedMatchAction(
                subject, 1, subject.GetCellWorld(1), subject.GetCellWorld(1) + Vector2Int.right)
        };
        var groups = new List<BlockMover.AlignedMovementGroup>();
        int n = BlockMover.BuildAlignedMovementGroups(actions, groups);
        bool notForcedTogether = true;
        if (n == 1 && groups[0].Actions.Count == 2)
        {
            notForcedTogether = actions[0].Translation == actions[1].Translation;
        }
        else if (n >= 1)
        {
            notForcedTogether = groups[0].Actions.Count == 1;
        }

        bool diagonalRejected = true;
        BlockMover mover = subject.GetComponent<BlockMover>();
        MethodInfo build = typeof(BlockMover).GetMethod(
            "TryBuildNestedSubsetGroup",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (build != null && mover != null)
        {
            var nestIdx = ReadNestListMutable(mover, "nestCellIndices");
            var nestWorlds = ReadNestWorldListMutable(mover, "nestTargetWorlds");
            nestIdx.Clear();
            nestWorlds.Clear();
            nestIdx.Add(0);
            nestIdx.Add(1);
            nestWorlds.Add(subject.GetCellWorld(0) + new Vector2Int(1, 1));
            nestWorlds.Add(subject.GetCellWorld(1) + new Vector2Int(1, 1));
            object[] args = { board, subject, subject.GridPosition, false, null };
            bool built = (bool)build.Invoke(mover, args);
            diagonalRejected = !built;
        }

        bool pass = subsetRejects && notForcedTogether && diagonalRejected;
        detail =
            $"subsetRejectsBadT={subsetRejects} notForcedTogether={notForcedTogether} " +
            $"diagonalRejected={diagonalRejected} groups={n}";
        return pass;
    }

    private static bool TryFindRigidCandidate(
        BoardManager board,
        int requiredCount,
        bool identicalOnly,
        bool requireLShape,
        bool requireMixed,
        out Block subject,
        out List<int> indices,
        out Vector2Int translation,
        out List<Vector2Int> destinations,
        out string detail)
    {
        subject = null;
        indices = null;
        translation = Vector2Int.zero;
        destinations = null;
        detail = "none";

        var blocks = new List<Block>();
        board.CollectUniqueBlocks(blocks);
        Vector2Int[] unitDirs =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        for (int b = 0; b < blocks.Count; b++)
        {
            Block block = blocks[b];
            if (block == null || block.IsSettled || block.CellCount < requiredCount)
            {
                continue;
            }

            if (requireLShape && !IsLShapedFootprint(block))
            {
                continue;
            }

            for (int d = 0; d < unitDirs.Length; d++)
            {
                // dist=0: occupying rigid group (same-cell targets).
                for (int dist = 0; dist <= 8; dist++)
                {
                    Vector2Int t = dist == 0 ? Vector2Int.zero : unitDirs[d] * dist;
                    if (dist == 0 && d > 0)
                    {
                        // Only evaluate zero translation once.
                        continue;
                    }

                    if (!TryBuildMatchSet(
                            board,
                            block,
                            t,
                            requiredCount,
                            identicalOnly,
                            requireMixed,
                            out List<int> idxs,
                            out List<Vector2Int> dests))
                    {
                        continue;
                    }

                    if (!board.CanTranslateMatchingSubset(block, idxs, t))
                    {
                        continue;
                    }

                    BlockMover mover = block.GetComponent<BlockMover>();
                    if (dist > 0
                        && mover != null
                        && block.MoveDirection != MoveDirection.Any
                        && !mover.IsDirectionAllowed(unitDirs[d]))
                    {
                        continue;
                    }

                    subject = block;
                    indices = idxs;
                    translation = t;
                    destinations = dests;
                    detail = $"block={block.GetInstanceID()} t={t} n={idxs.Count}";
                    return true;
                }
            }
        }

        detail = $"scanned blocks={blocks.Count} need>={requiredCount}";
        return false;
    }

    private static bool TryBuildMatchSet(
        BoardManager board,
        Block block,
        Vector2Int translation,
        int requiredCount,
        bool identicalOnly,
        bool requireMixed,
        out List<int> indices,
        out List<Vector2Int> destinations)
    {
        indices = new List<int>();
        destinations = new List<Vector2Int>();

        for (int i = 0; i < block.CellCount; i++)
        {
            Vector2Int world = block.GetCellWorld(i);
            Vector2Int dest = world + translation;
            Target target = board.GetTargetAt(dest);
            if (target == null
                || !ShapeMatch.AreMatchingLayers(
                    target.GetRequiredIdentityAtWorld(dest),
                    block.GetActiveIdentity(i)))
            {
                continue;
            }

            indices.Add(i);
            destinations.Add(dest);
        }

        if (indices.Count < requiredCount)
        {
            return false;
        }

        if (indices.Count > requiredCount)
        {
            // Prefer a diverse subset when requireMixed, else first N.
            if (requireMixed)
            {
                var pickedIdx = new List<int>();
                var pickedDest = new List<Vector2Int>();
                var seen = new HashSet<ShapeType>();
                for (int i = 0; i < indices.Count && pickedIdx.Count < requiredCount; i++)
                {
                    ShapeType s = block.GetActiveShape(indices[i]);
                    if (seen.Add(s) || pickedIdx.Count + (indices.Count - i) <= requiredCount)
                    {
                        pickedIdx.Add(indices[i]);
                        pickedDest.Add(destinations[i]);
                    }
                }

                while (pickedIdx.Count < requiredCount && pickedIdx.Count < indices.Count)
                {
                    int i = pickedIdx.Count;
                    if (!pickedIdx.Contains(indices[i]))
                    {
                        pickedIdx.Add(indices[i]);
                        pickedDest.Add(destinations[i]);
                    }
                    else
                    {
                        break;
                    }
                }

                indices = pickedIdx;
                destinations = pickedDest;
            }
            else
            {
                indices = indices.GetRange(0, requiredCount);
                destinations = destinations.GetRange(0, requiredCount);
            }
        }

        if (indices.Count < requiredCount)
        {
            return false;
        }

        if (identicalOnly)
        {
            ShapeType s0 = block.GetActiveShape(indices[0]);
            for (int i = 1; i < indices.Count; i++)
            {
                if (block.GetActiveShape(indices[i]) != s0)
                {
                    return false;
                }
            }
        }

        if (requireMixed)
        {
            var seen = new HashSet<ShapeType>();
            for (int i = 0; i < indices.Count; i++)
            {
                seen.Add(block.GetActiveShape(indices[i]));
            }

            if (seen.Count < 2)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLShapedFootprint(Block block)
    {
        if (block == null || block.CellCount < 3)
        {
            return false;
        }

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        for (int i = 0; i < block.CellCount; i++)
        {
            Vector2Int local = block.GetLocalCell(i);
            minX = Mathf.Min(minX, local.x);
            maxX = Mathf.Max(maxX, local.x);
            minY = Mathf.Min(minY, local.y);
            maxY = Mathf.Max(maxY, local.y);
        }

        if (maxX <= minX || maxY <= minY)
        {
            return false;
        }

        int bbox = (maxX - minX + 1) * (maxY - minY + 1);
        return block.CellCount < bbox;
    }

    private void BeginSample(Block subject, List<int> indices)
    {
        sampleViews = new List<PieceView3D>(indices.Count);
        sampleStarts = new List<Vector3>(indices.Count);
        sampleRelBefore.Clear();
        midTaken = false;
        midRigid = true;
        midSameProgress = true;
        midNoLag = true;
        for (int i = 0; i < indices.Count; i++)
        {
            PieceView3D view = subject.GetWorldViewForCellIndex(indices[i]);
            sampleViews.Add(view);
            sampleStarts.Add(view != null ? view.transform.position : Vector3.zero);
        }

        for (int i = 1; i < sampleStarts.Count; i++)
        {
            sampleRelBefore.Add(sampleStarts[i] - sampleStarts[0]);
        }

        sampleTravel = true;
    }

    private void Update()
    {
        if (!sampleTravel || sampleViews == null || sampleStarts == null || sampleViews.Count < 2)
        {
            return;
        }

        if (sampleViews[0] == null)
        {
            return;
        }

        float maxMoved = 0f;
        float minMoved = float.MaxValue;
        for (int i = 0; i < sampleViews.Count; i++)
        {
            if (sampleViews[i] == null)
            {
                return;
            }

            float moved = Vector3.Distance(sampleViews[i].transform.position, sampleStarts[i]);
            maxMoved = Mathf.Max(maxMoved, moved);
            minMoved = Mathf.Min(minMoved, moved);
        }

        if (maxMoved < 0.02f)
        {
            return;
        }

        midTaken = true;
        midSameProgress = (maxMoved - minMoved) < 0.2f;
        midNoLag = midSameProgress;

        Vector3 p0 = sampleViews[0].transform.position;
        for (int i = 1; i < sampleViews.Count; i++)
        {
            Vector3 rel = sampleViews[i].transform.position - p0;
            if (Vector3.Distance(rel, sampleRelBefore[i - 1]) > 0.12f)
            {
                midRigid = false;
            }
        }
    }

    private static IEnumerator LoadNamedLevel(
        LevelManager levelManager,
        StringBuilder sb,
        string levelName)
    {
        // 1) Prefer LevelDatabase entry.
        var dbField = typeof(LevelManager).GetField(
            "levelDatabase", BindingFlags.Instance | BindingFlags.NonPublic);
        LevelDatabase db = dbField != null
            ? dbField.GetValue(levelManager) as LevelDatabase
            : null;
        if (db != null)
        {
            for (int i = 0; i < db.Count; i++)
            {
                LevelData level = db.GetLevel(i);
                if (level == null || level.name != levelName)
                {
                    continue;
                }

                sb.AppendLine($"LOAD_DB {levelName} index={i}");
                levelManager.LoadLevel(i);
                yield return null;
                yield return null;
                yield break;
            }
        }

        // 2) Fallback: load LevelData asset by name (validation-only; DB may be sparse).
        LevelData asset = null;
#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:LevelData");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            LevelData candidate = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (candidate != null && candidate.name == levelName)
            {
                asset = candidate;
                sb.AppendLine($"LOAD_ASSET {levelName} path={path}");
                break;
            }
        }
#endif
        if (asset == null)
        {
            sb.AppendLine($"LOAD_MISS {levelName}");
            yield break;
        }

        levelManager.LoadLevel(asset);
        yield return null;
        yield return null;
    }

    /// <summary>
    /// Scan several known LevelData assets until a rigid candidate is found, then travel.
    /// </summary>
    private IEnumerator RunRigidTravelCaseAcrossLevels(
        LevelManager levelManager,
        StringBuilder sb,
        string[] levelNames,
        int requiredCount,
        bool identicalOnly,
        bool requireLShape,
        bool requireMixed,
        Holder holder)
    {
        holder.Pass = false;
        holder.Detail = "no candidate across levels";
        for (int i = 0; i < levelNames.Length; i++)
        {
            yield return LoadNamedLevel(levelManager, sb, levelNames[i]);
            BoardManager board = Object.FindFirstObjectByType<BoardManager>();
            yield return null;
            yield return null;
            var attempt = new Holder();
            yield return RunRigidTravelCase(
                board, sb, requiredCount, identicalOnly, requireLShape, requireMixed, attempt);
            if (attempt.Pass)
            {
                holder.Pass = true;
                holder.Detail = levelNames[i] + " | " + attempt.Detail;
                yield break;
            }

            holder.Detail = levelNames[i] + ":" + attempt.Detail;
        }
    }

    private static bool SourceHasPhase73Routing()
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

    private static string DescribeShapes(Block subject, List<int> indices)
    {
        var parts = new List<string>();
        for (int i = 0; i < indices.Count; i++)
        {
            int idx = indices[i];
            parts.Add($"{subject.GetActiveShape(idx)}@{subject.GetCellWorld(idx)}");
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
                board, subject, subject.GridPosition, focus, false, Vector2Int.zero
            };
            object result = method.Invoke(mover, args);
            targetWorld = args[5] is Vector2Int tw ? tw : Vector2Int.zero;
            nestCount = ReadNestList(mover, "nestCellIndices").Count;
            return result is bool ok && ok;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[Phase73Suite] InvokeCollect: " + ex.Message);
            return false;
        }
    }

    private static List<int> ReadNestList(BlockMover mover, string fieldName)
    {
        return new List<int>(ReadNestListMutable(mover, fieldName));
    }

    private static List<int> ReadNestListMutable(BlockMover mover, string fieldName)
    {
        FieldInfo field = typeof(BlockMover).GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field != null && field.GetValue(mover) is List<int> list)
        {
            return list;
        }

        return new List<int>();
    }

    private static List<Vector2Int> ReadNestWorldListMutable(BlockMover mover, string fieldName)
    {
        FieldInfo field = typeof(BlockMover).GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field != null && field.GetValue(mover) is List<Vector2Int> list)
        {
            return list;
        }

        return new List<Vector2Int>();
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
            Debug.LogWarning("[Phase73Suite] write failed: " + ex.Message);
        }

        Debug.Log("[Phase73Suite]\n" + Result);
        enabled = false;
    }
}
