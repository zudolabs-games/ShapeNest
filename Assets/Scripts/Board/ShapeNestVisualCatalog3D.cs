using UnityEngine;

/// <summary>
/// Presentation-only mapping from gameplay identity to optional designer 3D prefabs.
/// Empty slots keep the current procedural World3D visuals. Does not own gameplay state.
/// Prefabs should be authored at unit local scale (XZ footprint 1, height 1); presenters apply layout scale.
/// </summary>
[CreateAssetMenu(fileName = "ShapeNestVisualCatalog3D", menuName = "Shape Nest/3D Visual Catalog", order = 1)]
public class ShapeNestVisualCatalog3D : ScriptableObject
{
    private static ShapeNestVisualCatalog3D active;

    [Header("Board (optional)")]
    [Tooltip("Leave empty to keep procedural cell tiles.")]
    public GameObject cellPrefab;

    [Tooltip("Leave empty to keep the procedural board slab.")]
    public GameObject boardSurfacePrefab;

    [Tooltip("Leave empty to keep the procedural frame walls.")]
    public GameObject boardFramePrefab;

    [Header("Blocks (optional — one prefab per ShapeType)")]
    [Tooltip("Leave empty to keep the procedural block mesh for that shape.")]
    public GameObject blockSquare;
    public GameObject blockCircle;
    public GameObject blockTriangle;
    public GameObject blockDiamond;
    public GameObject blockHexagon;
    public GameObject blockStar;

    [Header("Nests / targets (optional)")]
    [Tooltip("Leave empty to keep the procedural nest mesh for that shape.")]
    public GameObject nestSquare;
    public GameObject nestCircle;
    public GameObject nestTriangle;
    public GameObject nestDiamond;
    public GameObject nestHexagon;
    public GameObject nestStar;

    [Header("Obstacles (optional)")]
    [Tooltip("Leave empty to keep the procedural ice shell.")]
    public GameObject icePrefab;

    [Tooltip("Leave empty to keep procedural shutter plates. Instantiated once per covered cell.")]
    public GameObject shutterPrefab;

    [Header("Later (optional, unused until assigned)")]
    public GameObject magnetBoosterPrefab;
    public GameObject matchVfxPrefab;
    public GameObject environmentPrefab;

    public static ShapeNestVisualCatalog3D Active
    {
        get
        {
            if (active != null)
            {
                return active;
            }

            BoardPresentationController controller =
                Object.FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Include);
            return controller != null ? controller.VisualCatalog : null;
        }
    }

    public static void Bind(ShapeNestVisualCatalog3D catalog)
    {
        active = catalog;
    }

    public static void Unbind(ShapeNestVisualCatalog3D catalog)
    {
        if (active == catalog)
        {
            active = null;
        }
    }

    public static bool TryGetCellPrefab(out GameObject prefab)
    {
        ShapeNestVisualCatalog3D catalog = Active;
        prefab = catalog != null ? catalog.cellPrefab : null;
        return prefab != null;
    }

    public static bool TryGetBoardSurfacePrefab(out GameObject prefab)
    {
        ShapeNestVisualCatalog3D catalog = Active;
        prefab = catalog != null ? catalog.boardSurfacePrefab : null;
        return prefab != null;
    }

    public static bool TryGetBoardFramePrefab(out GameObject prefab)
    {
        ShapeNestVisualCatalog3D catalog = Active;
        prefab = catalog != null ? catalog.boardFramePrefab : null;
        return prefab != null;
    }

    public static bool TryGetPiecePrefab(ShapeType shape, bool asNest, out GameObject prefab)
    {
        prefab = asNest ? ResolveNest(shape) : ResolveBlock(shape);
        return prefab != null;
    }

    public static bool TryGetIcePrefab(out GameObject prefab)
    {
        ShapeNestVisualCatalog3D catalog = Active;
        prefab = catalog != null ? catalog.icePrefab : null;
        return prefab != null;
    }

    public static bool TryGetShutterPrefab(out GameObject prefab)
    {
        ShapeNestVisualCatalog3D catalog = Active;
        prefab = catalog != null ? catalog.shutterPrefab : null;
        return prefab != null;
    }

    private static GameObject ResolveBlock(ShapeType shape)
    {
        ShapeNestVisualCatalog3D catalog = Active;
        if (catalog == null)
        {
            return null;
        }

        switch (shape)
        {
            case ShapeType.Circle:
                return catalog.blockCircle;
            case ShapeType.Triangle:
                return catalog.blockTriangle;
            case ShapeType.Diamond:
                return catalog.blockDiamond;
            case ShapeType.Hexagon:
                return catalog.blockHexagon;
            case ShapeType.Star:
                return catalog.blockStar;
            default:
                return catalog.blockSquare;
        }
    }

    private static GameObject ResolveNest(ShapeType shape)
    {
        ShapeNestVisualCatalog3D catalog = Active;
        if (catalog == null)
        {
            return null;
        }

        switch (shape)
        {
            case ShapeType.Circle:
                return catalog.nestCircle;
            case ShapeType.Triangle:
                return catalog.nestTriangle;
            case ShapeType.Diamond:
                return catalog.nestDiamond;
            case ShapeType.Hexagon:
                return catalog.nestHexagon;
            case ShapeType.Star:
                return catalog.nestStar;
            default:
                return catalog.nestSquare;
        }
    }
}
