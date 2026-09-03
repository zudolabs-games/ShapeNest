using UnityEditor;
using UnityEngine;

/// <summary>TEMP Phase 68C forensic controls.</summary>
public static class Phase68CForensicMenu
{
    [MenuItem("Shape Nest/Phase 68C Enable Forensic Logging")]
    public static void Enable()
    {
        Phase68CForensic.EnableForSession();
        EnsureDriver();
    }

    [MenuItem("Shape Nest/Phase 68C Disable Forensic Logging")]
    public static void Disable()
    {
        Phase68CForensic.Disable();
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
