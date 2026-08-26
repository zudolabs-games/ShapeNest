using UnityEngine;

[RequireComponent(typeof(Block))]
public class IceState : MonoBehaviour
{
    private Block block;
    private int durability;
    private bool configured;

    public int Durability => durability;
    public bool IsFrozen => configured && durability > 0;

    public void SetUiPresentationVisible(bool visible)
    {
        // Phase 11: UI ice overlay removed; World3D uses IceView3D.
    }

    public void Configure(Block source, bool enabled, int startingDurability)
    {
        block = source != null ? source : GetComponent<Block>();
        configured = enabled;
        durability = enabled ? Mathf.Max(1, startingDurability) : 0;
    }

    public void ConsumeSuccessfulMatch()
    {
        if (!IsFrozen)
        {
            return;
        }

        durability = Mathf.Max(0, durability - 1);
        // Presentation feedback is handled by IceView3D / BoardVfx3D.
    }

    private void Awake()
    {
        block = GetComponent<Block>();
    }
}
