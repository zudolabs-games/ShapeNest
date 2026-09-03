using UnityEditor;
using UnityEngine;

/// <summary>Phase 71A forensic menu — diagnostic only.</summary>
public static class Phase71AForensicMenu
{
    [MenuItem("Shape Nest/Phase 71A Enable Forensic")]
    public static void Enable()
    {
        Phase71AForensic.EnableForSession();
    }

    [MenuItem("Shape Nest/Phase 71A Disable Forensic")]
    public static void Disable()
    {
        Phase71AForensic.Disable();
    }

    [MenuItem("Shape Nest/Phase 71A Dump Path Proof (Edit Mode)")]
    public static void DumpPath()
    {
        Debug.Log(Phase71AForensic.ProveDragPathFromSource());
        Phase71AForensic.Flush();
    }
}
