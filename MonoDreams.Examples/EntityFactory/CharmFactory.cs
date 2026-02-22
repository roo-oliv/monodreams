using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.Draw;
using MonoDreams.EntityFactory;
using MonoDreams.Examples.Runner;
using MonoDreams.Examples.Screens;
using MonoDreams.Message;

namespace MonoDreams.Examples.EntityFactory;

public class CharmFactory(DrawLayerMap layers) : IEntityFactory
{
    public Entity CreateEntity(World world, in EntitySpawnRequest request)
    {
        var entity = world.CreateEntity();
        entity.Set(new EntityInfo("Collectible"));

        var size = (int)RunnerConstants.CharmSize;
        entity.Set(new Transform(request.Position, rotation: MathHelper.PiOver4));
        entity.Set(new BoxCollider(
            new Rectangle(-size / 2, -size / 2, size, size),
            passive: true));
        entity.Set(new Velocity(new Vector2(-RunnerConstants.TreadmillScrollSpeed, 0)));

        var mesh = new FilledRectangleMeshGenerator(
            new Rectangle(-size / 2, -size / 2, size, size),
            RunnerConstants.CharmColor).Generate();
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            Vertices = mesh.Vertices,
            Indices = mesh.Indices,
            PrimitiveType = mesh.PrimitiveType,
            LayerDepth = layers.GetDepth(InfiniteRunnerScreen.RunnerDrawLayer.Collectible)
        });
        entity.Set(new Visible());

        return entity;
    }
}
