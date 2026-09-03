using UnityEngine;

/// <summary>TEMP Phase 68C: drives per-frame forensic transform logs.</summary>
public sealed class Phase68CForensicDriver : MonoBehaviour
{
    private void LateUpdate()
    {
        Phase68CForensic.LateUpdateTick();
    }
}
