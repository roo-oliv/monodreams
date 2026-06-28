using System.ComponentModel;
using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Component.UI;

/// <summary>
/// Component for level selection buttons that tracks level metadata and selection state.
/// </summary>
public struct LevelSelector : IComponent
{
    public int LevelIndex { get; set; }
    public string LevelName { get; set; }
    public string TargetScreen { get; set; }
    public bool IsClickable { get; set; }
    public bool IsHovered { get; set; }
    public Color DefaultColor { get; set; }
    public Color HoveredColor { get; set; }
    public Color DisabledColor { get; set; }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public ISite? Site { get; set; }
    public event EventHandler? Disposed;
}
