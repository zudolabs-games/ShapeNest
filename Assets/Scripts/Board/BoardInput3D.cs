using UnityEngine;

/// <summary>
/// World3D pointer picking: camera ray → PieceView3D → gameplay <see cref="Block"/>.
/// Does not move pieces; used by <see cref="InputManager"/> fingerwise drag pickup.
/// </summary>
[DisallowMultipleComponent]
public class BoardInput3D : MonoBehaviour
{
    [SerializeField]
    private BoardPresentationController presentationController;

    [SerializeField]
    private BoardCamera3D boardCamera3D;

    [SerializeField]
    private float raycastDistance = 200f;

    [SerializeField]
    private LayerMask pickMask = ~0;

    [SerializeField]
    private bool debugPicks;

    /// <summary>
    /// Extra pick radius beyond half a cell pitch (board-plane). Softens edge/near-miss taps.
    /// </summary>
    private const float PickupMarginCellFraction = 0.28f;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    public bool IsActive
    {
        get => isActiveAndEnabled;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Resolves a gameplay block under the screen pointer via physics raycast,
    /// then a small board-plane proximity fallback for near-miss taps.
    /// Returns null for nests, board geometry, empty space, or when World3D input is inactive.
    /// </summary>
    public Block TryFindBlock(Vector2 screenPosition)
    {
        if (!IsActive)
        {
            return null;
        }

        ResolveReferences();
        Block found = RaycastFindBlock(screenPosition);
        if (found != null)
        {
            return found;
        }

        // Phase 60: if playable blocks exist without WorldView yet (post-spawn race),
        // bind immediately and retry once — not a timed wait.
        if (presentationController != null && presentationController.HasUnboundPlayableBlocks())
        {
            presentationController.EnsureWorldViewsBound();
            found = RaycastFindBlock(screenPosition);
            if (found != null)
            {
                return found;
            }
        }

        return FindNearestBlockWithinPickupMargin(screenPosition);
    }

    private Block RaycastFindBlock(Vector2 screenPosition)
    {
        Camera camera = boardCamera3D != null ? boardCamera3D.Camera : null;
        if (camera == null || !camera.isActiveAndEnabled)
        {
            return null;
        }

        Physics.SyncTransforms();
        // ScreenPixel coords must match this camera's active pixel rect / targetTexture.
        Ray ray = camera.ScreenPointToRay(screenPosition);
        int hitCount = Physics.RaycastNonAlloc(
            ray,
            hitBuffer,
            raycastDistance,
            pickMask,
            QueryTriggerInteraction.Collide);

        if (hitCount <= 0)
        {
            return null;
        }

        // Closest hit first.
        System.Array.Sort(hitBuffer, 0, hitCount, HitDistanceComparer.Instance);

        for (int i = 0; i < hitCount; i++)
        {
            Collider collider = hitBuffer[i].collider;
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            PieceView3D view = collider.GetComponentInParent<PieceView3D>();
            if (view == null || !view.IsSelectable)
            {
                continue;
            }

            Block block = view.SourceBlock;
            if (block == null || block.IsSettled)
            {
                continue;
            }

            if (debugPicks)
            {
                Debug.Log(
                    $"BoardInput3D: hit {view.name} → Block {block.name} @ {block.GridPosition}",
                    block);
            }

            return block;
        }

        return null;
    }

    /// <summary>
    /// Near-miss pickup: closest movable piece within half-cell + small grid-scaled margin.
    /// Direct raycast hits still win; this only runs after a miss.
    /// </summary>
    private Block FindNearestBlockWithinPickupMargin(Vector2 screenPosition)
    {
        Camera camera = boardCamera3D != null ? boardCamera3D.Camera : null;
        BoardPresenter3D boardPresenter = FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (camera == null || boardPresenter == null || boardPresenter.GridSpace3D == null)
        {
            return null;
        }

        if (!FingerDragController.TryScreenToBoardWorld(camera, boardPresenter, screenPosition, out Vector3 boardHit))
        {
            return null;
        }

        float pitch = Mathf.Max(0.01f, boardPresenter.GridSpace3D.CellPitch);
        float maxDistance = (pitch * 0.5f) + (pitch * PickupMarginCellFraction);
        float maxDistanceSq = maxDistance * maxDistance;

        Block best = null;
        float bestDistSq = float.MaxValue;
        PieceView3D[] views = FindObjectsByType<PieceView3D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < views.Length; i++)
        {
            PieceView3D view = views[i];
            if (view == null || !view.IsSelectable)
            {
                continue;
            }

            Block block = view.SourceBlock;
            if (block == null || block.IsSettled || block.IsFrozen)
            {
                continue;
            }

            BlockMover mover = block.GetComponent<BlockMover>();
            if (mover == null || mover.IsMoving || mover.IsDragging)
            {
                continue;
            }

            Vector3 center = view.PickWorldCenter;
            float dx = center.x - boardHit.x;
            float dz = center.z - boardHit.z;
            float distSq = (dx * dx) + (dz * dz);
            if (distSq > maxDistanceSq || distSq >= bestDistSq)
            {
                continue;
            }

            bestDistSq = distSq;
            best = block;
        }

        if (best != null && debugPicks)
        {
            Debug.Log(
                $"BoardInput3D: proximity pick → Block {best.name} @ {best.GridPosition} dist={Mathf.Sqrt(bestDistSq):F3}",
                best);
        }

        return best;
    }

    /// <summary>Camera used for World3D pointer picking (BoardCamera3D).</summary>
    public Camera PickCamera
    {
        get
        {
            ResolveReferences();
            return boardCamera3D != null ? boardCamera3D.Camera : null;
        }
    }

    private void ResolveReferences()
    {
        if (presentationController == null)
        {
            presentationController = FindFirstObjectByType<BoardPresentationController>();
        }

        if (boardCamera3D == null)
        {
            boardCamera3D = FindFirstObjectByType<BoardCamera3D>(FindObjectsInactive.Include);
        }
    }

    private sealed class HitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public static readonly HitDistanceComparer Instance = new HitDistanceComparer();

        public int Compare(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }
    }
}
