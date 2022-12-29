using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

public struct CameraBoundary
{
    public Rectangle Boundary;
    
    public CameraBoundary(Rectangle boundary)
    {
        Boundary = boundary;
    }
}