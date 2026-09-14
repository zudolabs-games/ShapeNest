using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World3D presentation for permanent static board obstacles (<see cref="BoardManager.StaticBlockedCells"/>).
/// </summary>
[DisallowMultipleComponent]
public class StaticObstacleView3D : MonoBehaviour
{
    [SerializeField]
    private Transform cellsRoot;

    [SerializeField]
    private float plateHeight = 0.16f;

    [SerializeField]
    private float plateLift = 0.36f;

    [SerializeField]
    private float footprintFactor = 0.94f;

    private static Material sharedPlateMaterial;
    private readonly List<Transform> cellPlates = new List<Transform>();
    private readonly List<Vector2Int> boundCells = new List<Vector2Int>();

    public void SyncCells(IReadOnlyCollection<Vector2Int> cells, Material plateMaterial)
    {
        if (plateMaterial != null)
        {
            sharedPlateMaterial = plateMaterial;
        }

        EnsureRoot();
        boundCells.Clear();
        if (cells != null)
        {
            foreach (Vector2Int cell in cells)
            {
                boundCells.Add(cell);
            }
        }

        if (boundCells.Count == 0)
        {
            ClearPlates();
            gameObject.SetActive(false);
            return;
        }

        ApplyLayout();
    }

    public void ClearBind()
    {
        boundCells.Clear();
        ClearPlates();
        gameObject.SetActive(false);
    }

    private void ApplyLayout()
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter == null || boundCells.Count == 0)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        EnsurePlateCount(boundCells.Count);

        float cell = presenter.CellWorldSize;
        float scale = cell / BoardAdaptivePresentation3D.ReferenceCellSize;
        float plateSize = cell * footprintFactor;
        float height = plateHeight * scale;
        float lift = plateLift * scale;
        IGridSpace space = presenter.GridSpace;
        float y = presenter.CellSurfaceWorldY + lift;

        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < boundCells.Count; i++)
        {
            Transform plate = cellPlates[i];
            Vector3 world = space.GridToWorld(boundCells[i]);
            world.y = y;
            plate.position = world;
            plate.localScale = new Vector3(plateSize, height, plateSize);
            plate.gameObject.SetActive(true);
            centroid += world;
        }

        transform.position = Vector3.zero;

        for (int i = boundCells.Count; i < cellPlates.Count; i++)
        {
            cellPlates[i].gameObject.SetActive(false);
        }
    }

    private void EnsureRoot()
    {
        if (cellsRoot == null)
        {
            var root = transform.Find("Cells");
            cellsRoot = root != null ? root : new GameObject("Cells").transform;
            cellsRoot.SetParent(transform, false);
        }

        cellPlates.Clear();
        for (int i = 0; i < cellsRoot.childCount; i++)
        {
            cellPlates.Add(cellsRoot.GetChild(i));
        }
    }

    private void EnsurePlateCount(int count)
    {
        while (cellPlates.Count < count)
        {
            var plate = new GameObject("StaticObstacleCell_" + cellPlates.Count);
            plate.transform.SetParent(cellsRoot, false);
            var filter = plate.AddComponent<MeshFilter>();
            filter.sharedMesh = BoardMeshFactory3D.GetRoundedBox(1f, 1f, 1f, 0.20f, 4);
            MeshRenderer renderer = plate.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            if (sharedPlateMaterial != null)
            {
                renderer.sharedMaterial = sharedPlateMaterial;
            }

            cellPlates.Add(plate.transform);
        }
    }

    private void ClearPlates()
    {
        for (int i = 0; i < cellPlates.Count; i++)
        {
            if (cellPlates[i] != null)
            {
                cellPlates[i].gameObject.SetActive(false);
            }
        }
    }
}
