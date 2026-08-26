using UnityEngine;

/// <summary>
/// World3D pointer picking: camera ray → PieceView3D → gameplay <see cref="Block"/>.
/// Does not move pieces; forwards selection into the existing <see cref="InputManager"/> path.
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
        Camera camera = boardCamera3D != null ? boardCamera3D.Camera : null;
        if (camera == null || !camera.isActiveAndEnabled)
        {
            return null;
        }

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
            if (collider == null)
            {
                continue;
            }

            PieceView3D view = collider.GetComponentInParent<PieceView3D>();
            if (view == null || !view.IsSelectable)
            {
                continue;
            }

            Block block = view.SourceBlock;
            if (block == null)
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
