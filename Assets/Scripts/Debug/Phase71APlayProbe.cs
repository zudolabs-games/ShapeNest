using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase 71A Play Mode forensic driver — diagnostic only.
/// Loads Level 43, dumps white-triangle + nested state, proves drag-path reduction.
/// Does not alter gameplay systems beyond reading/public dump APIs and optional
/// reflection into private match collectors for observation.
/// </summary>
public sealed class Phase71APlayProbe : MonoBehaviour
{
    private const string ReportPath = "Captures/phase71a-play-report.txt";

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
        var sb = new StringBuilder(4096);
        Phase71AForensic.EnableForSession();
        sb.AppendLine("PHASE 71A — PLAY MODE FORENSIC");
        sb.AppendLine("==============================");
        sb.AppendLine(Phase71AForensic.ProveDragPathFromSource());
        sb.AppendLine();

        LevelManager lm = Object.FindFirstObjectByType<LevelManager>();
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (lm == null || board == null)
        {
            Finish(sb, "missing lm/board");
            yield break;
        }

        lm.LoadLevel(42);
        yield return null;
        yield return null;
        float wait = 0f;
        while (wait < 2f && FindWhiteTriangle() == null)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        Block white = FindWhiteTriangle();
        if (white == null)
        {
            Finish(sb, "white triangle not found");
            yield break;
        }

        sb.AppendLine(Phase71AForensic.DumpBlockSnapshot(board, white, "WHITE_BEFORE"));
        sb.AppendLine(Phase71AForensic.CompareLogicalVsVisual(white));
        sb.AppendLine(Phase71AForensic.DumpWhiteTriangleSubsets(board, white));
        sb.AppendLine(Phase71AForensic.DumpAutoMatchPipeline(board, false, default, default));

        // Simulate player releasing while aiming toward Target A (north): focus = grid+(0,1)
        // relative to drag toward (1,9)/(2,9) → focus often committed+up or a nest cell.
        Vector2Int focusTowardTargetA = white.GridPosition + new Vector2Int(0, 1);
        sb.AppendLine("--- Simulate focused reduction (drag toward Target A) ---");
        sb.AppendLine(Phase71AForensic.SimulateFocusedReduction(board, white, focusTowardTargetA));
        sb.AppendLine(Phase71AForensic.SimulateFocusedReduction(board, white, new Vector2Int(1, 9)));
        sb.AppendLine(Phase71AForensic.SimulateFocusedReduction(board, white, new Vector2Int(2, 9)));

        // Nested sample
        Block nested = FindAnyNested();
        if (nested != null)
        {
            sb.AppendLine(Phase71AForensic.DumpBlockSnapshot(board, nested, "NESTED_SAMPLE"));
            sb.AppendLine(Phase71AForensic.CompareLogicalVsVisual(nested));
        }
        else
        {
            sb.AppendLine("NESTED_SAMPLE: none found");
        }

        // Prove EnterMatchingTargetBody branch exists via reflection (read-only of IL/method).
        var enterBody = typeof(BlockMover).GetMethod(
            "EnterMatchingTargetBody",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var enterChain = typeof(BlockMover).GetMethod(
            "EnterChainPartialMatch",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var subset = typeof(BlockMover).GetMethod(
            "PlayMatchingSubsetAlignedMatch",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        sb.AppendLine(
            $"methods: EnterMatchingTargetBody={enterBody != null} " +
            $"EnterChainPartialMatch={enterChain != null} " +
            $"PlayMatchingSubsetAlignedMatch={subset != null}");

        // Source scan: does EnterMatchingTargetBody call EnterChainPartialMatch?
        string moverPath = "Assets/Scripts/Blocks/BlockMover.cs";
        if (File.Exists(moverPath))
        {
            string text = File.ReadAllText(moverPath);
            int body = text.IndexOf("private IEnumerator EnterMatchingTargetBody");
            int chainCall = text.IndexOf("EnterChainPartialMatch", body >= 0 ? body : 0);
            int subsetInBody = -1;
            if (body >= 0)
            {
                int next = text.IndexOf("\n    private IEnumerator", body + 10);
                string bodyText = next > body ? text.Substring(body, next - body) : text.Substring(body, 800);
                subsetInBody = bodyText.IndexOf("PlayMatchingSubsetAlignedMatch");
                sb.AppendLine(
                    $"SOURCE EnterMatchingTargetBody contains EnterChainPartialMatch={chainCall > body} " +
                    $"contains PlayMatchingSubsetAlignedMatch={subsetInBody >= 0}");
            }
        }

        Phase71AForensic.Flush();
        Finish(sb, "OK");
        yield break;
    }

    private static Block FindWhiteTriangle()
    {
        Block[] blocks = Object.FindObjectsByType<Block>(FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            Block b = blocks[i];
            if (b == null || b.IsSettled || b.CellCount != 4)
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

    private static Block FindAnyNested()
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
                if (b.HasInnerLayerAt(c))
                {
                    return b;
                }
            }
        }

        return null;
    }

    private void Finish(StringBuilder sb, string tag)
    {
        sb.AppendLine();
        sb.AppendLine("TAG: " + tag);
        Result = sb.ToString();
        Done = true;
        try
        {
            File.WriteAllText(ReportPath, Result);
        }
        catch
        {
            // ignore
        }

        Debug.Log("[71A][PLAY]\n" + Result);
        Phase71AForensic.Disable();
    }
}
