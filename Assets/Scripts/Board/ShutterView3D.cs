using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// World3D presentation for <see cref="ShutterState"/>.
/// Shutters cover cells (no direction enum). Owns DOTween open animation.
/// SyncFromSource never rebuilds closed geometry or restarts open while opening.
/// </summary>
[DisallowMultipleComponent]
public class ShutterView3D : MonoBehaviour
{
    private const float OpenDuration = 0.32f;

    [SerializeField]
    private Transform cellsRoot;

    [SerializeField]
    private float plateHeight = 0.13f;

    [SerializeField]
    private float plateLift = 0.34f;

    [SerializeField]
    private float footprintFactor = 0.94f;

    private ShutterState source;
    private static Material sharedPlateMaterial;
    private static Material sharedSlatMaterial;
    private readonly List<Transform> cellPlates = new List<Transform>();
    private readonly List<Vector3> plateRestPositions = new List<Vector3>();
    private readonly List<Vector3> plateRestScales = new List<Vector3>();

    private bool wasClosed;
    private bool isOpening;
    private bool openPresentationComplete;
    private bool openVfxPlayed;
    private Sequence activeSequence;
    private float cachedPlateSize;
    private float cachedPlateHeight;
    private float cachedLift;

    public ShutterState Source => source;

    /// <summary>True while the closed→open DOTween is running.</summary>
    public bool IsOpeningPresentation => isOpening && !openPresentationComplete;

    /// <summary>True after open animation finished (safe to prune).</summary>
    public bool IsOpenPresentationComplete => openPresentationComplete;

    public void Bind(ShutterState shutter, Material plateMaterial, Material slatMaterial)
    {
        bool sameSource = source == shutter;
        source = shutter;
        EnsureRoot();
        if (plateMaterial != null)
        {
            sharedPlateMaterial = plateMaterial;
        }

        if (slatMaterial != null)
        {
            sharedSlatMaterial = slatMaterial;
        }

        if (!sameSource)
        {
            KillOwnedTweens(false);
            isOpening = false;
            openPresentationComplete = false;
            openVfxPlayed = false;
            wasClosed = shutter != null && shutter.IsClosed;
        }

        SyncFromSource();
    }

    public void ClearBind()
    {
        KillOwnedTweens(false);
        source = null;
        wasClosed = false;
        isOpening = false;
        openPresentationComplete = false;
        openVfxPlayed = false;
        ClearPlates();
        gameObject.SetActive(false);
    }

    public void SyncFromSource()
    {
        EnsureRoot();
        bool closed = source != null && source.IsClosed && source.Cells != null && source.Cells.Count > 0;

        if (isOpening)
        {
            // Opening owns geometry until complete — do not rebuild or snap.
            return;
        }

        if (openPresentationComplete && !closed)
        {
            ClearPlates();
            gameObject.SetActive(false);
            return;
        }

        if (!closed)
        {
            if (wasClosed)
            {
                BeginOpen();
            }
            else
            {
                ClearPlates();
                gameObject.SetActive(false);
                openPresentationComplete = true;
            }

            wasClosed = false;
            return;
        }

        // Closed: rebuild / refresh plates (level load or durability still closed).
        wasClosed = true;
        openPresentationComplete = false;
        openVfxPlayed = false;
        isOpening = false;
        ApplyClosedLayout();
    }

    private void OnDisable()
    {
        KillOwnedTweens(false);
        isOpening = false;
        activeSequence = null;
    }

    private void OnDestroy()
    {
        KillOwnedTweens(false);
        isOpening = false;
        activeSequence = null;
    }

    private void ApplyClosedLayout()
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        if (presenter == null || source == null || source.Cells == null || source.Cells.Count == 0)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        IReadOnlyList<Vector2Int> cells = source.Cells;
        EnsurePlateCount(cells.Count);

        float cell = presenter.CellWorldSize;
        float scale = cell / BoardAdaptivePresentation3D.ReferenceCellSize;
        cachedPlateSize = cell * footprintFactor;
        cachedPlateHeight = plateHeight * scale;
        cachedLift = plateLift * scale;
        IGridSpace space = presenter.GridSpace;
        float y = presenter.CellSurfaceWorldY + cachedLift;

        plateRestPositions.Clear();
        plateRestScales.Clear();
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < cells.Count; i++)
        {
            Transform plate = cellPlates[i];
            Vector3 world = space.GridToWorld(cells[i]);
            world.y = y;
            Vector3 restScale = new Vector3(cachedPlateSize, cachedPlateHeight, cachedPlateSize);
            plate.position = world;
            plate.localScale = restScale;
            plate.gameObject.SetActive(true);
            ApplySlats(plate, cachedPlateSize);
            plateRestPositions.Add(world);
            plateRestScales.Add(restScale);
            centroid += world;
        }

        if (cells.Count > 0)
        {
            transform.position = centroid / cells.Count;
        }

        for (int i = cells.Count; i < cellPlates.Count; i++)
        {
            cellPlates[i].gameObject.SetActive(false);
        }
    }

    private void BeginOpen()
    {
        if (isOpening || openPresentationComplete)
        {
            return;
        }

        // Need existing plates; if none, snap closed layout once then open.
        if (cellPlates.Count == 0 || !AnyPlateActive())
        {
            wasClosed = true;
            ApplyClosedLayout();
        }

        if (!AnyPlateActive())
        {
            openPresentationComplete = true;
            ClearPlates();
            gameObject.SetActive(false);
            return;
        }

        isOpening = true;
        openPresentationComplete = false;
        gameObject.SetActive(true);
        KillOwnedTweens(false);

        if (!openVfxPlayed)
        {
            openVfxPlayed = true;
            BoardVfx3D.PlayShutterOpen(transform.position);
        }

        // Capture rests aligned to plate indices.
        if (plateRestPositions.Count != cellPlates.Count)
        {
            CaptureRestsFromCurrent();
        }

        float rise = Mathf.Max(0.05f, cachedLift * 0.85f);
        if (rise < 0.05f)
        {
            rise = cachedPlateSize * 0.35f;
        }

        activeSequence = DOTween.Sequence().SetLink(gameObject);
        activeSequence.Append(TweenAnimationUtility.Progress(OpenDuration, t =>
        {
            float eased = TweenAnimationUtility.EvaluateEaseOutQuad(t);
            int count = Mathf.Min(cellPlates.Count, plateRestPositions.Count);
            for (int i = 0; i < count; i++)
            {
                Transform plate = cellPlates[i];
                if (plate == null || !plate.gameObject.activeSelf)
                {
                    continue;
                }

                Vector3 restPos = plateRestPositions[i];
                Vector3 restScale = i < plateRestScales.Count
                    ? plateRestScales[i]
                    : plate.localScale;
                // Retract upward and slightly outward from centroid; shrink height.
                Vector3 away = restPos - transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f)
                {
                    away = Vector3.forward;
                }

                away.Normalize();
                Vector3 pos = restPos
                    + Vector3.up * (rise * eased)
                    + away * (cachedPlateSize * 0.22f * eased);
                Vector3 scale = Vector3.LerpUnclamped(
                    restScale,
                    new Vector3(restScale.x * 0.85f, restScale.y * 0.08f, restScale.z * 0.85f),
                    eased);
                plate.position = pos;
                plate.localScale = scale;
                // Subtle yaw for polish (not gameplay direction).
                float yaw = (i % 2 == 0 ? 1f : -1f) * 12f * eased;
                plate.localRotation = Quaternion.Euler(8f * eased, yaw, 0f);
            }
        }));
        activeSequence.OnComplete(() =>
        {
            FinishOpen();
        });
    }

    private void FinishOpen()
    {
        isOpening = false;
        openPresentationComplete = true;
        wasClosed = false;
        activeSequence = null;
        ClearPlates();
        gameObject.SetActive(false);
    }

    private void CaptureRestsFromCurrent()
    {
        plateRestPositions.Clear();
        plateRestScales.Clear();
        for (int i = 0; i < cellPlates.Count; i++)
        {
            Transform plate = cellPlates[i];
            if (plate == null)
            {
                plateRestPositions.Add(Vector3.zero);
                plateRestScales.Add(Vector3.one);
                continue;
            }

            plateRestPositions.Add(plate.position);
            plateRestScales.Add(plate.localScale);
        }
    }

    private bool AnyPlateActive()
    {
        for (int i = 0; i < cellPlates.Count; i++)
        {
            if (cellPlates[i] != null && cellPlates[i].gameObject.activeSelf)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureRoot()
    {
        if (cellsRoot == null)
        {
            Transform existing = transform.Find("Cells");
            if (existing != null)
            {
                cellsRoot = existing;
            }
            else
            {
                var go = new GameObject("Cells");
                go.transform.SetParent(transform, false);
                cellsRoot = go.transform;
            }
        }
    }

    private void EnsurePlateCount(int count)
    {
        while (cellPlates.Count < count)
        {
            GameObject plate;
            bool designerPlate = ShapeNestVisualCatalog3D.TryGetShutterPrefab(out GameObject shutterPrefab);
            if (designerPlate)
            {
                plate = Instantiate(shutterPrefab);
            }
            else
            {
                // Phase 52K: rounded molded plate (visible thickness + beveled edges).
                plate = new GameObject("ShutterPlate");
                var filter = plate.AddComponent<MeshFilter>();
                filter.sharedMesh = BoardMeshFactory3D.GetRoundedBox(1f, 1f, 1f, 0.14f, 3);
                plate.AddComponent<MeshRenderer>();
            }

            plate.name = "ShutterPlate_" + cellPlates.Count;
            plate.transform.SetParent(cellsRoot, false);
            RemoveCollider(plate);
            if (!designerPlate)
            {
                ApplyMaterial(plate, GetPlateMaterial(), new Color(0.78f, 0.52f, 0.22f, 1f));
                CreateSlat(plate.transform, "SlatTop", new Vector3(0f, 0.52f, 0.30f));
                CreateSlat(plate.transform, "SlatBottom", new Vector3(0f, 0.52f, -0.30f));
            }

            cellPlates.Add(plate.transform);
        }
    }

    private void ApplySlats(Transform plate, float plateSize)
    {
        _ = plateSize;
        for (int i = 0; i < plate.childCount; i++)
        {
            Transform slat = plate.GetChild(i);
            if (!slat.name.StartsWith("Slat"))
            {
                continue;
            }

            Vector3 lp = slat.localPosition;
            // Slightly taller ridges for readable side depth under board lighting.
            slat.localScale = new Vector3(0.90f, 0.16f, 0.10f);
            slat.localPosition = new Vector3(0f, 0.52f, lp.z);
            ApplyMaterial(slat.gameObject, GetSlatMaterial(), new Color(0.88f, 0.68f, 0.30f, 1f));
        }
    }

    private static void CreateSlat(Transform parent, string name, Vector3 localPos)
    {
        GameObject slat = new GameObject(name);
        var filter = slat.AddComponent<MeshFilter>();
        filter.sharedMesh = BoardMeshFactory3D.GetRoundedBox(1f, 1f, 1f, 0.18f, 2);
        slat.AddComponent<MeshRenderer>();
        slat.transform.SetParent(parent, false);
        slat.transform.localPosition = localPos;
        slat.transform.localScale = new Vector3(0.90f, 0.16f, 0.10f);
        ApplyMaterial(slat, GetSlatMaterial(), new Color(0.88f, 0.68f, 0.30f, 1f));
    }

    private void ClearPlates()
    {
        for (int i = 0; i < cellPlates.Count; i++)
        {
            if (cellPlates[i] != null)
            {
                cellPlates[i].localRotation = Quaternion.identity;
                cellPlates[i].gameObject.SetActive(false);
            }
        }
    }

    private void KillOwnedTweens(bool complete)
    {
        if (activeSequence != null && activeSequence.IsActive())
        {
            activeSequence.Kill(complete);
        }

        activeSequence = null;
    }

    private static void RemoveCollider(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();
        if (collider == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(collider);
        }
        else
        {
            Object.DestroyImmediate(collider);
        }
    }

    private static void ApplyMaterial(GameObject target, Material material, Color fallback)
    {
        MeshRenderer renderer = target.GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            return;
        }

        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;

        if (material != null)
        {
            renderer.sharedMaterial = material;
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        renderer.sharedMaterial = new Material(shader) { color = fallback };
    }

    /// <summary>Clears cached shutter materials so presentation retunes pick up after domain reload.</summary>
    public static void InvalidateSharedMaterials()
    {
        sharedPlateMaterial = null;
        sharedSlatMaterial = null;
    }

    public static Material GetPlateMaterial()
    {
        if (sharedPlateMaterial != null)
        {
            return sharedPlateMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        // Phase 52K: molded-plastic warm barrier — Metallic 0, no emission, board-family specular.
        sharedPlateMaterial = new Material(shader)
        {
            name = "ShutterPlate3D_Runtime",
            color = new Color(0.86f, 0.62f, 0.22f, 1f)
        };
        if (sharedPlateMaterial.HasProperty("_BaseColor"))
        {
            sharedPlateMaterial.SetColor("_BaseColor", sharedPlateMaterial.color);
        }

        if (sharedPlateMaterial.HasProperty("_Smoothness"))
        {
            sharedPlateMaterial.SetFloat("_Smoothness", 0.58f);
        }

        if (sharedPlateMaterial.HasProperty("_Metallic"))
        {
            sharedPlateMaterial.SetFloat("_Metallic", 0f);
        }

        if (sharedPlateMaterial.HasProperty("_EmissionColor"))
        {
            sharedPlateMaterial.DisableKeyword("_EMISSION");
            sharedPlateMaterial.SetColor("_EmissionColor", Color.black);
        }

        return sharedPlateMaterial;
    }

    public static Material GetSlatMaterial()
    {
        if (sharedSlatMaterial != null)
        {
            return sharedSlatMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        sharedSlatMaterial = new Material(shader)
        {
            name = "ShutterSlat3D_Runtime",
            color = new Color(0.94f, 0.76f, 0.34f, 1f)
        };
        if (sharedSlatMaterial.HasProperty("_BaseColor"))
        {
            sharedSlatMaterial.SetColor("_BaseColor", sharedSlatMaterial.color);
        }

        if (sharedSlatMaterial.HasProperty("_Smoothness"))
        {
            sharedSlatMaterial.SetFloat("_Smoothness", 0.55f);
        }

        if (sharedSlatMaterial.HasProperty("_Metallic"))
        {
            sharedSlatMaterial.SetFloat("_Metallic", 0f);
        }

        if (sharedSlatMaterial.HasProperty("_EmissionColor"))
        {
            sharedSlatMaterial.DisableKeyword("_EMISSION");
            sharedSlatMaterial.SetColor("_EmissionColor", Color.black);
        }

        return sharedSlatMaterial;
    }
}
