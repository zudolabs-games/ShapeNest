using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation-only world Hammer. Reuses HammerButton's Icon sprite.
/// Does not select blocks, move occupancy, or consume charges.
/// </summary>
[DisallowMultipleComponent]
public class HammerSmashView3D : MonoBehaviour
{
    public const string ObjectName = "HammerSmashView3D";

    private SpriteRenderer spriteRenderer;
    private Transform swingPivot;
    private Transform billboard;
    private Sequence swaySequence;
    private Camera boardCamera;
    private bool visible;

    public bool IsVisible => visible && gameObject.activeInHierarchy;

    public static HammerSmashView3D Ensure()
    {
        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        Transform root = presenter != null ? presenter.VfxRoot : null;
        if (root != null)
        {
            Transform existing = root.Find(ObjectName);
            if (existing != null)
            {
                HammerSmashView3D view = existing.GetComponent<HammerSmashView3D>();
                if (view != null)
                {
                    return view;
                }
            }
        }

        HammerSmashView3D created = Object.FindFirstObjectByType<HammerSmashView3D>(FindObjectsInactive.Include);
        if (created != null)
        {
            return created;
        }

        GameObject go = new GameObject(ObjectName);
        if (root != null)
        {
            go.transform.SetParent(root, false);
        }

        created = go.AddComponent<HammerSmashView3D>();
        created.EnsureVisuals();
        return created;
    }

    public static Sprite ResolveHammerButtonSprite()
    {
        HammerBoosterButton button = Object.FindFirstObjectByType<HammerBoosterButton>(FindObjectsInactive.Include);
        if (button == null)
        {
            return null;
        }

        Transform icon = button.transform.Find("Icon - Image");
        if (icon != null)
        {
            Image image = icon.GetComponent<Image>();
            if (image != null && image.sprite != null)
            {
                return image.sprite;
            }
        }

        Image[] images = button.GetComponentsInChildren<Image>(true);
        Image background = button.GetComponent<Image>();
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] == null || images[i] == background || images[i].sprite == null)
            {
                continue;
            }

            return images[i].sprite;
        }

        return null;
    }

    public void ShowAt(Vector3 worldPosition)
    {
        EnsureVisuals();
        visible = true;
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        transform.position = OffsetTowardCamera(worldPosition);
        RefreshSprite();
        StartIdleSway();
    }

    public void SetWorldPosition(Vector3 worldPosition)
    {
        transform.position = OffsetTowardCamera(worldPosition);
    }

    public void Hide()
    {
        visible = false;
        KillSway();
        if (swingPivot != null)
        {
            swingPivot.localRotation = Quaternion.identity;
        }

        if (spriteRenderer != null)
        {
            RefreshSprite();
        }

        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    public IEnumerator PlaySwing(Vector3 impactPosition, float duration)
    {
        float life = Mathf.Max(0.24f, duration);
        float wind = Mathf.Clamp(life * 0.42f, 0.10f, 0.14f);
        float slam = Mathf.Clamp(life - wind, 0.14f, 0.18f);
        yield return PlayWindUp(wind);
        yield return PlaySlam(impactPosition, slam);
    }

    public IEnumerator PlayWindUp(float duration)
    {
        EnsureVisuals();
        KillSway();
        if (swingPivot == null)
        {
            yield break;
        }

        float life = Mathf.Max(0.10f, duration);
        Quaternion rest = Quaternion.Euler(0f, 0f, 16f);
        Quaternion back = Quaternion.Euler(0f, 0f, 58f);
        swingPivot.localRotation = rest;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Clamp01(t + Time.deltaTime / life);
            float eased = TweenAnimationUtility.EvaluateEaseInCubic(t);
            swingPivot.localRotation = Quaternion.Slerp(rest, back, eased);
            yield return null;
        }

        swingPivot.localRotation = back;
    }

    public IEnumerator PlaySlam(Vector3 impactPosition, float duration)
    {
        EnsureVisuals();
        KillSway();
        if (swingPivot == null)
        {
            yield break;
        }

        float life = Mathf.Max(0.14f, duration);
        Quaternion back = swingPivot.localRotation;
        Quaternion hit = Quaternion.Euler(0f, 0f, -72f);
        Vector3 startPos = transform.position;
        Vector3 hitPos = OffsetTowardCamera(impactPosition - Vector3.up * 0.06f);
        Vector3 spriteRest = spriteRenderer != null ? spriteRenderer.transform.localScale : Vector3.one;
        Vector3 spritePeak = spriteRest * 1.12f;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Clamp01(t + Time.deltaTime / life);
            float eased = TweenAnimationUtility.EvaluateEaseInCubic(t);
            swingPivot.localRotation = Quaternion.Slerp(back, hit, eased);
            transform.position = Vector3.LerpUnclamped(startPos, hitPos, eased);
            if (spriteRenderer != null)
            {
                spriteRenderer.transform.localScale = Vector3.LerpUnclamped(spriteRest, spritePeak, eased);
            }

            yield return null;
        }

        swingPivot.localRotation = hit;
        transform.position = hitPos;
    }

    public IEnumerator PlayImpactSettle(float duration)
    {
        EnsureVisuals();
        if (swingPivot == null)
        {
            yield break;
        }

        float life = Mathf.Max(0.03f, duration);
        Quaternion hit = swingPivot.localRotation;
        Quaternion settle = Quaternion.Euler(0f, 0f, -58f);
        Vector3 startPos = transform.position;
        Vector3 settlePos = startPos + Vector3.up * 0.03f;
        Vector3 spriteHit = spriteRenderer != null ? spriteRenderer.transform.localScale : Vector3.one;
        Vector3 spriteSettle = spriteHit / 1.12f;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Clamp01(t + Time.deltaTime / life);
            float eased = TweenAnimationUtility.EvaluateEaseOutCubic(t);
            swingPivot.localRotation = Quaternion.Slerp(hit, settle, eased);
            transform.position = Vector3.LerpUnclamped(startPos, settlePos, eased);
            if (spriteRenderer != null)
            {
                spriteRenderer.transform.localScale = Vector3.LerpUnclamped(spriteHit, spriteSettle, eased);
            }

            yield return null;
        }
    }

    private void LateUpdate()
    {
        if (!visible || billboard == null)
        {
            return;
        }

        if (boardCamera == null || !boardCamera.isActiveAndEnabled)
        {
            BoardCamera3D cam3d = Object.FindFirstObjectByType<BoardCamera3D>(FindObjectsInactive.Include);
            boardCamera = cam3d != null ? cam3d.Camera : Camera.main;
        }

        if (boardCamera == null)
        {
            return;
        }

        billboard.rotation = boardCamera.transform.rotation;
    }

    private void OnDisable()
    {
        KillSway();
        visible = false;
    }

    private void EnsureVisuals()
    {
        if (billboard == null)
        {
            Transform existing = transform.Find("Billboard");
            GameObject billboardGo = existing != null ? existing.gameObject : new GameObject("Billboard");
            if (existing == null)
            {
                billboardGo.transform.SetParent(transform, false);
            }

            billboard = billboardGo.transform;
            billboard.localPosition = Vector3.zero;
        }

        if (swingPivot == null)
        {
            Transform existing = billboard.Find("SwingPivot");
            GameObject pivotGo = existing != null ? existing.gameObject : new GameObject("SwingPivot");
            if (existing == null)
            {
                pivotGo.transform.SetParent(billboard, false);
            }

            swingPivot = pivotGo.transform;
            swingPivot.localPosition = Vector3.zero;
            swingPivot.localRotation = Quaternion.identity;
        }

        if (spriteRenderer == null)
        {
            Transform existing = swingPivot.Find("Sprite");
            GameObject spriteGo = existing != null ? existing.gameObject : new GameObject("Sprite");
            if (existing == null)
            {
                spriteGo.transform.SetParent(swingPivot, false);
            }

            spriteRenderer = spriteGo.GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
            {
                spriteRenderer = spriteGo.AddComponent<SpriteRenderer>();
            }

            spriteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            spriteRenderer.receiveShadows = false;
            spriteRenderer.sortingOrder = 40;
            Material spriteMat = spriteRenderer.sharedMaterial;
            if (spriteMat == null)
            {
                spriteRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            }
        }

        RefreshSprite();
    }

    private void RefreshSprite()
    {
        if (spriteRenderer == null)
        {
            return;
        }

        Sprite sprite = ResolveHammerButtonSprite();
        spriteRenderer.sprite = sprite;
        spriteRenderer.enabled = sprite != null;

        BoardPresenter3D presenter = Object.FindFirstObjectByType<BoardPresenter3D>(FindObjectsInactive.Exclude);
        float cell = presenter != null ? presenter.CellWorldSize : 1f;
        float desired = Mathf.Max(0.2f, cell * 1.05f);
        float spriteHeight = sprite != null ? sprite.bounds.size.y : 1f;
        float scale = spriteHeight > 0.0001f ? desired / spriteHeight : desired;
        spriteRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        spriteRenderer.transform.localPosition = new Vector3(0f, desired * 0.28f, 0f);
        spriteRenderer.color = Color.white;
    }

    private void StartIdleSway()
    {
        KillSway();
        if (swingPivot == null)
        {
            return;
        }

        Quaternion a = Quaternion.Euler(0f, 0f, 14f);
        Quaternion b = Quaternion.Euler(0f, 0f, 28f);
        swingPivot.localRotation = a;
        swaySequence = DOTween.Sequence().SetId(TweenAnimationUtility.VfxId).SetLink(gameObject).SetLoops(-1, LoopType.Yoyo);
        swaySequence.Append(TweenAnimationUtility.Progress(0.35f, t =>
        {
            swingPivot.localRotation = Quaternion.Slerp(a, b, TweenAnimationUtility.EvaluateEaseInOutSine(t));
        }));
    }

    private Vector3 OffsetTowardCamera(Vector3 worldPosition)
    {
        if (boardCamera == null || !boardCamera.isActiveAndEnabled)
        {
            BoardCamera3D cam3d = Object.FindFirstObjectByType<BoardCamera3D>(FindObjectsInactive.Include);
            boardCamera = cam3d != null ? cam3d.Camera : Camera.main;
        }

        if (boardCamera == null)
        {
            return worldPosition;
        }

        return worldPosition - boardCamera.transform.forward * 0.22f;
    }

    private void KillSway()
    {
        if (swaySequence != null && swaySequence.IsActive())
        {
            swaySequence.Kill(false);
        }

        swaySequence = null;
    }
}
