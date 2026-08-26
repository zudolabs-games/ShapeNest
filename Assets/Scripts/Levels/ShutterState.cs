using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime state for a level shutter. Covers board cells independently of blocks/targets.
/// Every successful match reduces durability by one; at zero the shutter opens.
/// World3D presentation is handled by <see cref="ShutterView3D"/>.
/// </summary>
public class ShutterState : MonoBehaviour
{
    private BoardManager boardManager;
    private readonly List<Vector2Int> cells = new List<Vector2Int>();
    private int durability;

    public int Durability => durability;
    public bool IsClosed => durability > 0;
    public IReadOnlyList<Vector2Int> Cells => cells;

    public void SetUiPresentationVisible(bool visible)
    {
        // Phase 11: UI shutter overlay removed.
    }

    public void Configure(BoardManager board, LevelShutterData data)
    {
        boardManager = board;
        cells.Clear();
        if (data != null && data.cells != null)
        {
            cells.AddRange(data.cells);
        }

        durability = Mathf.Max(1, data != null ? data.durability : 1);
        if (boardManager != null)
        {
            boardManager.RegisterShutter(this);
        }
    }

    public bool CoversCell(Vector2Int cell)
    {
        return IsClosed && cells.Contains(cell);
    }

    public void ConsumeSuccessfulMatch()
    {
        if (!IsClosed)
        {
            return;
        }

        durability = Mathf.Max(0, durability - 1);
        if (!IsClosed && boardManager != null)
        {
            boardManager.UnregisterShutter(this);
        }
    }

    private void OnDestroy()
    {
        if (boardManager != null)
        {
            boardManager.UnregisterShutter(this);
        }
    }

    public void RefreshLayoutVisuals()
    {
        // No UI shutter layout after Phase 11.
    }
}
