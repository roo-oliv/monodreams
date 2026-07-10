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
        entity.Set(new EntityInfoComponent("Collectible"));

        var size = (int)RunnerConstants.CharmSize;
        entity.Set(new TransformComponent(request.Position, rotation: MathHelper.PiOver4));
        // Colliders-as-entities: the charm IS its own collider (standalone; Velocity makes it its own
        // body). The former centered bounds become a centered Size — byte-identical world rect.
        entity.Set(new BoxColliderComponent(new Vector2(size, size), passive: true));
        entity.Set(new VelocityComponent(new Vector2(-RunnerConstants.TreadmillScrollSpeed, 0)));

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
        entity.Set(new VisibleComponent());

        return entity;
    }
}
