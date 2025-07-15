using System.ComponentModel;
using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Component.Draw;

public class DrawComponent : IComponent
{
    // Consider using pooling if performance becomes an issue
    public List<DrawElement> Drawables { get; } = [];
    public void Dispose()
    {
        Drawables.Clear();
        GC.SuppressFinalize(this);
    }

    public ISite Site { get; set; }
    public event EventHandler Disposed;
}