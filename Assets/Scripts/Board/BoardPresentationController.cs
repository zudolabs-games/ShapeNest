using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds gameplay Blocks/Targets/Ice/Shutters to World3D presentation.
/// World3D is the only active board presentation (Phase 11).
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class BoardPresentationController : MonoBehaviour
{
    [SerializeField]
    private BoardPresentationMode mode = BoardPresentationMode.World3D;

    [SerializeField]
    private BoardManager boardManager;

    [SerializeField]
    private BoardPresenter3D boardPresenter3D;

    [SerializeField]
    [Tooltip("Optional. When unset, BoardBackground + RuntimeGrid under BoardManager are hidden.")]
    private GameObject[] uiBoardVisualRoots;

    [SerializeField]
    [Tooltip("Opaque gameplay Screen Space Overlay plate (GameplayCanvas/BG). Image is disabled in World3D so the 3D board can show through; GameObject stays active.")]
    private Image gamePlayOverlayBackground;

    [SerializeField]
    private BoardCamera3D boardCamera3D;

    [SerializeField]
    [Tooltip("Legacy UI-board camera. Kept inactive while World3D is the board presentation.")]
    private Camera uiModeCamera;

    [SerializeField]
    private Light boardLight;

    [Header("World3D piece presentation")]
    [SerializeField]
    private ShapeNestTheme theme;

    [SerializeField]
    [Tooltip("Optional designer 3D prefabs. Empty slots keep the current procedural World3D visuals.")]
    private ShapeNestVisualCatalog3D visualCatalog;

    [SerializeField]
    [Range(0.4f, 1f)]
    private float blockFootprintFactor = 0.76f;

    [SerializeField]
    [Range(0.4f, 1f)]
    private float nestFootprintFactor = 0.9f;

    [SerializeField]
    [Min(0.05f)]
    private float blockHeight = 0.26f;

    [SerializeField]
    [Min(0.03f)]
    private float nestHeight = 0.09f;

    [Header("Adaptive board presentation (Phase 14)")]
    [SerializeField]
    [Tooltip("Gameplay Area RectTransform. Auto-resolved from UIController/GameplayCanvas/Gameplay Area when empty.")]
    private RectTransform gameplayArea;

    [SerializeField]
    [Range(0.8f, 0.98f)]
    [Tooltip("Board footprint as a fraction of available Gameplay Area (0.92 ≈ 92%).")]
    private float presentationFitPadding = 0.92f;

    [SerializeField]
    private BoardEnvironment3D boardEnvironment;

    private readonly Dictionary<int, PieceView3D> worldViewsByBlockId = new Dictionary<int, PieceView3D>();
    private readonly Dictionary<int, PieceView3D> worldViewsByTargetId = new Dictionary<int, PieceView3D>();
    private readonly Dictionary<int, List<PieceView3D>> extraViewsByBlockId = new Dictionary<int, List<PieceView3D>>();
    private readonly Dictionary<int, List<PieceView3D>> extraViewsByTargetId = new Dictionary<int, List<PieceView3D>>();
    private readonly Dictionary<int, List<ChainConnectorView3D>> connectorsByBlockId = new Dictionary<int, List<ChainConnectorView3D>>();
    private readonly HashSet<PieceView3D> dissolvingViews = new HashSet<PieceView3D>();
    // Nested inner travelers are siblings under Pieces3D, not Block.WorldView / extras.
    // Owner value is Block.GetInstanceID() at spawn time.
    private readonly Dictionary<PieceView3D, int> nestedInnerTravelers = new Dictionary<PieceView3D, int>();
    private int destroyViewDepth;
    private PieceView3D chainTravelView;
    private int chainTravelBlockId;
    private int chainTravelCellIndex = -1;
    private bool chainTravelInnerLayer;
    private readonly Dictionary<int, IceView3D> worldViewsByIceId = new Dictionary<int, IceView3D>();
    private readonly Dictionary<int, ShutterView3D> worldViewsByShutterId = new Dictionary<int, ShutterView3D>();
    private int lastSyncedBlockCount = -1;
    private int lastSyncedTargetCount = -1;
    private int lastObstacleFingerprint = int.MinValue;
    private int lastAdaptiveRows = -1;
    private int lastAdaptiveColumns = -1;
    private float lastAdaptiveCell = -1f;
    private Vector2 lastAdaptiveAreaScreen = new Vector2(-1f, -1f);
    private Vector2 lastAdaptiveScreen = new Vector2(-1f, -1f);

    /// <summary>Optional designer 3D prefabs. Null / empty slots keep procedural World3D visuals.</summary>
    public ShapeNestVisualCatalog3D VisualCatalog => visualCatalog;

    /// <summary>Board presentation is always World3D after Phase 11.</summary>
    public BoardPresentationMode Mode => BoardPresentationMode.World3D;

    public bool IsWorld3DActive => true;

    /// <summary>
    /// When true, gameplay Block/Target (and Ice/Shutter) uGUI Images must stay hidden —
    /// World3D owns the board look. Call sites that would re-enable Images (e.g. match dissolve)
    /// must respect this. Returns false when no controller exists or UI presentation is active.
    /// </summary>
    public static bool SuppressGameplayPieceUiImages()
    {
        BoardPresentationController controller =
            FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Include);
        return controller != null && controller.IsWorld3DActive;
    }

    public static void BeginChainCellTravel(Block block, PieceView3D view, int cellIndex)
    {
        BoardPresentationController controller = FindController();
        if (controller != null)
        {
            controller.BeginChainCellTravelInternal(block, view, cellIndex);
        }
    }

    /// <summary>
    /// Presentation-only: hide the nested inner child and spawn a World3D traveler
    /// for that layer. Outer cell view stays at occupancy.
    /// </summary>
    public static PieceView3D BeginNestedInnerTravel(Block block, int cellIndex)
    {
        BoardPresentationController controller = FindController();
        return controller != null ? controller.BeginNestedInnerTravelInternal(block, cellIndex) : null;
    }

    /// <summary>Restore nested inner on the cell and discard a rejected inner traveler.</summary>
    public static void CancelNestedInnerTravel(Block block, int cellIndex)
    {
        BoardPresentationController controller = FindController();
        if (controller != null)
        {
            controller.CancelNestedInnerTravelInternal(block, cellIndex);
        }
    }

    public static void AdoptSurvivorWorldView(Block block, Vector2Int survivorWorld)
    {
        BoardPresentationController controller = FindController();
        if (controller != null)
        {
            controller.AdoptSurvivorWorldViewInternal(block, survivorWorld);
        }
    }

    public static void NotifyChainCellTravelCleared(Block block)
    {
        BoardPresentationController controller = FindController();
        if (controller != null)
        {
            controller.NotifyChainCellTravelClearedInternal(block);
        }
    }

    /// <summary>
    /// Presentation-only: destroy every NestedInnerTravel view immediately.
    /// Called on level clear so a traveler cannot leak into the next board.
    /// </summary>
    public static void ClearNestedInnerTravelers()
    {
        BoardPresentationController controller = FindController();
        if (controller != null)
        {
            controller.ClearAllNestedInnerTravelersImmediate();
        }
    }

    private static BoardPresentationController FindController()
    {
        return FindFirstObjectByType<BoardPresentationController>(FindObjectsInactive.Include);
    }

    private void Awake()
    {
        mode = BoardPresentationMode.World3D;
        ShapeNestVisualCatalog3D.Bind(visualCatalog);
        ResolveReferences();
        ApplyMode();
    }

    private void OnEnable()
    {
        mode = BoardPresentationMode.World3D;
        ShapeNestVisualCatalog3D.Bind(visualCatalog);
        ResolveReferences();
        ApplyMode();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        RefreshAdaptivePresentation(force: false);

        Block[] blocks = FindObjectsByType<Block>(FindObjectsSortMode.None);
        Target[] targets = FindObjectsByType<Target>(FindObjectsSortMode.None);
        IceState[] ices = FindObjectsByType<IceState>(FindObjectsSortMode.None);
        ShutterState[] shutters = FindObjectsByType<ShutterState>(FindObjectsSortMode.None);
        int obstacleFp = ComputeObstacleFingerprint(ices, shutters);
        bool dirty = blocks.Length != lastSyncedBlockCount
            || targets.Length != lastSyncedTargetCount
            || obstacleFp != lastObstacleFingerprint;
        if (!dirty)
        {
            dirty = NeedsPresentationResync(blocks, targets);
        }

        if (dirty)
        {
            SyncWorldPieceViews(blocks, targets);
            SyncWorldObstacleViews(ices, shutters);
        }
        else
        {
            RefreshWorldPositions(blocks, targets);
            RefreshObstaclePositions(ices, shutters);
        }

        FollowMultiCellWorldViews(blocks, targets);
        SyncDragDestinationHighlight(blocks);

        CleanupFinishedObstacleViews();
        CleanupFinishedPieceViews(blocks, targets);
        DestroyUntrackedPieceViews();
        DestroyUntrackedConnectors();

        // Layout systems may re-enable UI board chrome — keep it hidden.
        SetUiBoardVisualsActive(false);
    }

    private void OnDisable()
    {
        ShapeNestVisualCatalog3D.Unbind(visualCatalog);
        BoardCellDestinationHighlight3D.HideImmediate(boardPresenter3D);
    }

    private void SyncDragDestinationHighlight(Block[] blocks)
    {
        if (boardPresenter3D == null)
        {
            return;
        }

        BoardCellDestinationHighlight3D highlight = BoardCellDestinationHighlight3D.Ensure(boardPresenter3D);
        if (highlight != null)
        {
            highlight.Sync(blocks);
        }
    }

    private void OnValidate()
    {
        mode = BoardPresentationMode.World3D;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || !isActiveAndEnabled)
                {
                    return;
                }

                ResolveReferences();
                ApplyMode();
            };
            return;
        }
#endif
        if (isActiveAndEnabled)
        {
            ResolveReferences();
            ApplyMode();
        }
    }

    public void SetMode(BoardPresentationMode next)
    {
        // UI board presentation removed — World3D only.
        mode = BoardPresentationMode.World3D;
        ApplyMode();
    }

    public void ApplyMode()
    {
        ResolveReferences();
        mode = BoardPresentationMode.World3D;

        if (boardPresenter3D != null)
        {
            boardPresenter3D.gameObject.SetActive(true);
            boardPresenter3D.ApplyArtDirectionDefaults();
            boardPresenter3D.SetBoardManager(boardManager);
            _ = boardPresenter3D.VfxRoot;
        }

        SetUiBoardVisualsActive(false);

        if (boardCamera3D != null)
        {
            boardCamera3D.gameObject.SetActive(true);
            EnsureSingleAudioListener(boardCamera3D.gameObject);
        }

        if (uiModeCamera != null && (boardCamera3D == null || uiModeCamera != boardCamera3D.Camera))
        {
            uiModeCamera.gameObject.SetActive(false);
            AudioListener uiListener = uiModeCamera.GetComponent<AudioListener>();
            if (uiListener != null)
            {
                uiListener.enabled = false;
            }
        }

        if (boardLight != null)
        {
            boardLight.gameObject.SetActive(true);
            boardLight.intensity = 1.55f;
            boardLight.color = new Color(1f, 0.97f, 0.94f, 1f);
            boardLight.shadows = LightShadows.Soft;
            boardLight.shadowStrength = 0.72f;
            boardLight.transform.rotation = Quaternion.Euler(55f, 320f, 0f);
        }

        Light fillLight = FindNamedLight("BoardFillLight");
        if (fillLight != null)
        {
            fillLight.gameObject.SetActive(true);
            fillLight.intensity = 0.35f;
            fillLight.color = new Color(0.45f, 0.4f, 0.85f, 1f);
            fillLight.shadows = LightShadows.None;
            fillLight.transform.rotation = Quaternion.Euler(25f, 150f, 0f);
        }

        EnsureBoardEnvironment();
        ShapeVisuals3D.Invalidate();
        ShapeMeshFactory3D.ClearCache();

        // Clean geometric pieces — footprint ratios; absolute height scales with cell size.
        blockFootprintFactor = 0.72f;
        nestFootprintFactor = 0.86f;

        RefreshAdaptivePresentation(force: true);

        if (Application.isPlaying)
        {
            SyncWorldPieceViews(
                FindObjectsByType<Block>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
                FindObjectsByType<Target>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            SyncWorldObstacleViews(
                FindObjectsByType<IceState>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
                FindObjectsByType<ShutterState>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }
    }

    /// <summary>
    /// Phase 14: size presentation cell so the board fills Gameplay Area (~fit padding).
    /// Logical grid coordinates are unchanged.
    /// </summary>
    public void RefreshAdaptivePresentation(bool force)
    {
        ResolveReferences();
        if (boardPresenter3D == null || boardManager == null)
        {
            return;
        }

        if (gameplayArea == null)
        {
            gameplayArea = BoardAdaptivePresentation3D.FindGameplayArea();
        }

        int columns = Mathf.Max(1, boardManager.Width);
        int rows = Mathf.Max(1, boardManager.Height);
        Vector2 screen = new Vector2(Screen.width, Screen.height);
        Vector2 areaScreen = Vector2.zero;
        Rect gpScreen = default;
        if (gameplayArea != null
            && BoardAdaptivePresentation3D.TryGetScreenRect(gameplayArea, out gpScreen))
        {
            areaScreen = gpScreen.size;
        }

        bool inputsChanged = force
            || columns != lastAdaptiveColumns
            || rows != lastAdaptiveRows
            || (screen - lastAdaptiveScreen).sqrMagnitude > 0.5f
            || (areaScreen - lastAdaptiveAreaScreen).sqrMagnitude > 1f;

        if (!inputsChanged && lastAdaptiveCell > 0f)
        {
            return;
        }

        if (boardCamera3D != null)
        {
            boardCamera3D.PrepareMeasurementPose();
        }

        Camera cam = boardCamera3D != null ? boardCamera3D.Camera : Camera.main;
        float planeY = 0f;
        Vector2 available = Vector2.zero;
        Vector3 areaCenter = Vector3.zero;
        bool measured = cam != null
            && gameplayArea != null
            && BoardAdaptivePresentation3D.TryMeasureBoardPlaneRect(
                cam,
                gameplayArea,
                planeY,
                out available,
                out areaCenter);

        // Single pass only: measuring again after FrameBoard shrinks ortho and collapses cell size.
        float cell;
        if (measured && available.x > 0.01f && available.y > 0.01f)
        {
            cell = BoardAdaptivePresentation3D.ComputeAdaptiveCellSize(
                columns,
                rows,
                available.x,
                available.y,
                presentationFitPadding);
        }
        else
        {
            cell = Mathf.Max(0.05f, lastAdaptiveCell > 0f ? lastAdaptiveCell : BoardAdaptivePresentation3D.ReferenceCellSize);
        }

        ApplyPresentationCell(cell, areaCenter, measured);
        if (boardCamera3D != null)
        {
            boardCamera3D.FrameBoard(boardPresenter3D, gameplayArea);
        }

        lastAdaptiveColumns = columns;
        lastAdaptiveRows = rows;
        lastAdaptiveCell = cell;
        lastAdaptiveScreen = screen;
        lastAdaptiveAreaScreen = areaScreen;
        lastSyncedBlockCount = -1;
        lastSyncedTargetCount = -1;
        lastObstacleFingerprint = int.MinValue;
    }

    private void ApplyPresentationCell(float cell, Vector3 areaCenter, bool centerOnArea)
    {
        blockHeight = cell * BoardAdaptivePresentation3D.BlockHeightRatio;
        nestHeight = cell * BoardAdaptivePresentation3D.NestHeightRatio;
        boardPresenter3D.ApplyPresentationScale(cell);

        if (centerOnArea)
        {
            Vector3 p = boardPresenter3D.transform.position;
            boardPresenter3D.transform.position = new Vector3(areaCenter.x, p.y, areaCenter.z);
        }
    }

    private void EnsureBoardEnvironment()
    {
        if (boardEnvironment == null)
        {
            boardEnvironment = FindFirstObjectByType<BoardEnvironment3D>(FindObjectsInactive.Include);
        }

        if (boardEnvironment == null)
        {
            var go = new GameObject("BoardEnvironment3D");
            boardEnvironment = go.AddComponent<BoardEnvironment3D>();
        }

        Camera cam = boardCamera3D != null ? boardCamera3D.Camera : null;
        boardEnvironment.Apply(boardPresenter3D, cam);
    }

    private static Light FindNamedLight(string lightName)
    {
        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].name == lightName)
            {
                return lights[i];
            }
        }

        return null;
    }

    private bool NeedsPresentationResync(Block[] blocks, Target[] targets)
    {
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || block.IsMatchPresentationActive)
            {
                continue;
            }

            if (NeedsPieceViewResync(block.WorldView, block.GetOuterShape(block.AnchorCellIndex), expectNest: false))
            {
                return true;
            }

            if (NeedsExtraViewsResync(
                    extraViewsByBlockId,
                    block.GetInstanceID(),
                    block.CellCount,
                    block.AnchorCellIndex,
                    i2 => block.GetOuterShape(i2),
                    expectNest: false))
            {
                return true;
            }

            if (NeedsNestedCompositionResync(block))
            {
                return true;
            }

            if (NeedsConnectorsResync(block))
            {
                return true;
            }
        }

        for (int i = 0; i < targets.Length; i++)
        {
            Target target = targets[i];
            if (target == null || target.IsMatchPresentationActive)
            {
                continue;
            }

            if (NeedsPieceViewResync(target.WorldView, target.GetOuterShapeAtIndex(target.AnchorCellIndex), expectNest: true))
            {
                return true;
            }

            if (NeedsExtraViewsResync(
                    extraViewsByTargetId,
                    target.GetInstanceID(),
                    target.CellCount,
                    target.AnchorCellIndex,
                    i2 => target.GetOuterShapeAtIndex(i2),
                    expectNest: true))
            {
                return true;
            }

            if (NeedsNestedTargetCompositionResync(target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NeedsPieceViewResync(PieceView3D view, ShapeType shape, bool expectNest)
    {
        if (view == null)
        {
            return true;
        }

        if (!view.gameObject.activeSelf || !view.gameObject.activeInHierarchy)
        {
            return true;
        }

        if (!view.HasRenderableMesh || !view.HasValidPresentationScale)
        {
            return true;
        }

        if (view.ConfiguredShape != shape || view.ConfiguredAsNest != expectNest)
        {
            return true;
        }

        return false;
    }

    private bool NeedsNestedCompositionResync(Block block)
    {
        if (block == null)
        {
            return false;
        }

        for (int i = 0; i < block.CellCount; i++)
        {
            PieceView3D view = block.GetWorldViewForCellIndex(i);
            bool wantInner = block.HasInnerLayerAt(i) && !IsTravelingInnerCell(block, i);
            if (NeedsNestedInnerResync(view, wantInner, block.GetActiveShape(i)))
            {
                return true;
            }
        }

        return false;
    }

    private bool NeedsNestedTargetCompositionResync(Target target)
    {
        if (target == null)
        {
            return false;
        }

        for (int i = 0; i < target.CellCount; i++)
        {
            PieceView3D view = i == target.AnchorCellIndex
                ? target.WorldView
                : GetTargetExtraView(target, i);
            bool wantInner = target.HasInnerLayerAt(i);
            ShapeType inner = target.GetShapeAtIndex(i);
            if (NeedsNestedInnerResync(view, wantInner, inner))
            {
                return true;
            }
        }

        return false;
    }

    private PieceView3D GetTargetExtraView(Target target, int cellIndex)
    {
        if (target == null
            || !extraViewsByTargetId.TryGetValue(target.GetInstanceID(), out List<PieceView3D> extras)
            || extras == null)
        {
            return null;
        }

        int extraSlot = 0;
        int anchor = target.AnchorCellIndex;
        for (int i = 0; i < target.CellCount; i++)
        {
            if (i == anchor)
            {
                continue;
            }

            if (i == cellIndex)
            {
                return extraSlot < extras.Count ? extras[extraSlot] : null;
            }

            extraSlot++;
        }

        return null;
    }

    private static bool NeedsNestedInnerResync(PieceView3D view, bool wantInner, ShapeType innerShape)
    {
        if (view == null)
        {
            return wantInner;
        }

        if (view.HasNestedInner != wantInner)
        {
            return true;
        }

        return wantInner && view.ConfiguredInnerShape != innerShape;
    }

    private bool IsTravelingInnerCell(Block block, int cellIndex)
    {
        return IsChainTraveling(block) && chainTravelInnerLayer && chainTravelCellIndex == cellIndex;
    }

    private float NestedInnerRelativeScale()
    {
        return PieceGameplayVisuals.NestedInnerLook.FromTheme(theme).scale;
    }

    private void SyncBlockNestedInner(PieceView3D view, Block block, int cellIndex)
    {
        if (view == null || block == null)
        {
            return;
        }

        bool show = block.HasInnerLayerAt(cellIndex) && !IsTravelingInnerCell(block, cellIndex);
        ShapeType inner = block.GetActiveShape(cellIndex);
        view.ConfigureNestedInner(
            show,
            inner,
            show ? ShapeVisuals3D.BlockMaterial(inner, theme) : null,
            NestedInnerRelativeScale(),
            asNest: false);
    }

    private void SyncTargetNestedInner(PieceView3D view, Target target, int cellIndex)
    {
        if (view == null || target == null)
        {
            return;
        }

        bool show = target.HasInnerLayerAt(cellIndex);
        ShapeType inner = target.GetShapeAtIndex(cellIndex);
        view.ConfigureNestedInner(
            show,
            inner,
            show ? ShapeVisuals3D.NestMaterial(inner, theme) : null,
            NestedInnerRelativeScale(),
            asNest: true);
    }

    private void SyncWorldPieceViews(Block[] blocks, Target[] targets)
    {
        if (boardPresenter3D == null)
        {
            return;
        }

        Transform piecesRoot = boardPresenter3D.PiecesRoot;
        Transform nestsRoot = boardPresenter3D.NestsRoot;
        IGridSpace worldSpace = boardPresenter3D.GridSpace;
        float cell = boardPresenter3D.CellWorldSize;
        var keepBlocks = new HashSet<int>();
        var keepTargets = new HashSet<int>();

        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null)
            {
                continue;
            }

            int id = block.GetInstanceID();

            // Match presentation owns the view until dissolve finishes (Matched → prune).
            if (block.IsMatchPresentationActive)
            {
                if (block.IsMatched)
                {
                    HideMappedView(worldViewsByBlockId, id);
                    HideExtraViews(extraViewsByBlockId, id);
                    HideConnectors(id);
                    continue;
                }

                // Dissolving: keep the existing view, never ConfigureVisual / reactivate.
                keepBlocks.Add(id);
                continue;
            }

            keepBlocks.Add(id);

            if (!worldViewsByBlockId.TryGetValue(id, out PieceView3D view) || view == null)
            {
                view = CreateView($"Block3D_{block.ShapeType}_{id}", piecesRoot);
                worldViewsByBlockId[id] = view;
            }

            BlockMover mover = block.GetComponent<BlockMover>();
            bool motionBusy = mover != null && (mover.IsMoving || mover.IsDragging);
            bool motionLocked = view.IsMotionLocked;
            ShapeType outer = block.GetOuterShape(block.AnchorCellIndex);
            bool needsVisual = NeedsPieceViewResync(view, outer, expectNest: false)
                || block.WorldView != view;

            // Broken / unbound / shape-stale views must be repaired even during motion.
            // Idle pieces also get a full ConfigureVisual refresh.
            if (needsVisual || (!motionBusy && !motionLocked))
            {
                view.ConfigureVisual(
                    outer,
                    ShapeVisuals3D.BlockMaterial(outer, theme),
                    asNest: false,
                    footprint: cell * blockFootprintFactor,
                    height: blockHeight);
            }
            else
            {
                view.EnsurePresentationVisible();
            }

            SyncBlockNestedInner(view, block, block.AnchorCellIndex);

            // Avoid SetWorldView while moving when already bound — it SyncWorldViewPositions
            // and would overwrite nest-entry seating (Phase 23) after motion lock ends.
            if (block.WorldView != view)
            {
                block.SetWorldView(view, worldSpace);
            }
            else
            {
                view.BindSourceBlock(block);
            }

            block.SetUiPresentationVisible(false);

            if (!motionBusy && !motionLocked)
            {
                block.SetGridPosition(block.GridPosition);
            }

            IceState ice = block.GetComponent<IceState>();
            if (ice != null)
            {
                ice.SetUiPresentationVisible(false);
            }

            SyncBlockOccupantExtras(block, piecesRoot, cell, motionBusy, motionLocked);
            SyncBlockOccupantConnectors(block, piecesRoot);
        }

        for (int i = 0; i < targets.Length; i++)
        {
            Target target = targets[i];
            if (target == null)
            {
                continue;
            }

            int id = target.GetInstanceID();

            // Match presentation owns the nest until dissolve finishes (Matched → prune).
            if (target.IsMatchPresentationActive)
            {
                if (target.IsMatched)
                {
                    HideMappedView(worldViewsByTargetId, id);
                    HideExtraViews(extraViewsByTargetId, id);
                    continue;
                }

                keepTargets.Add(id);
                continue;
            }

            keepTargets.Add(id);

            if (!worldViewsByTargetId.TryGetValue(id, out PieceView3D view) || view == null)
            {
                view = CreateView($"Nest3D_{target.ShapeType}_{id}", nestsRoot);
                worldViewsByTargetId[id] = view;
            }

            ShapeType nestOuter = target.GetOuterShapeAtIndex(target.AnchorCellIndex);
            view.ConfigureVisual(
                nestOuter,
                ShapeVisuals3D.NestMaterial(nestOuter, theme),
                asNest: true,
                footprint: cell * nestFootprintFactor,
                height: nestHeight);
            view.ClearSourceBlock();
            view.SetPieceHeight(nestHeight);
            SyncTargetNestedInner(view, target, target.AnchorCellIndex);
            target.SetWorldView(view, worldSpace);
            target.SetUiPresentationVisible(false);
            target.RefreshWorldPresentation();
            SyncTargetOccupantExtras(target, nestsRoot, worldSpace, cell);
        }

        PruneViews(worldViewsByBlockId, keepBlocks);
        PruneViews(worldViewsByTargetId, keepTargets);
        PruneExtraViews(extraViewsByBlockId, keepBlocks);
        PruneExtraViews(extraViewsByTargetId, keepTargets);
        PruneConnectors(keepBlocks);
        PruneNestedInnerTravelers(blocks);

        lastSyncedBlockCount = blocks.Length;
        lastSyncedTargetCount = targets.Length;
    }

    private void SyncWorldObstacleViews(IceState[] ices, ShutterState[] shutters)
    {
        if (boardPresenter3D == null)
        {
            return;
        }

        Transform iceRoot = boardPresenter3D.IceRoot;
        Transform shuttersRoot = boardPresenter3D.ShuttersRoot;
        var keepIce = new HashSet<int>();
        var keepShutters = new HashSet<int>();

        for (int i = 0; i < ices.Length; i++)
        {
            IceState ice = ices[i];
            if (ice == null)
            {
                continue;
            }

            ice.SetUiPresentationVisible(false);
            int id = ice.GetInstanceID();

            if (!ice.IsFrozen)
            {
                // Gameplay melted: keep the view alive until IceView3D melt tween finishes.
                if (worldViewsByIceId.TryGetValue(id, out IceView3D melting) && melting != null)
                {
                    melting.SyncFromSource();
                    if (melting.IsPresentationAnimating)
                    {
                        keepIce.Add(id);
                    }
                }

                continue;
            }

            keepIce.Add(id);
            if (!worldViewsByIceId.TryGetValue(id, out IceView3D view) || view == null)
            {
                var go = new GameObject($"Ice3D_{id}");
                go.transform.SetParent(iceRoot, false);
                view = go.AddComponent<IceView3D>();
                worldViewsByIceId[id] = view;
                view.Bind(ice, IceView3D.GetSharedIceMaterial());
            }
            else
            {
                // Sync only — re-Bind would reset presented durability and snap visuals.
                view.SyncFromSource();
            }
        }

        for (int i = 0; i < shutters.Length; i++)
        {
            ShutterState shutter = shutters[i];
            if (shutter == null)
            {
                continue;
            }

            shutter.SetUiPresentationVisible(false);
            int id = shutter.GetInstanceID();

            if (!shutter.IsClosed)
            {
                // Gameplay open: keep view until open presentation completes.
                if (worldViewsByShutterId.TryGetValue(id, out ShutterView3D opening) && opening != null)
                {
                    opening.SyncFromSource();
                    if (opening.IsOpeningPresentation)
                    {
                        keepShutters.Add(id);
                    }
                }

                continue;
            }

            keepShutters.Add(id);
            if (!worldViewsByShutterId.TryGetValue(id, out ShutterView3D view) || view == null)
            {
                var go = new GameObject($"Shutter3D_{id}");
                go.transform.SetParent(shuttersRoot, false);
                view = go.AddComponent<ShutterView3D>();
                worldViewsByShutterId[id] = view;
                view.Bind(shutter, ShutterView3D.GetPlateMaterial(), ShutterView3D.GetSlatMaterial());
            }
            else
            {
                view.SyncFromSource();
            }
        }

        PruneObstacleViews(worldViewsByIceId, keepIce);
        PruneObstacleViews(worldViewsByShutterId, keepShutters);
        lastObstacleFingerprint = ComputeObstacleFingerprint(ices, shutters);
    }

    private void RefreshObstaclePositions(IceState[] ices, ShutterState[] shutters)
    {
        for (int i = 0; i < ices.Length; i++)
        {
            IceState ice = ices[i];
            if (ice == null)
            {
                continue;
            }

            int id = ice.GetInstanceID();
            if (worldViewsByIceId.TryGetValue(id, out IceView3D view) && view != null)
            {
                // Safe: SyncFromSource no-ops / layout-follows when state unchanged or animating.
                view.SyncFromSource();
            }
        }

        for (int i = 0; i < shutters.Length; i++)
        {
            ShutterState shutter = shutters[i];
            if (shutter == null)
            {
                continue;
            }

            int id = shutter.GetInstanceID();
            if (worldViewsByShutterId.TryGetValue(id, out ShutterView3D view) && view != null)
            {
                if (view.IsOpeningPresentation)
                {
                    continue;
                }

                view.SyncFromSource();
            }
        }
    }

    private static int ComputeObstacleFingerprint(IceState[] ices, ShutterState[] shutters)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (ices != null ? ices.Length : 0);
            hash = hash * 31 + (shutters != null ? shutters.Length : 0);
            if (ices != null)
            {
                for (int i = 0; i < ices.Length; i++)
                {
                    IceState ice = ices[i];
                    if (ice == null)
                    {
                        continue;
                    }

                    hash = hash * 31 + ice.GetInstanceID();
                    hash = hash * 31 + (ice.IsFrozen ? ice.Durability : 0);
                }
            }

            if (shutters != null)
            {
                for (int i = 0; i < shutters.Length; i++)
                {
                    ShutterState shutter = shutters[i];
                    if (shutter == null)
                    {
                        continue;
                    }

                    hash = hash * 31 + shutter.GetInstanceID();
                    hash = hash * 31 + (shutter.IsClosed ? shutter.Durability : 0);
                    hash = hash * 31 + (shutter.Cells != null ? shutter.Cells.Count : 0);
                }
            }

            return hash;
        }
    }

    private void RefreshWorldPositions(Block[] blocks, Target[] targets)
    {
        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || block.WorldView == null || block.IsMatchPresentationActive)
            {
                continue;
            }

            BlockMover mover = block.GetComponent<BlockMover>();
            if (mover != null && (mover.IsMoving || mover.IsDragging))
            {
                continue;
            }

            if (block.WorldView.IsMotionLocked)
            {
                continue;
            }

            block.SetGridPosition(block.GridPosition);
        }

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null && !targets[i].IsMatchPresentationActive)
            {
                targets[i].RefreshWorldPresentation();
            }
        }
    }

    private void FollowMultiCellWorldViews(Block[] blocks, Target[] targets)
    {
        if (boardPresenter3D == null)
        {
            return;
        }

        IGridSpace space = boardPresenter3D.GridSpace;
        if (space == null)
        {
            return;
        }

        if (blocks != null)
        {
            for (int i = 0; i < blocks.Length; i++)
            {
                FollowBlockExtras(blocks[i], space);
                FollowBlockConnectors(blocks[i], space);
            }
        }

        if (targets != null)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                FollowTargetExtras(targets[i], space);
            }
        }
    }

    private void FollowBlockExtras(Block block, IGridSpace space)
    {
        if (block == null || block.WorldView == null)
        {
            return;
        }

        if (!extraViewsByBlockId.TryGetValue(block.GetInstanceID(), out List<PieceView3D> extras)
            || extras == null
            || extras.Count == 0)
        {
            return;
        }

        int extraSlot = 0;
        int anchor = block.AnchorCellIndex;
        Vector2Int anchorCell = block.GridPosition;
        PieceView3D primary = block.WorldView;
        bool primaryTraveling = IsChainTravelView(primary) && IsChainTraveling(block);
        for (int i = 0; i < block.CellCount; i++)
        {
            if (i == anchor)
            {
                continue;
            }

            if (extraSlot >= extras.Count)
            {
                break;
            }

            PieceView3D extra = extras[extraSlot++];
            if (extra == null || IsChainTravelView(extra))
            {
                continue;
            }

            if (primaryTraveling)
            {
                extra.MatchCarryPresentation(primary);
                extra.ApplyGridPosition(space, block.GetCellWorld(i));
                extra.LocalScale = extra.ConfiguredFootprintScale;
            }
            else
            {
                FollowPrimaryView(extra, primary, space, block.GetCellWorld(i), anchorCell);
            }
        }
    }

    private void FollowBlockConnectors(Block block, IGridSpace space)
    {
        if (block == null || block.WorldView == null || space == null)
        {
            return;
        }

        if (!connectorsByBlockId.TryGetValue(block.GetInstanceID(), out List<ChainConnectorView3D> links)
            || links == null
            || links.Count == 0)
        {
            return;
        }

        PieceView3D primary = block.WorldView;
        Vector3 rest = primary.ConfiguredFootprintScale;
        Vector3 factor = Vector3.one;
        if (rest.x > 0.0001f && rest.y > 0.0001f && rest.z > 0.0001f)
        {
            factor = new Vector3(
                primary.LocalScale.x / rest.x,
                primary.LocalScale.y / rest.y,
                primary.LocalScale.z / rest.z);
        }

        int link = 0;
        int count = block.CellCount;
        Vector2Int anchorCell = block.GridPosition;
        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                Vector2Int a = block.GetLocalCell(i);
                Vector2Int b = block.GetLocalCell(j);
                Vector2Int delta = b - a;
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                {
                    continue;
                }

                if (link >= links.Count || links[link] == null)
                {
                    return;
                }

                ChainConnectorView3D connector = links[link];
                if (IsChainTraveling(block) && !chainTravelInnerLayer && (i == chainTravelCellIndex || j == chainTravelCellIndex))
                {
                    connector.gameObject.SetActive(false);
                    link++;
                    continue;
                }

                if (!connector.gameObject.activeSelf)
                {
                    connector.gameObject.SetActive(true);
                }

                Vector3 worldA;
                Vector3 worldB;
                if (IsChainTraveling(block) && IsChainTravelView(primary))
                {
                    worldA = OccupancyCellWorld(block, space, i);
                    worldB = OccupancyCellWorld(block, space, j);
                }
                else
                {
                    worldA = primary.transform.position
                        + (space.GridToWorld(anchorCell + a) - space.GridToWorld(anchorCell));
                    worldB = primary.transform.position
                        + (space.GridToWorld(anchorCell + b) - space.GridToWorld(anchorCell));
                }

                Vector3 mid = (worldA + worldB) * 0.5f;
                mid.y -= ConnectorOcclusionDrop(primary) * Mathf.Max(0.25f, factor.y);
                connector.Follow(mid, factor);
                link++;
            }
        }
    }

    private static float ConnectorOcclusionDrop(PieceView3D primary)
    {
        float height = primary != null ? primary.ConfiguredFootprintScale.y : 0.16f;
        return Mathf.Max(0.01f, height * 0.16f);
    }

    private Vector3 OccupancyCellWorld(Block block, IGridSpace space, int cellIndex)
    {
        PieceView3D view = block != null ? block.GetWorldViewForCellIndex(cellIndex) : null;
        if (view != null && !IsChainTravelView(view))
        {
            return view.transform.position;
        }

        Vector3 world = space.GridToWorld(block.GetCellWorld(cellIndex));
        PieceView3D sample = block.WorldView;
        if (IsChainTravelView(sample))
        {
            sample = null;
            IReadOnlyList<PieceView3D> extras = block.ExtraWorldViews;
            if (extras != null)
            {
                for (int i = 0; i < extras.Count; i++)
                {
                    if (extras[i] != null && !IsChainTravelView(extras[i]))
                    {
                        sample = extras[i];
                        break;
                    }
                }
            }
        }

        if (sample != null)
        {
            world.y = sample.transform.position.y;
        }

        return world;
    }

    private bool IsChainTraveling(Block block)
    {
        return block != null
            && chainTravelView != null
            && chainTravelBlockId == block.GetInstanceID();
    }

    private bool IsChainTravelView(PieceView3D view)
    {
        return view != null && view == chainTravelView;
    }

    private void BeginChainCellTravelInternal(Block block, PieceView3D view, int cellIndex)
    {
        if (block == null || view == null)
        {
            return;
        }

        if (chainTravelView != null && chainTravelView != view)
        {
            PieceView3D previous = chainTravelView;
            bool previousInner = chainTravelInnerLayer;
            ClearChainTravelState();
            if (previousInner && previous != null && !IsOccupantCellView(previous))
            {
                BeginDissolveView(previous);
            }
        }

        chainTravelView = view;
        chainTravelBlockId = block.GetInstanceID();
        chainTravelCellIndex = cellIndex;
        chainTravelInnerLayer = false;
        view.BeginMotionLock();
    }

    private PieceView3D BeginNestedInnerTravelInternal(Block block, int cellIndex)
    {
        if (block == null || boardPresenter3D == null || cellIndex < 0 || cellIndex >= block.CellCount)
        {
            return null;
        }

        PieceView3D cellView = block.GetWorldViewForCellIndex(cellIndex);
        if (cellView == null)
        {
            return null;
        }

        if (chainTravelView != null)
        {
            PieceView3D previous = chainTravelView;
            bool previousInner = chainTravelInnerLayer;
            ClearChainTravelState();
            if (previousInner && previous != null && !IsOccupantCellView(previous))
            {
                DestroyView(previous);
            }
        }

        cellView.ConfigureNestedInner(false, block.GetActiveShape(cellIndex), null, NestedInnerRelativeScale(), asNest: false);

        Transform piecesRoot = boardPresenter3D.PiecesRoot;
        float cell = boardPresenter3D.CellWorldSize;
        ShapeType inner = block.GetActiveShape(cellIndex);
        PieceView3D travel = CreateView($"NestedInnerTravel_{block.GetInstanceID()}_c{cellIndex}", piecesRoot);
        RegisterNestedInnerTraveler(travel, block.GetInstanceID());
        travel.ConfigureVisual(
            inner,
            ShapeVisuals3D.BlockMaterial(inner, theme),
            asNest: false,
            footprint: cell * blockFootprintFactor,
            height: blockHeight);
        travel.ClearSourceBlock();
        travel.ConfigureNestedInner(false, inner, null, NestedInnerRelativeScale(), asNest: false);
        travel.transform.position = cellView.transform.position;
        travel.LocalScale = travel.ConfiguredFootprintScale * NestedInnerRelativeScale();

        chainTravelView = travel;
        chainTravelBlockId = block.GetInstanceID();
        chainTravelCellIndex = cellIndex;
        chainTravelInnerLayer = true;
        travel.BeginMotionLock();
        return travel;
    }

    private void CancelNestedInnerTravelInternal(Block block, int cellIndex)
    {
        if (block == null || !IsChainTraveling(block) || !chainTravelInnerLayer)
        {
            return;
        }

        PieceView3D travel = chainTravelView;
        ClearChainTravelState();
        if (travel != null && !IsOccupantCellView(travel))
        {
            DestroyView(travel);
        }

        PieceView3D cellView = block.GetWorldViewForCellIndex(cellIndex);
        if (cellView != null)
        {
            SyncBlockNestedInner(cellView, block, cellIndex);
        }
    }

    private bool IsOccupantCellView(PieceView3D view)
    {
        if (view == null)
        {
            return false;
        }

        foreach (KeyValuePair<int, PieceView3D> pair in worldViewsByBlockId)
        {
            if (pair.Value == view)
            {
                return true;
            }
        }

        return IsInExtraMap(extraViewsByBlockId, view);
    }

    private void NotifyChainCellTravelClearedInternal(Block block)
    {
        if (block != null && IsChainTraveling(block))
        {
            PieceView3D travel = chainTravelView;
            bool inner = chainTravelInnerLayer;
            ClearChainTravelState();
            if (inner && travel != null && !IsOccupantCellView(travel))
            {
                BeginDissolveView(travel);
            }

            return;
        }

        if (block != null)
        {
            DissolveNestedInnerTravelersForOwner(block.GetInstanceID());
        }
    }

    private void EndChainCellTravelLock()
    {
        if (chainTravelView != null)
        {
            chainTravelView.EndMotionLock();
        }
    }

    private void ClearChainTravelState()
    {
        EndChainCellTravelLock();
        chainTravelView = null;
        chainTravelBlockId = 0;
        chainTravelCellIndex = -1;
        chainTravelInnerLayer = false;
    }

    private void AdoptSurvivorWorldViewInternal(Block block, Vector2Int survivorWorld)
    {
        if (block == null || boardPresenter3D == null)
        {
            return;
        }

        IGridSpace space = boardPresenter3D.GridSpace;
        int id = block.GetInstanceID();
        var pool = new List<PieceView3D>();
        if (block.WorldView != null)
        {
            pool.Add(block.WorldView);
        }

        IReadOnlyList<PieceView3D> extras = block.ExtraWorldViews;
        if (extras != null)
        {
            for (int i = 0; i < extras.Count; i++)
            {
                if (extras[i] != null && !pool.Contains(extras[i]))
                {
                    pool.Add(extras[i]);
                }
            }
        }

        var assigned = new PieceView3D[Mathf.Max(0, block.CellCount)];
        var used = new HashSet<PieceView3D>();
        for (int i = 0; i < block.CellCount; i++)
        {
            ShapeType shape = block.GetOuterShape(i);
            Vector3 target = space != null
                ? space.GridToWorld(block.GetCellWorld(i))
                : Vector3.zero;
            PieceView3D best = null;
            float bestDist = float.MaxValue;
            for (int p = 0; p < pool.Count; p++)
            {
                PieceView3D candidate = pool[p];
                if (candidate == null || used.Contains(candidate) || candidate.ConfiguredShape != shape)
                {
                    continue;
                }

                if (IsChainTravelView(candidate) && block.CellCount > 0)
                {
                    continue;
                }

                Vector3 pos = candidate.transform.position;
                float dist = (pos.x - target.x) * (pos.x - target.x) + (pos.z - target.z) * (pos.z - target.z);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }

            assigned[i] = best;
            if (best != null)
            {
                used.Add(best);
            }
        }

        int anchor = block.AnchorCellIndex;
        PieceView3D survivor = anchor >= 0 && anchor < assigned.Length ? assigned[anchor] : null;
        if (survivor == null && assigned.Length > 0)
        {
            survivor = assigned[0];
        }
        if (survivor != null)
        {
            worldViewsByBlockId[id] = survivor;
            List<PieceView3D> nextExtras = GetOrCreateExtraList(extraViewsByBlockId, id);
            nextExtras.Clear();
            for (int i = 0; i < assigned.Length; i++)
            {
                if (i == anchor || assigned[i] == null || assigned[i] == survivor)
                {
                    continue;
                }

                nextExtras.Add(assigned[i]);
            }

            bool alreadyAtCell = space == null
                || new Vector2(
                    survivor.transform.position.x - space.GridToWorld(survivorWorld).x,
                    survivor.transform.position.z - space.GridToWorld(survivorWorld).z).sqrMagnitude < 0.0001f;
            block.SetWorldView(survivor, space, syncPosition: !alreadyAtCell);
            block.SetExtraWorldViews(nextExtras);
        }

        if (chainTravelView != null && chainTravelView != survivor)
        {
            BeginDissolveView(chainTravelView);
        }

        if (IsChainTraveling(block))
        {
            NotifyChainCellTravelClearedInternal(block);
        }
    }

    private void BeginDissolveView(PieceView3D view)
    {
        if (view == null || dissolvingViews.Contains(view))
        {
            return;
        }

        dissolvingViews.Add(view);
        view.ClearSourceBlock();
        Vector3 from = view.LocalScale;
        TweenAnimationUtility.Progress(0.28f, t =>
        {
            if (view == null)
            {
                return;
            }

            float eased = TweenAnimationUtility.EvaluateSmoothStep(t);
            view.LocalScale = Vector3.LerpUnclamped(from, Vector3.zero, eased);
        })
            .SetId(TweenAnimationUtility.TravelerId)
            .SetTarget(view.gameObject)
            .SetLink(view.gameObject)
            .OnComplete(() => FinishDissolvingView(view))
            .OnKill(() => FinishDissolvingView(view));
    }

    private void FinishDissolvingView(PieceView3D view)
    {
        dissolvingViews.Remove(view);
        DestroyView(view);
    }

    private void FollowTargetExtras(Target target, IGridSpace space)
    {
        if (target == null)
        {
            return;
        }

        if (!extraViewsByTargetId.TryGetValue(target.GetInstanceID(), out List<PieceView3D> extras)
            || extras == null
            || extras.Count == 0)
        {
            return;
        }

        int extraSlot = 0;
        int anchor = target.AnchorCellIndex;
        for (int i = 0; i < target.CellCount; i++)
        {
            if (i == anchor)
            {
                continue;
            }

            if (extraSlot >= extras.Count)
            {
                break;
            }

            PieceView3D extra = extras[extraSlot++];
            if (extra == null)
            {
                continue;
            }

            if (!target.IsMatchPresentationActive)
            {
                extra.ApplyGridPosition(space, target.GridPosition + target.GetLocalCell(i));
            }

            if (target.WorldView != null)
            {
                CopyWorldScaleFactor(target.WorldView, extra);
            }
        }
    }

    private static void FollowPrimaryView(
        PieceView3D extra,
        PieceView3D primary,
        IGridSpace space,
        Vector2Int extraCell,
        Vector2Int anchorCell)
    {
        if (extra == null || primary == null || space == null)
        {
            return;
        }

        Vector3 delta = space.GridToWorld(extraCell) - space.GridToWorld(anchorCell);
        extra.transform.position = primary.transform.position + delta;
        extra.MatchCarryPresentation(primary);
        CopyWorldScaleFactor(primary, extra);
    }

    private static void CopyWorldScaleFactor(PieceView3D primary, PieceView3D extra)
    {
        if (primary == null || extra == null)
        {
            return;
        }

        Vector3 rest = primary.ConfiguredFootprintScale;
        if (rest.x <= 0.0001f || rest.y <= 0.0001f || rest.z <= 0.0001f)
        {
            return;
        }

        Vector3 factor = new Vector3(
            primary.LocalScale.x / rest.x,
            primary.LocalScale.y / rest.y,
            primary.LocalScale.z / rest.z);
        extra.LocalScale = Vector3.Scale(extra.ConfiguredFootprintScale, factor);
    }

    private bool NeedsExtraViewsResync(
        Dictionary<int, List<PieceView3D>> map,
        int id,
        int cellCount,
        int anchorIndex,
        Func<int, ShapeType> shapeAt,
        bool expectNest)
    {
        int extraNeeded = Mathf.Max(0, cellCount - 1);
        if (!map.TryGetValue(id, out List<PieceView3D> extras) || extras == null)
        {
            return extraNeeded > 0;
        }

        if (LiveViewCount(extras) != extraNeeded)
        {
            return true;
        }

        int extraSlot = 0;
        for (int i = 0; i < cellCount; i++)
        {
            if (i == anchorIndex)
            {
                continue;
            }

            if (extraSlot >= extras.Count)
            {
                return true;
            }

            PieceView3D view = extras[extraSlot++];
            ShapeType shape = shapeAt != null ? shapeAt(i) : ShapeType.Square;
            if (NeedsPieceViewResync(view, shape, expectNest))
            {
                return true;
            }
        }

        return false;
    }

    private static int LiveViewCount(List<PieceView3D> views)
    {
        if (views == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] != null)
            {
                count++;
            }
        }

        return count;
    }

    private void SyncBlockOccupantExtras(
        Block block,
        Transform piecesRoot,
        float cell,
        bool motionBusy,
        bool motionLocked)
    {
        int id = block.GetInstanceID();
        int extraNeeded = Mathf.Max(0, block.CellCount - 1);
        List<PieceView3D> extras = GetOrCreateExtraList(extraViewsByBlockId, id);
        TrimViewList(extras, extraNeeded);
        while (extras.Count < extraNeeded)
        {
            extras.Add(CreateView($"Block3D_{block.ShapeType}_{id}_c{extras.Count + 1}", piecesRoot));
        }

        int extraSlot = 0;
        int anchor = block.AnchorCellIndex;
        for (int i = 0; i < block.CellCount; i++)
        {
            if (i == anchor)
            {
                continue;
            }

            PieceView3D view = extras[extraSlot++];
            ShapeType shape = block.GetOuterShape(i);
            bool needsVisual = NeedsPieceViewResync(view, shape, expectNest: false);
            if (needsVisual || (!motionBusy && !motionLocked))
            {
                view.ConfigureVisual(
                    shape,
                    ShapeVisuals3D.BlockMaterial(shape, theme),
                    asNest: false,
                    footprint: cell * blockFootprintFactor,
                    height: blockHeight);
            }
            else
            {
                view.EnsurePresentationVisible();
            }

            view.BindSourceBlock(block);
            SyncBlockNestedInner(view, block, i);
        }

        block.SetExtraWorldViews(extras);
    }

    private void SyncBlockOccupantConnectors(Block block, Transform piecesRoot)
    {
        if (block == null || piecesRoot == null || boardPresenter3D == null)
        {
            return;
        }

        int id = block.GetInstanceID();
        int needed = CountFourConnectedLinks(block);
        List<ChainConnectorView3D> links = GetOrCreateConnectorList(id);
        TrimConnectorList(links, needed);
        IGridSpace space = boardPresenter3D.GridSpace;
        float pitch = MeasureCellPitch(space, block.GridPosition);
        float height = Mathf.Max(0.01f, blockHeight);
        Material material = ShapeVisuals3D.ChainConnectorMaterial(theme);

        int link = 0;
        int count = block.CellCount;
        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                Vector2Int a = block.GetLocalCell(i);
                Vector2Int b = block.GetLocalCell(j);
                Vector2Int delta = b - a;
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                {
                    continue;
                }

                if (link >= links.Count)
                {
                    links.Add(CreateConnector($"ChainLink_{id}_{link}", piecesRoot));
                }

                ChainConnectorView3D view = links[link];
                bool horizontal = a.y == b.y;
                float thickness = Mathf.Max(0.01f, pitch * PieceGameplayVisuals.ConnectorThickness);
                float length = Mathf.Max(0.01f, pitch * PieceGameplayVisuals.ConnectorOverlap);
                Vector3 scale = horizontal
                    ? new Vector3(length, height, thickness)
                    : new Vector3(thickness, height, length);
                view.Configure(scale, material);
                link++;
            }
        }

        if (needed == 0)
        {
            TrimConnectorList(links, 0);
            return;
        }

        if (block.WorldView != null && space != null)
        {
            FollowBlockConnectors(block, space);
        }
    }

    private bool NeedsConnectorsResync(Block block)
    {
        if (block == null)
        {
            return false;
        }

        int needed = CountFourConnectedLinks(block);
        if (!connectorsByBlockId.TryGetValue(block.GetInstanceID(), out List<ChainConnectorView3D> links)
            || links == null)
        {
            return needed > 0;
        }

        int live = 0;
        for (int i = 0; i < links.Count; i++)
        {
            if (links[i] != null)
            {
                live++;
            }
        }

        return live != needed;
    }

    private static int CountFourConnectedLinks(Block block)
    {
        if (block == null || block.CellCount <= 1)
        {
            return 0;
        }

        int links = 0;
        int count = block.CellCount;
        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                Vector2Int delta = block.GetLocalCell(j) - block.GetLocalCell(i);
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) == 1)
                {
                    links++;
                }
            }
        }

        return links;
    }

    private static float MeasureCellPitch(IGridSpace space, Vector2Int sample)
    {
        if (space == null)
        {
            return 1f;
        }

        Vector3 a = space.GridToWorld(sample);
        Vector3 b = space.GridToWorld(sample + Vector2Int.right);
        float pitch = new Vector2(b.x - a.x, b.z - a.z).magnitude;
        return pitch > 0.0001f ? pitch : 1f;
    }

    private List<ChainConnectorView3D> GetOrCreateConnectorList(int id)
    {
        if (!connectorsByBlockId.TryGetValue(id, out List<ChainConnectorView3D> links) || links == null)
        {
            links = new List<ChainConnectorView3D>();
            connectorsByBlockId[id] = links;
        }

        return links;
    }

    private ChainConnectorView3D CreateConnector(string objectName, Transform parent)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        return go.AddComponent<ChainConnectorView3D>();
    }

    private void TrimConnectorList(List<ChainConnectorView3D> list, int keepCount)
    {
        if (list == null)
        {
            return;
        }

        for (int i = list.Count - 1; i >= keepCount; i--)
        {
            DestroyConnector(list[i]);
            list.RemoveAt(i);
        }
    }

    private void DestroyConnector(ChainConnectorView3D view)
    {
        if (view == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(view.gameObject);
        }
        else
        {
            DestroyImmediate(view.gameObject);
        }
    }

    private void HideConnectors(int id)
    {
        if (!connectorsByBlockId.TryGetValue(id, out List<ChainConnectorView3D> links) || links == null)
        {
            return;
        }

        for (int i = 0; i < links.Count; i++)
        {
            if (links[i] != null)
            {
                links[i].gameObject.SetActive(false);
            }
        }
    }

    private void PruneConnectors(HashSet<int> keep)
    {
        var remove = new List<int>();
        foreach (KeyValuePair<int, List<ChainConnectorView3D>> pair in connectorsByBlockId)
        {
            if (!keep.Contains(pair.Key))
            {
                remove.Add(pair.Key);
            }
        }

        for (int i = 0; i < remove.Count; i++)
        {
            int id = remove[i];
            if (connectorsByBlockId.TryGetValue(id, out List<ChainConnectorView3D> links) && links != null)
            {
                TrimConnectorList(links, 0);
            }

            connectorsByBlockId.Remove(id);
        }
    }

    private void SyncTargetOccupantExtras(
        Target target,
        Transform nestsRoot,
        IGridSpace worldSpace,
        float cell)
    {
        int id = target.GetInstanceID();
        int extraNeeded = Mathf.Max(0, target.CellCount - 1);
        List<PieceView3D> extras = GetOrCreateExtraList(extraViewsByTargetId, id);
        TrimViewList(extras, extraNeeded);
        while (extras.Count < extraNeeded)
        {
            extras.Add(CreateView($"Nest3D_{target.ShapeType}_{id}_c{extras.Count + 1}", nestsRoot));
        }

        int extraSlot = 0;
        int anchor = target.AnchorCellIndex;
        for (int i = 0; i < target.CellCount; i++)
        {
            if (i == anchor)
            {
                continue;
            }

            PieceView3D view = extras[extraSlot++];
            ShapeType shape = target.GetOuterShapeAtIndex(i);
            view.ConfigureVisual(
                shape,
                ShapeVisuals3D.NestMaterial(shape, theme),
                asNest: true,
                footprint: cell * nestFootprintFactor,
                height: nestHeight);
            view.ClearSourceBlock();
            view.SetPieceHeight(nestHeight);
            SyncTargetNestedInner(view, target, i);
            if (worldSpace != null)
            {
                view.ApplyGridPosition(worldSpace, target.GridPosition + target.GetLocalCell(i));
            }
        }

        target.SetExtraWorldViews(extras);
    }

    private static List<PieceView3D> GetOrCreateExtraList(Dictionary<int, List<PieceView3D>> map, int id)
    {
        if (!map.TryGetValue(id, out List<PieceView3D> extras) || extras == null)
        {
            extras = new List<PieceView3D>();
            map[id] = extras;
        }

        return extras;
    }

    private void TrimViewList(List<PieceView3D> list, int keepCount)
    {
        for (int i = list.Count - 1; i >= keepCount; i--)
        {
            DestroyView(list[i]);
            list.RemoveAt(i);
        }
    }

    private void DestroyView(PieceView3D view, bool immediate = false)
    {
        if (view == null)
        {
            PurgeDestroyedNestedInnerTravelers();
            return;
        }

        nestedInnerTravelers.Remove(view);
        dissolvingViews.Remove(view);
        if (chainTravelView == view)
        {
            ClearChainTravelState();
        }

        GameObject go = view.gameObject;
        if (go == null)
        {
            return;
        }

        destroyViewDepth++;
        try
        {
            if (destroyViewDepth == 1)
            {
                TweenAnimationUtility.KillById(go, TweenAnimationUtility.TravelerId, complete: false);
                DOTween.Kill(go, complete: false);
            }

            if (!Application.isPlaying || immediate)
            {
                DestroyImmediate(go);
            }
            else
            {
                Destroy(go);
            }
        }
        finally
        {
            destroyViewDepth--;
        }
    }

    private static void HideMappedView(Dictionary<int, PieceView3D> map, int id)
    {
        if (map.TryGetValue(id, out PieceView3D view) && view != null)
        {
            view.gameObject.SetActive(false);
        }
    }

    private static void HideExtraViews(Dictionary<int, List<PieceView3D>> map, int id)
    {
        if (!map.TryGetValue(id, out List<PieceView3D> extras) || extras == null)
        {
            return;
        }

        for (int i = 0; i < extras.Count; i++)
        {
            if (extras[i] != null)
            {
                extras[i].gameObject.SetActive(false);
            }
        }
    }

    private void PruneExtraViews(Dictionary<int, List<PieceView3D>> map, HashSet<int> keep)
    {
        var remove = new List<int>();
        foreach (KeyValuePair<int, List<PieceView3D>> pair in map)
        {
            if (!keep.Contains(pair.Key))
            {
                remove.Add(pair.Key);
            }
        }

        for (int i = 0; i < remove.Count; i++)
        {
            int id = remove[i];
            if (map.TryGetValue(id, out List<PieceView3D> extras) && extras != null)
            {
                TrimViewList(extras, 0);
            }

            map.Remove(id);
        }
    }

    private PieceView3D CreateView(string objectName, Transform parent)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        return go.AddComponent<PieceView3D>();
    }

    private void PruneViews(Dictionary<int, PieceView3D> map, HashSet<int> keep)
    {
        var remove = new List<int>();
        foreach (var pair in map)
        {
            if (!keep.Contains(pair.Key))
            {
                remove.Add(pair.Key);
            }
        }

        for (int i = 0; i < remove.Count; i++)
        {
            int id = remove[i];
            if (map.TryGetValue(id, out PieceView3D view) && view != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(view.gameObject);
                }
                else
                {
                    DestroyImmediate(view.gameObject);
                }
            }

            map.Remove(id);
        }
    }

    private void CleanupFinishedPieceViews(Block[] blocks, Target[] targets)
    {
        var keepBlocks = new HashSet<int>();
        if (blocks != null)
        {
            for (int i = 0; i < blocks.Length; i++)
            {
                Block block = blocks[i];
                if (block == null || block.IsMatched)
                {
                    continue;
                }

                keepBlocks.Add(block.GetInstanceID());
            }
        }

        PruneViews(worldViewsByBlockId, keepBlocks);
        PruneExtraViews(extraViewsByBlockId, keepBlocks);
        PruneConnectors(keepBlocks);
        PruneNestedInnerTravelers(blocks);

        var keepTargets = new HashSet<int>();
        if (targets != null)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                Target target = targets[i];
                if (target == null || target.IsMatched)
                {
                    continue;
                }

                keepTargets.Add(target.GetInstanceID());
            }
        }

        PruneViews(worldViewsByTargetId, keepTargets);
        PruneExtraViews(extraViewsByTargetId, keepTargets);
    }

    private void DestroyUntrackedPieceViews()
    {
        if (boardPresenter3D == null)
        {
            return;
        }

        DestroyUntrackedUnder(boardPresenter3D.PiecesRoot);
        DestroyUntrackedUnder(boardPresenter3D.NestsRoot);
    }

    private void DestroyUntrackedConnectors()
    {
        if (boardPresenter3D == null || boardPresenter3D.PiecesRoot == null)
        {
            return;
        }

        Transform root = boardPresenter3D.PiecesRoot;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null)
            {
                continue;
            }

            ChainConnectorView3D link = child.GetComponent<ChainConnectorView3D>();
            if (link == null || IsTrackedConnector(link))
            {
                continue;
            }

            DestroyConnector(link);
        }
    }

    private bool IsTrackedConnector(ChainConnectorView3D view)
    {
        if (view == null)
        {
            return false;
        }

        foreach (KeyValuePair<int, List<ChainConnectorView3D>> pair in connectorsByBlockId)
        {
            List<ChainConnectorView3D> links = pair.Value;
            if (links == null)
            {
                continue;
            }

            for (int i = 0; i < links.Count; i++)
            {
                if (links[i] == view)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void DestroyUntrackedUnder(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null)
            {
                continue;
            }

            PieceView3D view = child.GetComponent<PieceView3D>();
            if (view == null)
            {
                continue;
            }

            if (IsUnregisteredNestedInnerTravel(view) || !IsTrackedView(view))
            {
                DestroyView(view);
            }
        }
    }

    private bool IsTrackedView(PieceView3D view)
    {
        if (view == null)
        {
            return false;
        }

        foreach (KeyValuePair<int, PieceView3D> pair in worldViewsByBlockId)
        {
            if (pair.Value == view)
            {
                return true;
            }
        }

        foreach (KeyValuePair<int, PieceView3D> pair in worldViewsByTargetId)
        {
            if (pair.Value == view)
            {
                return true;
            }
        }

        if (IsInExtraMap(extraViewsByBlockId, view) || IsInExtraMap(extraViewsByTargetId, view))
        {
            return true;
        }

        if (dissolvingViews.Contains(view) || view == chainTravelView)
        {
            return true;
        }

        if (nestedInnerTravelers.ContainsKey(view))
        {
            return true;
        }

        return false;
    }

    private static bool IsNestedInnerTravelName(string objectName)
    {
        return !string.IsNullOrEmpty(objectName)
            && objectName.IndexOf("NestedInnerTravel", StringComparison.Ordinal) >= 0;
    }

    private bool IsUnregisteredNestedInnerTravel(PieceView3D view)
    {
        return view != null
            && IsNestedInnerTravelName(view.name)
            && !nestedInnerTravelers.ContainsKey(view);
    }

    private void RegisterNestedInnerTraveler(PieceView3D travel, int ownerBlockId)
    {
        if (travel == null)
        {
            return;
        }

        nestedInnerTravelers[travel] = ownerBlockId;
    }

    private void DissolveNestedInnerTravelersForOwner(int ownerBlockId)
    {
        if (nestedInnerTravelers.Count == 0)
        {
            return;
        }

        var owned = new List<PieceView3D>();
        foreach (KeyValuePair<PieceView3D, int> pair in nestedInnerTravelers)
        {
            if (pair.Key != null && pair.Value == ownerBlockId)
            {
                owned.Add(pair.Key);
            }
        }

        for (int i = 0; i < owned.Count; i++)
        {
            BeginDissolveView(owned[i]);
        }
    }

    private void PruneNestedInnerTravelers(Block[] blocks)
    {
        var liveOwners = new HashSet<int>();
        if (blocks != null)
        {
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i] != null)
                {
                    liveOwners.Add(blocks[i].GetInstanceID());
                }
            }
        }

        if (nestedInnerTravelers.Count > 0)
        {
            var doomed = new List<PieceView3D>();
            foreach (KeyValuePair<PieceView3D, int> pair in nestedInnerTravelers)
            {
                PieceView3D travel = pair.Key;
                if (travel == null || !liveOwners.Contains(pair.Value))
                {
                    doomed.Add(travel);
                }
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                DestroyView(doomed[i]);
            }
        }

        PurgeDestroyedNestedInnerTravelers();
        SweepUnregisteredNestedInnerTravelers(immediate: false);
    }

    private void ClearAllNestedInnerTravelersImmediate()
    {
        if (nestedInnerTravelers.Count > 0)
        {
            var snapshot = new List<PieceView3D>(nestedInnerTravelers.Keys);
            for (int i = 0; i < snapshot.Count; i++)
            {
                DestroyView(snapshot[i], immediate: true);
            }
        }

        nestedInnerTravelers.Clear();
        if (chainTravelInnerLayer && chainTravelView != null && !IsOccupantCellView(chainTravelView))
        {
            DestroyView(chainTravelView, immediate: true);
        }

        if (dissolvingViews.Count > 0)
        {
            var dissolving = new List<PieceView3D>(dissolvingViews);
            for (int i = 0; i < dissolving.Count; i++)
            {
                DestroyView(dissolving[i], immediate: true);
            }

            dissolvingViews.Clear();
        }

        PurgeDestroyedNestedInnerTravelers();
        SweepUnregisteredNestedInnerTravelers(immediate: true);
    }

    private void SweepUnregisteredNestedInnerTravelers(bool immediate)
    {
        if (boardPresenter3D == null)
        {
            return;
        }

        SweepUnregisteredNestedInnerTravelersUnder(boardPresenter3D.PiecesRoot, immediate);
        SweepUnregisteredNestedInnerTravelersUnder(boardPresenter3D.NestsRoot, immediate);
    }

    private void SweepUnregisteredNestedInnerTravelersUnder(Transform root, bool immediate)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null || !IsNestedInnerTravelName(child.name))
            {
                continue;
            }

            PieceView3D view = child.GetComponent<PieceView3D>();
            if (view == null)
            {
                if (!Application.isPlaying || immediate)
                {
                    DestroyImmediate(child.gameObject);
                }
                else
                {
                    Destroy(child.gameObject);
                }

                continue;
            }

            if (!nestedInnerTravelers.ContainsKey(view))
            {
                DestroyView(view, immediate);
            }
        }
    }

    private void PurgeDestroyedNestedInnerTravelers()
    {
        if (nestedInnerTravelers.Count == 0)
        {
            return;
        }

        var stale = new List<PieceView3D>();
        foreach (KeyValuePair<PieceView3D, int> pair in nestedInnerTravelers)
        {
            if (pair.Key == null)
            {
                stale.Add(pair.Key);
            }
        }

        for (int i = 0; i < stale.Count; i++)
        {
            nestedInnerTravelers.Remove(stale[i]);
        }
    }

    private static bool IsInExtraMap(Dictionary<int, List<PieceView3D>> map, PieceView3D view)
    {
        foreach (KeyValuePair<int, List<PieceView3D>> pair in map)
        {
            List<PieceView3D> extras = pair.Value;
            if (extras == null)
            {
                continue;
            }

            for (int i = 0; i < extras.Count; i++)
            {
                if (extras[i] == view)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Drops ice/shutter views whose gameplay state is gone and presentation tween finished.
    /// Needed because obstacle fingerprint may not change again after melt/open completes.
    /// </summary>
    private void CleanupFinishedObstacleViews()
    {
        var removeIce = new List<int>();
        foreach (KeyValuePair<int, IceView3D> pair in worldViewsByIceId)
        {
            IceView3D view = pair.Value;
            if (view == null)
            {
                removeIce.Add(pair.Key);
                continue;
            }

            IceState ice = view.Source;
            if (ice == null || (!ice.IsFrozen && !view.IsPresentationAnimating))
            {
                removeIce.Add(pair.Key);
            }
        }

        for (int i = 0; i < removeIce.Count; i++)
        {
            int id = removeIce[i];
            if (worldViewsByIceId.TryGetValue(id, out IceView3D view) && view != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(view.gameObject);
                }
                else
                {
                    DestroyImmediate(view.gameObject);
                }
            }

            worldViewsByIceId.Remove(id);
        }

        var removeShutters = new List<int>();
        foreach (KeyValuePair<int, ShutterView3D> pair in worldViewsByShutterId)
        {
            ShutterView3D view = pair.Value;
            if (view == null)
            {
                removeShutters.Add(pair.Key);
                continue;
            }

            ShutterState shutter = view.Source;
            if (shutter == null || (!shutter.IsClosed && !view.IsOpeningPresentation))
            {
                removeShutters.Add(pair.Key);
            }
        }

        for (int i = 0; i < removeShutters.Count; i++)
        {
            int id = removeShutters[i];
            if (worldViewsByShutterId.TryGetValue(id, out ShutterView3D view) && view != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(view.gameObject);
                }
                else
                {
                    DestroyImmediate(view.gameObject);
                }
            }

            worldViewsByShutterId.Remove(id);
        }
    }

    private void PruneObstacleViews<T>(Dictionary<int, T> map, HashSet<int> keep) where T : Component
    {
        var remove = new List<int>();
        foreach (var pair in map)
        {
            if (!keep.Contains(pair.Key))
            {
                remove.Add(pair.Key);
            }
        }

        for (int i = 0; i < remove.Count; i++)
        {
            int id = remove[i];
            if (map.TryGetValue(id, out T view) && view != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(view.gameObject);
                }
                else
                {
                    DestroyImmediate(view.gameObject);
                }
            }

            map.Remove(id);
        }
    }

    private void SetUiBoardVisualsActive(bool active)
    {
        // World3D: hide opaque gameplay overlay plate (Image only — keep HUD hierarchy).
        SetGamePlayOverlayBackgroundRendering(active);

        if (uiBoardVisualRoots != null && uiBoardVisualRoots.Length > 0)
        {
            for (int i = 0; i < uiBoardVisualRoots.Length; i++)
            {
                if (uiBoardVisualRoots[i] != null)
                {
                    uiBoardVisualRoots[i].SetActive(active);
                }
            }

            return;
        }

        if (boardManager == null)
        {
            return;
        }

        Transform board = boardManager.transform;
        SetChildActive(board, "BoardBackground", active);
        SetChildActive(board, "RuntimeGrid", active);
    }

    private void SetGamePlayOverlayBackgroundRendering(bool enabled)
    {
        if (gamePlayOverlayBackground == null)
        {
            ResolveGamePlayOverlayBackground();
        }

        if (gamePlayOverlayBackground != null)
        {
            gamePlayOverlayBackground.enabled = enabled;
        }
    }

    private void ResolveGamePlayOverlayBackground()
    {
        if (gamePlayOverlayBackground != null)
        {
            return;
        }

        GameObject uiController = GameObject.Find("UIController");
        if (uiController == null)
        {
            return;
        }

        Transform bg = uiController.transform.Find("GameplayCanvas/BG");
        if (bg == null)
        {
            bg = uiController.transform.Find("GamePlay/BG");
        }

        if (bg != null)
        {
            gamePlayOverlayBackground = bg.GetComponent<Image>();
        }
    }

    private static void SetChildActive(Transform parent, string childName, bool active)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            child.gameObject.SetActive(active);
        }
    }

    private static void EnsureSingleAudioListener(GameObject cameraObject)
    {
        if (cameraObject == null)
        {
            return;
        }

        AudioListener listener = cameraObject.GetComponent<AudioListener>();
        if (listener == null)
        {
            listener = cameraObject.AddComponent<AudioListener>();
        }

        listener.enabled = true;
    }

    private void ResolveReferences()
    {
        if (boardManager == null)
        {
            boardManager = FindFirstObjectByType<BoardManager>();
        }

        if (boardPresenter3D == null)
        {
            boardPresenter3D = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Include);
        }

        if (boardCamera3D == null)
        {
            boardCamera3D = FindFirstObjectByType<BoardCamera3D>(FindObjectsInactive.Include);
        }

        if (uiModeCamera == null)
        {
            Camera main = Camera.main;
            if (main != null && (boardCamera3D == null || main != boardCamera3D.Camera))
            {
                uiModeCamera = main;
            }
        }

        if (boardLight == null)
        {
            var lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional && lights[i].name.Contains("Board"))
                {
                    boardLight = lights[i];
                    break;
                }
            }
        }

        if (theme == null)
        {
            theme = FindThemeAsset();
        }

        ResolveGamePlayOverlayBackground();
    }

    private static ShapeNestTheme FindThemeAsset()
    {
#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ShapeNestTheme");
        if (guids.Length > 0)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ShapeNestTheme>(path);
        }
#endif
        return null;
    }
}
