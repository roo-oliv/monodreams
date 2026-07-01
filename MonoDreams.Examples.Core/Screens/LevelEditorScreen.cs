using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.Examples.Screens;

/// <summary>
/// The registered editor screen (<c>ScreenName.LevelEditor</c>, the menu's per-level "Edit"
/// button): it IS <see cref="LoadLevelExampleGameScreen"/> with the editor overlay always on —
/// the editor is a mode of the game, not a separate composition. Since Wave 6 the whole pipeline
/// (game systems + editor systems, gate policies, weave order) lives in the base screen behind
/// its <c>editorEnabled</c> flag; this subclass only pins the flag, so the menu-entered editor
/// and a <c>--editor</c>-flagged <c>ScreenName.Game</c> are the same composition path.
///
/// <para>Entering Edit is a <see cref="GameState.RunMode"/> flip (F1), not a screen swap. From
/// this screen the mode starts as whatever the host booted (<c>Play</c> by default; <c>Edit</c>
/// under the <c>--editor</c> / <c>MONODREAMS_EDITOR=1</c> run flag).</para>
/// </summary>
public class LevelEditorScreen(
    Game game,
    GraphicsDevice graphicsDevice,
    ContentManager content,
    Camera camera,
    ViewportManager viewportManager,
    DefaultParallelRunner parallelRunner,
    SpriteBatch spriteBatch)
    : LoadLevelExampleGameScreen(game, graphicsDevice, content, camera, viewportManager, parallelRunner,
        spriteBatch, editorEnabled: true);
