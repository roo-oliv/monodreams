using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Serialization;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the PF-G prefab-UX resolver (<see cref="PrefabSpritePreview.TryResolve"/>): the Prefabs
/// shelf card thumbnail and the placement ghost resolve a prefab's dominant sprite by a ROOT-FIRST walk
/// of its entities (the user's <c>house</c> prefab keeps its sprite on the child <c>House2</c>, so the
/// walk must descend), and its placement fields are the sprite's WORLD transform within the prefab so
/// the ghost lands where the placed instance's sprite will. Names the premise "Prefab cards + placement
/// ghost show the prefab's dominant sprite" in MonoDreams/level-editor/docs/premises.md.
/// </summary>
public class PrefabSpritePreviewTests
{
    private static PrefabData Prefab(string id, string json) =>
        PrefabData.FromScene(id, CanonicalJson.Deserialize<SceneData>(json)!);

    // A house-like prefab: the ROOT carries only a collider (no sprite); the CHILD carries the sprite,
    // at a local offset and a sub-1 scale (exactly the shape the user reported).
    private const string HouseJson = """
    {
      "version": 1,
      "entities": [
        {
          "id": 0,
          "components": {
            "core.BoxCollider": { "bounds": [-15, 5, 27, 20], "activeLayers": [-1], "passive": true, "enabled": true },
            "core.EntityInfo": { "type": "house" },
            "core.Transform": { "position": [0, 0], "rotation": 0, "scale": [1, 1], "origin": [0, 0] }
          }
        },
        {
          "components": {
            "core.SpriteInfo": {
              "assetKey": "file:Island/House2.png", "source": [0, 0, 128, 192], "size": [128, 192],
              "color": "/////w==", "origin": [4, 6], "offset": [0, 0], "target": 0, "layerDepth": 0.1
            },
            "core.Transform": { "position": [-7, -40], "rotation": 0, "scale": [0.5, 0.5], "origin": [0, 0] }
          },
          "parent": 0
        }
      ]
    }
    """;

    [Fact]
    public void TryResolve_FindsChildSprite_RootFirstWalk_WithWorldPlacement()
    {
        Assert.True(PrefabSpritePreview.TryResolve(Prefab("house", HouseJson), out var preview));

        Assert.Equal("file:Island/House2.png", preview.AssetKey);
        Assert.Equal(new Rectangle(0, 0, 128, 192), preview.Source);
        Assert.Equal(new Vector2(4, 6), preview.SpriteOrigin);
        Assert.Equal(0.1f, preview.LayerDepth, 3);
        // The sprite's prefab-space WORLD transform (root normalized to origin): its local offset/scale.
        Assert.Equal(new Vector2(-7, -40), preview.Offset);
        Assert.Equal(new Vector2(0.5f, 0.5f), preview.Scale);
        Assert.Equal(0f, preview.Rotation, 3);
    }

    [Fact]
    public void TryResolve_PrefersRootSprite_WhenTheRootHasOne()
    {
        const string rootSprite = """
        {
          "version": 1,
          "entities": [
            {
              "id": 0,
              "components": {
                "core.SpriteInfo": { "assetKey": "file:root.png", "source": [0, 0, 16, 16], "target": 0, "layerDepth": 0.2 },
                "core.Transform": { "position": [0, 0], "rotation": 0, "scale": [1, 1], "origin": [0, 0] }
              }
            },
            {
              "components": {
                "core.SpriteInfo": { "assetKey": "file:child.png", "source": [0, 0, 8, 8], "target": 0, "layerDepth": 0.3 },
                "core.Transform": { "position": [50, 50], "rotation": 0, "scale": [1, 1], "origin": [0, 0] }
              },
              "parent": 0
            }
          ]
        }
        """;
        Assert.True(PrefabSpritePreview.TryResolve(Prefab("two", rootSprite), out var preview));
        Assert.Equal("file:root.png", preview.AssetKey);     // the root wins the root-first walk
        Assert.Equal(Vector2.Zero, preview.Offset);
    }

    [Fact]
    public void TryResolve_ReturnsFalse_ForSpritelessPrefab()
    {
        // A dialogue-zone-style prefab: a collider + a game component, no sprite anywhere.
        const string spriteless = """
        {
          "version": 1,
          "entities": [
            {
              "id": 0,
              "components": {
                "core.BoxCollider": { "bounds": [0, 0, 32, 32], "activeLayers": [-1], "passive": true, "enabled": true },
                "core.Transform": { "position": [0, 0], "rotation": 0, "scale": [1, 1], "origin": [0, 0] }
              }
            }
          ]
        }
        """;
        Assert.False(PrefabSpritePreview.TryResolve(Prefab("zone", spriteless), out _));
    }

    [Fact]
    public void TryResolve_ReturnsFalse_ForNullPrefab()
    {
        Assert.False(PrefabSpritePreview.TryResolve(null, out _));
    }
}
