#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Editor/batch-mode verification for BoardLayout sizing. Writes Assets/BoardLayoutVerificationReport.txt.
/// Menu: Tools/Shape Nest/Verify Board Layout
/// Batch: -executeMethod BoardLayoutVerification.RunFromCommandLine
/// </summary>
public static class BoardLayoutVerification
{
    private const string ReportPath = "Assets/BoardLayoutVerificationReport.txt";
    private static readonly int[] TestWidths = { 4, 5, 7, 6 };
    private static readonly int[] TestHeights = { 4, 5, 7, 7 };

    [MenuItem("Tools/Shape Nest/Verify Board Layout")]
    public static void RunFromMenu()
    {
        RunVerification(false);
    }

    public static void RunFromCommandLine()
    {
        RunVerification(true);
    }

    private static void RunVerification(bool quitWhenDone)
    {
        var report = new StringBuilder();
        report.AppendLine("BoardLayout Verification Report");
        report.AppendLine($"Unity {Application.unityVersion}");
        report.AppendLine($"Time: {System.DateTime.Now:O}");
        report.AppendLine();

        if (!OpenSampleScene(report))
        {
            WriteReport(report, quitWhenDone);
            return;
        }

        Canvas.ForceUpdateCanvases();

        BoardLayout layout = Object.FindFirstObjectByType<BoardLayout>(FindObjectsInactive.Include);
        BoardManager board = Object.FindFirstObjectByType<BoardManager>(FindObjectsInactive.Include);
        if (layout == null || board == null)
        {
            report.AppendLine("FAIL: BoardLayout or BoardManager not found in SampleScene.");
            WriteReport(report, quitWhenDone);
            return;
        }

        RectTransform boardRect = board.transform as RectTransform;
        RectTransform gameplayArea = ResolveGameplayAreaRect(layout, boardRect, report);

        report.AppendLine("=== Scene wiring ===");
        report.AppendLine($"Board: {board.name} ({GetPath(board.transform)})");
        report.AppendLine($"BoardLayout on Board: YES");
        report.AppendLine($"Gameplay area source: {DescribeGameplayAreaSource(layout, boardRect)}");
        report.AppendLine($"Gameplay area rect: {FormatSize(gameplayArea.rect.size)}");
        report.AppendLine($"BoardLayout padding: {layout.GameplayAreaPadding}");
        report.AppendLine($"BoardManager gridPadding: {board.GridPadding}");
        report.AppendLine();

        report.AppendLine("=== Grid size tests (editor layout pass) ===");
        for (int i = 0; i < TestWidths.Length; i++)
        {
            RunGridTest(report, layout, board, boardRect, gameplayArea, TestWidths[i], TestHeights[i]);
        }

        report.AppendLine("=== Aspect ratio simulation (Middle rect resize) ===");
        RunAspectRatioTests(report, layout, board, boardRect, gameplayArea);

        WriteReport(report, quitWhenDone);
    }

    private static bool OpenSampleScene(StringBuilder report)
    {
        const string scenePath = "Assets/Scenes/SampleScene.unity";
        if (!File.Exists(scenePath))
        {
            report.AppendLine($"FAIL: Scene not found at {scenePath}");
            return false;
        }

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        report.AppendLine($"Opened scene: {scene.path}");
        return scene.IsValid();
    }

    private static RectTransform ResolveGameplayAreaRect(BoardLayout layout, RectTransform boardRect, StringBuilder report)
    {
        var serialized = new SerializedObject(layout);
        RectTransform assigned = serialized.FindProperty("gameplayArea").objectReferenceValue as RectTransform;
        bool useParent = serialized.FindProperty("useParentAsGameplayArea").boolValue;
        Vector2 fallback = serialized.FindProperty("defaultGameplayAreaSize").vector2Value;

        if (assigned != null)
        {
            return assigned;
        }

        if (useParent && boardRect.parent is RectTransform parent)
        {
            return parent;
        }

        report.AppendLine($"NOTE: Using fallback default area {fallback} (no valid gameplay rect).");
        var temp = new GameObject("TempGameplayAreaProbe", typeof(RectTransform)).GetComponent<RectTransform>();
        temp.sizeDelta = fallback;
        Object.DestroyImmediate(temp.gameObject);
        return boardRect.parent as RectTransform ?? boardRect;
    }

    private static string DescribeGameplayAreaSource(BoardLayout layout, RectTransform boardRect)
    {
        var serialized = new SerializedObject(layout);
        if (serialized.FindProperty("gameplayArea").objectReferenceValue is RectTransform assigned)
        {
            return $"Assigned RectTransform '{assigned.name}'";
        }

        if (serialized.FindProperty("useParentAsGameplayArea").boolValue && boardRect.parent is RectTransform parent)
        {
            return $"Parent RectTransform '{parent.name}'";
        }

        return "DefaultGameplayAreaSize fallback";
    }

    private static void RunGridTest(
        StringBuilder report,
        BoardLayout layout,
        BoardManager board,
        RectTransform boardRect,
        RectTransform gameplayArea,
        int gridW,
        int gridH)
    {
        Vector2 areaSize = gameplayArea.rect.size;
        if (areaSize.x <= 1f || areaSize.y <= 1f)
        {
            areaSize = layout.DefaultGameplayAreaSize;
        }

        board.ApplyGridSize(gridW, gridH);
        Canvas.ForceUpdateCanvases();

        Vector2 boardSize = boardRect.sizeDelta;
        Vector2 cell = board.VisualCellSize;
        float expectedCell = BoardLayoutMath.ComputeSquareCellSize(
            gridW,
            gridH,
            areaSize.x,
            areaSize.y,
            layout.GameplayAreaPadding.x,
            layout.GameplayAreaPadding.y);
        Vector2 expectedBoard = BoardLayoutMath.ComputeBoardSize(gridW, gridH, expectedCell, board.GridPadding);

        Rect cellGrid = ComputeCellGridRect(board, gridW, gridH);
        bool squareCells = Mathf.Abs(cell.x - cell.y) < 0.01f;
        bool cellMatchesExpected = Mathf.Abs(cell.x - expectedCell) < 0.5f;
        bool boardMatchesExpected = Vector2.Distance(boardSize, expectedBoard) < 1f;
        bool centered = Mathf.Abs(boardRect.anchoredPosition.x) < 0.01f && Mathf.Abs(boardRect.anchoredPosition.y) < 0.01f;
        bool fitsWidth = boardSize.x <= areaSize.x - layout.GameplayAreaPadding.x * 2f + 0.5f;
        bool fitsHeight = boardSize.y <= areaSize.y - layout.GameplayAreaPadding.y * 2f + 0.5f;
        bool gridInsideBoard = cellGrid.width <= boardSize.x && cellGrid.height <= boardSize.y;

        bool pass = squareCells && cellMatchesExpected && boardMatchesExpected && centered && fitsWidth && fitsHeight && gridInsideBoard;

        report.AppendLine($"--- {gridW}x{gridH} => {(pass ? "PASS" : "FAIL")} ---");
        report.AppendLine($"  area={FormatSize(areaSize)} board={FormatSize(boardSize)} expectedBoard={FormatSize(expectedBoard)}");
        report.AppendLine($"  cell={cell.x:F2} expectedCell={expectedCell:F2} square={squareCells}");
        report.AppendLine($"  boardAnchoredPos={boardRect.anchoredPosition} centered={centered}");
        report.AppendLine($"  fitsArea W={fitsWidth} H={fitsHeight} cellGrid={FormatRect(cellGrid)}");
        report.AppendLine();
    }

    private static void RunAspectRatioTests(
        StringBuilder report,
        BoardLayout layout,
        BoardManager board,
        RectTransform boardRect,
        RectTransform gameplayArea)
    {
        if (gameplayArea == null)
        {
            report.AppendLine("SKIP: no gameplay area rect to resize.");
            return;
        }

        Vector2 originalSize = gameplayArea.sizeDelta;
        Vector2[] simulated = {
            new Vector2(900f, 1600f),
            new Vector2(1080f, 1920f),
            new Vector2(1200f, 900f),
            originalSize
        };

        int passCount = 0;
        foreach (Vector2 sim in simulated)
        {
            gameplayArea.sizeDelta = sim;
            Canvas.ForceUpdateCanvases();
            layout.RefreshLayout();
            Canvas.ForceUpdateCanvases();

            Vector2 area = gameplayArea.rect.size;
            Vector2 boardSize = boardRect.sizeDelta;
            Vector2 cell = board.VisualCellSize;
            bool square = Mathf.Abs(cell.x - cell.y) < 0.01f;
            bool fits = boardSize.x <= area.x - layout.GameplayAreaPadding.x * 2f + 0.5f
                && boardSize.y <= area.y - layout.GameplayAreaPadding.y * 2f + 0.5f;
            bool centered = Mathf.Abs(boardRect.anchoredPosition.x) < 0.01f && Mathf.Abs(boardRect.anchoredPosition.y) < 0.01f;
            bool pass = square && fits && centered;
            if (pass)
            {
                passCount++;
            }

            report.AppendLine(
                $"  aspect {FormatSize(sim)} -> area {FormatSize(area)} board {FormatSize(boardSize)} cell {cell.x:F2} {(pass ? "PASS" : "FAIL")}");
        }

        gameplayArea.sizeDelta = originalSize;
        Canvas.ForceUpdateCanvases();
        layout.RefreshLayout();

        report.AppendLine($"Aspect-ratio resize overall: {(passCount == simulated.Length ? "PASS" : "FAIL")} ({passCount}/{simulated.Length})");
        report.AppendLine();
    }

    private static Rect ComputeCellGridRect(BoardManager board, int gridW, int gridH)
    {
        if (gridW <= 0 || gridH <= 0)
        {
            return Rect.zero;
        }

        Vector2 cell = board.VisualCellSize;
        Vector3 bl = board.GridToLocal(Vector2Int.zero);
        Vector3 tr = board.GridToLocal(new Vector2Int(gridW - 1, gridH - 1));
        float half = cell.x * 0.5f;
        return new Rect(bl.x - half, bl.y - half, (tr.x - bl.x) + cell.x, (tr.y - bl.y) + cell.y);
    }

    private static string FormatSize(Vector2 size) => $"{size.x:F1} x {size.y:F1}";

    private static string FormatRect(Rect rect) => $"({rect.x:F1},{rect.y:F1}) {rect.width:F1}x{rect.height:F1}";

    private static string GetPath(Transform t)
    {
        if (t == null)
        {
            return "<null>";
        }

        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }

    private static void WriteReport(StringBuilder report, bool quitWhenDone)
    {
        File.WriteAllText(ReportPath, report.ToString());
        AssetDatabase.Refresh();
        //Debug.Log(report.ToString());

        if (quitWhenDone)
        {
            EditorApplication.Exit(0);
        }
    }
}
#endif
