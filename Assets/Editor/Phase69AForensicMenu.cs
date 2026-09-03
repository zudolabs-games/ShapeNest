using UnityEditor;
using UnityEngine;

/// <summary>TEMP Phase 69A forensic controls. Diagnostic only.</summary>
public static class Phase69AForensicMenu
{
    [MenuItem("Shape Nest/Phase 69A Enable Nested Chain Forensic")]
    public static void Enable()
    {
        Phase69AForensic.EnableForSession();
        EnsureDriver();
    }

    [MenuItem("Shape Nest/Phase 69A Disable Nested Chain Forensic")]
    public static void Disable()
    {
        Phase69AForensic.Disable();
    }

    private static void EnsureDriver()
    {
        if (Object.FindFirstObjectByType<Phase68CForensicDriver>() != null)
        {
            return;
        }

        var go = new GameObject("Phase68CForensicDriver");
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<Phase68CForensicDriver>();
    }
}
