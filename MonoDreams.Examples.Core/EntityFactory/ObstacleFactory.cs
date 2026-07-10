using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.Draw;
using MonoDreams.EntityFactory;
using MonoDreams.Extension;
using MonoDreams.Examples.Runner;
using MonoDreams.Examples.Screens;
using MonoDreams.Message;

namespace MonoDreams.Examples.EntityFactory;

public class ObstacleFactory(DrawLayerMap layers) : IEntityFactory
{
    public Entity CreateEntity(World world, in EntitySpawnRequest request)
    {
        var entity = world.CreateEntity();
        entity.Set(new EntityInfoComponent("Obstacle"));

        var size = (int)RunnerConstants.ObstacleSize;
        entity.Set(new TransformComponent(request.Position));
        entity.Set(new VelocityComponent(new Vector2(-RunnerConstants.TreadmillScrollSpeed, 0)));

        // Colliders-as-entities: the collider is a child entity; the obstacle is the body (Velocity).
        // The former top-left footprint's centre (size/2) keeps the world rect aligned with the mesh.
        var obstacleCollider = world.CreateEntity();
        obstacleCollider.Set(new TransformComponent(new Vector2(size / 2f, size / 2f)));
        obstacleCollider.Set(new BoxColliderComponent(new Vector2(size, size), passive: true));
        obstacleCollider.SetParent(entity);

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
        entity.Set(new VisibleComponent());

        return entity;
    }
}
