using System.ComponentModel;
using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Component;

public struct LevelSelector : IComponent
{
    public int LevelIndex { get; set; }
    public string LevelName { get; set; }
    public bool IsSelected { get; set; }
    public Color DefaultColor { get; set; }
    public Color SelectedColor { get; set; }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public ISite? Site { get; set; }
    public event EventHandler? Disposed;
}
