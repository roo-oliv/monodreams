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

public class ObstacleFactory(DrawLayerMap layers) : IEntityFactory
{
    public Entity CreateEntity(World world, in EntitySpawnRequest request)
    {
        var entity = world.CreateEntity();
        entity.Set(new EntityInfo("Obstacle"));

        var size = (int)RunnerConstants.ObstacleSize;
        entity.Set(new Transform(request.Position));
        entity.Set(new BoxCollider(
            new Rectangle(0, 0, size, size),
            passive: true));
        entity.Set(new Velocity(new Vector2(-RunnerConstants.TreadmillScrollSpeed, 0)));

        var mesh = new FilledRectangleMeshGenerator(
            new Rectangle(0, 0, size, size),
            RunnerConstants.ObstacleColor).Generate();
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            Vertices = mesh.Vertices,
            Indices = mesh.Indices,
            PrimitiveType = mesh.PrimitiveType,
            LayerDepth = layers.GetDepth(InfiniteRunnerScreen.RunnerDrawLayer.Obstacle)
        });
        entity.Set(new Visible());

        return entity;
    }
}
