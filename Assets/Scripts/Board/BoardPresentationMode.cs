/// <summary>
/// Selects board presentation layer.
/// After Phase 11, gameplay uses <see cref="World3D"/> only.
/// <see cref="UI"/> is retained for serialized compatibility and is ignored at runtime.
/// </summary>
public enum BoardPresentationMode
{
    /// <summary>Obsolete board Canvas presentation (ignored at runtime after Phase 11).</summary>
    UI = 0,

    /// <summary>World-space 3D board — the only active gameplay board presentation.</summary>
    World3D = 1
}
