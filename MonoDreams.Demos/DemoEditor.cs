using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Demos;

/// Draw layers for the demo screens' editor composition. The demos set literal LayerDepths and
/// run no Y-sort of their own, so this map exists for the editor seam only (SceneWriter layer
/// banding + the flag-on YSortSystem, which passes depths through since no layer is Y-sorted —
/// the documented graceful degradation from Wave 8a).
public enum DemoDrawLayer
{
    Front,
    Back,
}

/// One-call editor composition for the Demos host: under the editor run flag every demo screen
/// builds an <see cref="EditorOverlay"/> over its OWN world/camera/layers through this helper —
/// the editor is host-agnostic and screen-agnostic (docs/CORE_TENETS.md section 9; the recipe in
/// MonoDreams/level-editor/docs/overview.md "Adding the editor to a screen/host").
///
/// The Demos host has no keyboard-action mapping layer of its own, so the helper pairs the
/// overlay with the engine's <see cref="DefaultEditorKeys"/> (Delete / Z / Y / Home) — weave it
/// as the `editor.keys` registrar entry before the editor systems that read it. The chrome uses
/// the same PPMondwest font as the Examples shells, so the editor reads identically across hosts.
public sealed class DemoEditor
{
    private DemoEditor(DefaultEditorKeys keys, EditorOverlay overlay)
    {
        Keys = keys;
        Overlay = overlay;
    }

    /// The default editor keyboard surface (registrar entry `editor.keys`).
    public DefaultEditorKeys Keys { get; }

    /// The universal editor overlay, built over the calling screen's world/camera/layers.
    public EditorOverlay Overlay { get; }

    /// The minimal <see cref="DrawLayerMap"/> a demo screen hands the editor seam
    /// (see <see cref="DemoDrawLayer"/>).
    public static DrawLayerMap CreateLayers() => DrawLayerMap.FromEnum<DemoDrawLayer>();

    /// Builds the overlay + default keys for a demo screen, or returns null when the editor run
    /// flag is off (nothing editor-related is constructed — the flag-off screen stays
    /// byte-identical). <paramref name="game"/> resolves the host <see cref="Game"/> lazily
    /// (demo screens only receive their <c>ScreenController</c> in <c>Load</c>, after this runs):
    /// it wires the OS-cursor swap and the headless op channel's exit request.
    public static DemoEditor? TryCreate(
        bool editorEnabled,
        World world,
        MonoDreams.Component.Camera camera,
        DrawLayerMap layers,
        ContentManager content,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        ViewportManager viewportManager,
        Func<Game?> game)
    {
        if (!editorEnabled) return null;

        var debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        var chromeFont = content.Load<BitmapFont>("Fonts/PPMondwest-Regular-fnt");
        var keys = new DefaultEditorKeys();
        var overlay = new EditorOverlay(
            world, camera, layers, content, chromeFont, graphicsDevice, spriteBatch, viewportManager,
            keys.Bindings, debugDir,
            requestExit: () => game()?.Exit(),
            setOsCursorVisible: visible =>
            {
                var host = game();
                if (host != null) host.IsMouseVisible = visible;
            });
        // Modal capture (keyboard half): while a Save/Load dialog is open the editor keyboard stands
        // down so the dialog owns the keys (the mouse half is the dialog consuming the cursor edges).
        keys.ShouldSuppressInput = () => overlay.Dialog.IsOpen || overlay.Menu.IsOpen;
        return new DemoEditor(keys, overlay);
    }
}
