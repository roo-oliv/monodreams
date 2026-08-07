using DefaultEcs;
using LDtk;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Level;
using MonoDreams.Message;
using MonoDreams.System.Level;

namespace MonoDreams.Tests.LevelLdtk;

/// <summary>
/// Pure-logic cover for the issue #54 decoupling of <c>level-loading</c> from LDtk. Two things moved and
/// this test pins both:
/// <list type="bullet">
/// <item><description>the parser now subscribes to the LDtk-local <see cref="LDtkLevelDataComponent"/>
/// being added (not the plain-string <c>CurrentLevelComponent</c>), so <c>level-loading</c> never carries
/// an <c>LDtkLevel</c>;</description></item>
/// <item><description>layer-derived data rides in <c>EntitySpawnRequest.CustomFields</c> under the
/// <see cref="LDtkSpawnFields"/> <c>ldtk:</c> keys (typed exactly <see cref="float"/> /
/// <see cref="int"/>), replacing the deleted LDtk-typed <c>EntitySpawnRequest.Layer</c>
/// member.</description></item>
/// </list>
/// No <c>GraphicsDevice</c> and no <c>ContentManager</c>: the vendored LDtk JSON types are plain data
/// classes, so a minimal level is hand-built here.
/// </summary>
public class LDtkEntityParserSystemTests
{
    [Fact]
    public void EntityParser_OnLDtkLevelDataAdded_PublishesSpawnRequestsWithLdtkChannelFields()
    {
        using var world = new World();
        var published = new List<EntitySpawnRequest>();
        world.Subscribe((in EntitySpawnRequest r) => published.Add(r));

        using var parser = new LDtkEntityParserSystem(world);

        var level = MinimalLevel(layerOpacity: 0.5f, gridSize: 32,
            entityIdentifier: "Player", entityPixel: new Point(48, 96));

        // The component-driven trigger: setting the LDtk-local level data IS the parse signal.
        world.Set(new LDtkLevelDataComponent(level));

        var request = Assert.Single(published);
        Assert.Equal("Player", request.Identifier);
        Assert.Equal(new Vector2(48, 96), request.Position);

        // The ldtk: channel carries the layer-derived data the deleted Layer member used to carry —
        // and the boxed values must be exactly float / int so a factory's `is float` / `is int`
        // pattern match hits.
        Assert.True(request.CustomFields.TryGetValue(LDtkSpawnFields.LayerOpacity, out var opacity));
        Assert.IsType<float>(opacity);
        Assert.Equal(0.5f, (float)opacity);

        Assert.True(request.CustomFields.TryGetValue(LDtkSpawnFields.GridSize, out var gridSize));
        Assert.IsType<int>(gridSize);
        Assert.Equal(32, (int)gridSize);
    }

    /// <summary>A one-layer, one-entity <see cref="LDtkLevel"/> — the smallest shape the parser walks.</summary>
    private static LDtkLevel MinimalLevel(float layerOpacity, int gridSize,
        string entityIdentifier, Point entityPixel) =>
        new()
        {
            Identifier = "TestLevel",
            LayerInstances =
            [
                new LayerInstance
                {
                    _Identifier = "Entities",
                    _Type = LayerType.Entities,
                    _Opacity = layerOpacity,
                    _GridSize = gridSize,
                    Visible = true,
                    GridTiles = [],
                    AutoLayerTiles = [],
                    EntityInstances =
                    [
                        new EntityInstance
                        {
                            _Identifier = entityIdentifier,
                            Iid = Guid.NewGuid(),
                            Px = entityPixel,
                            Width = 16,
                            Height = 16,
                            _Pivot = Vector2.Zero,
                            _Tile = new TilesetRectangle { X = 0, Y = 0, W = 16, H = 16 },
                            FieldInstances = [],
                        },
                    ],
                },
            ],
        };
}
