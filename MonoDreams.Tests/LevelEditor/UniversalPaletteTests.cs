using System.Linq;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Composition;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// PF-F universal palette: the overlay builds a default band set from a screen's own
/// <see cref="DrawLayerMap"/> (the generalization of a screen hand-listing its bands), so a screen that
/// supplies none — a menu, a demo — still gets a usable palette. Pure — the band build has no
/// GraphicsDevice (the catalog scan is covered by AssetCatalogTests).
/// </summary>
public class UniversalPaletteTests
{
    private enum TestLayer { Front, Middle, Back }

    [Fact]
    public void DefaultBandsFromLayers_OneBandPerLayer_CarriesNameDepthAndYSort()
    {
        var layers = DrawLayerMap.FromEnum<TestLayer>().WithYSort(TestLayer.Middle);

        var bands = EditorOverlay.DefaultBandsFromLayers(layers);

        Assert.Equal(3, bands.Count);
        // The Y-sorted layer is marked; the others are not.
        var middle = bands.Single(b => b.Name == nameof(TestLayer.Middle));
        Assert.True(middle.YSorted);
        Assert.False(bands.Single(b => b.Name == nameof(TestLayer.Front)).YSorted);
        Assert.False(bands.Single(b => b.Name == nameof(TestLayer.Back)).YSorted);
        // Depths mirror the layer map's source depths (front = 1.0 … back = 0.0 for 3 layers).
        Assert.Equal(layers.GetDepth(TestLayer.Front), bands.Single(b => b.Name == "Front").LayerDepth);
        Assert.Equal(layers.GetDepth(TestLayer.Middle), middle.LayerDepth);
    }

    [Fact]
    public void DefaultBandsFromLayers_NullMap_IsEmpty()
    {
        Assert.Empty(EditorOverlay.DefaultBandsFromLayers(null!));
    }
}
