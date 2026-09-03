using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Presentation-only overlays for occupied cells during user drag or Magnet movement.
/// User drag: visibility follows <see cref="BlockMover.IsDragAiming"/> on the active
/// block; each occupied chain cell uses <see cref="BlockMover.VisualGridCell"/> +
/// <see cref="Block.GetLocalCell"/>. Magnet: one tile per
/// <see cref="BlockMover.IsMagnetPresenting"/> mover at that mover's
/// <see cref="BlockMover.VisualGridCell"/>. Nest-entry travel is not board movement,
/// so the overlay hides even if <see cref="BlockMover.IsMatchEntryPresenting"/> is true.
/// Does not pick cells, validate moves, or tint the shared board-cell material.
/// </summary>
[DisallowMultipleComponent]
public class BoardCellDestinationHighlight3D : MonoBehaviour
{
    private const string OverlayName = "CellDestinationHighlight3D";
    private const string TileNamePrefix = "Highlight_";
    private const float SurfaceClearance = 0.045f;
    private const float TileFaceFactor = 0.88f;
    private const float OverlayThickness = 0.045f;
    private const float FillAlpha = 0.30f;
    private const float RimAlpha = 0.44f;
    private const float CornerFactor = 0.16f;
    private const float MoveDuration = 0.12f;
    private const float BreathAmplitude = 0.028f;
    private const float BreathHz = 0.55f;

    // Soft light blue (#9AD9FF) with a slightly brighter rim of the same hue.
    private static readonly Color FillColor = new Color(0.6039216f, 0.8509804f, 1f, FillAlpha);
    private static readonly Color RimColor = new Color(0.75f, 0.91f, 1f, RimAlpha);

    private static Material sharedFillMaterial;
    private static Material sharedRimMaterial;
    private static Material[] sharedOverlayMaterials;

    private BoardPresenter3D presenter;
    private readonly List<OverlayTile> tiles = new List<OverlayTile>();
    private readonly List<BlockMover> magnetMovers = new List<BlockMover>();
    private readonly HashSet<int> magnetMoverIds = new HashSet<int>();
    private float builtFace;

    private sealed class OverlayTile
    {
        public Transform transform;
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public Vector2Int boundCell;
        public bool hasBoundCell;
        public bool isMoving;
        public Vector3 moveFromWorld;
        public Vector3 moveToWorld;
        public float moveElapsed;
        public int boundMoverId;
    }

    public static BoardCellDestinationHighlight3D Ensure(BoardPresenter3D boardPresenter)
    {
        if (boardPresenter == null)
        {
            return null;
        }

        Transform existing = boardPresenter.transform.Find(OverlayName);
        BoardCellDestinationHighlight3D highlight = existing != null
            ? existing.GetComponent<BoardCellDestinationHighlight3D>()
            : boardPresenter.GetComponentInChildren<BoardCellDestinationHighlight3D>(true);

        if (highlight == null)
        {
            var go = new GameObject(OverlayName);
            go.transform.SetParent(boardPresenter.transform, false);
            highlight = go.AddComponent<BoardCellDestinationHighlight3D>();
        }

        highlight.presenter = boardPresenter;
        highlight.EnsureController();
        return highlight;
    }

    public static void HideImmediate(BoardPresenter3D boardPresenter)
    {
        if (boardPresenter == null)
        {
            return;
        }

        Transform existing = boardPresenter.transform.Find(OverlayName);
        if (existing == null)
        {
            return;
        }

        BoardCellDestinationHighlight3D highlight = existing.GetComponent<BoardCellDestinationHighlight3D>();
        if (highlight != null)
        {
            highlight.HideNow();
        }
    }

    public void Sync(Block[] blocks)
    {
        if (!isActiveAndEnabled || presenter == null)
        {
            HideNow();
            return;
        }

        EnsureController();
        if (TryResolveUserDragAimingBlock(blocks, out Block block, out BlockMover mover))
        {
            ShowUserDragFootprint(block, mover);
            return;
        }

        CollectMagnetPresentationMovers(blocks);
        if (magnetMovers.Count > 0)
        {
            ShowMagnetMovers();
            return;
        }

        HideNow();
    }

    private void OnDisable()
    {
        HideNow();
    }

    private void OnDestroy()
    {
        HideNow();
    }

    private void LateUpdate()
    {
        if (!isActiveAndEnabled || tiles.Count == 0)
        {
            return;
        }

        float breath = 1f + (BreathAmplitude * Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f * BreathHz)));
        if (!PieceMotionMath.IsFinite(breath))
        {
            breath = 1f;
        }

        for (int i = 0; i < tiles.Count; i++)
        {
            OverlayTile tile = tiles[i];
            if (tile == null
                || tile.transform == null
                || tile.meshRenderer == null
                || !tile.meshRenderer.enabled)
            {
                continue;
            }

            tile.transform.localScale = Vector3.one * breath;
        }
    }

    private bool TryResolveUserDragAimingBlock(Block[] blocks, out Block block, out BlockMover mover)
    {
        block = null;
        mover = null;
        if (blocks == null || presenter == null)
        {
            return false;
        }

        for (int i = 0; i < blocks.Length; i++)
        {
            Block candidate = blocks[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            BlockMover candidateMover = candidate.GetComponent<BlockMover>();
            if (candidateMover == null
                || !candidateMover.IsDragAiming
                || candidateMover.IsMagnetPresenting)
            {
                continue;
            }

            block = candidate;
            mover = candidateMover;
            return true;
        }

        return false;
    }

    private void ShowUserDragFootprint(Block block, BlockMover mover)
    {
        if (block == null || mover == null)
        {
            HideNow();
            return;
        }

        ClearMoverBindings();
        Vector2Int origin = mover.VisualGridCell;
        int count = Mathf.Max(1, block.CellCount);
        EnsureTileCount(count);
        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = origin + block.GetLocalCell(i);
            if (IsCellOnBoard(cell))
            {
                ShowAt(tiles[i], cell);
            }
            else
            {
                HideTile(tiles[i]);
            }
        }
    }

    private void CollectMagnetPresentationMovers(Block[] blocks)
    {
        magnetMovers.Clear();
        magnetMoverIds.Clear();
        if (blocks == null)
        {
            return;
        }

        for (int i = 0; i < blocks.Length; i++)
        {
            Block candidate = blocks[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            BlockMover candidateMover = candidate.GetComponent<BlockMover>();
            if (candidateMover == null
                || !candidateMover.IsMagnetPresenting
                || candidateMover.IsMatchEntryPresenting)
            {
                continue;
            }

            int id = candidateMover.GetInstanceID();
            if (!magnetMoverIds.Add(id))
            {
                continue;
            }

            magnetMovers.Add(candidateMover);
        }
    }

    private void ShowMagnetMovers()
    {
        for (int i = 0; i < tiles.Count; i++)
        {
            OverlayTile tile = tiles[i];
            if (tile == null || tile.boundMoverId == 0)
            {
                continue;
            }

            if (!magnetMoverIds.Contains(tile.boundMoverId))
            {
                HideTile(tile);
            }
        }

        for (int m = 0; m < magnetMovers.Count; m++)
        {
            BlockMover mover = magnetMovers[m];
            if (mover == null)
            {
                continue;
            }

            int id = mover.GetInstanceID();
            OverlayTile tile = FindTileByMoverId(id) ?? FindUnboundTile();
            if (tile == null)
            {
                tile = CreateTile(tiles.Count);
                tiles.Add(tile);
            }

            tile.boundMoverId = id;
            Vector2Int cell = mover.VisualGridCell;
            if (IsCellOnBoard(cell))
            {
                ShowAt(tile, cell);
            }
            else if (tile.meshRenderer != null && tile.meshRenderer.enabled)
            {
                tile.meshRenderer.enabled = false;
            }
        }

        for (int i = 0; i < tiles.Count; i++)
        {
            OverlayTile tile = tiles[i];
            if (tile == null || tile.boundMoverId == 0 || !magnetMoverIds.Contains(tile.boundMoverId))
            {
                HideTile(tile);
            }
        }

        ReleaseUnboundTiles();
    }

    private OverlayTile FindTileByMoverId(int moverId)
    {
        if (moverId == 0)
        {
            return null;
        }

        for (int i = 0; i < tiles.Count; i++)
        {
            OverlayTile tile = tiles[i];
            if (tile != null && tile.boundMoverId == moverId)
            {
                return tile;
            }
        }

        return null;
    }

    private OverlayTile FindUnboundTile()
    {
        for (int i = 0; i < tiles.Count; i++)
        {
            OverlayTile tile = tiles[i];
            if (tile != null && tile.boundMoverId == 0)
            {
                return tile;
            }
        }

        return null;
    }

    private void ReleaseUnboundTiles()
    {
        for (int i = tiles.Count - 1; i >= 0; i--)
        {
            OverlayTile tile = tiles[i];
            if (tile != null && tile.boundMoverId != 0)
            {
                continue;
            }

            if (tile != null && tile.transform != null)
            {
                Destroy(tile.transform.gameObject);
            }

            tiles.RemoveAt(i);
        }

        for (int i = 0; i < tiles.Count; i++)
        {
            OverlayTile tile = tiles[i];
            if (tile != null && tile.transform != null)
            {
                tile.transform.name = TileNamePrefix + i;
            }
        }
    }

    private void ClearMoverBindings()
    {
        for (int i = 0; i < tiles.Count; i++)
        {
            OverlayTile tile = tiles[i];
            if (tile != null)
            {
                tile.boundMoverId = 0;
            }
        }
    }

    private bool IsCellOnBoard(Vector2Int cell)
    {
        int width = presenter.BuiltWidth;
        int height = presenter.BuiltHeight;
        return cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;
    }

    private void ShowAt(OverlayTile tile, Vector2Int cell)
    {
        if (tile == null || tile.transform == null)
        {
            return;
        }

        EnsureMeshForCurrentCellSize(tile);
        Vector3 targetWorld = CellWorldPosition(cell);
        if (!tile.hasBoundCell || tile.meshRenderer == null || !tile.meshRenderer.enabled)
        {
            tile.boundCell = cell;
            tile.hasBoundCell = true;
            tile.isMoving = false;
            tile.moveElapsed = 0f;
            ApplyWorldPose(tile, targetWorld);
        }
        else if (tile.boundCell != cell)
        {
            // Retarget from the current visual pose so fast hops do not snap back.
            tile.moveFromWorld = tile.isMoving ? CurrentInterpolatedWorld(tile) : tile.transform.position;
            tile.moveToWorld = targetWorld;
            tile.moveElapsed = 0f;
            tile.isMoving = true;
            tile.boundCell = cell;
        }
        else if (tile.isMoving)
        {
            tile.moveToWorld = targetWorld;
        }

        StepMove(tile);
        if (tile.meshRenderer != null && !tile.meshRenderer.enabled)
        {
            tile.meshRenderer.enabled = true;
        }
    }

    private void StepMove(OverlayTile tile)
    {
        if (tile == null || !tile.isMoving)
        {
            return;
        }

        float dt = Time.deltaTime;
        if (dt < 0f || float.IsNaN(dt) || float.IsInfinity(dt))
        {
            dt = 0f;
        }

        tile.moveElapsed += dt;
        float t = MoveDuration > 0.0001f ? Mathf.Clamp01(tile.moveElapsed / MoveDuration) : 1f;
        float smooth = t * t * (3f - (2f * t));
        ApplyWorldPose(tile, Vector3.LerpUnclamped(tile.moveFromWorld, tile.moveToWorld, smooth));
        if (t >= 1f)
        {
            tile.isMoving = false;
            ApplyWorldPose(tile, tile.moveToWorld);
        }
    }

    private static Vector3 CurrentInterpolatedWorld(OverlayTile tile)
    {
        if (tile == null || tile.transform == null)
        {
            return Vector3.zero;
        }

        if (!tile.isMoving)
        {
            return tile.transform.position;
        }

        float t = MoveDuration > 0.0001f ? Mathf.Clamp01(tile.moveElapsed / MoveDuration) : 1f;
        float smooth = t * t * (3f - (2f * t));
        return Vector3.LerpUnclamped(tile.moveFromWorld, tile.moveToWorld, smooth);
    }

    private Vector3 CellWorldPosition(Vector2Int cell)
    {
        GridSpace3D space = presenter.GridSpace3D;
        if (space == null)
        {
            return transform.position;
        }

        float lift = (OverlayThickness * 0.5f) + SurfaceClearance;
        Vector3 world = space.GridToWorld(cell);
        world += presenter.transform.up * lift;
        return world;
    }

    private void ApplyWorldPose(OverlayTile tile, Vector3 world)
    {
        if (tile == null || tile.transform == null || presenter == null)
        {
            return;
        }

        tile.transform.SetPositionAndRotation(world, presenter.transform.rotation);
        // Scale is driven by LateUpdate breath; pose only writes position/rotation.
    }

    private void EnsureMeshForCurrentCellSize(OverlayTile tile)
    {
        if (presenter == null || tile == null)
        {
            return;
        }

        AssignHighlightMesh(tile, presenter.CellWorldSize * TileFaceFactor);
    }

    private void AssignHighlightMesh(OverlayTile tile, float face)
    {
        if (tile == null || tile.meshFilter == null)
        {
            return;
        }

        Mesh mesh = tile.meshFilter.sharedMesh;
        bool needsRebuild = !Mathf.Approximately(face, builtFace)
            || mesh == null
            || mesh.name != "BoardHighlightTile";
        if (needsRebuild)
        {
            builtFace = face;
            mesh = BoardMeshFactory3D.GetHighlightTile(
                face,
                OverlayThickness,
                face,
                face * CornerFactor);
        }

        tile.meshFilter.sharedMesh = mesh;
    }

    private void HideNow()
    {
        for (int i = 0; i < tiles.Count; i++)
        {
            HideTile(tiles[i]);
        }

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null || !child.name.StartsWith(TileNamePrefix))
            {
                continue;
            }

            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.enabled)
            {
                renderer.enabled = false;
            }
        }
    }

    private static void HideTile(OverlayTile tile)
    {
        if (tile == null)
        {
            return;
        }

        tile.hasBoundCell = false;
        tile.isMoving = false;
        tile.moveElapsed = 0f;
        tile.boundMoverId = 0;
        if (tile.meshRenderer != null && tile.meshRenderer.enabled)
        {
            tile.meshRenderer.enabled = false;
        }
    }

    private void EnsureController()
    {
        DisableLegacyRootOverlay();
        if (presenter != null && tiles.Count > 0)
        {
            float face = presenter.CellWorldSize * TileFaceFactor;
            for (int i = 0; i < tiles.Count; i++)
            {
                AssignHighlightMesh(tiles[i], face);
            }
        }
    }

    private void DisableLegacyRootOverlay()
    {
        MeshRenderer rootRenderer = GetComponent<MeshRenderer>();
        if (rootRenderer != null && rootRenderer.enabled)
        {
            rootRenderer.enabled = false;
        }
    }

    private void EnsureTileCount(int count)
    {
        if (count < 1)
        {
            count = 1;
        }

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == null || !child.name.StartsWith(TileNamePrefix))
            {
                continue;
            }

            int index;
            if (!int.TryParse(child.name.Substring(TileNamePrefix.Length), out index)
                || index < 0
                || index >= count)
            {
                MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = false;
                }

                Destroy(child.gameObject);
            }
        }

        while (tiles.Count > count)
        {
            tiles.RemoveAt(tiles.Count - 1);
        }

        while (tiles.Count < count)
        {
            tiles.Add(null);
        }

        for (int i = 0; i < count; i++)
        {
            OverlayTile tile = tiles[i];
            if (tile == null || tile.transform == null)
            {
                tiles[i] = CreateTile(i);
            }
        }
    }

    private OverlayTile CreateTile(int index)
    {
        Transform existing = transform.Find(TileNamePrefix + index);
        if (existing != null)
        {
            return BindTile(existing);
        }

        var go = new GameObject(TileNamePrefix + index);
        go.transform.SetParent(transform, false);
        return BindTile(go.transform);
    }

    private OverlayTile BindTile(Transform tileTransform)
    {
        if (tileTransform == null)
        {
            return null;
        }

        MeshFilter filter = tileTransform.GetComponent<MeshFilter>();
        if (filter == null)
        {
            filter = tileTransform.gameObject.AddComponent<MeshFilter>();
        }

        MeshRenderer renderer = tileTransform.GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            renderer = tileTransform.gameObject.AddComponent<MeshRenderer>();
        }

        renderer.sharedMaterials = GetOverlayMaterials();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.enabled = false;

        var tile = new OverlayTile
        {
            transform = tileTransform,
            meshFilter = filter,
            meshRenderer = renderer
        };
        if (presenter != null)
        {
            AssignHighlightMesh(tile, presenter.CellWorldSize * TileFaceFactor);
        }

        return tile;
    }

    private static Material[] GetOverlayMaterials()
    {
        if (sharedOverlayMaterials != null)
        {
            return sharedOverlayMaterials;
        }

        sharedFillMaterial = CreateUnlitTransparent("BoardCellDestinationHighlight3D_Fill", FillColor);
        sharedRimMaterial = CreateUnlitTransparent("BoardCellDestinationHighlight3D_Rim", RimColor);
        sharedOverlayMaterials = new[] { sharedFillMaterial, sharedRimMaterial };
        return sharedOverlayMaterials;
    }

    private static Material CreateUnlitTransparent(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard");
        var material = new Material(shader)
        {
            name = materialName,
            color = color
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        }

        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.renderQueue = 3100;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", 0f);
        }

        material.SetShaderPassEnabled("ShadowCaster", false);
        return material;
    }
}
