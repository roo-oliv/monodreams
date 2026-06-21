using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Cursor;
using MonoDreams.Draw;

namespace MonoDreams.Cursor;

public static class Cursor
{
    /// <summary>
    /// Creates a mesh-rendered cursor (e.g. a generated arrow) instead of a textured one.
    /// The cursor's silhouette is the supplied <paramref name="cursorMesh"/>, authored in
    /// local space with its hot-spot at the origin. It needs no <see cref="CursorTexturesComponent"/>
    /// and no <c>CursorDrawPrepSystem</c>: <c>CursorInputSystem</c> → <c>CursorPositionSystem</c>
    /// place the transform, and the standard <c>MeshPrepSystem</c> (which requires
    /// <see cref="VisibleComponent"/>) writes its world matrix each frame.
    /// </summary>
    public static Entity CreateMesh(
        World world,
        MeshData cursorMesh,
        RenderTargetID renderTarget = RenderTargetID.HUD,
        CursorType initialType = CursorType.Default,
        Vector2 hotSpot = default)
    {
        var entity = world.CreateEntity();

        entity.Set(new CursorControllerComponent(initialType, isVisible: true, hotSpot));
        entity.Set(new CursorInputComponent());
        entity.Set(new TransformComponent(Vector2.Zero));
        var draw = new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = renderTarget,
            LayerDepth = 1.0f, // Highest layer for cursor
        };
        draw.SetMeshData(cursorMesh);
        entity.Set(draw);
        entity.Set<VisibleComponent>();

        return entity;
    }

    public static Entity Create(
        World world,
        Dictionary<CursorType, Texture2D> cursorTextures,
        RenderTargetID renderTarget,
        CursorType initialType = CursorType.Default)
    {
        var entity = world.CreateEntity();

        entity.Set(new CursorControllerComponent(initialType));
        entity.Set(new CursorInputComponent());
        entity.Set(new TransformComponent(Vector2.Zero));
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = renderTarget,
            Texture = cursorTextures[initialType],
            SourceRectangle = new Rectangle(0, 0, cursorTextures[initialType].Width, cursorTextures[initialType].Height),
            Color = Color.White,
            Position = Vector2.Zero,
            Size = new Vector2(32),
            LayerDepth = 1.0f, // Highest layer for cursor
        });
        entity.Set(new CursorTexturesComponent { Textures = cursorTextures });
        
        return entity;
    }
}