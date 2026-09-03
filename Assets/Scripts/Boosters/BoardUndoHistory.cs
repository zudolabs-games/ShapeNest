using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One-step pre-action board snapshot for Undo. Stores block anchor positions only;
/// identity, chains, nesting, Ice, and Shutters are preserved on existing Block instances.
/// </summary>
[DisallowMultipleComponent]
public class BoardUndoHistory : MonoBehaviour
{
    [Serializable]
    private sealed class BoardSnapshot
    {
        public Dictionary<Block, Vector2Int> Positions = new Dictionary<Block, Vector2Int>();

        public bool HasData => Positions != null && Positions.Count > 0;
    }

    private BoardSnapshot pendingSnapshot;
    private BoardSnapshot activeSnapshot;

    public bool HasUndoableSnapshot => activeSnapshot != null && activeSnapshot.HasData;

    public event Action OnHistoryChanged;

    public static BoardUndoHistory Resolve()
    {
        return FindFirstObjectByType<BoardUndoHistory>(FindObjectsInactive.Exclude);
    }

    public void ClearAll(string reason = null)
    {
        pendingSnapshot = null;
        activeSnapshot = null;
        OnHistoryChanged?.Invoke();
    }

    /// <summary>Captures the current board as the active undo target (e.g. before Shuffle).</summary>
    public void CaptureActiveSnapshot(BoardManager board)
    {
        pendingSnapshot = null;
        activeSnapshot = CaptureBoard(board);
        OnHistoryChanged?.Invoke();
    }

    /// <summary>Captures state at drag start; committed only after a successful non-match move.</summary>
    public void BeginPendingCapture(BoardManager board)
    {
        pendingSnapshot = CaptureBoard(board);
    }

    public void CommitPendingAsActive()
    {
        if (pendingSnapshot == null || !pendingSnapshot.HasData)
        {
            pendingSnapshot = null;
            return;
        }

        activeSnapshot = pendingSnapshot;
        pendingSnapshot = null;
        OnHistoryChanged?.Invoke();
    }

    public void DiscardPending()
    {
        pendingSnapshot = null;
    }

    public bool TryGetActiveSnapshot(
        out Dictionary<Block, Vector2Int> positions,
        out Dictionary<Block, Vector2Int> currentPositions)
    {
        positions = null;
        currentPositions = null;
        if (activeSnapshot == null || !activeSnapshot.HasData)
        {
            return false;
        }

        positions = new Dictionary<Block, Vector2Int>(activeSnapshot.Positions);
        currentPositions = new Dictionary<Block, Vector2Int>();
        foreach (KeyValuePair<Block, Vector2Int> entry in positions)
        {
            Block block = entry.Key;
            if (block == null || block.IsSettled || !block.isActiveAndEnabled)
            {
                continue;
            }

            currentPositions[block] = block.GridPosition;
        }

        return currentPositions.Count > 0;
    }

    public void ConsumeActiveSnapshot()
    {
        activeSnapshot = null;
        OnHistoryChanged?.Invoke();
    }

    private static BoardSnapshot CaptureBoard(BoardManager board)
    {
        var snapshot = new BoardSnapshot();
        if (board == null)
        {
            return snapshot;
        }

        var blocks = new List<Block>();
        board.CollectUniqueBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsSettled || !block.isActiveAndEnabled)
            {
                continue;
            }

            snapshot.Positions[block] = block.GridPosition;
        }

        return snapshot;
    }
}
