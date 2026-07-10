#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
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
using MonoDreams.LevelEditor.EntityFactory;
using MonoDreams.LevelEditor.Input;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.Screen;
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
    private readonly Entity _overlaySettings; // UX3-D: ShowGrid / OutlineSelected / ShowCameraGlyph
    private readonly EditorCameraRig _cameraRig;
    private readonly EditorShellStateComponent _shellState = new();
    // The concrete reader (SceneReader is the ISystem view of it) — read SceneWasLoaded for the
    // empty-save guard.
    private readonly SceneReaderSystem _sceneReaderSystem;
    // The concrete selection + context-menu systems: the viewport right-click callback + the menu:*
    // ops call their public methods directly (like _editorCommands / _leftPanel).
    private readonly SelectionSystem _selection;
    private readonly EditorContextMenuSystem _menu;
    // Scene-catalog binding (UX-C), set late in the screen's Load (the screen name + ScreenController
    // + hand-off are only known there). Null until bound → the Scenes panel shows nothing / no switch.
    private string? _currentScreenName;
    private Func<IReadOnlyList<(string Name, ScreenInfo Info)>>? _registeredScreens;
    private Action<SceneCatalogEntry>? _switchScene;

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
        // The transient notification seam (PF-F): user-action sites raise a one-line status message
        // (a save refusal, a prefab confirmation, a guardrail hint) AS WELL AS logging it. The status
        // bar renders the current one on its LEFT; guarded editor systems that emit hints share it.
        Notifications = new EditorNotifications();

        // The single gizmo-state entity: the toolbar's tool-select / snap-toggle mutate it,
        // GizmoSystem reads it.
        _gizmoState = world.CreateEntity();
        _gizmoState.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        _gizmoState.Set(GizmoStateComponent.Default);

        // The viewport-overlay settings (UX3-D): grid off / outline on / camera glyph on, session-scoped.
        // A standalone editor-state entity (survives a transport Restart, discoverable). Spacing is NOT
        // here — it IS GizmoStateComponent.GridStep above (the one grid quantum the overlay menu edits).
        _overlaySettings = world.CreateEntity();
        _overlaySettings.Set(new EditorInfrastructureComponent());
        _overlaySettings.Set(ViewportOverlaySettingsComponent.Default);

        // The single shell-state entity (UX-B): the ONE source of the resizable region sizes, the
        // active tab per region, and the drag ownership shared by the shell / panel / palette. On an
        // editor-infra entity so it is discoverable + survives a transport Restart.
        var shellStateEntity = world.CreateEntity();
        shellStateEntity.Set(new EditorInfrastructureComponent());
        shellStateEntity.Set(_shellState);

        // The transport owns RunMode AND drives the ViewportContextStack (PF-B — the ONE tab-switching
        // mechanism). It is handed the shared shell state (so the stack rewrites the tab-strip descriptors
        // the tab-strip system reads) and the current scene id (the Scene tab's id).
        Transport = new EditorTransport(world, History, _shellState, _sceneId);

        // The camera rig (UX2-E): the authored game-camera state as a standalone entity, split from the
        // free editor VIEW (the shared Camera CameraNavSystem drives). It re-syncs from scene.camera on
        // every load (the reader's rig seam below), Save reads scene.camera FROM it (not the view), and
        // it emits its frustum glyph in the overlay-prep pass. Constructed before the reader so the seam
        // is ready for the first load.
        // UX3-D: the "Camera" overlay toggle + the Game-mode sandbox gate the frustum glyph. Transport
        // is already constructed above; the lambda reads both at emit time.
        // PF-B: the rig glyph is SCENE-CONTEXT-ONLY (the obvious PF-D seam — a prefab context has no rig,
        // so its glyph must not show either). For PF-B (Scene / Game only) this equals "hidden while the
        // Game tab is active".
        _cameraRig = new EditorCameraRig(world, camera, viewportManager,
            glyphVisible: () => Settings.ShowCameraGlyph && Transport.ActiveContextKind == ViewportContextKind.Scene);

        // The file-asset texture loader is always composed (a loaded scene can carry file: keys
        // whether or not this screen shows a palette); textures load lazily, and a missing file
        // shows the magenta placeholder instead of an invisible sprite.
        AssetTextures = new FileAssetTextureLoader(graphicsDevice, content?.RootDirectory ?? "Content");

        // Prefab resolution (PF-C): source-first via the project context in-editor, else bundled via
        // TitleContainer. The ONE PrefabExpander is shared by the scene reader (below — so a scene with
        // linked instances expands on load), the PrefabFactory (exposed for the screen's spawn
        // registration + the "prefab:<id>" channel), and live propagation on prefab-save (PF-D). The
        // prefab source also feeds the SceneWriters below so an instance root compacts to
        // { prefab + Transform + overrides } on Save.
        PrefabSource = new PrefabFileSource(content?.RootDirectory ?? "Content", projectContext).Resolve;
        PrefabExpander = new PrefabExpander(Serializer, PrefabSource,
            loadTexture: content != null ? key => content.Load<Texture2D>(key) : null,
            fileTextureLoader: AssetTextures.Load);
        PrefabFactory = new PrefabFactory(PrefabExpander);

        // The rig seam (applyCameraToRig) makes THIS reader the editor path: scene.camera → the rig, and
        // the live VIEW auto-frames the content. (A shipped reader with no seam applies scene.camera to
        // the live camera directly — see SceneReaderSystem.) The prefab expander expands linked instances.
        _sceneReaderSystem = new SceneReaderSystem(world, Serializer, content,
            fileTextureLoader: AssetTextures.Load, camera: camera,
            applyCameraToRig: _cameraRig.SyncFromScene, prefabExpander: PrefabExpander);
        SceneReader = _sceneReaderSystem;

        // UX2-F: wire the transport's Game-mode sandbox seams to the SHARED instances (Transport was
        // constructed above; the rig + reader now exist). The snapshot is a SceneData built from the rig
        // camera + layers (no file I/O); the restore publishes an in-memory LoadSceneRequest so it runs
        // the SAME reader pipeline as a file load (re-tag / rehydrate / DrawComponent / rig re-sync —
        // pre-mortem #2, the reader is the ONE restore path); the view capture/restore is the live VIEW
        // (Camera) state; and Game-mode entry adopts the game-camera view (the rig).
        // PF-D (pre-mortem #8): a PREFAB context snapshots a CAMERA-LESS scene (no rig, no layers — a
        // prefab is a class, not a scene) and restores with the camera rig SUPPRESSED (the stack sets
        // RestoringPrefabContext for the duration of a prefab-context restore). A Scene/Game context is
        // unchanged (rig camera + layers, rig re-synced).
        Transport.CaptureSnapshot = () => Transport.ActiveContextKind == ViewportContextKind.Prefab
            ? new SceneWriter(Serializer, PrefabSource).BuildScene(world)
            : new SceneWriter(Serializer, PrefabSource).BuildScene(world, _cameraRig.AsCamera(), layers);
        Transport.RestoreSnapshot = snapshot => world.Publish(
            new LoadSceneRequest(snapshot, suppressCameraRig: Transport.ContextStack.RestoringPrefabContext));
        Transport.CaptureView = () => new CameraViewSnapshot(camera.Position, camera.Zoom, camera.Rotation);
        Transport.RestoreView = view =>
        {
            camera.Position = view.Position;
            camera.Zoom = view.Zoom;
            camera.Rotation = view.Rotation;
        };
        Transport.SnapViewToRig = _cameraRig.SnapViewToRig;
        _editorCommands = new EditorCommandSystem(
            world, History, Serializer,
            layers, input.OrderForwardRequested, input.OrderBackRequested, camera);
        // UX3-D gates: the Game-mode sandbox hides ALL gizmo overlays; "Outline Selected" (off) hides
        // only the selection outline (selection unaffected).
        var gizmo = new GizmoSystem(world, camera, History, viewportManager,
            viewportOverlaysVisible: () => Transport.ActiveContextKind != ViewportContextKind.Game,
            selectionOutlineVisible: () => Settings.OutlineSelected);
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

        // UX3-D: the world-space reference grid — beneath the other overlays, at the shared grid quantum
        // (GizmoStateComponent.GridStep), hidden outside Edit / when off / in the Game-mode sandbox.
        var grid = new EditorGrid(world, camera, viewportManager,
            spacing: () => GridSpacing,
            visible: () => Settings.ShowGrid && Transport.ActiveContextKind != ViewportContextKind.Game);
        OverlayPrep = new EditorOverlayPrepSystem(gizmo, proxySync, boundaryTool, triggerOverlay, _cameraRig, grid);
        _cameraNav = new CameraNavSystem(world, camera);
        CameraNav = _cameraNav;
        _selection = new SelectionSystem(world, camera);
        Selection = _selection;

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
        // The Save button additionally dims (and its click is suppressed) while Paused for a cause the
        // transport rule does not already cover: the project is unresolved (NoProjectRoot) OR the editor
        // is in the Game-mode sandbox (GameMode — UX2-F). The "Playing" cause is already covered by the
        // toolbar's transport rule (editing buttons dim while Playing).
        ToolbarClicks = new ToolbarSystem(world, DispatchToolbarAction,
            (action, state) => action == EditorToolbarAction.Save
                               && SaveBlock(state, _projectContext, Transport.ActiveContextKind)
                                   is SaveBlockReason.NoProjectRoot or SaveBlockReason.GameMode,
            // A shell splitter/scrollbar drag that happens to release over the toolbar must not also
            // fire the button (the drag holds the shared token through its release edge).
            isInputSuppressed: () => _shellState.IsDragging);
        // The ONE pooled hover tooltip for the icon buttons (UX2-C) — reads the per-button hover clock
        // ToolbarClicks advances, so it weaves right AFTER ToolbarClicks in the editor.toolbar group.
        Tooltip = new EditorTooltipSystem(world, viewportManager, toolbarFont);
        // PF-B: the viewport tab strip ([Scene] [▶ Game ×]) at the Scene header's start — replaces the
        // retired Scene/Game mode toggle. Descriptor-driven from the shell state the ViewportContextStack
        // writes; clicks route to the transport (SwitchToTab / the dirty-gated CloseTab) by slot. Live in
        // both transport states (leaving the Game tab must work while Playing) and suppressed during a
        // shell drag (a drag releasing over a tab must not fire it).
        ViewportTabs = new ViewportTabStripSystem(world, viewportManager, toolbarFont, _shellState,
            switchToTab: (index, state) => Transport.SwitchToTab(index, state),
            closeTab: (index, state) => Transport.CloseTab(index, state),
            isInputSuppressed: () => _shellState.IsDragging);
        // The two editor panels (UX2-B) share ONE collapse/expand state component (ECS purity: the
        // state lives once, both panels read/write their own fields). On an editor-infra entity so it
        // is discoverable + survives a transport Restart.
        var panelStateEntity = world.CreateEntity();
        panelStateEntity.Set(new EditorInfrastructureComponent());
        var panelState = new EditorPanelStateComponent();
        panelStateEntity.Set(panelState);

        // The LEFT-strip tabbed panel (Entities / Systems / Scenes). The Systems tab binds lazily to
        // the pipelines the screen hands over via BindPipelines — they don't exist yet while the
        // overlay itself is being constructed; the Entities + Scenes tabs read live state directly.
        _leftPanel = new EditorPanelSystem(world, viewportManager, toolbarFont,
            () => (UpdatePipeline, DrawPipeline), _shellState,
            () => new EditorProjectInfo(_projectContext?.ProjectRoot, _projectContext?.LevelsPath, _sceneId),
            // UX-C: the Scenes tab's list + the dirty-gated switch, both bound late (BindSceneCatalog).
            sceneCatalog: BuildCatalog,
            selectScene: SelectScene,
            // PF-B: while the Game tab is active the dirty ● reflects the SNAPSHOT's captured dirty state,
            // not the sandbox churn (the sandbox is discarded on leave, so its edits never count as unsaved).
            isDirty: () => Transport.ActiveContextKind == ViewportContextKind.Game
                ? Transport.SnapshotWasDirty
                : History.IsDirty,
            role: EditorPanelRole.LeftTabs,
            panelState: panelState);
        SystemsPanel = _leftPanel;

        // The RIGHT-strip dedicated Inspector panel (selection-bound components + members) — the same
        // parameterized panel system, RightInspector role, sharing the one panel state. PF-A: it drives
        // the SHARED History + serializer Registry for the editable Inspector (value edits / add / remove
        // through undoable commands), and reads the keyboard for its filter + inline edit fields.
        _inspectorPanel = new EditorPanelSystem(world, viewportManager, toolbarFont,
            shellState: _shellState, role: EditorPanelRole.RightInspector, panelState: panelState,
            history: History, registry: Registry);
        Inspector = _inspectorPanel;
        Shell = new EditorShellSystem(world, viewportManager, Chrome, setOsCursorVisible, _shellState);
        ChromeRender = new EditorChromeRenderSystem(spriteBatch, graphicsDevice, world, viewportManager);
        ChromeLayer = RenderLayer.Native(() => ChromeRender.CurrentTarget!);

        // The modal three-action Save dialog (native-resolution chrome on the Editor target; UX-D). Its
        // actions route back into the SAME shared instances (no second writer/history/transport): Save
        // Scene / Save Project run the guarded SaveCurrentScene / SaveProject, and Save Backup As… writes
        // a dangling <name>.mdscene then reloads the bound scene via the transport's Restart. The toolbar's
        // Save button OPENS it (see DispatchToolbarAction); there is no Load button — a scene is opened by
        // selecting it in the Scenes panel (UX-C/UX-D). The screen wires the host keyboard system's
        // ShouldSuppressInput to Dialog.IsOpen so editor/game keys (including Escape-to-exit) stand down
        // while it owns input.
        Dialog = new EditorDialogSystem(
            world, viewportManager, toolbarFont,
            onSaveScene: SaveCurrentScene,
            onSaveProject: SaveProject,
            onSaveBackup: SaveBackupAs,
            // Create Empty Scene (UX2-D §4): the collision predicate (loud refuse + keep open) and the
            // create callback (write the minimal canonical scene + bundle + dirty-gated switch).
            onSceneNameExists: SceneNameExists,
            onCreateScene: CreateEmptyScene);

        // The context-menu primitive (UX2-D §4): the viewport / Entities / Scenes / Entity-header menus.
        // Woven immediately AFTER editor.dialog (so the dialog wins when both could open); a menu never
        // opens while the dialog is open OR a shell drag owns the pointer (isBlocked). A clicked/picked
        // item fires its action-id path through DispatchMenuAction (the overlay's map — the menu stays
        // game-agnostic).
        _menu = new EditorContextMenuSystem(
            world, viewportManager, toolbarFont, DispatchMenuAction,
            isBlocked: () => Dialog.IsOpen || _shellState.IsDragging);
        Menu = _menu;

        // The modal transform owner (UX3-F): G/S/R enter a Blender-style modal transform over the
        // selection, driven live by the mouse without a button held and committed/cancelled through the
        // same coalescing history. It owns the pointer + keyboard while active (its own keyboard seam +
        // the consume + the ShouldSuppressInput/shortcut-gate ORs). Constructed before the shortcut
        // system so the gate can read Modal.IsActive.
        _modal = new ModalTransformSystem(world, camera, History);

        // The editor shortcut owner (UX3-E): reads the ONE EditorShortcuts chord table off the raw
        // keyboard, gated by ViewportShortcutContext (over the viewport, no dialog/menu/modal open,
        // Paused), and dispatches to the SAME shared instances via DispatchShortcut. commandIsMeta
        // resolves PlatformCommand → ⌘ on macOS / Ctrl elsewhere; OperatingSystem.IsMacOS() is a runtime
        // query (not a #if), mirroring EditorHiDpi in this same Composition layer — the foundation chord
        // layer stays platform-blind (the bool is injected). The Dialog/Menu/Modal predicates give the
        // shortcut path the SAME modal suppression the host keyboard gets via ShouldSuppressInput — and
        // stop a mid-modal G/S/R re-trigger.
        _shortcutSystem = new EditorShortcutSystem(
            world, _shortcuts, DispatchShortcut,
            dialogOpen: () => Dialog.IsOpen,
            menuOpen: () => Menu.IsOpen,
            commandIsMeta: OperatingSystem.IsMacOS(),
            modalActive: () => _modal.IsActive,
            // PF-A: while the Inspector filter is focused or a member is being inline-edited, no editor
            // chord fires (typing g/s/r/Delete/a name in a field must not fire a shortcut).
            inspectorEditing: () => _inspectorPanel.OwnsKeyboard);

        // The window status bar (UX3-F): the live modal readout / contextual status on the left, the
        // scene id + view mode + dirty dot on the right. Reads the SAME dirty source the Scenes panel
        // uses (the Game-mode snapshot dirty while sandboxed, else the history), so the ● never reflects
        // sandbox churn. RunNormally — live in both transport states.
        StatusBar = new EditorStatusBarSystem(
            world, viewportManager, toolbarFont, _modal,
            sceneId: () => _sceneId,
            isDirty: () => Transport.ActiveContextKind == ViewportContextKind.Game
                ? Transport.SnapshotWasDirty
                : History.IsDirty,
            activeKind: () => Transport.ActiveContextKind,
            notifications: Notifications);
        // The viewport right-click (SelectionSystem, SelectTransform + a hit) opens the entity menu at
        // the cursor — SelectionSystem has already picked + selected, so open directly (no re-pick); the
        // left panel's right-click opens the Entities/Scenes menu (per the active tab).
        _selection.ViewportContextMenuRequested = _ =>
            _menu.OpenAt(EditorContextMenuModel.EntityMenu(hasSelection: true, SelectionIsPrefabInstance()), CursorScreenPoint());
        _leftPanel.ContextMenuRequested = OpenLeftPanelContextMenu;

        // PF-D (pre-mortem #9): a dirty prefab tab's × routes the Save & Close / Discard / Cancel confirm.
        // The transport activates the tab first (so Save/Discard act on ITS world), then invokes this with
        // the now-active index. Save & Close writes the prefab then closes; Discard closes discarding edits.
        Transport.ConfirmDirtyClose = (index, s) =>
        {
            var ctxs = Transport.ContextStack.Contexts;
            var id = index >= 0 && index < ctxs.Count ? ctxs[index].Id : "prefab";
            Dialog.OpenConfirmClose(id,
                onSaveAndClose: st => { SavePrefabCurrent(st); Transport.ContextStack.CloseCleanContext(index); },
                onDiscardAndClose: st => Transport.ContextStack.CloseCleanContext(index));
        };
        // PF-A: the Inspector's "+ Add component" row opens the filterable command-palette popup with the
        // selection's candidate components (registered minus present minus structural). A pick dispatches
        // "add-component:<key>" back through DispatchMenuAction → the shared inspector panel.
        _inspectorPanel.AddComponentRequested = OpenAddComponentPopup;

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
                input.RotateCwRequested, input.RotateCcwRequested, bandConfig, _shellState,
                // PF-D — the Prefabs shelf tab: the lister feeds the cards, placePrefab stamps a linked
                // instance (undoable), and the menu/edit hooks route to the overlay's prefab flows.
                prefabLister: ListPrefabIds,
                placePrefab: PlacePrefabInstance,
                prefabCardMenu: (id, pt) => _menu.OpenAt(EditorContextMenuModel.PrefabCardMenu(id), pt),
                prefabShelfMenu: pt => _menu.OpenAt(EditorContextMenuModel.PrefabShelfMenu(), pt),
                editPrefab: (id, s) => OpenPrefabTab(id, s));
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

    /// <summary>The transient notification seam (PF-F): raise a one-line status message (severity-colored)
    /// on the status bar's LEFT alongside logging it. Shared with the guarded editor systems (delete /
    /// guardrail hints) so every user-action refusal/confirmation can surface without tailing the log.</summary>
    public EditorNotifications Notifications { get; }

    /// <summary>The prefab resolver (<c>id → <see cref="PrefabData"/></c>): source-first in-editor, else
    /// bundled via <c>TitleContainer</c>. Shared by the reader, the writer (instance compaction), the
    /// <see cref="PrefabExpander"/>, and the <see cref="PrefabFactory"/>.</summary>
    public Func<string, PrefabData?> PrefabSource { get; }

    /// <summary>The ONE prefab-expansion implementation — the reader expands linked instances through it,
    /// and the <see cref="PrefabFactory"/> + live propagation reuse it.</summary>
    public PrefabExpander PrefabExpander { get; }

    /// <summary>The prefab spawn factory for the <c>"prefab:&lt;id&gt;"</c> channel — the screen registers
    /// it on its <c>EntitySpawnSystem</c> (<c>RegisterEntityFactoryPrefix(PrefabFactory.IdentifierPrefix, …)</c>)
    /// so game code spawns any prefab via <c>EntitySpawnRequest("prefab:&lt;id&gt;", pos)</c>.</summary>
    public PrefabFactory PrefabFactory { get; }

    /// <summary>The single bounded undo/redo history (never construct a second one).</summary>
    public EditorHistory History { get; }

    /// <summary>The shared gizmo-state entity (tool / snap config).</summary>
    public Entity GizmoState => _gizmoState;

    /// <summary>The viewport-overlay settings entity (UX3-D: ShowGrid / OutlineSelected /
    /// ShowCameraGlyph). Grid spacing is NOT here — it is <see cref="GizmoStateComponent.GridStep"/> on
    /// <see cref="GizmoState"/> (the one grid quantum).</summary>
    public Entity OverlaySettings => _overlaySettings;

    /// <summary>A read snapshot of the current viewport-overlay settings (UX3-D).</summary>
    private ViewportOverlaySettingsComponent Settings => _overlaySettings.Get<ViewportOverlaySettingsComponent>();

    /// <summary>The shared grid quantum (= the gizmo snap step) the grid draws at and the presets edit.</summary>
    private float GridSpacing => _gizmoState.IsAlive ? _gizmoState.Get<GizmoStateComponent>().GridStep : 0f;

    /// <summary>The resolved project context (desktop-only, host-supplied), or null when none was
    /// supplied. Gates Save (the "no project root" cause) and, when resolved, its
    /// <see cref="EditorProjectContext.LevelsPath"/> is the directory Save targets.</summary>
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

    /// <summary>The camera rig (UX2-E): the authored game-camera state as a standalone entity, split
    /// from the free editor VIEW. Save reads <c>scene.camera</c> from it; every load re-syncs it; the
    /// <c>view:camera</c> op / header button snaps the view onto it; its frustum glyph draws in the
    /// overlay-prep pass. Exposed so tests + a future Game-mode transport (UX2-F) can read it.</summary>
    public EditorCameraRig CameraRig => _cameraRig;

    /// <summary>Native-scene loading (<c>LoadSceneRequest</c>). Weave with the level-load group.</summary>
    public ISystem<GameState> SceneReader { get; }

    // The concrete command system: the toolbar dispatch calls its selection-edit actions
    // (ordering / collider add-remove / add vertex) directly, and the shortcut dispatch calls
    // DeleteSelection.
    private readonly EditorCommandSystem _editorCommands;

    // The concrete camera-nav system: the shortcut dispatch + the view:frame op call FrameScene().
    private readonly CameraNavSystem _cameraNav;

    // The ONE editor shortcut table (UX3-E) + the system that reads it. The dispatch drives the SAME
    // shared instances (History / _editorCommands / _cameraNav / _menu / _modal) — never a second path.
    private readonly EditorShortcuts _shortcuts = new();
    private readonly EditorShortcutSystem _shortcutSystem;

    // The modal transform system (UX3-F): G/S/R modal transforms + the status bar's live readout. The
    // shortcut dispatch enters it, the modal:* ops drive it, and the status bar reads its readout.
    private readonly ModalTransformSystem _modal;

    /// <summary>The toolbar's + context menu's selection-edit actions plus the optional order-nudge
    /// keys (Edit-guarded). Delete/undo/redo are driven by <see cref="Shortcuts"/> now. Weave after
    /// logic, before <see cref="Gizmo"/>.</summary>
    public ISystem<GameState> EditorCommands => _editorCommands;

    /// <summary>The editor keyboard-shortcut owner (UX3-E): the ONE <c>EditorShortcuts</c> chord table
    /// (Undo/Redo/Delete/FrameScene/AddMenu) read through the raw keyboard, gated by the shared
    /// <c>ViewportShortcutContext</c>. Weave with the input-owner block, immediately AFTER
    /// <see cref="Menu"/> (registrar entry <c>editor.shortcuts</c>, <c>RunNormally</c>) so modality
    /// wins.</summary>
    public ISystem<GameState> Shortcuts => _shortcutSystem;

    /// <summary>The modal transform system (UX3-F): the Blender-style <c>G</c>/<c>S</c>/<c>R</c> modal
    /// transforms, entered by the shortcut table and driven by the <c>modal:*</c> ops. Weave the returned
    /// system as <c>editor.modal</c> with the input-owner block, immediately AFTER <see cref="Shortcuts"/>
    /// (which enters it) and BEFORE <see cref="Gizmo"/> + the draw pipeline's <see cref="Selection"/> —
    /// so its pointer-consume reaches them. <c>RunNormally</c> (it self-guards to Edit). Exposed concrete
    /// so the screen ORs <see cref="ModalTransformSystem.IsActive"/> into the host keyboard's
    /// <c>ShouldSuppressInput</c> and the status bar reads its <c>Readout</c>.</summary>
    public ModalTransformSystem Modal => _modal;

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

    /// <summary>The icon-button hover tooltip (UX2-C): the ONE pooled box + label on the Editor target,
    /// shown after a short hover. Weave as the LAST child of the <c>editor.toolbar</c> group (after
    /// <see cref="ToolbarClicks"/>, whose per-button hover clock it reads), <c>RunNormally</c>.</summary>
    public ISystem<GameState> Tooltip { get; }

    /// <summary>The viewport tab strip (PF-B): <c>[Scene] [▶ Game ×]</c> at the Scene header's start (the
    /// retired Scene/Game mode toggle's replacement). Weave in the <c>editor.toolbar</c> group alongside
    /// <see cref="ToolbarClicks"/> (order-independent — its tab bounds never overlap the transport row),
    /// <c>RunNormally</c> (live in both transport states — leaving the Game tab must work while Playing).</summary>
    public ISystem<GameState> ViewportTabs { get; }

    // The concrete panels: the headless panel:* ops drive their section/group/tree/inspector toggles
    // directly (like _editorCommands for the selection-edit ops).
    private readonly EditorPanelSystem _leftPanel;
    private readonly EditorPanelSystem _inspectorPanel;

    /// <summary>The LEFT-strip tabbed panel (UX2-B): <b>Entities</b> (the world's entities as a
    /// selectable parent/child tree), <b>Systems</b> (every bound registrar entry of both pipelines,
    /// groups indented + collapsible, tri-state group checkboxes, live enabled toggle), and
    /// <b>Scenes</b> (the scene catalog + project info). Weave after the <c>editor.toolbar</c> group
    /// (whose mesh prep bakes its checkbox meshes). The Systems tab requires <see cref="BindPipelines"/>;
    /// the Entities + Scenes tabs work immediately. Kept the <c>editor.systemsPanel</c> entry name +
    /// the <c>SystemsPanel</c> hook so every screen weaves it unchanged.</summary>
    public ISystem<GameState> SystemsPanel { get; }

    /// <summary>The RIGHT-strip dedicated Inspector panel (UX2-B): a slim title header over the
    /// selected entity's attached components, each expandable to its member values (the same pooled-row
    /// machinery as the left panel). Weave right after <see cref="SystemsPanel"/> as its own
    /// <c>editor.inspector</c> entry, <c>RunNormally</c>. A tree click in the left panel sets
    /// <c>SelectedComponent</c>, which this panel reads — two-way selection across the two panels.</summary>
    public ISystem<GameState> Inspector { get; }

    /// <summary>Whether the editable Inspector currently owns the keyboard (its filter field is focused or
    /// a member is being inline-edited — PF-A §3). The composing screen ORs this into the host keyboard
    /// system's <c>ShouldSuppressInput</c> (alongside <c>Dialog.IsOpen</c> / <c>Menu.IsOpen</c> /
    /// <c>Modal.IsActive</c>) so typing in an Inspector field never leaks to editor/game keys.</summary>
    public bool InspectorOwnsKeyboard => _inspectorPanel.OwnsKeyboard;

    /// <summary>The modal Save dialog + the confirm-on-switch modal (native-resolution chrome). Weave
    /// EARLY in the update pipeline — after the cursor input read, before the editing tools + toolbar
    /// (registrar entry <c>editor.dialog</c>, <c>RunNormally</c>) — so that while a dialog is open it
    /// consumes the cursor's pointer edges before any mouse-driven editor system reads them (the mouse
    /// half of the modal capture). The keyboard half is the screen wiring the host keyboard system's
    /// <c>ShouldSuppressInput</c> to <see cref="EditorDialogSystem.IsOpen"/>. Exposed concrete so the
    /// screen reads <c>Dialog.IsOpen</c> for that wiring and the headless <c>dialog:*</c> ops drive
    /// its public methods.</summary>
    public EditorDialogSystem Dialog { get; }

    /// <summary>The context-menu primitive (UX2-D §4): the viewport / Entities / Scenes / Entity-header
    /// popup menus. Weave immediately AFTER <see cref="Dialog"/> (registrar entry
    /// <c>editor.contextMenu</c>, <c>RunNormally</c>) so, in the rare case both could open, the dialog
    /// consumes the cursor first and wins. The screen ORs <c>Menu.IsOpen</c> into the host keyboard
    /// system's <c>ShouldSuppressInput</c> (with <c>Dialog.IsOpen</c>) so Escape closes the menu; the
    /// headless <c>menu:*</c> ops drive its public methods. Exposed concrete for that wiring.</summary>
    public EditorContextMenuSystem Menu { get; }

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

    /// <summary>The window status bar (UX3-F): the live modal readout / contextual status (left) + the
    /// scene id, view mode, and dirty dot (right), as pooled labels on the native Editor target. Weave as
    /// <c>editor.statusBar</c> after <see cref="Shell"/> (it lays out this frame's content for the chrome
    /// render pass), <c>RunNormally</c> — live in both transport states.</summary>
    public ISystem<GameState> StatusBar { get; }

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
    /// Sets the scene id the editor holds — the Game screen calls this in its <c>Load</c> from the
    /// requested level id, so Save targets that level's file (not the manifest default). Explicit
    /// per-screen scene ids are what kill the "all screens save to <c>startScene</c>" hazard. A blank
    /// id is ignored (the ctor-resolved default stands).
    /// </summary>
    public void SetSceneId(string? sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId)) return;
        _sceneId = sceneId!;
        Transport.SetSceneId(_sceneId); // keep the Scene tab's context id in step (PF-B)
    }

    /// <summary>
    /// Binds the Scenes-panel inputs (UX-C), called from the screen's <c>Load</c> once the screen name,
    /// the registered-screens enumeration, and the host switch hand-off are known:
    /// <paramref name="currentScreenName"/> identifies the running screen (for current-entry
    /// detection), <paramref name="registeredScreens"/> is the <see cref="ScreenController.RegisteredScreens"/>
    /// enumeration, and <paramref name="switchScene"/> is the host-supplied seam that actually performs a
    /// switch (Examples: set the requested level + <c>LoadScreen</c>; Demos: plain <c>LoadScreen</c>) — the
    /// editor module never references a game screen type, exactly like <see cref="EditorTransport.Reload"/>.
    /// </summary>
    public void BindSceneCatalog(
        string currentScreenName,
        Func<IReadOnlyList<(string Name, ScreenInfo Info)>> registeredScreens,
        Action<SceneCatalogEntry> switchScene)
    {
        _currentScreenName = currentScreenName;
        _registeredScreens = registeredScreens ?? throw new ArgumentNullException(nameof(registeredScreens));
        _switchScene = switchScene ?? throw new ArgumentNullException(nameof(switchScene));
    }

    /// <summary>Builds the current Scenes catalog from the bound inputs (empty until
    /// <see cref="BindSceneCatalog"/> runs). The module never reads the filesystem in the pure
    /// <see cref="SceneCatalog"/> — the overlay supplies the scene-id list via <see cref="ListSceneIds"/>
    /// (its existing desktop directory IO), gated on the project being resolved.</summary>
    private IReadOnlyList<SceneCatalogEntry> BuildCatalog()
    {
        if (_registeredScreens == null) return Array.Empty<SceneCatalogEntry>();
        var resolved = _projectContext is { Resolved: true };
        var sceneIds = resolved ? ListSceneIds() : Array.Empty<string>();
        return SceneCatalog.Build(_registeredScreens(), sceneIds, _currentScreenName, _sceneId, resolved);
    }

    /// <summary>The <c>.mdscene</c> ids under the project's levels dir, via desktop directory IO
    /// (<see cref="System.IO.Directory"/> — a desktop editor-UI concern that never runs on web; the pure
    /// <see cref="SceneCatalog"/> takes this as an injected list). Empty when unresolved or on any IO
    /// error (never throws — a listing failure yields an empty catalog rather than crashing).</summary>
    private IReadOnlyList<string> ListSceneIds()
    {
        var levelsPath = _projectContext?.LevelsPath;
        if (string.IsNullOrEmpty(levelsPath) || !Directory.Exists(levelsPath)) return Array.Empty<string>();
        try
        {
            var ids = new List<string>();
            foreach (var file in Directory.EnumerateFiles(levelsPath!))
                if (file.EndsWith(SceneWriter.SceneFileExtension, StringComparison.OrdinalIgnoreCase))
                    ids.Add(Path.GetFileNameWithoutExtension(file));
            return ids;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[level-editor] Could not list scenes under '{levelsPath}': {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The ONE place a scene switch is initiated (pre-mortem #7 — the dirty gate lives here, and both
    /// the Scenes-panel click and the <c>scenes:select</c> op route through it). Same entry → no-op;
    /// clean → the host <see cref="_switchScene"/> callback fires immediately; dirty → the confirm-switch
    /// modal opens ("Unsaved changes in &lt;scene&gt;"), whose Save &amp; Switch runs the SAME guarded
    /// <see cref="SaveCurrentScene"/> then switches, Discard switches without saving, and Cancel stays.
    /// </summary>
    public void SelectScene(SceneCatalogEntry entry, GameState state)
    {
        // PF-B: a scene switch while the Game tab is active LEAVES the Game tab first (full snapshot
        // restore), so the normal dirty gate below runs on the RESTORED real scene — not on sandbox churn.
        // One gate flavor, no bypass.
        if (Transport.ActiveContextKind == ViewportContextKind.Game) Transport.ExitToSceneMode(state);

        switch (SceneCatalog.DecideSwitch(entry, History.IsDirty))
        {
            case SceneSwitchDecision.NoOp:
                return; // clicking the active scene is a no-op
            case SceneSwitchDecision.Switch:
                if (SwitchGuardMissing()) return;
                _switchScene!(entry);
                return;
            case SceneSwitchDecision.Confirm:
                if (SwitchGuardMissing()) return;
                // Save & Switch runs the SAME guarded SaveCurrentScene then switches; Discard switches
                // without saving; Cancel (dialog close) does neither.
                Dialog.OpenConfirmSwitch(_sceneId,
                    onSaveAndSwitch: s => { SaveCurrentScene(s); _switchScene!(entry); },
                    onDiscardAndSwitch: _ => _switchScene!(entry));
                return;
        }
    }

    private bool SwitchGuardMissing()
    {
        if (_switchScene != null) return false;
        Logger.Warning(
            "[level-editor] Scene switch requested but no switch callback is bound " +
            "(call EditorOverlay.BindSceneCatalog in the screen's Load).");
        return true;
    }

    /// <summary>Finds the catalog entry whose <see cref="SceneCatalogEntry.Key"/> (or, as a fallback,
    /// <see cref="SceneCatalogEntry.Label"/>) matches — the <c>scenes:select &lt;key&gt;</c> lookup.</summary>
    private SceneCatalogEntry? FindCatalogEntry(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var entry in BuildCatalog())
            if (string.Equals(entry.Key, key, StringComparison.Ordinal) ||
                string.Equals(entry.Label, key, StringComparison.Ordinal))
                return entry;
        return null;
    }

    // ─── Context menus (UX2-D §4) ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the right context menu for <paramref name="context"/> — the ONE coordinator both the real
    /// right-click paths and the <c>menu:open</c> ops route through. <b>Viewport</b>: only in
    /// <see cref="EditorToolMode.SelectTransform"/>, picks the entity under the cursor with the SAME
    /// <see cref="SelectionSystem.TryPick"/> logic (empty → no menu), selects it, opens the entity menu
    /// at the cursor. <b>EntitiesPanel</b>: selects the row entity under the cursor (if any) and opens
    /// the Entities menu (its Order/Delete items enabled only when a row was hit). <b>ScenesPanel</b>:
    /// opens the Create-Empty-Scene menu. <b>EntityHeader</b>: opens the entity menu below the header
    /// <c>Entity ▾</c> button, acting on the current selection. Never opens while the dialog/menu is
    /// already open.
    /// </summary>
    public void OpenContextMenu(EditorMenuContext context, GameState state)
    {
        if (Dialog.IsOpen || Menu.IsOpen) return;
        switch (context)
        {
            case EditorMenuContext.Viewport:
                if (!InSelectTransform()) return; // armed → right-click disarms (palette/boundary), no menu
                if (!TryPickAtCursor(out var hit)) return; // empty viewport → no menu, no selection change
                _selection.SelectExclusive(hit);
                _menu.OpenAt(EditorContextMenuModel.EntityMenu(hasSelection: true, SelectionIsPrefabInstance()), CursorScreenPoint());
                break;
            case EditorMenuContext.EntitiesPanel:
                var row = _leftPanel.EntityAtPoint(CursorScreenPoint());
                if (row.IsAlive) _selection.SelectExclusive(row);
                _menu.OpenAt(EditorContextMenuModel.EntitiesPanelMenu(row.IsAlive), CursorScreenPoint());
                break;
            case EditorMenuContext.ScenesPanel:
                _menu.OpenAt(EditorContextMenuModel.ScenesPanelMenu(), CursorScreenPoint());
                break;
            case EditorMenuContext.EntityHeader:
                _menu.OpenBelow(EditorContextMenuModel.EntityMenu(HasSelection(), SelectionIsPrefabInstance()), EntityButtonBounds());
                break;
            case EditorMenuContext.AddAtCursor:
                // The Shift+A shortcut (UX3-E) + the menu:open add op: the Entities-panel ADD section
                // (Add Empty Entity now, more later), anchored at the cursor — no panel row to pick.
                _menu.OpenAt(EditorContextMenuModel.EntitiesPanelMenu(hasRowEntity: false), CursorScreenPoint());
                break;
            case EditorMenuContext.OverlaysHeader:
                // The Overlays dropdown (UX3-D): built from the live settings + shared grid step, and
                // rebuilt after each toggle so its check flips in place without closing the menu.
                _menu.OpenBelow(BuildOverlaysMenu(), OverlaysButtonBounds(), rebuild: BuildOverlaysMenu);
                break;
        }
    }

    /// <summary>Builds the Overlays dropdown model from the live settings + the shared grid quantum
    /// (UX3-D) — the rebuild hook the menu re-invokes after each toggle.</summary>
    private IReadOnlyList<EditorMenuItem> BuildOverlaysMenu()
    {
        var s = Settings;
        return EditorContextMenuModel.OverlaysMenu(s.ShowGrid, GridSpacing, s.OutlineSelected, s.ShowCameraGlyph);
    }

    /// <summary>The left panel's right-click handler (wired to <c>EditorPanelSystem.ContextMenuRequested</c>
    /// for the LEFT strip only): opens the Entities or Scenes context menu by the active tab (Systems tab
    /// = no menu).</summary>
    private void OpenLeftPanelContextMenu(GameState state)
    {
        switch (_shellState.ActiveLeftTab)
        {
            case EditorPanelTab.Entities: OpenContextMenu(EditorMenuContext.EntitiesPanel, state); break;
            case EditorPanelTab.Scenes: OpenContextMenu(EditorMenuContext.ScenesPanel, state); break;
        }
    }

    /// <summary>Maps a context-menu item's action-id <see cref="EditorMenuItem.Path"/> to the SAME shared
    /// editor instances (no second history / command system): Order → the within-band nudges, Delete →
    /// the snapshotting delete, Add Empty Entity → the undoable create, Create Empty Scene → the dialog.
    /// The menu supplies the frame's <see cref="GameState"/>; each command guards itself (Edit-only).</summary>
    /// <summary>The context-menu action-id prefix for an Inspector "+ Add component" candidate — the rest
    /// is the serializer-registry key (PF-A §3). Kept off the enum so the game stays out of it.</summary>
    private const string AddComponentPath = "add-component:";

    /// <summary>Opens the filterable "+ Add component" popup (PF-A §3): the selection's candidate
    /// components (registered − present − structural/never-addable) as command-palette items; a pick
    /// dispatches <c>add-component:&lt;key&gt;</c> back through <see cref="DispatchMenuAction"/>.</summary>
    private void OpenAddComponentPopup(GameState state)
    {
        var candidates = _inspectorPanel.AddComponentCandidates();
        if (candidates.Count == 0)
        {
            Logger.Info("[level-editor] Add component: the selection has no addable components.");
            return;
        }
        var items = new List<EditorMenuItem>(candidates.Count);
        foreach (var c in candidates)
            items.Add(new EditorMenuItem
            {
                Kind = EditorMenuItemKind.Action,
                Label = c.DisplayName,
                Path = AddComponentPath + c.Key,
            });
        _menu.OpenFiltered(items, CursorScreenPoint());
    }

    private void DispatchMenuAction(string path, GameState state)
    {
        // PF-A: an "add-component:<key>" pick from the filterable popup → the shared inspector panel.
        if (path.StartsWith(AddComponentPath, StringComparison.Ordinal))
        {
            _inspectorPanel.AddComponent(path.Substring(AddComponentPath.Length), state);
            return;
        }

        // PF-D: the per-card prefab actions carry the prefab id as a suffix.
        if (path.StartsWith(EditorContextMenuModel.PrefabEditPathPrefix, StringComparison.Ordinal))
        {
            OpenPrefabTab(path.Substring(EditorContextMenuModel.PrefabEditPathPrefix.Length), state);
            return;
        }
        if (path.StartsWith(EditorContextMenuModel.PrefabDeletePathPrefix, StringComparison.Ordinal))
        {
            RequestDeletePrefab(path.Substring(EditorContextMenuModel.PrefabDeletePathPrefix.Length), state);
            return;
        }

        switch (path)
        {
            case EditorContextMenuModel.OrderForwardPath: _editorCommands.BringForward(state); break;
            case EditorContextMenuModel.OrderBackPath: _editorCommands.SendBack(state); break;
            case EditorContextMenuModel.DeletePath: _editorCommands.DeleteSelection(state); break;
            case EditorContextMenuModel.AddEmptyPath: _editorCommands.AddEmptyEntity(state); break;
            case EditorContextMenuModel.CreateScenePath: Dialog.OpenCreateScene(); break;
            // PF-D prefab actions (entity menu + prefab shelf menu).
            case EditorContextMenuModel.CreatePrefabFromSelectionPath: OpenCreatePrefabFromSelectionDialog(state); break;
            case EditorContextMenuModel.UnpackPrefabPath: UnpackSelection(state); break;
            case EditorContextMenuModel.CreateEmptyPrefabPath: OpenCreateEmptyPrefabDialog(); break;
            default:
                // The Overlays dropdown paths (UX3-D): a toggle flips its setting, a spacing preset sets
                // the SHARED grid step (GizmoStateComponent.GridStep — the one grid quantum, so the grid
                // and the gizmo snap stay identical). The menu system rebuilds the model after a toggle.
                if (!ApplyOverlayMenuPath(path))
                    Logger.Warning($"[level-editor] Unknown context-menu action '{path}'.");
                break;
        }
    }

    /// <summary>
    /// Maps a matched <see cref="EditorShortcutAction"/> (UX3-E) to the SAME shared editor instances the
    /// toolbar/menu use — never a second history / command system / camera-nav / menu. Undo/Redo drive
    /// the shared <see cref="History"/>; Delete the snapshotting <see cref="EditorCommandSystem.DeleteSelection"/>;
    /// FrameScene the shared camera-nav <see cref="CameraNavSystem.FrameScene"/>; AddMenu the ONE
    /// <see cref="OpenContextMenu"/> coordinator (the Add section at the cursor). The shortcut system only
    /// calls this while its context gate allows (Edit, over the viewport, no dialog/menu).
    /// </summary>
    private void DispatchShortcut(EditorShortcutAction action, GameState state)
    {
        switch (action)
        {
            case EditorShortcutAction.Undo: History.Undo(); break;
            case EditorShortcutAction.Redo: History.Redo(); break;
            case EditorShortcutAction.Delete: _editorCommands.DeleteSelection(state); break;
            case EditorShortcutAction.FrameScene: _cameraNav.FrameScene(); break;
            case EditorShortcutAction.AddMenu: OpenContextMenu(EditorMenuContext.AddAtCursor, state); break;
            // UX3-F: G/S/R enter the modal transform over the selection (the modal then owns input).
            case EditorShortcutAction.ModalGrab: _modal.Enter(EditorModalMode.Grab, state); break;
            case EditorShortcutAction.ModalScale: _modal.Enter(EditorModalMode.Scale, state); break;
            case EditorShortcutAction.ModalRotate: _modal.Enter(EditorModalMode.Rotate, state); break;
        }
    }

    /// <summary>Routes an <c>overlay/*</c> menu path through the shared <see cref="ViewportOverlayOps"/>
    /// (the same field a spacing op writes). Returns false when the path is not an overlay path.</summary>
    private bool ApplyOverlayMenuPath(string path)
    {
        if (!_overlaySettings.IsAlive || !_gizmoState.IsAlive) return false;
        ref var settings = ref _overlaySettings.Get<ViewportOverlaySettingsComponent>();
        ref var gizmo = ref _gizmoState.Get<GizmoStateComponent>();
        return ViewportOverlayOps.TryApplyMenuPath(path, ref settings, ref gizmo);
    }

    /// <summary>The Create-Empty-Scene name-collision predicate the dialog uses to refuse an existing
    /// name (loud, stays open): true when <c>&lt;LevelsPath&gt;/&lt;id&gt;.mdscene</c> already exists.</summary>
    private bool SceneNameExists(string id)
    {
        var target = SceneFilePath(_projectContext, id);
        return !string.IsNullOrEmpty(target) && PlatformServices.Current.FileExists(target!);
    }

    /// <summary>
    /// Creates a minimal empty scene <paramref name="id"/> (UX2-D §4, the dialog's confirm callback):
    /// writes <b>empty <c>entities[]</c> + the current camera/layers</b> the writer emits for an empty
    /// world — through <see cref="SceneWriter"/> / <see cref="CanonicalJson"/>, never hand-written JSON —
    /// into <see cref="EditorProjectContext.LevelsPath"/>, applies the SAME zero-touch bundling a Save
    /// gets (<see cref="EnsureLevelBundled"/>), then switches to it through the NORMAL dirty-gated
    /// <see cref="SelectScene"/> flow (the catalog re-scan shows the new file immediately). Blocked when
    /// no project root is resolved (nowhere versioned to write). The dialog already refused an existing
    /// name upstream.
    /// </summary>
    private void CreateEmptyScene(string id, GameState state)
    {
        if (_projectContext is not { Resolved: true })
        {
            Logger.Warning(
                $"[level-editor] Create Empty Scene '{id}' is blocked: no project root resolved (no " +
                $"{GameProject.FileName} found).");
            return;
        }
        var target = SceneFilePath(_projectContext, id);
        if (string.IsNullOrEmpty(target)) return;

        // Build the empty scene from a throwaway empty world (zero SceneObjectComponent roots ⇒ empty
        // entities[]) with the current camera + the screen's layers — the canonical bytes for an empty
        // world — and write them through the shared serializer/writer.
        using var emptyWorld = new World();
        var writer = new SceneWriter(Serializer, PrefabSource);
        var scene = writer.BuildScene(emptyWorld, _cameraRig.AsCamera(), _layers);
        var savedPath = writer.Save(scene, target);
        if (savedPath == null) return;

        EnsureLevelBundled(id); // same treatment a Save gets — the new level bundles on the next build
        Logger.Info($"[level-editor] Created empty scene '{id}' at '{savedPath}'.");

        // Switch to the new scene through the ONE dirty-gated initiator (a re-scan surfaces the file).
        if (FindCatalogEntry(id) is { } entry) SelectScene(entry, state);
        else Logger.Warning(
            $"[level-editor] Created '{id}' but it did not surface in the scene catalog to switch to " +
            "(no scene-file host screen, or the catalog is not bound).");
    }

    // ─── Prefabs (PF-D) ──────────────────────────────────────────────────────────────────────────

    /// <summary>The <c>.mdprefab</c> ids under the project's <c>Prefabs</c> source dir (source-first, via
    /// desktop directory IO — the Prefabs shelf lister + the collision check). Empty when the project is
    /// unresolved (an unresolved context ⇒ an empty shelf + a message) or on any IO error.</summary>
    public IReadOnlyList<string> ListPrefabIds()
    {
        if (_projectContext is not { Resolved: true, ProjectRoot: { } root }) return Array.Empty<string>();
        var dir = Path.Combine(root, MgcbLevelBundle.PrefabsDirectoryName);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        try
        {
            var ids = new List<string>();
            foreach (var file in Directory.EnumerateFiles(dir))
                if (file.EndsWith(PrefabWriter.PrefabFileExtension, StringComparison.OrdinalIgnoreCase))
                    ids.Add(Path.GetFileNameWithoutExtension(file));
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[level-editor] Could not list prefabs under '{dir}': {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>The absolute source-tree path a prefab writes to: <c>&lt;ProjectRoot&gt;/Prefabs/&lt;id&gt;.mdprefab</c>,
    /// or null when the project is unresolved (matches <c>PrefabFileSource</c>'s source-first path).</summary>
    private string? PrefabFilePath(string prefabId) =>
        _projectContext is { Resolved: true, ProjectRoot: { } root }
            ? Path.Combine(root, MgcbLevelBundle.PrefabsDirectoryName, prefabId + PrefabWriter.PrefabFileExtension)
            : null;

    /// <summary>Whether a prefab id already has a source <c>.mdprefab</c> — the Create-Prefab collision
    /// predicate the name modal refuses on (loud, stays open).</summary>
    private bool PrefabNameExists(string prefabId)
    {
        var path = PrefabFilePath(prefabId);
        return !string.IsNullOrEmpty(path) && PlatformServices.Current.FileExists(path!);
    }

    /// <summary>The selected entity (the first <see cref="SelectedComponent"/>), or false when none.</summary>
    private bool TryGetSelectedRoot(out Entity root)
    {
        using var set = _world.GetEntities().With<SelectedComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.IsAlive) { root = e; return true; }
        root = default;
        return false;
    }

    /// <summary>Whether the current selection is a prefab instance ROOT (drives the entity menu's Unpack
    /// enabled state).</summary>
    private bool SelectionIsPrefabInstance() =>
        TryGetSelectedRoot(out var root) && root.Has<PrefabInstanceComponent>();

    private void ClearWorldSelection()
    {
        var toClear = new List<Entity>();
        using (var set = _world.GetEntities().With<SelectedComponent>().AsSet())
            foreach (var e in set.GetEntities()) toClear.Add(e);
        foreach (var e in toClear)
            if (e.IsAlive && e.Has<SelectedComponent>()) e.Remove<SelectedComponent>();
    }

    /// <summary>Whether the OPEN scene has any instance of <paramref name="prefabId"/> — the live world's
    /// <see cref="PrefabInstanceComponent"/> roots AND the backgrounded Scene context snapshot's compact
    /// <c>prefab</c> entries. The prefab-delete refusal reads it (loud) so a prefab in use is never deleted
    /// out from under its instances.</summary>
    private bool OpenSceneHasInstancesOf(string prefabId)
    {
        using (var set = _world.GetEntities().With<PrefabInstanceComponent>().AsSet())
            foreach (var e in set.GetEntities())
                if (e.Get<PrefabInstanceComponent>().PrefabId == prefabId) return true;

        var snapshot = Transport.ContextStack.SceneContext.Snapshot;
        if (snapshot != null)
            foreach (var entry in snapshot.Entities)
                if (string.Equals(entry.Prefab, prefabId, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>The current cursor's world position (for the <c>prefab:place</c> one-shot op).</summary>
    private Vector2 CursorWorldPoint()
    {
        using var set = _world.GetEntities().With<CursorInputComponent>().AsSet();
        foreach (var cursor in set.GetEntities())
            return cursor.Get<CursorInputComponent>().WorldPosition;
        return Vector2.Zero;
    }

    /// <summary>
    /// Places a <b>linked prefab instance</b> at <paramref name="worldPos"/> as one undoable
    /// <see cref="CreateInstanceCommand"/> (the ONE expansion path), then auto-selects the instance root.
    /// Used by the Prefabs shelf's click-place AND the <c>prefab:place</c> op. Loud no-op on an unknown /
    /// unresolved prefab.
    /// </summary>
    public void PlacePrefabInstance(string prefabId, Vector2 worldPos)
    {
        if (string.IsNullOrEmpty(prefabId)) return;
        // v1: placing a prefab instance INSIDE a prefab tab (nested-prefab authoring) is refused with a
        // hint — the PF-C expansion recursion exists but is not surfaced (terrain). Assemble prefabs from
        // scene selections instead.
        if (Transport.ActiveContextKind == ViewportContextKind.Prefab)
        {
            Logger.Warning(
                "[level-editor] Place prefab: nesting a prefab instance inside a prefab is not supported " +
                "yet (nested prefabs are terrain). Place it in a scene instead.");
            return;
        }
        PrefabData? data;
        try { data = PrefabSource(prefabId); }
        catch (Exception ex) { Logger.Warning($"[level-editor] Place prefab '{prefabId}' failed: {ex.Message}"); return; }
        if (data == null) { Logger.Warning($"[level-editor] Place prefab: no prefab '{prefabId}' resolved."); return; }

        var cmd = new CreateInstanceCommand(PrefabExpander, prefabId, worldPos, autoName: true);
        History.Push(cmd);
        ClearWorldSelection();
        if (cmd.Root.IsAlive) cmd.Root.Set(new SelectedComponent());
        var placedName = cmd.Root.IsAlive && cmd.Root.Has<EntityInfoComponent>()
            ? cmd.Root.Get<EntityInfoComponent>().Name : prefabId;
        Logger.Info($"[level-editor] Placed prefab instance '{prefabId}' at ({worldPos.X:0.##}, {worldPos.Y:0.##}).");
        Notifications.Notify($"Placed '{placedName}'", EditorNotifySeverity.Info);
    }

    /// <summary>Opens (or activates) a prefab tab (PF-D — the card's Edit / double-click / <c>prefab:edit</c>):
    /// resolves the prefab source-first and drives <see cref="EditorTransport.OpenPrefab"/>. Loud no-op on a
    /// missing / malformed prefab.</summary>
    public void OpenPrefabTab(string prefabId, GameState state)
    {
        PrefabData? data;
        try { data = PrefabSource(prefabId); }
        catch (Exception ex) { Logger.Warning($"[level-editor] Edit Prefab '{prefabId}' failed: {ex.Message}"); return; }
        if (data == null) { Logger.Warning($"[level-editor] Edit Prefab: no prefab '{prefabId}' resolved."); return; }
        Transport.OpenPrefab(prefabId, data.Scene, state);
    }

    /// <summary>
    /// Writes the active prefab context's world to its <c>.mdprefab</c> (PF-D — the Save-Prefab dialog's
    /// confirm and the <c>dialog:prefab</c> op): the PF-C validated writer (one-root + origin-normalize +
    /// no camera + cycle-refuse — a multi-root world / cycle is refused loud, keeping the dialog's intent),
    /// then bundle (zero-touch MGCB <c>/copy:</c>), mark the save point, and propagate. <b>No empty-save
    /// guard</b> — an empty prefab (its one root, nothing else) is LEGAL while assembling; the one-root
    /// validation is the only gate. <b>Propagation:</b> the LIVE world (the prefab world) never self-instances
    /// so <see cref="PrefabPropagation.ReExpand"/> rebuilds 0 and leaves the history clean; a backgrounded
    /// SCENE context re-expands on its next restore (its snapshot holds compact <c>prefab</c> entries, so the
    /// reader reads the just-saved prefab source-first) — no eager action needed.
    /// </summary>
    public void SavePrefabCurrent(GameState state)
    {
        if (Transport.ActiveContextKind != ViewportContextKind.Prefab)
        {
            Logger.Warning("[level-editor] Save Prefab: not in a prefab context.");
            return;
        }
        switch (SaveBlock(state, _projectContext, Transport.ActiveContextKind))
        {
            case SaveBlockReason.Playing:
                Logger.Warning("[level-editor] Save Prefab is blocked while Playing — pause first.");
                return;
            case SaveBlockReason.NoProjectRoot:
                Logger.Warning(
                    "[level-editor] Save Prefab is blocked: no project root resolved (no " +
                    $"{GameProject.FileName} found).");
                return;
        }

        var prefabId = Transport.ContextStack.Active.Id;
        var target = PrefabFilePath(prefabId);
        if (string.IsNullOrEmpty(target)) return;

        // Capture the OLD prefab (for the propagation override-diff) BEFORE the write overwrites the file.
        PrefabData? oldPrefab = null;
        try { oldPrefab = PrefabSource(prefabId); } catch { /* a malformed old file: skip the diff */ }

        SceneData prefabScene;
        try
        {
            prefabScene = new PrefabWriter(new SceneWriter(Serializer, PrefabSource))
                .BuildPrefab(_world, prefabId, PrefabSource);
        }
        catch (Exception ex)
        {
            Logger.Warning(
                $"[level-editor] Save Prefab '{prefabId}' refused: {ex.Message} " +
                "(a prefab needs exactly one root and no cycle).");
            return;
        }

        var savedPath = new SceneWriter(Serializer, PrefabSource).Save(prefabScene, target!);
        if (savedPath == null) return;

        History.MarkSavePoint();      // the prefab context is now clean
        EnsurePrefabBundled(prefabId);
        Palette?.RefreshPrefabs();    // keep the shelf in sync (a first save adds the card)

        // Live-world propagation (history-clear rule only when instances were rebuilt — the prefab world
        // has none, so this is a no-op here). Backgrounded scenes re-expand on restore (the chosen mechanism).
        if (oldPrefab != null)
            PrefabPropagation.ReExpand(_world, prefabId, oldPrefab, PrefabExpander, Registry, History);

        Logger.Info($"[level-editor] Saved prefab '{prefabId}' to '{savedPath}'.");
        Notifications.Notify($"Saved prefab '{prefabId}'", EditorNotifySeverity.Success);
    }

    /// <summary>Opens the Create-Prefab-from-Selection name modal (PF-D): refuses with no selection, else
    /// opens the dialog whose confirm runs <see cref="CreatePrefabFromSelection"/> (collision-refused).</summary>
    private void OpenCreatePrefabFromSelectionDialog(GameState state)
    {
        if (!HasSelection())
        {
            Logger.Warning("[level-editor] Create Prefab from Selection: nothing is selected.");
            return;
        }
        Dialog.OpenCreatePrefab("Create Prefab from Selection", "prefab", PrefabNameExists,
            (id, s) => CreatePrefabFromSelection(id, s));
    }

    /// <summary>
    /// Captures the current single-root selection into <c>Content/Prefabs/&lt;id&gt;.mdprefab</c>
    /// (origin-normalized) and <b>replaces the selection with a linked instance</b> preserving its world
    /// position — ONE undoable composite (Delete originals + CreateInstance). Undo restores the original
    /// entities and removes the instance; the FILE stays (a written prefab is durable). Multi-root / a
    /// prefab-owned child / an already-an-instance selection is refused loud (single-root only, v1). The
    /// file write + bundle happen once, before the composite.
    /// </summary>
    public void CreatePrefabFromSelection(string id, GameState state)
    {
        if (string.IsNullOrEmpty(id)) return;
        var target = PrefabFilePath(id);
        if (string.IsNullOrEmpty(target))
        {
            Logger.Warning($"[level-editor] Create Prefab '{id}' is blocked: no project root resolved.");
            return;
        }
        if (!TryGetSelectedRoot(out var root))
        {
            Logger.Warning("[level-editor] Create Prefab from Selection: nothing is selected.");
            Notifications.Notify("Create prefab: nothing is selected.", EditorNotifySeverity.Warning);
            return;
        }
        if (PrefabGuards.IsPrefabOwned(root))
        {
            Logger.Warning(PrefabGuards.Refusal("Create prefab"));
            Notifications.Notify("Create prefab: open the prefab or Unpack - can't capture a prefab child.",
                EditorNotifySeverity.Warning);
            return;
        }
        if (root.Has<PrefabInstanceComponent>())
        {
            Logger.Warning(
                "[level-editor] Create Prefab from Selection refused: the selection is already a prefab " +
                "instance — Unpack it first, or edit the prefab directly.");
            Notifications.Notify("Create prefab: the selection is already an instance - Unpack it first.",
                EditorNotifySeverity.Warning);
            return;
        }

        // Build + validate the prefab through the ONE shared capture helper (robust root-finding +
        // empty-capture refusal + naming). Refuse loud + status on an empty/invalid capture.
        var capture = PrefabCapture.Build(_world, root, id, Serializer, PrefabSource);
        if (!capture.Ok)
        {
            Logger.Warning($"[level-editor] Create Prefab '{id}' refused: {capture.Refusal}");
            Notifications.Notify(capture.Refusal!, EditorNotifySeverity.Danger);
            return;
        }

        var savedPath = new SceneWriter(Serializer, PrefabSource).Save(capture.Scene!, target!);
        if (savedPath == null) return;
        EnsurePrefabBundled(id);
        Palette?.RefreshPrefabs(); // the new card appears immediately (no relaunch)

        // Replace the selection with a linked instance at its world position — ONE undoable composite.
        var worldPos = root.Has<TransformComponent>() ? root.Get<TransformComponent>().Position : Vector2.Zero;
        var delete = new DeleteEntityCommand(_world, root, Serializer);
        var create = new CreateInstanceCommand(PrefabExpander, id, worldPos, autoName: true);
        History.Push(new CompositeCommand(new List<IEditorCommand> { delete, create }));

        ClearWorldSelection();
        if (create.Root.IsAlive) create.Root.Set(new SelectedComponent());
        Logger.Info(
            $"[level-editor] Created prefab '{id}' ({capture.EntityCount} entities) from selection; " +
            "replaced it with a linked instance.");
        Notifications.Notify($"Created prefab '{id}' ({capture.EntityCount} entities)",
            EditorNotifySeverity.Success);
    }

    /// <summary>Opens the Create-Empty-Prefab name modal (PF-D — the Prefabs shelf menu): confirm runs
    /// <see cref="CreateEmptyPrefab"/> (collision-refused).</summary>
    private void OpenCreateEmptyPrefabDialog() =>
        Dialog.OpenCreatePrefab("New Prefab", "prefab", PrefabNameExists, (id, s) => CreateEmptyPrefab(id, s));

    /// <summary>Writes a minimal valid <c>.mdprefab</c> — <b>one empty root entity at origin</b> (satisfies
    /// the one-root validation) — bundles it, then opens its tab to assemble from scratch (PF-D). Blocked
    /// when the project is unresolved.</summary>
    public void CreateEmptyPrefab(string id, GameState state)
    {
        if (string.IsNullOrEmpty(id)) return;
        var target = PrefabFilePath(id);
        if (string.IsNullOrEmpty(target))
        {
            Logger.Warning($"[level-editor] Create Empty Prefab '{id}' is blocked: no project root resolved.");
            return;
        }

        SceneData prefabScene;
        using (var tmp = new World())
        {
            var root = tmp.CreateEntity();
            root.Set(new SceneObjectComponent());
            root.Set(new TransformComponent(Vector2.Zero));
            // Name the empty root after the prefab (PF-F) so the tree reads the prefab id, not "Entity 1".
            root.Set(new EntityInfoComponent(id));
            prefabScene = new PrefabWriter(new SceneWriter(Serializer, PrefabSource)).BuildPrefab(tmp, id, PrefabSource);
        }

        var savedPath = new SceneWriter(Serializer, PrefabSource).Save(prefabScene, target!);
        if (savedPath == null) return;
        EnsurePrefabBundled(id);
        Palette?.RefreshPrefabs(); // the new card appears immediately (no relaunch)
        Logger.Info($"[level-editor] Created empty prefab '{id}' at '{savedPath}'.");
        Notifications.Notify($"Created empty prefab '{id}'", EditorNotifySeverity.Success);

        OpenPrefabTab(id, state); // resolves the just-written prefab source-first + opens its tab
    }

    /// <summary>Unpacks the selected prefab instance (PF-D — the entity menu's Unpack): pushes an undoable
    /// <see cref="UnpackPrefabCommand"/> (drop the marker; undo re-links). Loud no-op when the selection is
    /// not an instance root, or while Playing.</summary>
    public void UnpackSelection(GameState state)
    {
        if (state.RunMode != RunMode.Edit)
        {
            Logger.Warning("[level-editor] Unpack Prefab: pause first (editing is inert while Playing).");
            return;
        }
        if (!TryGetSelectedRoot(out var root) || !root.Has<PrefabInstanceComponent>())
        {
            Logger.Warning("[level-editor] Unpack Prefab: the selection is not a prefab instance.");
            return;
        }
        var prefabId = root.Get<PrefabInstanceComponent>().PrefabId;
        History.Push(new UnpackPrefabCommand(root));
        Logger.Info($"[level-editor] Unpacked prefab instance '{prefabId}' — its subtree is now ordinary scene entities.");
    }

    /// <summary>Requests deleting a prefab file (PF-D — the card's Delete): <b>refuses loud</b> if the open
    /// scene has instances of it (never delete a prefab in use), else opens the destructive confirm whose
    /// Delete runs <see cref="DeletePrefabFile"/>.</summary>
    public void RequestDeletePrefab(string prefabId, GameState state)
    {
        if (string.IsNullOrEmpty(prefabId)) return;
        if (OpenSceneHasInstancesOf(prefabId))
        {
            Logger.Warning(
                $"[level-editor] Delete Prefab '{prefabId}' refused: the open scene has instance(s) of it. " +
                "Unpack or remove them first.");
            return;
        }
        Dialog.OpenConfirmDelete(
            $"Delete prefab {prefabId}? This removes the {prefabId}{PrefabWriter.PrefabFileExtension} file.",
            _ => DeletePrefabFile(prefabId));
    }

    /// <summary>Deletes the prefab's source <c>.mdprefab</c> (desktop-editor-only file IO, like the scene
    /// listing). Not undoable (a file delete — the design's stance). The confirm + the instances-exist
    /// refusal gate it upstream.</summary>
    private void DeletePrefabFile(string prefabId)
    {
        var target = PrefabFilePath(prefabId);
        if (string.IsNullOrEmpty(target) || !PlatformServices.Current.FileExists(target!))
        {
            Logger.Warning($"[level-editor] Delete Prefab '{prefabId}': no source file to delete.");
            return;
        }
        try
        {
            File.Delete(target!);
            Palette?.RefreshPrefabs(); // the card disappears immediately
            Logger.Info($"[level-editor] Deleted prefab '{prefabId}' ('{target}'). Its MGCB /copy: entry (if any) is now dangling.");
            Notifications.Notify($"Deleted prefab '{prefabId}'", EditorNotifySeverity.Info);
        }
        catch (Exception ex)
        {
            Logger.Warning($"[level-editor] Delete Prefab '{prefabId}' failed: {ex.Message}");
            Notifications.Notify($"Delete prefab '{prefabId}' failed.", EditorNotifySeverity.Danger);
        }
    }

    /// <summary>Zero-touch prefab bundling (PF-D): appends the MGCB <c>/copy:./Prefabs/&lt;id&gt;.mdprefab</c>
    /// entry on the first Save of a new prefab id (idempotent), exactly like a new level bundles. Desktop-
    /// editor-only, gated on a resolved project root.</summary>
    private void EnsurePrefabBundled(string prefabId)
    {
        if (_projectContext is not { Resolved: true, ProjectRoot: { } root }) return;
        var mgcbPath = Path.Combine(root, MgcbLevelBundle.McgbFileName);
        if (!PlatformServices.Current.FileExists(mgcbPath))
        {
            Logger.Warning(
                $"[level-editor] Zero-touch prefab bundling skipped: no {MgcbLevelBundle.McgbFileName} at " +
                $"'{mgcbPath}'. Add '{MgcbLevelBundle.PrefabCopyLine(prefabId)}' by hand.");
            return;
        }
        var updated = MgcbLevelBundle.EnsurePrefabCopyEntry(
            PlatformServices.Current.ReadAllText(mgcbPath), prefabId, out var changed);
        if (!changed) return;
        PlatformServices.Current.WriteAllText(mgcbPath, updated);
        Logger.Info(
            $"[level-editor] Bundled new prefab '{prefabId}': appended '{MgcbLevelBundle.PrefabCopyLine(prefabId)}'.");
    }

    private bool InSelectTransform() =>
        _gizmoState.IsAlive &&
        _gizmoState.Get<GizmoStateComponent>().Mode == EditorToolMode.SelectTransform;

    private bool HasSelection()
    {
        using var set = _world.GetEntities().With<SelectedComponent>().AsSet();
        foreach (var _ in set.GetEntities()) return true;
        return false;
    }

    private Point CursorScreenPoint()
    {
        using var set = _world.GetEntities().With<CursorInputComponent>().AsSet();
        foreach (var cursor in set.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            return new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
        }
        return Point.Zero;
    }

    private bool TryPickAtCursor(out Entity hit)
    {
        using var set = _world.GetEntities().With<CursorInputComponent>().AsSet();
        foreach (var cursor in set.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            return _selection.TryPick(input.WorldPosition, input.VirtualPosition, out hit);
        }
        hit = default;
        return false;
    }

    private Rectangle EntityButtonBounds() => ButtonBounds(EditorToolbarAction.EntityMenu);

    /// <summary>The Scene-header Overlays button bounds (UX3-D) — the dropdown anchors below it.</summary>
    private Rectangle OverlaysButtonBounds() => ButtonBounds(EditorToolbarAction.Overlays);

    private Rectangle ButtonBounds(EditorToolbarAction action)
    {
        using var set = _world.GetEntities().With<ToolbarButtonComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<ToolbarButtonComponent>().Action == action)
                return e.Get<ToolbarButtonComponent>().Bounds;
        return Rectangle.Empty; // no such button on this screen → open at the origin (clamped)
    }

    /// <summary>The guarded Save the toolbar/dialog/switch all share: resolves the source path from the
    /// current scene id and writes through <see cref="SaveCurrentSceneTo"/> (which re-checks the Save
    /// guard + the empty-save guard and marks the history save point on success).</summary>
    public void SaveCurrentScene(GameState state)
    {
        var target = SceneFilePath(_projectContext, _sceneId);
        if (string.IsNullOrEmpty(target))
        {
            Logger.Warning(
                "[level-editor] Save is blocked: no project root resolved (no " +
                $"{GameProject.FileName} found).");
            return;
        }
        SaveCurrentSceneTo(target!, state);
    }

    /// <summary>
    /// The <b>Save Project</b> action (UX-D §4): v1 saves the current scene — the ONLY scene in memory —
    /// through the SAME guarded <see cref="SaveCurrentScene"/> path (source-tree write + zero-touch
    /// bundling + <see cref="EditorHistory.MarkSavePoint"/>). It is the terrain for multi-scene sessions;
    /// by construction it NEVER blanket-writes scenes that are not in memory (there is only one). When
    /// multi-scene sessions land, this iterates the in-memory scenes — never the on-disk set.
    /// </summary>
    public void SaveProject(GameState state) => SaveCurrentScene(state);

    /// <summary>
    /// The <b>Save Backup As…</b> action (UX-D §4): writes <c>&lt;backupName&gt;.mdscene</c> into
    /// <see cref="EditorProjectContext.LevelsPath"/> — WITHOUT rebinding the scene id
    /// (<see cref="SceneId"/> is unchanged), WITHOUT <see cref="EditorHistory.MarkSavePoint"/>, and
    /// WITHOUT <see cref="EnsureLevelBundled"/> (a backup is dangling by design — logged "not bundled")
    /// — then <b>reloads the bound scene from disk</b> via the transport's <see cref="EditorTransport.Restart"/>
    /// (teardown + screen-recorded reload + history clear ⇒ clean): the edits went to the backup file; the
    /// working scene returns to its on-disk truth. Obeys the SAME guards as Save (the <see cref="SaveBlock"/>
    /// Playing / no-project-root causes and the empty-save guard).
    /// </summary>
    public void SaveBackupAs(string backupName, GameState state)
    {
        switch (SaveBlock(state, _projectContext, Transport.ActiveContextKind))
        {
            case SaveBlockReason.Playing:
                Logger.Warning(
                    "[level-editor] Save Backup As is blocked while the transport is Playing — saving " +
                    "mid-simulation would bake transient run state into the backup. Pause first.");
                return;
            case SaveBlockReason.GameMode:
                Logger.Warning(
                    "[level-editor] Save Backup As is blocked in Game mode — the sandbox is not saved " +
                    "(its edits discard on exit). Return to Scene mode first.");
                return;
            case SaveBlockReason.NoProjectRoot:
                Logger.Warning(
                    "[level-editor] Save Backup As is blocked: no project root resolved (no " +
                    $"{GameProject.FileName} found).");
                return;
        }

        var id = EditorTextField.Sanitize(backupName);
        var target = SceneFilePath(_projectContext, id);
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target))
        {
            Logger.Warning(
                $"[level-editor] Save Backup As refused: '{backupName}' has no valid file id after sanitizing " +
                "(letters, digits, '-' and '_'), or the project is unresolved.");
            return;
        }

        // The empty-save guard applies to a backup too — nothing to back up if the world has no scene
        // content and none was loaded this session.
        if (EmptySaveRefused(CountSceneRoots(), _sceneReaderSystem.SceneWasLoaded))
        {
            Logger.Warning(
                $"[level-editor] Save Backup As refused for '{id}': the world has no scene content (zero " +
                "SceneObjectComponent roots) and no scene was loaded this session — nothing to back up.");
            return;
        }

        var writer = new SceneWriter(Serializer, PrefabSource);
        var scene = writer.BuildScene(_world, _cameraRig.AsCamera(), _layers);
        WarnIfNotShipReady(scene);
        var savedPath = writer.Save(scene, target);
        if (savedPath == null) return;

        // A backup is DANGLING by design: the scene id is NOT rebound, the save point is NOT marked
        // (the working scene is still dirty vs disk), and the MGCB /copy: line is NOT appended.
        Logger.Info(
            $"[level-editor] Backup written to '{savedPath}' (scene id stays '{_sceneId}'); not bundled — " +
            "a backup is dangling by design.");
        // Return the working scene to its on-disk truth: teardown + screen-recorded reload + history clear.
        Transport.Restart(state);
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
    /// for either cause and this guard closes the headless/dispatch path too) — and Save now OPENS the
    /// three-action Save dialog rather than writing immediately (there is no Load action; a scene is
    /// opened by selecting it in the Scenes panel). Undo/Redo drive <see cref="History"/>. Public so the
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
                // PF-D: in a PREFAB context, Save opens the Save-Prefab dialog (writes the .mdprefab). The
                // GameMode block cause can't apply here (a prefab tab is not the Game tab); Playing can't
                // either (Play is disabled in a prefab tab), but guard both defensively + NoProjectRoot.
                if (Transport.ActiveContextKind == ViewportContextKind.Prefab)
                {
                    var prefabBlock = SaveBlock(state, _projectContext, Transport.ActiveContextKind);
                    if (prefabBlock == SaveBlockReason.NoProjectRoot)
                    {
                        Logger.Warning(
                            "[level-editor] Save Prefab is blocked: no project root resolved (no " +
                            $"{GameProject.FileName} found). Set {EditorProjectContext.ProjectRootVariable}.");
                        return;
                    }
                    if (prefabBlock == SaveBlockReason.Playing)
                    {
                        Logger.Warning("[level-editor] Save Prefab is blocked while Playing — pause first.");
                        return;
                    }
                    Dialog.OpenSavePrefab(Transport.ContextStack.Active.Id, SavePrefabCurrent);
                    break;
                }
                // Open the Save dialog (name the scene, then confirm) rather than writing immediately.
                // Preserve the loud gate: when Save is blocked there is nothing to name, so log the
                // actionable cause and do NOT open (the toolbar already dims/deactivates the button for
                // either cause; this closes the headless/dispatch path). The confirm re-checks
                // (SaveCurrentScene) as defense-in-depth.
                switch (SaveBlock(state, _projectContext, Transport.ActiveContextKind))
                {
                    case SaveBlockReason.Playing:
                        Logger.Warning(
                            "[level-editor] Save is blocked while the transport is Playing — saving " +
                            "mid-simulation would bake transient run state into the scene. Pause first.");
                        return;
                    case SaveBlockReason.GameMode:
                        Logger.Warning(
                            "[level-editor] Save is blocked in Game mode — the sandbox is not saved (its " +
                            "edits discard on exit). Return to Scene mode to save the real scene.");
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
                {
                    Palette.Refresh();        // re-scan the asset drop folder
                    Palette.RefreshPrefabs(); // AND the prefab shelf (PF-F — one refresh action does both)
                    Notifications.Notify("Refreshed assets + prefabs", EditorNotifySeverity.Info);
                }
                break;
            // The Scene-header "Entity ▾" dropdown (UX2-D): open the entity menu below the button,
            // acting on the current selection (the discoverable twin of the viewport right-click).
            case EditorToolbarAction.EntityMenu: OpenContextMenu(EditorMenuContext.EntityHeader, state); break;
            // The Scene-header "Overlays" dropdown (UX3-D): open the viewport-overlays menu below it.
            case EditorToolbarAction.Overlays: OpenContextMenu(EditorMenuContext.OverlaysHeader, state); break;
            // The Scene-header nav-corner button (UX2-E): snap the free VIEW onto the authored camera rig
            // (Camera := rig). An editing action — the toolbar dims/suppresses it while Playing.
            case EditorToolbarAction.CameraView: _cameraRig.SnapViewToRig(); break;
            // (PF-B: the [Scene | Game] mode toggle retired — the viewport tab strip + the Play transport
            // button drive Scene/Game now; see the viewport-tab ops mode:/tab: in DispatchNamedAction.)
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
        switch (SaveBlock(state, _projectContext, Transport.ActiveContextKind))
        {
            case SaveBlockReason.Playing:
                Logger.Warning(
                    "[level-editor] Save is blocked while the transport is Playing — saving " +
                    "mid-simulation would bake transient run state into the scene. Pause first.");
                return;
            case SaveBlockReason.GameMode:
                Logger.Warning(
                    "[level-editor] Save is blocked in Game mode — the sandbox is not saved (its edits " +
                    "discard on exit). Return to Scene mode to save the real scene.");
                return;
            case SaveBlockReason.NoProjectRoot:
                Logger.Warning(
                    "[level-editor] Save is blocked: no project root resolved (no " +
                    $"{GameProject.FileName} found). Set {EditorProjectContext.ProjectRootVariable} " +
                    "in the run configuration, or run from a build output inside the project source tree.");
                return;
        }

        // Empty-save guard (UX-C §3.5, pre-mortem #4): refuse when the world has zero
        // SceneObjectComponent roots AND no scene was loaded into this world this session — "nothing to
        // save", so a mis-bound code-built screen can never blank a real level (regardless of whether the
        // target file already exists). A designer who deliberately emptied a LOADED scene may still save
        // it empty (SceneWasLoaded is the escape hatch).
        if (EmptySaveRefused(CountSceneRoots(), _sceneReaderSystem.SceneWasLoaded))
        {
            Logger.Warning(
                $"[level-editor] Save refused for '{_sceneId}': the world has no scene content (zero " +
                "SceneObjectComponent roots) and no scene was loaded this session — nothing to save. " +
                "Place or load something first.");
            return;
        }

        if (!string.IsNullOrEmpty(target) && PlatformServices.Current.FileExists(target))
            Logger.Info($"[level-editor] Overwriting existing scene '{_sceneId}' at '{target}'.");

        // Build once so the ship-readiness lint (PS6) can inspect the exact scene being written.
        var writer = new SceneWriter(Serializer, PrefabSource);
        var scene = writer.BuildScene(_world, _cameraRig.AsCamera(), _layers);
        WarnIfNotShipReady(scene);
        // Self-healing duplicate-id repair (PF-F): if the writer re-stamped colliding ids, surface it.
        if (writer.LastBuildDuplicateIdRestamps > 0)
            Notifications.Notify(
                $"Repaired {writer.LastBuildDuplicateIdRestamps} duplicate scene id(s) on save.",
                EditorNotifySeverity.Warning);
        var savedPath = writer.Save(scene, target);
        // Zero-touch bundling (PS6): append the MGCB /copy: entry for a NEW level so it bundles to the
        // title on the next build with no manual .mgcb edit (idempotent for existing ones). The copy
        // line is `./Levels/<id>.mdscene`, so it is only correct for a scene written directly into
        // LevelsPath — which Save always does now (SceneFilePath targets LevelsPath/<id>.mdscene). The
        // IsUnderLevelsRoot guard stays as defense-in-depth (a Save Backup As… writes into LevelsPath but
        // bundles nothing — it takes its own path, never this method).
        if (savedPath != null)
        {
            History.MarkSavePoint(); // the on-disk scene now matches the world → clean (dirty tracking)
            if (IsUnderLevelsRoot(target))
                EnsureLevelBundled(_sceneId);
            else
                Logger.Info(
                    $"[level-editor] Saved scene '{_sceneId}' to '{savedPath}' OUTSIDE the levels dir " +
                    $"('{_projectContext?.LevelsPath}') — not auto-bundled; move it under Content/Levels to ship it.");
            Logger.Info($"[level-editor] Saved scene '{_sceneId}' to '{savedPath}'.");
        }
    }

    /// <summary>The empty-save guard predicate (UX-C §3.5): refuse iff the world has zero scene roots
    /// AND no scene was loaded this session. Pure — a truth table the tests pin directly.</summary>
    public static bool EmptySaveRefused(int sceneRootCount, bool sceneWasLoaded) =>
        sceneRootCount == 0 && !sceneWasLoaded;

    /// <summary>The number of <see cref="SceneObjectComponent"/>-tagged scene roots in the world (the
    /// membership-closure seed set — what a Save would actually write).</summary>
    private int CountSceneRoots()
    {
        using var set = _world.GetEntities().With<SceneObjectComponent>().AsSet();
        var n = 0;
        foreach (var _ in set.GetEntities()) n++;
        return n;
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
    /// <see cref="IPlatformServices"/>; only reached from Save (and the UX2-D Create Empty Scene), both
    /// already gated on a resolved project root (<see cref="SaveBlock"/>). See <see cref="MgcbLevelBundle"/>.
    /// </summary>
    private void EnsureLevelBundled(string sceneId)
    {
        var ctx = _projectContext;
        if (ctx is not { Resolved: true } || string.IsNullOrEmpty(ctx.ProjectRoot)) return;

        var mgcbPath = Path.Combine(ctx.ProjectRoot!, MgcbLevelBundle.McgbFileName);
        if (!PlatformServices.Current.FileExists(mgcbPath))
        {
            Logger.Warning(
                $"[level-editor] Zero-touch bundling skipped: no {MgcbLevelBundle.McgbFileName} at " +
                $"'{mgcbPath}'. Add '{MgcbLevelBundle.CopyLine(sceneId)}' by hand so '{sceneId}' bundles to the title.");
            return;
        }

        var updated = MgcbLevelBundle.EnsureCopyEntry(PlatformServices.Current.ReadAllText(mgcbPath), sceneId, out var changed);
        if (!changed) return;

        PlatformServices.Current.WriteAllText(mgcbPath, updated);
        Logger.Info(
            $"[level-editor] Bundled new level '{sceneId}': appended '{MgcbLevelBundle.CopyLine(sceneId)}' to " +
            $"{MgcbLevelBundle.McgbFileName}. Rebuild to copy it into the title content.");
    }

    /// <summary>
    /// The save guard's distinguishable reasons: Save dispatches only while the transport is Paused
    /// (<see cref="RunMode.Edit"/>), on the <see cref="ViewportContextKind.Scene"/> tab, AND the project
    /// root is resolved. Pure — named by the save-guard premise and its test. The causes are reported
    /// separately so the toolbar/log can tell the user WHY Save is off. Precedence:
    /// <see cref="SaveBlockReason.Playing"/> (checked first) → <see cref="SaveBlockReason.GameMode"/>
    /// (the Game tab is active) → <see cref="SaveBlockReason.NoProjectRoot"/>. <paramref name="activeKind"/>
    /// defaults to <see cref="ViewportContextKind.Scene"/> so a Scene-tab call reads exactly as a
    /// Scene-mode call did before PF-B.
    /// </summary>
    public static SaveBlockReason SaveBlock(GameState state, EditorProjectContext? projectContext,
        ViewportContextKind activeKind = ViewportContextKind.Scene)
    {
        if (state.RunMode == RunMode.Play) return SaveBlockReason.Playing;
        if (activeKind == ViewportContextKind.Game) return SaveBlockReason.GameMode;
        if (projectContext is not { Resolved: true }) return SaveBlockReason.NoProjectRoot;
        return SaveBlockReason.None;
    }

    /// <summary>Whether Save is blocked for any reason (see <see cref="SaveBlock"/>).</summary>
    public static bool IsSaveBlocked(GameState state, EditorProjectContext? projectContext,
        ViewportContextKind activeKind = ViewportContextKind.Scene) =>
        SaveBlock(state, projectContext, activeKind) != SaveBlockReason.None;

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
    /// palette ghost, <c>panel:systems|entities</c> collapse a left-strip section (UX2-B: the
    /// <c>Inspector</c> section dissolved into the dedicated right panel — no toggle op),
    /// <c>panel:group &lt;name&gt;</c> collapses a pipeline group, <c>panel:inspect &lt;type&gt;</c>
    /// expands a component's member values in the Inspector panel, <c>panel:select &lt;name&gt;</c>
    /// selects a scene entity, <c>panel:tab &lt;entities|systems|scenes|assets&gt;</c> switches a
    /// region's active tab, <c>shell:right &lt;pt&gt;</c> / <c>shell:bottom &lt;pt&gt;</c> /
    /// <c>shell:left &lt;pt&gt;</c> resize a region (clamped),
    /// <c>scenes:select &lt;key&gt;</c> switches to a Scenes-panel entry (dirty-gated),
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
        const string scenesPrefix = "scenes:";
        const string menuPrefix = "menu:";
        const string viewPrefix = "view:";
        const string modePrefix = "mode:";
        const string tabPrefix = "tab:";
        const string modalPrefix = "modal:";
        const string inspectorPrefix = "inspector:";
        const string prefabsPrefix = "prefabs:"; // must be tested BEFORE prefabPrefix
        const string prefabPrefix = "prefab:";
        const string overlayPrefix = ViewportOverlayOps.OpPrefix;

        if (name.StartsWith(modalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            DispatchModalOp(name.Substring(modalPrefix.Length), name, state);
            return;
        }

        if (name.StartsWith(inspectorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            DispatchInspectorOp(name.Substring(inspectorPrefix.Length), name, state);
            return;
        }

        if (name.StartsWith(prefabsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // prefabs:list — log the prefab shelf's ids (headless twin of the Prefabs tab listing).
            var verb = name.Substring(prefabsPrefix.Length).Trim();
            if (string.Equals(verb, "list", StringComparison.OrdinalIgnoreCase))
                Logger.Info($"[level-editor] Prefabs: [{string.Join(", ", ListPrefabIds())}]");
            else
                Logger.Warning($"[level-editor] Editor-op '{name}': expected prefabs:list.");
            return;
        }

        if (name.StartsWith(prefabPrefix, StringComparison.OrdinalIgnoreCase))
        {
            DispatchPrefabOp(name.Substring(prefabPrefix.Length), name, state);
            return;
        }

        if (name.StartsWith(overlayPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // overlay:grid on|off / overlay:outline on|off / overlay:camera on|off / overlay:spacing <n>
            // — the headless twin of the Overlays dropdown. Spacing writes the SHARED grid step, so the
            // displayed grid stays the grid things snap to.
            if (_overlaySettings.IsAlive && _gizmoState.IsAlive)
            {
                ref var settings = ref _overlaySettings.Get<ViewportOverlaySettingsComponent>();
                ref var gizmo = ref _gizmoState.Get<GizmoStateComponent>();
                if (!ViewportOverlayOps.TryApplyOp(name, ref settings, ref gizmo))
                    Logger.Warning(
                        $"[level-editor] Editor-op '{name}': expected overlay:grid on|off / " +
                        "overlay:outline on|off / overlay:camera on|off / overlay:spacing <n>.");
            }
            return;
        }

        if (name.StartsWith(viewPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // view:camera — snap the free editor VIEW onto the authored camera rig (UX2-E), the headless
            // twin of the Scene-header nav-corner button. view:frame — centre + zoom-fit the VIEW on all
            // content (UX3-E), the headless twin of the Home shortcut (both call the shared CameraNav).
            var verb = name.Substring(viewPrefix.Length).Trim();
            if (string.Equals(verb, "camera", StringComparison.OrdinalIgnoreCase))
                DispatchToolbarAction(EditorToolbarAction.CameraView, state);
            else if (string.Equals(verb, "frame", StringComparison.OrdinalIgnoreCase))
                _cameraNav.FrameScene();
            else
                Logger.Warning($"[level-editor] Editor-op '{name}': expected view:camera or view:frame.");
            return;
        }

        if (name.StartsWith(tabPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // tab:scene | tab:game | tab:close <id> — the viewport tab strip's headless twin (PF-B). The
            // dirty-close gate lives in Transport.CloseTab, identically to the tab's × click.
            DispatchViewportTabOp(name.Substring(tabPrefix.Length), name, state);
            return;
        }

        if (name.StartsWith(modePrefix, StringComparison.OrdinalIgnoreCase))
        {
            // mode:scene | mode:game — the retired [Scene | Game] mode toggle's ops, kept as tab aliases
            // (the headless suite keeps passing). They forward to the SAME tab ops: mode:scene =
            // tab:scene (leave the Game tab → Scene), mode:game = tab:game (spawn the Game tab + play).
            var verb = name.Substring(modePrefix.Length).Trim();
            if (string.Equals(verb, "scene", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(verb, "game", StringComparison.OrdinalIgnoreCase))
                DispatchViewportTabOp(verb, name, state);
            else
                Logger.Warning($"[level-editor] Editor-op '{name}': expected mode:scene or mode:game.");
            return;
        }

        if (name.StartsWith(menuPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // menu:open <viewport|entities|scenes|entity> | menu:pick <path> | menu:close
            var rest = name.Substring(menuPrefix.Length);
            var space = rest.IndexOf(' ');
            var verb = space < 0 ? rest : rest.Substring(0, space);
            var arg = space < 0 ? string.Empty : rest.Substring(space + 1);
            switch (verb.ToLowerInvariant())
            {
                case "open": OpenMenuByName(arg, name, state); break;
                case "pick": _menu.Pick(arg, state); break;
                case "close": _menu.Close(); break;
                default:
                    Logger.Warning(
                        $"[level-editor] Editor-op '{name}': expected " +
                        "menu:open <viewport|entities|scenes|entity>|pick <path>|close.");
                    break;
            }
            return;
        }

        if (name.StartsWith(scenesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // scenes:select <entryKey> — switch to the catalog entry with that key (the dirty gate +
            // confirm-on-switch is applied in SelectScene, identically to a Scenes-panel row click).
            var rest = name.Substring(scenesPrefix.Length);
            var space = rest.IndexOf(' ');
            var verb = space < 0 ? rest : rest.Substring(0, space);
            var arg = space < 0 ? string.Empty : rest.Substring(space + 1);
            if (string.Equals(verb, "select", StringComparison.OrdinalIgnoreCase))
            {
                if (FindCatalogEntry(arg) is { } entry) SelectScene(entry, state);
                else Logger.Warning($"[level-editor] Editor-op '{name}': no scene catalog entry '{arg}'.");
            }
            else
            {
                Logger.Warning($"[level-editor] Editor-op '{name}': expected scenes:select <entryKey>.");
            }
            return;
        }

        if (name.StartsWith(panelPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // panel:systems|entities (toggle a section), panel:group <fullName> (toggle a pipeline
            // group's children), panel:inspect <typeName> (toggle a component's members in the
            // Inspector panel), panel:select <entityName> (select a scene entity by its EntityInfo
            // name/type). UX2-B: the Inspector section dissolved into the dedicated right panel, so
            // panel:inspector was removed (the Inspector is always shown — nothing to toggle).
            var rest = name.Substring(panelPrefix.Length);
            var space = rest.IndexOf(' ');
            var verb = space < 0 ? rest : rest.Substring(0, space);
            var arg = space < 0 ? string.Empty : rest.Substring(space + 1);
            switch (verb.ToLowerInvariant())
            {
                case "tab": SetPanelTab(arg, name); break;
                case "systems": _leftPanel.ToggleSection(EditorPanelSection.Systems); break;
                case "entities": _leftPanel.ToggleSection(EditorPanelSection.Entities); break;
                case "group": _leftPanel.ToggleGroupCollapsed(arg); break;
                case "inspect": _inspectorPanel.ToggleInspectorComponentKey(arg); break;
                case "select":
                    if (!_leftPanel.SelectEntityByName(arg))
                        Logger.Warning($"[level-editor] Editor-op '{name}': no scene entity named '{arg}'.");
                    break;
                default:
                    Logger.Warning(
                        $"[level-editor] Editor-op '{name}': expected " +
                        "panel:tab <entities|systems|scenes|assets>|systems|entities|group <name>|inspect <type>|select <name>.");
                    break;
            }
            return;
        }

        if (name.StartsWith(shellPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // shell:left <pt> | shell:right <pt> | shell:bottom <pt> — resize a region (clamped by the
            // shell state).
            var rest = name.Substring(shellPrefix.Length);
            var space = rest.IndexOf(' ');
            var verb = space < 0 ? rest : rest.Substring(0, space);
            var arg = space < 0 ? string.Empty : rest.Substring(space + 1);
            if (!int.TryParse(arg, out var pt))
            {
                Logger.Warning($"[level-editor] Editor-op '{name}': expected shell:left|right|bottom <pt>.");
                return;
            }
            switch (verb.ToLowerInvariant())
            {
                case "left": _shellState.LeftWidthPt = pt; break;
                case "right": _shellState.RightWidthPt = pt; break;
                case "bottom": _shellState.BottomHeightPt = pt; break;
                default:
                    Logger.Warning($"[level-editor] Editor-op '{name}': expected shell:left|right|bottom <pt>.");
                    break;
            }
            return;
        }

        if (name.StartsWith(dialogPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // dialog:save-open | scene | project | name <text> | backup <name> | confirm | discard | cancel
            var rest = name.Substring(dialogPrefix.Length);
            var space = rest.IndexOf(' ');
            var verb = space < 0 ? rest : rest.Substring(0, space);
            var arg = space < 0 ? string.Empty : rest.Substring(space + 1);
            switch (verb.ToLowerInvariant())
            {
                case "save-open": Dialog.OpenSave(_sceneId); break;
                case "scene": Dialog.SaveScene(state); break;
                case "project": Dialog.SaveProject(state); break;
                case "name": Dialog.SetName(arg); break;
                case "backup": Dialog.Backup(arg, state); break;   // one-shot: arm + set name + confirm
                case "confirm": Dialog.Confirm(state); break;      // the focused/default action
                case "discard": Dialog.Discard(state); break;
                case "cancel": Dialog.Cancel(); break;
                default:
                    Logger.Warning(
                        $"[level-editor] Editor-op '{name}': expected " +
                        "dialog:save-open|scene|project|name <text>|backup <name>|confirm|discard|cancel.");
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

    /// <summary>The <c>panel:tab &lt;entities|systems|scenes|assets&gt;</c> op — switches the left
    /// strip's active tab, or (assets) the bottom shelf's.</summary>
    private void SetPanelTab(string arg, string name)
    {
        switch (arg.Trim().ToLowerInvariant())
        {
            case "entities": _leftPanel.SetActiveTab(EditorPanelTab.Entities); break;
            case "systems": _leftPanel.SetActiveTab(EditorPanelTab.Systems); break;
            case "scenes": _leftPanel.SetActiveTab(EditorPanelTab.Scenes); break;
            case "assets": _shellState.ActiveBottomTab = EditorBottomTab.Assets; break;
            case "prefabs": _shellState.ActiveBottomTab = EditorBottomTab.Prefabs; break;
            default:
                Logger.Warning(
                    $"[level-editor] Editor-op '{name}': expected panel:tab <entities|systems|scenes|assets|prefabs>.");
                break;
        }
    }

    /// <summary>The <c>menu:open &lt;viewport|entities|scenes|entity&gt;</c> op — opens the named context
    /// menu through the ONE <see cref="OpenContextMenu"/> coordinator (viewport / the Entity header use
    /// the current cursor position; the panel menus read the row under the cursor).</summary>
    private void OpenMenuByName(string arg, string name, GameState state)
    {
        switch (arg.Trim().ToLowerInvariant())
        {
            case "viewport": OpenContextMenu(EditorMenuContext.Viewport, state); break;
            case "entities": OpenContextMenu(EditorMenuContext.EntitiesPanel, state); break;
            case "scenes": OpenContextMenu(EditorMenuContext.ScenesPanel, state); break;
            case "entity": OpenContextMenu(EditorMenuContext.EntityHeader, state); break;
            case "overlays": OpenContextMenu(EditorMenuContext.OverlaysHeader, state); break;
            case "add": OpenContextMenu(EditorMenuContext.AddAtCursor, state); break; // UX3-E: the Shift+A twin
            default:
                Logger.Warning(
                    $"[level-editor] Editor-op '{name}': expected menu:open <viewport|entities|scenes|entity|overlays|add>.");
                break;
        }
    }

    /// <summary>The <c>tab:*</c> / <c>mode:*</c> viewport-tab ops (PF-B): <c>tab:game</c> (= <c>mode:game</c>)
    /// spawns + activates the Game tab and auto-plays (<see cref="EditorTransport.Play"/> — the SAME
    /// composition the Play button uses); <c>tab:scene</c> (= <c>mode:scene</c>) leaves the Game tab back
    /// to the Scene tab (<see cref="EditorTransport.ExitToSceneMode"/>); <c>tab:close &lt;id&gt;</c> closes
    /// the named tab through the dirty-close gate (<see cref="EditorTransport.CloseTab"/> — the × click's
    /// headless twin). All route through the transport, so RunMode + the stack stay one owner.</summary>
    private void DispatchViewportTabOp(string rest, string name, GameState state)
    {
        rest = rest.Trim();
        var space = rest.IndexOf(' ');
        var verb = (space < 0 ? rest : rest.Substring(0, space)).Trim();
        var arg = space < 0 ? string.Empty : rest.Substring(space + 1).Trim();
        switch (verb.ToLowerInvariant())
        {
            case "scene": Transport.ExitToSceneMode(state); break;
            case "game": Transport.Play(state); break;
            case "close":
                var index = Transport.ContextStack.IndexOfId(arg);
                if (index >= 0) Transport.CloseTab(index, state);
                else Logger.Warning($"[level-editor] Editor-op '{name}': no viewport tab '{arg}' to close.");
                break;
            default:
                Logger.Warning(
                    $"[level-editor] Editor-op '{name}': expected tab:scene | tab:game | tab:close <id> " +
                    "(or the mode:scene / mode:game aliases).");
                break;
        }
    }

    /// <summary>The <c>prefab:*</c> ops (PF-D — the headless twin of the prefab UX): <c>prefab:edit &lt;id&gt;</c>
    /// opens its tab; <c>prefab:place &lt;id&gt;</c> stamps a linked instance at the cursor (one undoable
    /// command); <c>prefab:unpack</c> unpacks the selected instance; <c>prefab:delete &lt;id&gt;</c> routes
    /// the delete (instances-exist refusal + confirm); <c>prefab:create-from-selection &lt;name&gt;</c> and
    /// <c>prefab:create-empty &lt;name&gt;</c> run those flows directly with the given (sanitized) name,
    /// bypassing the name modal. All drive the SAME shared instances the menus/dialogs do.</summary>
    private void DispatchPrefabOp(string rest, string name, GameState state)
    {
        rest = rest.Trim();
        var space = rest.IndexOf(' ');
        var verb = (space < 0 ? rest : rest.Substring(0, space)).Trim().ToLowerInvariant();
        var arg = space < 0 ? string.Empty : rest.Substring(space + 1).Trim();
        switch (verb)
        {
            case "edit": OpenPrefabTab(arg, state); break;
            case "place": PlacePrefabInstance(arg, CursorWorldPoint()); break;
            case "unpack": UnpackSelection(state); break;
            case "delete": RequestDeletePrefab(arg, state); break;
            case "create-from-selection": CreatePrefabFromSelection(EditorTextField.Sanitize(arg), state); break;
            case "create-empty": CreateEmptyPrefab(EditorTextField.Sanitize(arg), state); break;
            default:
                Logger.Warning(
                    $"[level-editor] Editor-op '{name}': expected prefab:edit <id> | place <id> | unpack | " +
                    "delete <id> | create-from-selection <name> | create-empty <name>.");
                break;
        }
    }

    /// <summary>The <c>modal:*</c> ops (UX3-F): the headless twin of the G/S/R modal flow — enter
    /// (<c>grab</c>/<c>scale</c>/<c>rotate</c>), <c>axis x|y</c>, <c>digits &lt;text&gt;</c>,
    /// <c>cursor &lt;dx&gt; &lt;dy&gt;</c> (motion from the entry cursor), and <c>confirm</c>/<c>cancel</c>
    /// — all routed to the SAME shared <see cref="Modal"/> instance the keyboard/mouse path drives.</summary>
    private void DispatchModalOp(string rest, string name, GameState state)
    {
        var space = rest.IndexOf(' ');
        var verb = (space < 0 ? rest : rest.Substring(0, space)).Trim().ToLowerInvariant();
        var arg = space < 0 ? string.Empty : rest.Substring(space + 1).Trim();
        switch (verb)
        {
            case "grab": _modal.Enter(EditorModalMode.Grab, state); break;
            case "scale": _modal.Enter(EditorModalMode.Scale, state); break;
            case "rotate": _modal.Enter(EditorModalMode.Rotate, state); break;
            case "axis":
                if (string.Equals(arg, "x", StringComparison.OrdinalIgnoreCase)) _modal.SetAxis(ModalAxis.X);
                else if (string.Equals(arg, "y", StringComparison.OrdinalIgnoreCase)) _modal.SetAxis(ModalAxis.Y);
                else Logger.Warning($"[level-editor] Editor-op '{name}': expected modal:axis x|y.");
                break;
            case "digits": _modal.TypeDigits(arg); break;
            case "cursor":
                var parts = arg.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var dx)
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dy))
                    _modal.OpCursor(dx, dy);
                else Logger.Warning($"[level-editor] Editor-op '{name}': expected modal:cursor <dx> <dy>.");
                break;
            case "confirm": _modal.Confirm(state); break;
            case "cancel": _modal.Cancel(state); break;
            default:
                Logger.Warning(
                    $"[level-editor] Editor-op '{name}': expected " +
                    "modal:grab|scale|rotate|axis x|y|digits <text>|cursor <dx> <dy>|confirm|cancel.");
                break;
        }
    }

    /// <summary>The <c>inspector:*</c> ops (PF-A §3): the headless twin of the DevTools Inspector —
    /// <c>filter &lt;text&gt;</c> narrows the rows, <c>edit &lt;Component.Member&gt; &lt;value&gt;</c>
    /// value-edits a member (the component is a registry key OR a type name; the member is the LAST
    /// dotted segment, so <c>core.Transform.Position</c> works), <c>add &lt;ComponentKey&gt;</c> /
    /// <c>remove &lt;ComponentKey&gt;</c> add/remove a component — all routed to the SAME shared inspector
    /// panel the mouse/keyboard drive, so the whole surface is headless-testable through one grammar.</summary>
    private void DispatchInspectorOp(string rest, string name, GameState state)
    {
        var space = rest.IndexOf(' ');
        var verb = (space < 0 ? rest : rest.Substring(0, space)).Trim().ToLowerInvariant();
        var arg = space < 0 ? string.Empty : rest.Substring(space + 1);
        switch (verb)
        {
            case "filter":
                _inspectorPanel.SetInspectorFilter(arg);
                break;
            case "add":
                _inspectorPanel.AddComponent(arg.Trim(), state);
                break;
            case "remove":
                _inspectorPanel.RemoveComponent(arg.Trim(), state);
                break;
            case "edit":
                // arg = "<Component.Member> <value>" — split target from value on the FIRST space (the
                // value may itself contain a space, e.g. a Vector2 "x, y").
                var sp = arg.IndexOf(' ');
                if (sp < 0)
                {
                    Logger.Warning($"[level-editor] Editor-op '{name}': expected inspector:edit <Component.Member> <value>.");
                    break;
                }
                var target = arg.Substring(0, sp);
                var value = arg.Substring(sp + 1);
                // The member is the LAST dotted segment (a registry key like "core.Transform" has a dot).
                var dot = target.LastIndexOf('.');
                if (dot <= 0 || dot >= target.Length - 1)
                {
                    Logger.Warning($"[level-editor] Editor-op '{name}': expected inspector:edit <Component.Member> <value>.");
                    break;
                }
                _inspectorPanel.EditMember(target.Substring(0, dot), target.Substring(dot + 1), value, state);
                break;
            default:
                Logger.Warning(
                    $"[level-editor] Editor-op '{name}': expected " +
                    "inspector:filter <text>|edit <Component.Member> <value>|add <key>|remove <key>.");
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

/// <summary>Which context menu <see cref="EditorOverlay.OpenContextMenu"/> opens (UX2-D §4) — the four
/// surfaces that share the two entity-menu anchors + the two panel menus.</summary>
public enum EditorMenuContext
{
    /// <summary>The game viewport right-click (SelectTransform + a hit): the entity menu at the cursor.</summary>
    Viewport,
    /// <summary>The left panel's Entities tab right-click: Add Empty Entity (+ the row entity's items).</summary>
    EntitiesPanel,
    /// <summary>The left panel's Scenes tab right-click: Create Empty Scene….</summary>
    ScenesPanel,
    /// <summary>The Scene-header <c>Entity ▾</c> dropdown: the entity menu below the button.</summary>
    EntityHeader,
    /// <summary>The Scene-header <c>Overlays</c> dropdown (UX3-D): the viewport-overlays menu below the
    /// button (Grid / Grid Spacing ▸ / Outline Selected / Camera).</summary>
    OverlaysHeader,
    /// <summary>The <c>Shift+A</c> Add shortcut (UX3-E) + the <c>menu:open add</c> op: the Entities-panel
    /// ADD section, anchored at the cursor.</summary>
    AddAtCursor,
}

/// <summary>Why the editor's Save is disabled — reported by <see cref="EditorOverlay.SaveBlock"/> so
/// the toolbar can dim the right button and the log can name the cause.</summary>
public enum SaveBlockReason
{
    /// <summary>Save is allowed (Paused + Scene mode + a resolved project root).</summary>
    None,
    /// <summary>Blocked because the transport is Playing — a mid-simulation save would bake transient
    /// run state into the scene.</summary>
    Playing,
    /// <summary>Blocked because the editor is in the Game-mode sandbox (UX2-F) — sandbox edits are
    /// expressly not-to-be-saved (they discard on exit); Save reflects the real scene, not the sandbox.</summary>
    GameMode,
    /// <summary>Blocked because no project root resolved (shipped build / relocated output / console /
    /// no <c>game.mdproj</c>) — there is nowhere versioned to write.</summary>
    NoProjectRoot,
}
