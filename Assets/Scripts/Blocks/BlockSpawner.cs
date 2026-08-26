using UnityEngine;

/// <summary>
/// Legacy debug helper for Instantiating Block prefabs onto the Board.
/// Runtime levels are owned by <see cref="LevelManager"/> — this component must not
/// auto-spawn on Start (even if re-enabled on the Board).
/// </summary>
public class BlockSpawner : MonoBehaviour
{
    [SerializeField]
    private Block blockPrefab;

    [SerializeField]
    private BoardManager boardManager;

    /// <summary>
    /// TEMPORARY hardcoded test layout. Call manually from the Inspector/context menu only.
    /// Positions are Board grid cells; Block.Initialize applies presentation via BoardManager grid-space.
    /// </summary>
    [ContextMenu("Spawn Test Level (Debug)")]
    public void SpawnTestLevel()
    {
        SpawnBlock(new Vector2Int(1, 2), ShapeType.Square, MoveDirection.Any);
        SpawnBlock(new Vector2Int(3, 2), ShapeType.Circle, MoveDirection.Left);
        SpawnBlock(new Vector2Int(2, 4), ShapeType.Triangle, MoveDirection.Down);
    }

    public Block SpawnBlock(Vector2Int gridPosition, ShapeType shapeType, MoveDirection moveDirection)
    {
        if (blockPrefab == null)
        {
            Debug.LogError("BlockSpawner: Block prefab is not assigned.", this);
            return null;
        }

        if (boardManager == null)
        {
            Debug.LogError("BlockSpawner: BoardManager is not assigned.", this);
            return null;
        }

        var boardRect = (RectTransform)boardManager.transform;
        Block block = Instantiate(blockPrefab, boardRect, false);
        block.Initialize(boardManager, gridPosition);
        block.ShapeType = shapeType;
        block.MoveDirection = moveDirection;

        return block;
    }
}
