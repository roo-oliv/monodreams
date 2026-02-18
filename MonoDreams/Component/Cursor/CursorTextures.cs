using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Component.Cursor;

public struct CursorTextures
{
    public Dictionary<CursorType, Texture2D> Textures { get; set; }
}
