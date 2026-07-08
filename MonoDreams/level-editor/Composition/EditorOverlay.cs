#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Channel;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System.Cursor;
using MonoDreams.System.Draw;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The reusable in-game editor overlay: everything a screen needs to become editor-capable,
/// built once over the screen's <b>own</b> world / camera / layers (the editor is part of the
/// game — no second world, renderer, or data model). It encapsulates the shared editor
/// infrastructure (<see cref="Registry"/> + <see cref="Serializer"/> + bounded
/// <see cref="History"/> + the single gizmo-state entity), constructs every editor system, builds
/// the native-resolution editor chrome (panels + toolbar on <c>RenderTargetID.Editor</c>), wires
/// the toolbar dispatch to those same shared instances, and loads the headless editor-op channel
/// when a plan is present.
///
/// <para><b>The screen weaves; the overlay supplies.</b> Editor systems interleave with the game
/// pipeline at specific points (the ordering invariants of Waves 4–5), so the overlay exposes
/// individual hooks rather than one opaque block. Update-side weave order:</para>
/// <list type="number">
///   <item><see cref="SceneReader"/> — with/after the level-load group (message-driven).</item>
///   <item><see cref="EditorCommands"/> then <see cref="Gizmo"/> then <see cref="ProxySync"/> —
///   after the frozen logic block, <b>before</b> <c>HierarchySystem</c> (the edit must propagate
///   to world space this frame; the proxies re-derive from the same frame's write-back).</item>
///   <item><see cref="ToolbarMeshPrep"/> then <see cref="ToolbarClicks"/> (the
///   <c>editor.toolbar</c> group) — after camera-follow (button mesh prep, then click dispatch).</item>
///   <item><see cref="CameraNav"/> — <b>before</b> <c>CursorPositionSystem</c> (the camera
///   mutation this frame is what the cursor's world position derives from).</item>
///   <item><see cref="Shell"/> — after <c>CursorDrawPrepSystem</c> (keeps the viewport inset +
///   chrome layout applied and the OS pointer as the one visible cursor — the shell is constant
///   while the editor is composed; it never collapses when the transport is Playing).</item>
///   <item><see cref="EditorOpDriver"/> (when present) — <b>last</b>, after the cursor late
///   update, so its injected cursor is the final word the gizmo/toolbar read.</item>
/// </list>
/// <para>Draw-side: <see cref="Selection"/> goes after the prep/YSort group and before the render
/// passes (it must read the FINAL post-YSort <c>DrawComponent.LayerDepth</c> of this frame);
/// <see cref="OverlayPrep"/> goes right after it (the overlay visuals bake from the frame's final
/// camera + selection, in screen pixels on the Editor target — see
/// <c>EditorOverlayPrepSystem</c>); <see cref="ChromeRender"/> goes after the game render passes
/// and before final draw, and <see cref="ChromeLayer"/> is appended after the game layers in the
/// final-draw list.</para>
///
/// <para>The woven pipeline owns system disposal (gates forward <c>Dispose</c>); the overlay
/// itself holds no disposable state beyond world entities, which die with the world.</para>
/// </summary>
public sealed class EditorOverlay
{
    /// <summary>The scene id the editor holds when neither an explicit id nor the manifest's
    /// <see cref="GameProject.StartScene"/> supplies one — the fallback name a brand-new project's
    /// first Save writes (<c>untitled.mdscene</c>). See <see cref="ResolveSceneId"/>.</summary>
    public const string DefaultSceneId = "untitled";

    private readonly World _world;
    private readonly Camera _camera;
    private readonly DrawLayerMap _layers;
    // Mutable: the Save dialog's confirm renames the scene the editor holds (see DispatchToolbarAction).
    private string _sceneId;
    private readonly EditorProjectContext? _projectContext;
    private readonly Entity _gizmoState;
    private readonly EditorShellStateComponent _shellState = new();

    /// <summary>
    /// Builds the overlay over the screen's own world/camera/layers. <paramref name="toolbarFont"/>
    /// labels the toolbar buttons; <paramref name="graphicsDevice"/>/<paramref name="spriteBatch"/>
    /// back the native-resolution chrome render pass; <paramref name="input"/> supplies the game's
    /// editor key predicates; <paramref name="debugDirectory"/> is probed for a headless
    /// <c>editor_op_plan.json</c>; <paramref name="requestExit"/> lets the headless driver end the
    /// session (wire the host's <c>Game.Exit</c>); <paramref name="setOsCursorVisible"/> lets the
    /// shell show the OS pointer while editing (wire <c>v =&gt; game.IsMouseVisible = v</c>);
    /// <paramref name="provideCursorPipeline"/> makes the overlay <b>self-sufficient on a screen
    /// with no cursor pipeline</b> (e.g. the infinite runner): it constructs its own
    /// <see cref="CursorInput"/> / <see cref="CursorPosition"/> systems and a minimal invisible
    /// cursor entity (no textures — the OS pointer is the visible pointer while the editor is
    /// composed), which every editor system reads. Screens that already run the cursor pipeline
    /// leave it false — the overlay must never double the cursor.
    /// <paramref name="assetCatalog"/> + <paramref name="paletteBands"/> (both screen-supplied —
    /// the module stays game-agnostic) switch on the asset <see cref="Palette"/> in the shell's
    /// bottom strip: the catalog lists the drop-folder art, the bands map the palette's layer
    /// selector onto the SCREEN's <c>DrawLayerMap</c> depths. Omit both on screens without
    /// authoring (menus, demos) — the strip just stays empty.
    /// <paramref name="projectContext"/> (host-resolved, desktop-only — see
    /// <see cref="EditorProjectContext"/>) anchors the versioned project: Save (and the in-editor
    /// Load) write / read <c>&lt;ProjectContext.LevelsPath&gt;/&lt;sceneId&gt;.mdscene</c> in the source
    /// tree (PS3), and Save is gated on the project being resolved (the "no project root" cause —
    /// <see cref="SaveBlock"/>); null (a shipped build, or a host with no project such as the demos)
    /// leaves Save disabled with that cause. The head supplies it so the module stays game-agnostic.
    /// <paramref name="sceneId"/> names the scene the editor holds (Save writes
    /// <c>&lt;sceneId&gt;.mdscene</c>); null (the default) derives it from the manifest's
    /// <see cref="GameProject.StartScene"/>, or <see cref="DefaultSceneId"/> when there is none (see
    /// <see cref="ResolveSceneId"/>). A rename / new-scene UI is deferred — PS3 ships the default only.
    /// </summary>
    public EditorOverlay(
        World world,
        Camera camera,
        DrawLayerMap layers,
        ContentManager content,
        BitmapFont toolbarFont,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        ViewportManager viewportManager,
        EditorInputBindings input,
        string debugDirectory,
        Action? requestExit = null,
        Action<bool>? setOsCursorVisible = null,
        bool provideCursorPipeline = false,
        string? sceneId = null,
        AssetCatalog? assetCatalog = null,
        IReadOnlyList<PaletteBand>? paletteBands = null,
        IReadOnlyList<TriggerType>? triggerTypes = null,
        EditorProjectContext? projectContext = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
        if (input == null) throw new ArgumentNullException(nameof(input));
        _projectContext = projectContext;
        _sceneId = ResolveSceneId(sceneId, projectContext);

        // The shared editor infrastructure. The registry ships the engine serializers; a game
        // registers its own game-component serializers via the exposed Registry.
        Registry = new ComponentSerializerRegistry();
        Registry.RegisterEngineComponents();
        Serializer = new SceneSerializer(Registry);
        History = new EditorHistory(world);

        // The single gizmo-state entity: the toolbar's tool-select / snap-toggle mutate it,
        // GizmoSystem reads it.
        _gizmoState = world.CreateEntity();
        _gizmoState.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        _gizmoState.Set(GizmoStateComponent.Default);

        // The single shell-state entity (UX-B): the ONE source of the resizable region sizes, the
        // active tab per region, and the drag ownership shared by the shell / panel / palette. On an
        // editor-infra entity so it is discoverable + survives a transport Restart.
        var shellStateEntity = world.CreateEntity();
        shellStateEntity.Set(new EditorInfrastructureComponent());
        shellStateEntity.Set(_shellState);

        Transport = new EditorTransport(world, History);

        // The file-asset texture loader is always composed (a loaded scene can carry file: keys
        // whether or not this screen shows a palette); textures load lazily, and a missing file
        // shows the magenta placeholder instead of an invisible sprite.
        AssetTextures = new FileAssetTextureLoader(graphicsDevice, content?.RootDirectory ?? "Content");
        SceneReader = new SceneReaderSystem(world, Serializer, content,
            fileTextureLoader: AssetTextures.Load, camera: camera);
        _editorCommands = new EditorCommandSystem(
            world, History, Serializer,
            input.DeleteRequested, input.UndoRequested, input.RedoRequested,
            layers, input.OrderForwardRequested, input.OrderBackRequested);
        var gizmo = new GizmoSystem(world, camera, History, viewportManager);
        var proxySync = new ProxySyncSystem(world, camera, viewportManager);
        Gizmo = gizmo;
        ProxySync = proxySync;

        // The freeform boundary tool + its message-driven bake (island-authoring Slice 3). The bake
        // runs in BOTH run modes (a shipped game loading a native scene with a boundary must bake it
        // too — §S2); the tool is Edit-guarded. The trigger overlay draws Edit-only tinted outlines
        // for placed trigger zones + the palette's placement ghost.
        var boundaryTool = new BoundaryToolSystem(
            world, camera, History, Serializer, viewportManager,
            commitRequested: input.CommitRequested, cancelRequested: input.CancelRequested);
        _boundaryTool = boundaryTool;
        BoundaryTool = boundaryTool;
        BoundaryBake = new BoundaryBakeSystem(world);
        // The armed-trigger provider reads the palette lazily (it is constructed below).
        var triggerOverlay = new TriggerOverlaySystem(world, camera, viewportManager,
            () => Palette?.ArmedTrigger);
        TriggerOverlay = triggerOverlay;

        OverlayPrep = new EditorOverlayPrepSystem(gizmo, proxySync, boundaryTool, triggerOverlay);
        CameraNav = new CameraNavSystem(world, camera, input.FrameRequested);
        Selection = new SelectionSystem(world, camera);

        // Self-sufficient cursor (Wave 8a): a screen with no cursor pipeline (InfiniteRunner) asks
        // the overlay to bring its own — the input/position systems plus a minimal invisible
        // cursor entity (the OS pointer is the visible pointer under the editor; the entity has
        // no texture, so it never renders a sprite). CursorPositionSystem's query needs all four
        // components, hence the empty sprite DrawComponent (null texture = skipped by the renderer).
        if (provideCursorPipeline)
        {
            CursorInput = new CursorInputSystem(world, viewportManager);
            CursorPosition = new CursorPositionSystem(world, camera, viewportManager);
            var cursor = world.CreateEntity();
            cursor.Set(new EditorInfrastructureComponent()); // survives a transport Restart
            cursor.Set(new CursorControllerComponent(CursorType.Default));
            cursor.Set(new CursorInputComponent());
            cursor.Set(new TransformComponent(Vector2.Zero));
            cursor.Set(new DrawComponent
            {
                Type = DrawElementType.Sprite,
                Target = RenderTargetID.HUD,
                LayerDepth = 1.0f,
            });
        }

        // The Blender-style shell (Wave 7): native-resolution chrome (panel backgrounds + the
        // toolbar, on RenderTargetID.Editor, laid out in physical pixels), the shell system that
        // syncs the viewport inset + cursor swap with the run mode, and the chrome render pass
        // whose target the screen composites 1:1 above the game layers via ChromeLayer.
        Chrome = new EditorChromeBuilder(world, toolbarFont);
        Chrome.Build(viewportManager.ScreenWidth, viewportManager.ScreenHeight);
        // Exposed as two hooks (not one opaque composite) so the screen registers them as the
        // named children of an `editor.toolbar` registrar group — every system stays visible and
        // individually toggleable in the systems panel.
        ToolbarMeshPrep = new ButtonMeshPrepSystem(world);
        // The Save button additionally dims (and its click is suppressed) while the project is
        // unresolved, even when Paused — the "no project root" save-guard cause. The "Playing"
        // cause is already covered by the toolbar's transport rule.
        ToolbarClicks = new ToolbarSystem(world, DispatchToolbarAction,
            (action, state) => action == EditorToolbarAction.Save
                               && SaveBlock(state, _projectContext) == SaveBlockReason.NoProjectRoot,
            // A shell splitter/scrollbar drag that happens to release over the toolbar must not also
            // fire the button (the drag holds the shared token through its release edge).
            isInputSuppressed: () => _shellState.IsDragging);
        // The right-strip editor panel (Scene / Systems / Project tabs). The Systems tab binds lazily
        // to the pipelines the screen hands over via BindPipelines — they don't exist yet while the
        // overlay itself is being constructed; the Scene + Project tabs read live state directly.
        _editorPanel = new EditorPanelSystem(world, viewportManager, toolbarFont,
            () => (UpdatePipeline, DrawPipeline), _shellState,
            () => new EditorProjectInfo(_projectContext?.ProjectRoot, _projectContext?.LevelsPath, _sceneId));
        SystemsPanel = _editorPanel;
        Shell = new EditorShellSystem(world, viewportManager, Chrome, setOsCursorVisible, _shellState);
        ChromeRender = new EditorChromeRenderSystem(spriteBatch, graphicsDevice, world, viewportManager);
        ChromeLayer = RenderLayer.Native(() => ChromeRender.CurrentTarget!);

        // The modal Save / Load file-system navigator (native-resolution chrome on the Editor target).
        // Confirm / pick route back into the SAME guarded Save / Load paths the toolbar used (no second
        // writer/reader), carrying the resolved ABSOLUTE path from the browser; the browsable tree comes
        // from BrowserRoots (the project-root boundary + the LevelsPath open dir) + ListDirectory
        // (desktop file IO — see there). The toolbar's Save/Load buttons OPEN these dialogs (see
        // DispatchToolbarAction), and the screen wires the host keyboard system's ShouldSuppressInput to
        // Dialog.IsOpen so editor/game keys (including Escape-to-exit) stand down while it owns input.
        Dialog = new EditorDialogSystem(
            world, viewportManager, toolbarFont,
            onSaveConfirmed: (path, s) => { _sceneId = Path.GetFileNameWithoutExtension(path); SaveCurrentSceneTo(path, s); },
            onLoadSelected: (path, s) => { _sceneId = Path.GetFileNameWithoutExtension(path); LoadScene(path); },
            getRoots: BrowserRoots,
            listDirectory: ListDirectory);

        // The asset palette + placement (island-authoring Slice 1): only when the screen supplies
        // both the catalog (the drop-folder scan) and its layer-band map — the module never
        // guesses a game's layers. Lives in the shell's bottom strip.
        if (assetCatalog != null && paletteBands is { Count: > 0 })
        {
            // Per-asset band marks (FW3): loaded from asset-bands.json alongside the assets (the
            // catalog's scan root), so a mark survives an editor restart. Null root (no drop folder)
            // → an in-memory config (marks work for the session, don't persist).
            var bandConfig = AssetBandConfig.Load(assetCatalog.RootAbsolutePath);
            Palette = new PalettePlacementSystem(
                world, assetCatalog, paletteBands, AssetTextures, Serializer, History,
                viewportManager, toolbarFont, input.CancelRequested, triggerTypes,
                input.RotateCwRequested, input.RotateCcwRequested, bandConfig, _shellState);
        }

        // The headless editor-op channel (Wave 5): present only when a plan file exists — zero
        // cost in a normal run. The driver holds the session open (requests exit only after its
        // ops drain), and SuppressReplayAutoExit lets a coexisting keyboard replay defer to it.
        var editorOpPlan = EditorOpPlan.TryLoad(debugDirectory);
        if (editorOpPlan != null)
        {
            var driver = new EditorOpReplaySystem(world, editorOpPlan, DispatchToolbarAction, requestExit,
                Transport, dispatchNamed: DispatchNamedAction);
            EditorOpDriver = driver;
            SuppressReplayAutoExit = () => !driver.IsComplete;
        }
    }

    /// <summary>The component-serializer registry (engine components pre-registered); the game
    /// registers its own serializers here before saving game components.</summary>
    public ComponentSerializerRegistry Registry { get; }

    /// <summary>The scene serializer every save/load/undo-snapshot path shares.</summary>
    public SceneSerializer Serializer { get; }

    /// <summary>The single bounded undo/redo history (never construct a second one).</summary>
    public EditorHistory History { get; }

    /// <summary>The shared gizmo-state entity (tool / snap config).</summary>
    public Entity GizmoState => _gizmoState;

    /// <summary>The resolved project context (desktop-only, host-supplied), or null when none was
    /// supplied. Gates Save (the "no project root" cause) and, when resolved, its
    /// <see cref="EditorProjectContext.LevelsPath"/> is the directory Save/Load target.</summary>
    public EditorProjectContext? ProjectContext => _projectContext;

    /// <summary>The scene id the editor holds — Save writes <c>&lt;SceneId&gt;.mdscene</c> under the
    /// project's levels directory. Defaults from the manifest's <see cref="GameProject.StartScene"/>,
    /// or <see cref="DefaultSceneId"/>. (A rename / new-scene UI is deferred.)</summary>
    public string SceneId => _sceneId;

    /// <summary>The editor transport — the one owner of <see cref="GameState.RunMode"/> under the
    /// editor run configuration (Paused = Edit, Playing = Play, Restart = rebuild from the original
    /// load). The toolbar's Play/Pause + Restart buttons and the headless transport ops drive it;
    /// the SCREEN registers its restart callback (<see cref="EditorTransport.Reload"/>) — and any
    /// screen-infrastructure exclusions (<see cref="EditorTransport.KeepAlive"/>) — in
    /// <c>Load</c>, once it knows what it loaded.</summary>
    public EditorTransport Transport { get; }

    /// <summary>Native-scene loading (<c>LoadSceneRequest</c>). Weave with the level-load group.</summary>
    public ISystem<GameState> SceneReader { get; }

    // The concrete command system: the toolbar dispatch calls its selection-edit actions
    // (ordering / collider add-remove / add vertex) directly.
    private readonly EditorCommandSystem _editorCommands;

    /// <summary>Delete / undo / redo keys plus the toolbar's selection-edit actions
    /// (Edit-guarded). Weave after logic, before <see cref="Gizmo"/>.</summary>
    public ISystem<GameState> EditorCommands => _editorCommands;

    /// <summary>The transform gizmo (Edit-guarded). Weave before <c>HierarchySystem</c>.</summary>
    public ISystem<GameState> Gizmo { get; }

    /// <summary>The collider gizmo-proxy sync (Edit-guarded): spawns/places/despawns the
    /// standalone proxy entities over the selected entity's collider shapes. Weave right after
    /// <see cref="Gizmo"/> (so the same frame's collider write-back is what the proxies re-derive
    /// from), before <c>HierarchySystem</c>.</summary>
    public ISystem<GameState> ProxySync { get; }

    // The concrete boundary tool: the toolbar dispatch (ToolBoundary) and the named boundary ops
    // call its Begin/Commit/Cancel directly.
    private readonly BoundaryToolSystem _boundaryTool;

    /// <summary>The freeform boundary tool (Edit-guarded; island-authoring §5.2): lays a polyline,
    /// Enter/double-click commits, Escape/right-click cancels. Weave into the UPDATE pipeline after
    /// <c>CursorPositionSystem</c> (entry <c>editor.boundary</c>) so a lay click reads this frame's
    /// cursor world position; its overlay VISUALS are emitted by <see cref="OverlayPrep"/>.</summary>
    public ISystem<GameState> BoundaryTool { get; }

    /// <summary>The message-driven boundary bake (island-authoring §5.2): reacts to a
    /// <c>BoundaryComponent</c> being added/changed and generates one thin convex quad collider per
    /// polyline edge as <c>ChildOf</c> bake products (never serialized). Weave with the level-load
    /// group (entry <c>editor.boundaryBake</c>), <c>RunNormally</c> — it bakes in BOTH run modes
    /// (a scene-loading participant, not Edit-only tooling).</summary>
    public ISystem<GameState> BoundaryBake { get; }

    /// <summary>The trigger-zone overlay (island-authoring §5.3): draws Edit-only tinted outlines
    /// for placed trigger zones + the palette's placement ghost. Its VISUALS are emitted by
    /// <see cref="OverlayPrep"/>; no separate weave needed (its own <c>Update</c> is a no-op).</summary>
    public ISystem<GameState> TriggerOverlay { get; }

    /// <summary>The editor overlays' draw-phase emission pass: bakes the gizmo + proxy VISUALS
    /// (selection outline, tool handle, collider outlines) in screen pixels on the
    /// native-resolution Editor target, from the frame's FINAL camera and selection. Weave into
    /// the DRAW pipeline right after <see cref="Selection"/> (entry <c>editor.overlayPrep</c>),
    /// before the render passes.</summary>
    public ISystem<GameState> OverlayPrep { get; }

    /// <summary>The chrome button mesh prep. Weave with <see cref="ToolbarClicks"/> as the
    /// children of an <c>editor.toolbar</c> registrar group (mesh prep first), after
    /// camera-follow.</summary>
    public ISystem<GameState> ToolbarMeshPrep { get; }

    /// <summary>The toolbar click dispatch (native-pixel hit-test). Weave right after
    /// <see cref="ToolbarMeshPrep"/> (see there).</summary>
    public ISystem<GameState> ToolbarClicks { get; }

    // The concrete right-strip panel: the headless panel:* ops drive its section/group/tree/inspector
    // toggles directly (like _editorCommands for the selection-edit ops).
    private readonly EditorPanelSystem _editorPanel;

    /// <summary>The right-strip editor panel in the shell's right strip: three collapsible sections —
    /// <b>Systems</b> (every bound registrar entry of both pipelines, groups indented + collapsible,
    /// tri-state group checkboxes, live enabled toggle), <b>Scene</b> (the world's entities as a
    /// selectable parent/child tree), and <b>Inspector</b> (the selected entity's components +
    /// on-demand member values). Weave after the <c>editor.toolbar</c> group (whose mesh prep bakes
    /// its checkbox meshes). The Systems section requires <see cref="BindPipelines"/>; the Scene +
    /// Inspector sections work immediately. Kept the <c>editor.systemsPanel</c> entry name so every
    /// screen weaves it unchanged.</summary>
    public ISystem<GameState> SystemsPanel { get; }

    /// <summary>The modal Save / Load dialogs (native-resolution chrome). Weave EARLY in the update
    /// pipeline — after the cursor input read, before the editing tools + toolbar (registrar entry
    /// <c>editor.dialog</c>, <c>RunNormally</c>) — so that while a dialog is open it consumes the
    /// cursor's pointer edges before any mouse-driven editor system reads them (the mouse half of the
    /// modal capture). The keyboard half is the screen wiring the host keyboard system's
    /// <c>ShouldSuppressInput</c> to <see cref="EditorDialogSystem.IsOpen"/>. Exposed concrete so the
    /// screen reads <c>Dialog.IsOpen</c> for that wiring and the headless <c>dialog:*</c> ops drive
    /// its public methods.</summary>
    public EditorDialogSystem Dialog { get; }

    /// <summary>Editor pan/zoom/frame (Edit-guarded). Weave before <c>CursorPositionSystem</c>.</summary>
    public ISystem<GameState> CameraNav { get; }

    /// <summary>The file-asset texture loader behind <c>file:</c> AssetKeys (lazy, memoized,
    /// magenta placeholder for a missing file). Always composed — the scene reader rehydrates
    /// through it whether or not the screen shows a palette.</summary>
    public FileAssetTextureLoader AssetTextures { get; }

    /// <summary>The asset palette + placement system (bottom strip; ghost + click-place through
    /// the snapshotting create command); null unless the screen supplied an
    /// <see cref="AssetCatalog"/> + <see cref="PaletteBand"/> map. Weave as
    /// <c>editor.palette</c> AFTER <c>CursorPositionSystem</c> (the ghost follows this frame's
    /// cursor world position).</summary>
    public PalettePlacementSystem? Palette { get; }

    /// <summary>The overlay-owned <c>CursorInputSystem</c> when the screen asked for a
    /// self-sufficient cursor pipeline (<c>provideCursorPipeline: true</c>), else null. Weave with
    /// the screen's input group; set <c>SkipHardwareRead</c> when a headless editor-op plan is
    /// active (see <see cref="HasEditorOpPlan"/>).</summary>
    public CursorInputSystem? CursorInput { get; }

    /// <summary>The overlay-owned <c>CursorPositionSystem</c> (see <see cref="CursorInput"/>), else
    /// null. Weave after <see cref="CameraNav"/> — the camera mutation this frame is what the
    /// cursor's world position derives from.</summary>
    public ISystem<GameState>? CursorPosition { get; }

    /// <summary>The native-resolution chrome entities (panels + toolbar) and their layout;
    /// relayouted by <see cref="Shell"/> on window resize.</summary>
    public EditorChromeBuilder Chrome { get; }

    /// <summary>The shell system: keeps the viewport inset + chrome layout applied and the OS
    /// pointer active while the editor is composed (the shell is constant across transport
    /// states). Weave after <c>CursorDrawPrepSystem</c> (it hides the game cursor sprite the same
    /// frame).</summary>
    public ISystem<GameState> Shell { get; }

    /// <summary>The native-resolution chrome render pass (screen-space, always on while the
    /// editor is composed; owns the resize-tracked Editor target). Weave into the DRAW pipeline
    /// after the game render passes, before final draw.</summary>
    public EditorChromeRenderSystem ChromeRender { get; }

    /// <summary>The final-draw layer compositing <see cref="ChromeRender"/>'s target 1:1 over the
    /// whole window. Append it AFTER the game layers (topmost); it self-skips only when the chrome
    /// pass is disabled.</summary>
    public RenderLayer ChromeLayer { get; }

    /// <summary>The headless editor-op driver; null when no <c>editor_op_plan.json</c> is present.
    /// Weave LAST (after the cursor late update) so its injected cursor is the final word.</summary>
    public ISystem<GameState>? EditorOpDriver { get; }

    /// <summary>Click-to-select (Edit-guarded). Weave into the DRAW pipeline after the prep/YSort
    /// group and before the render passes (reads this frame's final post-YSort depth).</summary>
    public ISystem<GameState> Selection { get; }

    /// <summary>Whether a headless editor-op plan was found (the screen must then set
    /// <c>CursorInputSystem.SkipHardwareRead</c> so the injected cursor state survives).</summary>
    public bool HasEditorOpPlan => EditorOpDriver != null;

    /// <summary>When the op channel is active: assign to <c>InputReplaySystem.SuppressAutoExit</c>
    /// so a coexisting keyboard replay's auto-exit-on-drain defers to the editor-op driver.
    /// Null when no plan is present.</summary>
    public Func<bool>? SuppressReplayAutoExit { get; }

    /// <summary>The screen's update-pipeline registry, bound via <see cref="BindPipelines"/> —
    /// the seam the editor's systems panel enumerates/toggles. Null until bound.</summary>
    public EditorPipelineRegistrar? UpdatePipeline { get; private set; }

    /// <summary>The screen's draw-pipeline registry (see <see cref="UpdatePipeline"/>).</summary>
    public EditorPipelineRegistrar? DrawPipeline { get; private set; }

    /// <summary>
    /// Binds the screen's retained pipeline registrars so editor tooling (the upcoming systems
    /// panel) can enumerate and toggle the live pipeline. Call after both pipelines are built.
    /// </summary>
    public void BindPipelines(EditorPipelineRegistrar updatePipeline, EditorPipelineRegistrar drawPipeline)
    {
        UpdatePipeline = updatePipeline ?? throw new ArgumentNullException(nameof(updatePipeline));
        DrawPipeline = drawPipeline ?? throw new ArgumentNullException(nameof(drawPipeline));
    }

    /// <summary>
    /// Logs the composed editor pipeline (entry names, in order) — the observable contract the
    /// universal-overlay integration tests assert per screen, across hosts. Call it right after
    /// <see cref="BindPipelines"/> from the composing screen.
    /// </summary>
    public static void LogComposition(string screenName,
        EditorPipelineRegistrar updatePipeline, EditorPipelineRegistrar drawPipeline)
    {
        Logger.Info(
            $"[level-editor] Editor overlay composed on {screenName}: " +
            $"update=[{string.Join(", ", updatePipeline.Entries.Select(e => e.Name))}] " +
            $"draw=[{string.Join(", ", drawPipeline.Entries.Select(e => e.Name))}]");
    }

    /// <summary>
    /// Wires a toolbar <see cref="EditorToolbarAction"/> to concrete behaviour using the SAME
    /// shared instances the rest of the editor uses (no second history / serializer / transport).
    /// Play/Pause and Restart drive the <see cref="Transport"/> (they need the frame's
    /// <see cref="GameState"/> — the RunMode axis lives there); tool-select and snap-toggle mutate
    /// the single <see cref="GizmoState"/> entity (a tool-select also disarms the palette — the
    /// tool buttons are a radio over <see cref="EditorToolMode"/>); Save writes through
    /// <see cref="SceneWriter"/> (live camera + layers) <b>into the versioned project SOURCE tree</b>
    /// at <c>ProjectContext.LevelsPath/&lt;sceneId&gt;.mdscene</c> (<see cref="SceneFilePath"/>) via
    /// <c>IPlatformServices.WriteAllText</c> — git sees it immediately; it is <b>blocked, loudly,
    /// while the transport is Playing OR when no project root is resolved</b> (<see cref="SaveBlock"/>:
    /// saving mid-simulation would bake transient run state, e.g. a mid-air player, into the scene, and
    /// with no project root there is nowhere versioned to write; the toolbar renders the button dimmed
    /// for either cause and this guard closes the headless/dispatch path too); Load reads that SAME
    /// source path (a <see cref="LoadSceneRequest"/> with <c>fromContent: false</c> handled by the woven
    /// <see cref="SceneReader"/> — instant reload of what was just written, no build round-trip), and is
    /// a loud no-op when the project is unresolved; Undo/Redo drive <see cref="History"/>. Public so the
    /// headless channel and tests dispatch the same way.
    /// </summary>
    public void DispatchToolbarAction(EditorToolbarAction action, GameState state)
    {
        switch (action)
        {
            case EditorToolbarAction.PlayPause: Transport.TogglePlayPause(state); break;
            case EditorToolbarAction.Restart: Transport.Restart(state); break;
            case EditorToolbarAction.ToolMove: SetGizmoTool(GizmoTool.Move); break;
            case EditorToolbarAction.ToolRotate: SetGizmoTool(GizmoTool.Rotate); break;
            case EditorToolbarAction.ToolScale: SetGizmoTool(GizmoTool.Scale); break;
            case EditorToolbarAction.ToggleSnap: ToggleGizmoSnap(); break;
            case EditorToolbarAction.Save:
                // Open the Save dialog (name the scene, then confirm) rather than writing immediately.
                // Preserve the loud gate: when Save is blocked there is nothing to name, so log the
                // actionable cause and do NOT open (the toolbar already dims/deactivates the button for
                // either cause; this closes the headless/dispatch path). The confirm re-checks
                // (SaveCurrentScene) as defense-in-depth.
                switch (SaveBlock(state, _projectContext))
                {
                    case SaveBlockReason.Playing:
                        Logger.Warning(
                            "[level-editor] Save is blocked while the transport is Playing — saving " +
                            "mid-simulation would bake transient run state into the scene. Pause first.");
                        return;
                    case SaveBlockReason.NoProjectRoot:
                        Logger.Warning(
                            "[level-editor] Save is blocked: no project root resolved (no " +
                            $"{GameProject.FileName} found). Set {EditorProjectContext.ProjectRootVariable} " +
                            "in the run configuration, or run from a build output inside the project source tree.");
                        return;
                }
                Dialog.OpenSave(_sceneId);
                break;
            case EditorToolbarAction.Load:
                // Open the Load dialog listing the scenes under LevelsPath (or the actionable
                // no-project-root message); a row click / dialog:load routes through LoadScene.
                Dialog.OpenLoad();
                break;
            case EditorToolbarAction.Undo: History.Undo(); break;
            case EditorToolbarAction.Redo: History.Redo(); break;
            // The selection-edit actions (island-authoring Slice 2) — each guards itself
            // (Edit-only, loud) inside EditorCommandSystem, the same instance the woven
            // editor.commands entry runs.
            case EditorToolbarAction.OrderForward: _editorCommands.BringForward(state); break;
            case EditorToolbarAction.OrderBack: _editorCommands.SendBack(state); break;
            case EditorToolbarAction.ColliderAddBox: _editorCommands.AddBoxCollider(state); break;
            case EditorToolbarAction.ColliderAddConvex: _editorCommands.AddConvexCollider(state); break;
            case EditorToolbarAction.ColliderRemove: _editorCommands.RemoveCollider(state); break;
            case EditorToolbarAction.VertexAdd: _editorCommands.AddVertex(state); break;
            case EditorToolbarAction.ToolBoundary: BeginBoundary(); break;
            // Re-scan the drop folder + rebuild the palette live (island-authoring Slice 4). Loud
            // no-op on a screen that composes no palette.
            case EditorToolbarAction.RefreshCatalog:
                if (Palette == null)
                    Logger.Warning("[level-editor] RefreshCatalog: this screen composes no palette.");
                else
                    Palette.Refresh();
                break;
        }
    }

    /// <summary>
    /// Writes the current scene to <c>ProjectContext.LevelsPath/&lt;sceneId&gt;.mdscene</c> through the
    /// shared <see cref="SceneWriter"/> — the Save dialog's confirm callback (and the direct guarded
    /// path). Re-checks <see cref="SaveBlock"/> as defense-in-depth (the dialog only opens when
    /// allowed), builds the scene once so the ship-readiness lint can inspect it, logs an overwrite
    /// note when the target already exists (the simpler of the two overwrite options — overwrite,
    /// don't prompt), writes, then appends the zero-touch MGCB bundle entry for a new level.
    /// </summary>
    private void SaveCurrentSceneTo(string target, GameState state)
    {
        switch (SaveBlock(state, _projectContext))
        {
            case SaveBlockReason.Playing:
                Logger.Warning(
                    "[level-editor] Save is blocked while the transport is Playing — saving " +
                    "mid-simulation would bake transient run state into the scene. Pause first.");
                return;
            case SaveBlockReason.NoProjectRoot:
                Logger.Warning(
                    "[level-editor] Save is blocked: no project root resolved (no " +
                    $"{GameProject.FileName} found). Set {EditorProjectContext.ProjectRootVariable} " +
                    "in the run configuration, or run from a build output inside the project source tree.");
                return;
        }

        if (!string.IsNullOrEmpty(target) && PlatformServices.Current.FileExists(target))
            Logger.Info($"[level-editor] Overwriting existing scene '{_sceneId}' at '{target}'.");

        // Build once so the ship-readiness lint (PS6) can inspect the exact scene being written.
        var writer = new SceneWriter(Serializer);
        var scene = writer.BuildScene(_world, _camera, _layers);
        WarnIfNotShipReady(scene);
        var savedPath = writer.Save(scene, target);
        // Zero-touch bundling (PS6): append the MGCB /copy: entry for a NEW level so it bundles to the
        // title on the next build with no manual .mgcb edit (idempotent for existing ones). The copy
        // line is `./Levels/<id>.mdscene`, so it is only correct when the scene was written directly
        // into LevelsPath (the browser's default open dir) — a save the designer navigated ELSEWHERE
        // under the project root is authored but not auto-bundled (it must live under Content/Levels to
        // ship; see the navigator scoping premise).
        if (savedPath != null)
        {
            if (IsUnderLevelsRoot(target))
                EnsureLevelBundled();
            else
                Logger.Info(
                    $"[level-editor] Saved scene '{_sceneId}' to '{savedPath}' OUTSIDE the levels dir " +
                    $"('{_projectContext?.LevelsPath}') — not auto-bundled; move it under Content/Levels to ship it.");
            Logger.Info($"[level-editor] Saved scene '{_sceneId}' to '{savedPath}'.");
        }
    }

    /// <summary>Whether <paramref name="target"/> sits directly in the project's levels dir (the only
    /// location the zero-touch <c>./Levels/&lt;id&gt;.mdscene</c> MGCB copy line addresses correctly).</summary>
    private bool IsUnderLevelsRoot(string target)
    {
        var levels = _projectContext?.LevelsPath;
        if (string.IsNullOrEmpty(levels)) return false;
        var dir = Path.GetDirectoryName(target);
        return !string.IsNullOrEmpty(dir) &&
               string.Equals(dir!.TrimEnd('/', '\\'), levels!.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Publishes a <see cref="LoadSceneRequest"/> for the absolute <paramref name="filePath"/> the
    /// browser resolved (desktop file IO — no build round-trip; the woven <c>SceneReaderSystem</c>
    /// restores the DrawComponents + auto-frames so the scene lands visible). The Load dialog's
    /// file-pick callback; the browser only produces a non-empty path when the project is resolved.
    /// </summary>
    private void LoadScene(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Logger.Warning(
                "[level-editor] Load is blocked: no project root resolved (no " +
                $"{GameProject.FileName} found). Set {EditorProjectContext.ProjectRootVariable} " +
                "in the run configuration, or run from a build output inside the project source tree.");
            return;
        }
        _world.Publish(new LoadSceneRequest(filePath, fromContent: false));
    }

    /// <summary>
    /// The navigator's roots: the resolved project root is the <b>up-boundary</b> (the browser never
    /// climbs above it — no OS-wide file picker) and <see cref="EditorProjectContext.LevelsPath"/> is
    /// where it opens (scenes must live under <c>Content/Levels</c> to bundle + load — see the navigator
    /// scoping premise), or an <b>unresolved</b> roots record carrying the actionable
    /// "set MONODREAMS_PROJECT_ROOT" message when the project root did not resolve.
    /// </summary>
    private BrowserRoots BrowserRoots()
    {
        if (_projectContext is { Resolved: true } && !string.IsNullOrEmpty(_projectContext.ProjectRoot))
            return new BrowserRoots(true, _projectContext.ProjectRoot, _projectContext.LevelsPath, null);
        return new BrowserRoots(false, null, null,
            $"No project root resolved (no {GameProject.FileName} found). Set " +
            $"{EditorProjectContext.ProjectRootVariable} in the run configuration, or run from a " +
            "build output inside the project source tree.");
    }

    /// <summary>
    /// Lists a directory for the navigator: its immediate subfolders + every file name (the browser
    /// applies the <c>.mdscene</c> filter + folder/file classification on top). Desktop file IO through
    /// <see cref="System.IO.Directory"/> — a desktop editor-UI concern that never runs on web (the web
    /// head composes no project context), the same justification FW1's resolution enumeration uses; the
    /// dialog + browser stay filesystem-free (they take this as an injected provider, so the whole flow
    /// is unit-testable in-process). Never throws — a listing failure yields an empty, message-carrying
    /// directory rather than crashing the modal.
    /// </summary>
    private RawDirectory ListDirectory(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
                return new RawDirectory(true, Array.Empty<string>(), Array.Empty<string>(), "Folder not found.");
            var dirs = new List<string>();
            foreach (var sub in Directory.EnumerateDirectories(dir))
                dirs.Add(Path.GetFileName(sub.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            var files = new List<string>();
            foreach (var file in Directory.EnumerateFiles(dir))
                files.Add(Path.GetFileName(file));
            var message = dirs.Count == 0 && files.Count == 0 ? "Empty folder." : null;
            return new RawDirectory(true, dirs, files, message);
        }
        catch (Exception ex)
        {
            Logger.Warning($"[level-editor] Load/Save dialog: could not list '{dir}': {ex.Message}");
            return new RawDirectory(true, Array.Empty<string>(), Array.Empty<string>(), "Could not list this folder.");
        }
    }

    /// <summary>Enters the boundary tool (island-authoring §5.2) — a radio with the transform tools:
    /// disarm the palette first (so its Place mode + ghost stand down), then begin the lay.</summary>
    private void BeginBoundary()
    {
        Palette?.Disarm();
        _boundaryTool.BeginBoundary();
    }

    /// <summary>
    /// Ship-readiness lint on Save (PS6, nice-to-have surfacing): a scene is fully portable only when
    /// it has ZERO <c>file:</c> AssetKeys (all graduated to content keys — see the ship-readiness
    /// premise). When the scene being saved still references drop-folder art through the <c>file:</c>
    /// scheme, log a loud warning naming the count + a sample key so the designer knows the level is
    /// not yet shippable. Never blocks the save (a <c>file:</c> scene is valid to author + iterate on).
    /// </summary>
    private void WarnIfNotShipReady(SceneData scene)
    {
        var fileKeys = SceneLint.FindFileAssetKeys(scene);
        if (fileKeys.Count == 0) return;
        Logger.Warning(
            $"[level-editor] Scene '{_sceneId}' is NOT ship-ready: {fileKeys.Count} file: asset key(s) " +
            $"still reference the drop folder (e.g. '{fileKeys[0].AssetKey}'). Graduate them to MGCB " +
            "content keys before shipping (a scene ships-clean when it has zero file: keys).");
    }

    /// <summary>
    /// Zero-touch level bundling (PS6, banked decision 2 — editor-appends-copy-line): on Save, ensure
    /// the content project's <c>Content.mgcb</c> carries a <c>/copy:</c> entry for the scene just
    /// written, so a brand-new level bundles to the title (desktop + web) on the next build with no
    /// manual MGCB editing. Idempotent — a no-op for a level whose entry already exists (every
    /// committed level, and every re-save). Desktop-editor-only file IO through
    /// <see cref="IPlatformServices"/>; only reached from Save, which is already gated on a resolved
    /// project root (<see cref="SaveBlock"/>). See <see cref="MgcbLevelBundle"/>.
    /// </summary>
    private void EnsureLevelBundled()
    {
        var ctx = _projectContext;
        if (ctx is not { Resolved: true } || string.IsNullOrEmpty(ctx.ProjectRoot)) return;

        var mgcbPath = Path.Combine(ctx.ProjectRoot!, MgcbLevelBundle.McgbFileName);
        if (!PlatformServices.Current.FileExists(mgcbPath))
        {
            Logger.Warning(
                $"[level-editor] Zero-touch bundling skipped: no {MgcbLevelBundle.McgbFileName} at " +
                $"'{mgcbPath}'. Add '{MgcbLevelBundle.CopyLine(_sceneId)}' by hand so '{_sceneId}' bundles to the title.");
            return;
        }

        var updated = MgcbLevelBundle.EnsureCopyEntry(PlatformServices.Current.ReadAllText(mgcbPath), _sceneId, out var changed);
        if (!changed) return;

        PlatformServices.Current.WriteAllText(mgcbPath, updated);
        Logger.Info(
            $"[level-editor] Bundled new level '{_sceneId}': appended '{MgcbLevelBundle.CopyLine(_sceneId)}' to " +
            $"{MgcbLevelBundle.McgbFileName}. Rebuild to copy it into the title content.");
    }

    /// <summary>
    /// The save guard's distinguishable reasons: Save dispatches only while the transport is Paused
    /// (<see cref="RunMode.Edit"/>) AND the project root is resolved. Pure — named by the save-guard
    /// premise and its test. The two causes are reported separately so the toolbar/log can tell the
    /// user WHY Save is off. <see cref="SaveBlockReason.Playing"/> takes precedence (checked first).
    /// </summary>
    public static SaveBlockReason SaveBlock(GameState state, EditorProjectContext? projectContext)
    {
        if (state.RunMode == RunMode.Play) return SaveBlockReason.Playing;
        if (projectContext is not { Resolved: true }) return SaveBlockReason.NoProjectRoot;
        return SaveBlockReason.None;
    }

    /// <summary>Whether Save is blocked for any reason (see <see cref="SaveBlock"/>).</summary>
    public static bool IsSaveBlocked(GameState state, EditorProjectContext? projectContext) =>
        SaveBlock(state, projectContext) != SaveBlockReason.None;

    /// <summary>
    /// The scene id the editor holds: an explicit <paramref name="sceneId"/> wins; otherwise the
    /// manifest's <see cref="GameProject.StartScene"/> (when the project is resolved and it is
    /// non-empty); otherwise <see cref="DefaultSceneId"/>. Pure — testable without constructing the
    /// (GraphicsDevice-bound) overlay.
    /// </summary>
    public static string ResolveSceneId(string? sceneId, EditorProjectContext? projectContext)
    {
        if (!string.IsNullOrWhiteSpace(sceneId)) return sceneId!;
        var startScene = projectContext?.Manifest?.StartScene;
        return string.IsNullOrWhiteSpace(startScene) ? DefaultSceneId : startScene!;
    }

    /// <summary>
    /// The absolute source-tree path Save writes / Load reads:
    /// <c>&lt;LevelsPath&gt;/&lt;sceneId&gt;.mdscene</c>, or <c>null</c> when the project is unresolved
    /// (<see cref="EditorProjectContext.LevelsPath"/> null) — the caller then refuses (Save is already
    /// gated by <see cref="SaveBlock"/>; the writer's own guard is the defense-in-depth backstop). Pure.
    /// </summary>
    public static string? SceneFilePath(EditorProjectContext? projectContext, string sceneId)
    {
        var levelsPath = projectContext?.LevelsPath;
        return string.IsNullOrEmpty(levelsPath)
            ? null
            : Path.Combine(levelsPath!, sceneId + SceneWriter.SceneFileExtension);
    }

    /// <summary>
    /// The STRING-action dispatch the headless channel routes <c>ToolbarAction</c> ops through:
    /// <c>palette:&lt;entryId&gt;</c> arms a palette item, <c>palette:none</c> (or a bare
    /// <c>palette:</c>) disarms, <c>band:&lt;name&gt;</c> selects a layer band,
    /// <c>asset-band:&lt;entryId&gt;:&lt;band&gt;</c> permanently marks an asset's band
    /// (<c>:auto</c> clears it),
    /// <c>order:forward</c>/<c>order:back</c> nudge the selection's within-band order,
    /// <c>collider:addBox</c>/<c>addConvex</c>/<c>remove</c>/<c>addVertex</c>/<c>deleteVertex</c>
    /// drive the collider authoring actions, <c>ghost:cw</c>/<c>ghost:ccw</c> rotate the armed
    /// palette ghost, <c>panel:systems|scene|inspector</c> collapse a right-strip section,
    /// <c>panel:group &lt;name&gt;</c> collapses a pipeline group, <c>panel:inspect &lt;type&gt;</c>
    /// expands a component's member values, <c>panel:select &lt;name&gt;</c> selects a scene entity,
    /// <c>panel:tab &lt;scene|systems|project|assets&gt;</c> switches a region's active tab,
    /// <c>shell:right &lt;pt&gt;</c> / <c>shell:bottom &lt;pt&gt;</c> resize a region (clamped),
    /// and anything else parses as a plain
    /// <see cref="EditorToolbarAction"/> into <see cref="DispatchToolbarAction"/> — so every
    /// scripted editor action shares one grammar. Loud on unknown names / a palette op without a
    /// composed palette.
    /// </summary>
    public void DispatchNamedAction(string name, GameState state)
    {
        const string palettePrefix = "palette:";
        const string assetBandPrefix = "asset-band:";
        const string bandPrefix = "band:";
        const string orderPrefix = "order:";
        const string colliderPrefix = "collider:";
        const string boundaryPrefix = "boundary:";
        const string triggerPrefix = "trigger:";
        const string ghostPrefix = "ghost:";
        const string dialogPrefix = "dialog:";
        const string panelPrefix = "panel:";
        const string shellPrefix = "shell:";

        if (name.StartsWith(panelPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // panel:systems|scene|inspector (toggle a section), panel:group <fullName> (toggle a
            // pipeline group's children), panel:inspect <typeName> (toggle a component's members),
            // panel:select <entityName> (select a scene entity by its EntityInfo name/type).
            var rest = name.Substring(panelPrefix.Length);
            var space = rest.IndexOf(' ');
            var verb = space < 0 ? rest : rest.Substring(0, space);
            var arg = space < 0 ? string.Empty : rest.Substring(space + 1);
            switch (verb.ToLowerInvariant())
            {
                case "tab": SetPanelTab(arg, name); break;
                case "systems": _editorPanel.ToggleSection(EditorPanelSection.Systems); break;
                case "scene": _editorPanel.ToggleSection(EditorPanelSection.Scene); break;
                case "inspector": _editorPanel.ToggleSection(EditorPanelSection.Inspector); break;
                case "group": _editorPanel.ToggleGroupCollapsed(arg); break;
                case "inspect": _editorPanel.ToggleInspectorComponentKey(arg); break;
                case "select":
                    if (!_editorPanel.SelectEntityByName(arg))
                        Logger.Warning($"[level-editor] Editor-op '{name}': no scene entity named '{arg}'.");
                    break;
                default:
                    Logger.Warning(
                        $"[level-editor] Editor-op '{name}': expected " +
                        "panel:tab <scene|systems|project|assets>|systems|scene|inspector|group <name>|inspect <type>|select <name>.");
                    break;
            }
            return;
        }

        if (name.StartsWith(shellPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // shell:right <pt> | shell:bottom <pt> — resize a region (clamped by the shell state).
            var rest = name.Substring(shellPrefix.Length);
            var space = rest.IndexOf(' ');
            var verb = space < 0 ? rest : rest.Substring(0, space);
            var arg = space < 0 ? string.Empty : rest.Substring(space + 1);
            if (!int.TryParse(arg, out var pt))
            {
                Logger.Warning($"[level-editor] Editor-op '{name}': expected shell:right <pt> or shell:bottom <pt>.");
                return;
            }
            switch (verb.ToLowerInvariant())
            {
                case "right": _shellState.RightWidthPt = pt; break;
                case "bottom": _shellState.BottomHeightPt = pt; break;
                default:
                    Logger.Warning($"[level-editor] Editor-op '{name}': expected shell:right <pt> or shell:bottom <pt>.");
                    break;
            }
            return;
        }

        if (name.StartsWith(dialogPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // dialog:save-open | load-open | name <text> | confirm | cancel | cd <folder> | up |
            // pick <file> (alias: load <file>)
            var rest = name.Substring(dialogPrefix.Length);
            var space = rest.IndexOf(' ');
            var verb = space < 0 ? rest : rest.Substring(0, space);
            var arg = space < 0 ? string.Empty : rest.Substring(space + 1);
            switch (verb.ToLowerInvariant())
            {
                case "save-open": Dialog.OpenSave(_sceneId); break;
                case "load-open": Dialog.OpenLoad(); break;
                case "name": Dialog.SetName(arg); break;
                case "confirm": Dialog.Confirm(state); break;
                case "cancel": Dialog.Cancel(); break;
                case "cd": Dialog.EnterDirectory(arg); break;
                case "up": Dialog.GoUp(); break;
                case "pick": case "load": Dialog.PickFile(arg, state); break;
                default:
                    Logger.Warning(
                        $"[level-editor] Editor-op '{name}': expected " +
                        "dialog:save-open|load-open|name <text>|confirm|cancel|cd <folder>|up|pick <file>.");
                    break;
            }
            return;
        }

        if (name.StartsWith(boundaryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var op = name.Substring(boundaryPrefix.Length).ToLowerInvariant();
            switch (op)
            {
                case "begin": BeginBoundary(); break;
                case "commit": _boundaryTool.CommitBoundary(); break;
                case "cancel": _boundaryTool.CancelBoundary(); break;
                default:
                    Logger.Warning($"[level-editor] Editor-op '{name}': expected boundary:begin|commit|cancel.");
                    break;
            }
            return;
        }

        if (name.StartsWith(triggerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (Palette == null)
            {
                Logger.Warning($"[level-editor] Editor-op '{name}': this screen composes no palette.");
                return;
            }
            Palette.ArmTrigger(name.Substring(triggerPrefix.Length));
            return;
        }

        if (name.StartsWith(ghostPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (Palette == null)
            {
                Logger.Warning($"[level-editor] Editor-op '{name}': this screen composes no palette.");
                return;
            }
            var dir = name.Substring(ghostPrefix.Length).ToLowerInvariant();
            switch (dir)
            {
                case "cw": Palette.RotateArmedGhost(PalettePlacementSystem.GhostRotationStep); break;
                case "ccw": Palette.RotateArmedGhost(-PalettePlacementSystem.GhostRotationStep); break;
                default: Logger.Warning($"[level-editor] Editor-op '{name}': expected ghost:cw or ghost:ccw."); break;
            }
            return;
        }

        if (name.StartsWith(assetBandPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (Palette == null)
            {
                Logger.Warning($"[level-editor] Editor-op '{name}': this screen composes no palette.");
                return;
            }
            // asset-band:<entryId>:<band> — split on the LAST ':' (the band name has none; the entry
            // id may itself be a full file: key, which does). "auto"/"none" clears the mark.
            var rest = name.Substring(assetBandPrefix.Length);
            var sep = rest.LastIndexOf(':');
            if (sep <= 0 || sep >= rest.Length - 1)
                Logger.Warning($"[level-editor] Editor-op '{name}': expected asset-band:<entryId>:<band>.");
            else
                Palette.SetAssetBand(rest.Substring(0, sep), rest.Substring(sep + 1));
            return;
        }

        if (name.StartsWith(palettePrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (Palette == null)
            {
                Logger.Warning($"[level-editor] Editor-op '{name}': this screen composes no palette.");
                return;
            }
            var id = name.Substring(palettePrefix.Length);
            if (string.IsNullOrEmpty(id) || string.Equals(id, "none", StringComparison.OrdinalIgnoreCase))
                Palette.Disarm();
            else
                Palette.Arm(id);
            return;
        }

        if (name.StartsWith(bandPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (Palette == null)
                Logger.Warning($"[level-editor] Editor-op '{name}': this screen composes no palette.");
            else
                Palette.SelectBand(name.Substring(bandPrefix.Length));
            return;
        }

        if (name.StartsWith(orderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var direction = name.Substring(orderPrefix.Length);
            if (string.Equals(direction, "forward", StringComparison.OrdinalIgnoreCase))
                _editorCommands.BringForward(state);
            else if (string.Equals(direction, "back", StringComparison.OrdinalIgnoreCase))
                _editorCommands.SendBack(state);
            else
                Logger.Warning($"[level-editor] Editor-op '{name}': expected order:forward or order:back.");
            return;
        }

        if (name.StartsWith(colliderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var op = name.Substring(colliderPrefix.Length);
            switch (op.ToLowerInvariant())
            {
                case "addbox": _editorCommands.AddBoxCollider(state); break;
                case "addconvex": _editorCommands.AddConvexCollider(state); break;
                case "remove": _editorCommands.RemoveCollider(state); break;
                case "addvertex": _editorCommands.AddVertex(state); break;
                case "deletevertex": _editorCommands.DeleteSelection(state); break;
                default:
                    Logger.Warning($"[level-editor] Editor-op '{name}': unknown collider op.");
                    break;
            }
            return;
        }

        if (Enum.TryParse<EditorToolbarAction>(name, ignoreCase: true, out var action))
            DispatchToolbarAction(action, state);
        else
            Logger.Warning($"[level-editor] Editor-op: unknown action '{name}'.");
    }

    /// <summary>The <c>panel:tab &lt;scene|systems|project|assets&gt;</c> op — switches the right
    /// strip's active tab, or (assets) the bottom shelf's.</summary>
    private void SetPanelTab(string arg, string name)
    {
        switch (arg.Trim().ToLowerInvariant())
        {
            case "scene": _editorPanel.SetRightTab(EditorRightTab.Scene); break;
            case "systems": _editorPanel.SetRightTab(EditorRightTab.Systems); break;
            case "project": _editorPanel.SetRightTab(EditorRightTab.Project); break;
            case "assets": _shellState.ActiveBottomTab = EditorBottomTab.Assets; break;
            default:
                Logger.Warning(
                    $"[level-editor] Editor-op '{name}': expected panel:tab <scene|systems|project|assets>.");
                break;
        }
    }

    private void SetGizmoTool(GizmoTool tool)
    {
        if (!_gizmoState.IsAlive) return;
        ref var state = ref _gizmoState.Get<GizmoStateComponent>();
        state.Tool = tool;
        // The tool buttons are a radio over the coarse modality (§S1): picking a transform tool
        // leaves Place mode, which also despawns the ghost via the palette.
        if (state.Mode != EditorToolMode.SelectTransform)
        {
            if (Palette != null) Palette.Disarm();
            else state.Mode = EditorToolMode.SelectTransform;
        }
    }

    private void ToggleGizmoSnap()
    {
        if (!_gizmoState.IsAlive) return;
        ref var state = ref _gizmoState.Get<GizmoStateComponent>();
        state.SnapEnabled = !state.SnapEnabled;
    }
}

/// <summary>Why the editor's Save is disabled — reported by <see cref="EditorOverlay.SaveBlock"/> so
/// the toolbar can dim the right button and the log can name the cause.</summary>
public enum SaveBlockReason
{
    /// <summary>Save is allowed (Paused + a resolved project root).</summary>
    None,
    /// <summary>Blocked because the transport is Playing — a mid-simulation save would bake transient
    /// run state into the scene.</summary>
    Playing,
    /// <summary>Blocked because no project root resolved (shipped build / relocated output / console /
    /// no <c>game.mdproj</c>) — there is nowhere versioned to write.</summary>
    NoProjectRoot,
}
