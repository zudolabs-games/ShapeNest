using UnityEditor;
using UnityEngine;

/// <summary>TEMP Phase 70A forensic controls. Diagnostic only.</summary>
public static class Phase70AWhiteTriangleForensicMenu
{
    [MenuItem("Shape Nest/Phase 70A Enable White Triangle Forensic")]
    public static void Enable()
    {
        Phase70AWhiteTriangleForensic.EnableForSession();
    }

    [MenuItem("Shape Nest/Phase 70A Disable White Triangle Forensic")]
    public static void Disable()
    {
        Phase70AWhiteTriangleForensic.Disable();
    }
}
