using Microsoft.Xna.Framework;

namespace MonoDreams;

public struct Size
{
    public Vector2 CurrentSize;
    public Vector2 NextSize;
    public Vector2 LastSize;
    
    public bool HasUpdates => CurrentSize != NextSize || LastSize != CurrentSize;
}