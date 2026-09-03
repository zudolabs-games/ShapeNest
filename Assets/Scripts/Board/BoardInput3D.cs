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
    /// Resolves a gameplay block under the screen pointer via physics raycast.
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
        }

        return found;
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
