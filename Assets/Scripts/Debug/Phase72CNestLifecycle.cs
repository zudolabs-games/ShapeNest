using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase 72C — temporary lifecycle logging for nest reappear after consume.
/// Writes Captures/phase72c-nest-reappear.txt and mirrors to Debug.Log.
/// Presentation diagnostic only; does not change gameplay.
/// </summary>
public static class Phase72CNestLifecycle
{
    private const string ReportPath = "Captures/phase72c-nest-reappear.txt";
    private static readonly StringBuilder Buffer = new StringBuilder(32000);
    private static bool enabled = true;

    public static void SetEnabled(bool value)
    {
        enabled = value;
    }

    public static void Clear()
    {
        Buffer.Length = 0;
        Buffer.AppendLine("PHASE 72C — NEST LIFECYCLE");
        Buffer.AppendLine("=========================");
        Flush();
    }

    public static void LogTargetState(Target target, string reason)
    {
        if (!enabled || target == null || !IsInteresting(target))
        {
            return;
        }

        Append(
            "GREEN TARGET STATE [" + reason + "] " + DescribeTarget(target));
    }

    public static void LogTargetEnable(Target target)
    {
        if (!enabled || target == null || !IsInteresting(target))
        {
            return;
        }

        Append("GREEN TARGET: inactive -> active " + DescribeTarget(target));
    }

    public static void LogTargetDisable(Target target)
    {
        if (!enabled || target == null || !IsInteresting(target))
        {
            return;
        }

        Append("GREEN TARGET: active -> inactive " + DescribeTarget(target));
    }

    public static void LogTargetConsumed(Target target)
    {
        if (!enabled || target == null || !IsInteresting(target))
        {
            return;
        }

        Append("GREEN TARGET CONSUMED (cells empty) " + DescribeTarget(target));
    }

    public static void LogSyncDecision(Target target, string decision, bool onBoard, bool liveCells)
    {
        if (!enabled || target == null || !IsInteresting(target))
        {
            return;
        }

        Append(
            "GREEN SYNC " + decision +
            " onBoard=" + onBoard +
            " liveCells=" + liveCells +
            " " + DescribeTarget(target));
    }

    public static void LogNestCreate(Target target, PieceView3D view, string caller)
    {
        if (!enabled)
        {
            return;
        }

        Append(
            "GREEN NEST: created caller=" + caller +
            " nestId=" + (view != null ? view.GetInstanceID().ToString() : "0") +
            " nestName=" + (view != null ? view.name : "null") +
            " " + DescribeTarget(target) +
            "\n  stack=" + TrimStack(UnityEngine.StackTraceUtility.ExtractStackTrace()));
    }

    public static void LogNestReuse(Target target, PieceView3D view, string caller)
    {
        if (!enabled || target == null || !IsInteresting(target))
        {
            return;
        }

        Append(
            "GREEN NEST: reused caller=" + caller +
            " nestId=" + (view != null ? view.GetInstanceID().ToString() : "0") +
            " nestName=" + (view != null ? view.name : "null") +
            " active=" + (view != null && view.gameObject.activeInHierarchy) +
            " " + DescribeTarget(target));
    }

    public static void LogNestRemesh(
        Target target,
        PieceView3D view,
        ShapeType shape,
        ShapeColor color,
        string caller)
    {
        if (!enabled || target == null || !IsInteresting(target))
        {
            return;
        }

        Append(
            "GREEN NEST: remeshed caller=" + caller +
            " shape=" + shape +
            " color=" + color +
            " nestId=" + (view != null ? view.GetInstanceID().ToString() : "0") +
            " " + DescribeTarget(target) +
            "\n  stack=" + TrimStack(UnityEngine.StackTraceUtility.ExtractStackTrace()));
    }

    public static void LogNestDestroyed(PieceView3D view, string caller)
    {
        if (!enabled || view == null || view.name == null || view.name.IndexOf("Nest3D") < 0)
        {
            return;
        }

        Append(
            "GREEN NEST: destroyed caller=" + caller +
            " nestId=" + view.GetInstanceID() +
            " nestName=" + view.name);
    }

    private static bool IsInteresting(Target target)
    {
        if (target == null)
        {
            return false;
        }

        // Trace green diamond nests (Level 43 T-green-diamond) and any Nest3D create path.
        if (target.RequiredShape == ShapeType.Diamond)
        {
            ShapeColor c = target.GetOuterColorAtIndex(target.AnchorCellIndex);
            if (c == ShapeColor.Green || !target.HasLiveNestCells || target.IsMatched)
            {
                return true;
            }
        }

        return target.name != null && target.name.IndexOf("Diamond") >= 0;
    }

    private static string DescribeTarget(Target target)
    {
        if (target == null)
        {
            return "target=null";
        }

        return
            "targetId=" + target.GetInstanceID() +
            " name=" + target.name +
            " grid=" + target.GridPosition +
            " cells=" + target.CellCount +
            " liveCells=" + target.HasLiveNestCells +
            " matched=" + target.IsMatched +
            " matchActive=" + target.IsMatchPresentationActive +
            " boardReg=" + target.IsBoardRegistered +
            " goActive=" + target.gameObject.activeInHierarchy +
            " required=" + target.RequiredShape +
            " outer0=" + target.GetOuterColorAtIndex(target.AnchorCellIndex);
    }

    private static string TrimStack(string stack)
    {
        if (string.IsNullOrEmpty(stack))
        {
            return "";
        }

        string[] lines = stack.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        int kept = 0;
        for (int i = 0; i < lines.Length && kept < 8; i++)
        {
            string line = lines[i];
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            if (line.IndexOf("Phase72CNestLifecycle") >= 0
                || line.IndexOf("StackTraceUtility") >= 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(" | ");
            }

            sb.Append(line.Trim());
            kept++;
        }

        return sb.ToString();
    }

    private static void Append(string line)
    {
        string stamped = "[" + Time.frameCount + "] " + line;
        Buffer.AppendLine(stamped);
        UnityEngine.Debug.Log("[72C] " + stamped);
        Flush();
    }

    private static void Flush()
    {
        try
        {
            string dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(ReportPath, Buffer.ToString());
        }
        catch (IOException)
        {
            // Ignore IO during domain reload.
        }
    }
}
