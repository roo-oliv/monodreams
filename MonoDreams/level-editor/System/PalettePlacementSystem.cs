#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Brush;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The editor's <b>asset palette + placement</b> system (island-authoring plan §3, adopting the
/// wave-repass Wave-C design): the shell's bottom strip lists every <see cref="AssetCatalog"/>
/// entry as a native-resolution button — a lazy-loaded art <b>thumbnail</b> (Slice 4) beside its
/// text label, the label alone when the texture is missing/magenta — (grouped by folder in the
/// sort; wheel-over-strip scrolls whole rows) beside a <b>layer-band selector</b> built from the
/// screen-supplied
/// <see cref="PaletteBand"/>s. Clicking an item <b>arms</b> it — the shared
/// <see cref="GizmoStateComponent.Mode"/> flips to <see cref="EditorToolMode.Place"/> (selection
/// and the transform gizmo go dormant, §S1) and a semi-transparent <b>ghost</b> preview follows
/// the cursor's world position (snap-quantized via the shared snap settings; hidden while the
/// pointer is outside the game viewport). A viewport click <b>places</b>: one
/// <see cref="CreateEntityCommand"/> wrapping <see cref="SpritePropFactory"/> (one undo step,
/// auto-tagged <c>SceneObjectComponent</c>, sub-graph snapshot — all existing machinery), and the
/// placed entity lands auto-selected. Repeated clicks keep placing; <b>Escape or right-click
/// disarms</b> back to <see cref="EditorToolMode.SelectTransform"/> (as does picking a transform
/// tool on the toolbar — the radio).
///
/// <para><b>Chrome, native pixels.</b> The strip rows are ordinary chrome entities on
/// <c>RenderTargetID.Editor</c> (fill+outline <see cref="SimpleButtonComponent"/> meshes prepped by
/// the already-woven <c>ButtonMeshPrepSystem</c>; labels at
/// <see cref="EditorChromeBuilder.LabelScale"/>), laid out by the pure <see cref="PaletteLayout"/>
/// inside <see cref="EditorChromeLayout.BottomBar"/> and hit-tested against the cursor's raw
/// <c>ScreenPosition</c>. Per the chrome rule they carry <b>no</b> <c>VisibleComponent</c> and no
/// <c>ToolbarButtonComponent</c> (<c>ToolbarSystem</c> must not hit-test them). Scrolled-out rows
/// park off-screen (<see cref="SystemsPanelLayout.ParkedPosition"/>) with their labels blanked.</para>
///
/// <para><b>The ghost is editor infrastructure, never scene content.</b> It carries
/// <see cref="EditorInfrastructureComponent"/> (survives a transport Restart) and never
/// <c>SceneObjectComponent</c>, so it cannot be saved, deleted-with-undo, or swept as scene
/// content; it despawns on disarm / mode exit. It is a plain Main-target sprite, so the REAL
/// pipeline previews it exactly as the placed prop will render (culling manages its
/// <c>VisibleComponent</c>).</para>
///
/// <para><b>Edit-guarded.</b> Like selection/gizmo, the palette is an editing tool: while the
/// transport is Playing it neither hit-tests nor places, its item buttons render with the disabled
/// fill, and the ghost despawns (the armed selection is kept, so pausing resumes where you were).
/// Weave AFTER <c>CursorPositionSystem</c> (entry <c>editor.palette</c>) — the ghost must follow
/// THIS frame's cursor world position, lag-free.</para>
///
/// <para><b>Headless-drivable.</b> <see cref="Arm(string)"/> / <see cref="Disarm"/> /
/// <see cref="SelectBand(string)"/> are public: the overlay's named-action dispatch routes the
/// op-plan actions <c>palette:&lt;entryId&gt;</c> / <c>palette:none</c> / <c>band:&lt;name&gt;</c>
/// here, so a scripted plan can arm an item and click-place with no real mouse.</para>
/// </summary>
public sealed class PalettePlacementSystem : ISystem<GameState>
{
    /// <summary>The ghost preview's tint (semi-transparent white — premultiplied).</summary>
    public static readonly Color GhostColor = Color.White * 0.55f;

    /// <summary>The fill of the armed item's button (the palette's "active" accent).</summary>
    public static readonly Color ArmedItemFill = EditorChromeBuilder.CheckboxOnFill;

    /// <summary>The item thumbnails' draw depth on the Editor target — above the button fill
    /// (<see cref="EditorChromeBuilder.ButtonDepth"/> 0.5) so it shows over the button, below the
    /// label (<see cref="EditorChromeBuilder.LabelDepth"/> 0.6).</summary>
    private const float ThumbnailDepth = 0.56f;

    /// <summary>The per-press rotation step (radians) the ghost-rotate keys (Q/E) apply — 45°, so a
    /// road piece reaches the four cardinal + four diagonal orientations in whole steps (Slice 4).</summary>
    public const float GhostRotationStep = MathHelper.PiOver4;

    private readonly World _world;
    private readonly AssetCatalog _catalog;
    private readonly IReadOnlyList<PaletteBand> _bands;
    private readonly IReadOnlyList<TriggerType> _triggerTypes;
    private readonly FileAssetTextureLoader _textures;
    private readonly SceneSerializer _serializer;
    private readonly EditorHistory _history;
    private readonly ViewportManager? _viewportManager;
    private readonly BitmapFont? _font;
    private readonly Func<string, float> _measureLabel;
    private readonly Func<GameState, bool>? _cancelRequested;
    private readonly Func<GameState, bool>? _rotateCwRequested;
    private readonly Func<GameState, bool>? _rotateCcwRequested;

    private readonly EntitySet _cursorSet;
    private readonly EntitySet _gizmoStateSet;
    private readonly EntitySet _selectedSet;

    // Chrome entities (built lazily once; parked/positioned per layout pass).
    private sealed class ItemButton
    {
        public required AssetCatalogEntry Entry;
        public Entity Button;
        public Entity Label;
        public Entity Thumbnail; // native-res art preview sprite on the Editor target (Slice 4)
        public int Width;
        public (int Row, int X) Flowed;
    }

    // A trigger-type button (island-authoring Slice 3), flowed in the SAME item grid after the
    // sprite items (a "Triggers section" — prefixed labels distinguish them).
    private sealed class TriggerButton
    {
        public required TriggerType Type;
        public Entity Button;
        public Entity Label;
        public int Width;
        public (int Row, int X) Flowed;
    }

    private readonly List<ItemButton> _items = new();
    private readonly List<TriggerButton> _triggerItems = new();
    private readonly List<(int Index, Entity Button, Entity Label)> _bandButtons = new();
    private Entity _emptyHint;
    private bool _built;

    private int _scroll;
    private int _laidOutWidth, _laidOutHeight, _laidOutScroll = -1;
    private float _laidOutScale;

    private int _armedIndex = -1;
    private int _armedTrigger = -1;
    private int _bandIndex;
    private float _armedRotation; // the ghost's orientation (radians), set by Q/E; reset on disarm
    private Entity _ghost;
    private bool _ghostAlive;

    // Multi-stamp hold-drag state (Slice 4): a stroke coalesces all its stamps into one undo step.
    private bool _stamping;
    private Vector2 _lastStampWorld;    // the raw cursor world of the last stamp (arc-length anchor)
    private Vector2? _lastPlacedSnapped; // the last stamped (snapped) position — snap-collapse dedupe
    private Entity _lastStampCreated;   // auto-selected when the stroke ends

    public bool IsEnabled { get; set; } = true;

    /// <param name="viewportManager">Supplies the window size for the strip layout. Null (unit
    /// tests) = no chrome is built; arming/ghost/placement still work (the headless form).</param>
    /// <param name="font">The chrome label font; null (unit tests) = layout-only labels.</param>
    /// <param name="cancelRequested">The screen's Escape predicate (optional — right-click always
    /// disarms).</param>
    public PalettePlacementSystem(
        World world,
        AssetCatalog catalog,
        IReadOnlyList<PaletteBand> bands,
        FileAssetTextureLoader textures,
        SceneSerializer serializer,
        EditorHistory history,
        ViewportManager? viewportManager = null,
        BitmapFont? font = null,
        Func<GameState, bool>? cancelRequested = null,
        IReadOnlyList<TriggerType>? triggerTypes = null,
        Func<GameState, bool>? rotateCwRequested = null,
        Func<GameState, bool>? rotateCcwRequested = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _bands = bands ?? throw new ArgumentNullException(nameof(bands));
        if (bands.Count == 0) throw new ArgumentException("The palette needs at least one layer band.", nameof(bands));
        _triggerTypes = triggerTypes ?? Array.Empty<TriggerType>();
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _viewportManager = viewportManager;
        _font = font;
        _measureLabel = font != null
            ? label => font.MeasureString(label).Width * EditorChromeBuilder.LabelScale
            : label => label.Length * 7f; // layout-only approximation (tests run no text prep)
        _cancelRequested = cancelRequested;
        _rotateCwRequested = rotateCwRequested;
        _rotateCcwRequested = rotateCcwRequested;

        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        _gizmoStateSet = world.GetEntities().With<GizmoStateComponent>().AsSet();
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
    }

    /// <summary>The armed catalog entry, or null while disarmed.</summary>
    public AssetCatalogEntry? ArmedEntry =>
        _armedIndex >= 0 && _armedIndex < _catalog.Entries.Count ? _catalog.Entries[_armedIndex] : null;

    /// <summary>The selected layer band the next placement targets.</summary>
    public PaletteBand SelectedBand => _bands[_bandIndex];

    /// <summary>Whether the ghost preview entity currently exists.</summary>
    public bool HasGhost => _ghostAlive && _ghost.IsAlive;

    /// <summary>The ghost preview entity (default when none) — exposed for tests/tooling.</summary>
    public Entity Ghost => HasGhost ? _ghost : default;

    /// <summary>Arms the palette item with the given <see cref="AssetCatalogEntry.Id"/> or full
    /// <c>file:</c> AssetKey (the headless <c>palette:&lt;id&gt;</c> op). Returns false (loud) for
    /// an unknown id.</summary>
    public bool Arm(string idOrAssetKey)
    {
        if (_catalog.TryGet(idOrAssetKey, out var entry))
        {
            for (var i = 0; i < _catalog.Entries.Count; i++)
            {
                if (!ReferenceEquals(_catalog.Entries[i], entry)) continue;
                ArmByIndex(i);
                return true;
            }
        }

        Logger.Warning($"[level-editor] Palette: no catalog entry '{idOrAssetKey}' to arm.");
        return false;
    }

    /// <summary>Arms the item at <paramref name="index"/> into the catalog's entry list: the shared
    /// mode flips to <see cref="EditorToolMode.Place"/> and the ghost appears under the cursor.</summary>
    public void ArmByIndex(int index)
    {
        if (index < 0 || index >= _catalog.Entries.Count) return;
        _armedIndex = index;
        _armedTrigger = -1; // sprite item and trigger arming are mutually exclusive
        SetMode(EditorToolMode.Place);
        Logger.Info($"[level-editor] Palette: armed '{_catalog.Entries[index].Id}' " +
                    $"on band '{SelectedBand.Name}'.");
    }

    /// <summary>The armed trigger type (island-authoring §5.3), or null when a sprite item is armed
    /// (or nothing). The trigger overlay reads this to draw the placement ghost box.</summary>
    public TriggerType? ArmedTrigger =>
        _armedTrigger >= 0 && _armedTrigger < _triggerTypes.Count ? _triggerTypes[_armedTrigger] : null;

    /// <summary>Arms the trigger type whose <see cref="TriggerType.Prefix"/> matches (the headless
    /// <c>trigger:&lt;prefix&gt;</c> op). Returns false (loud) for an unknown prefix.</summary>
    public bool ArmTrigger(string prefix)
    {
        for (var i = 0; i < _triggerTypes.Count; i++)
        {
            if (!string.Equals(_triggerTypes[i].Prefix, prefix, StringComparison.OrdinalIgnoreCase)) continue;
            ArmTriggerByIndex(i);
            return true;
        }
        Logger.Warning($"[level-editor] Palette: no trigger type '{prefix}' to arm.");
        return false;
    }

    /// <summary>Arms the trigger type at <paramref name="index"/>: mode flips to
    /// <see cref="EditorToolMode.Place"/>; a click places the zone (no sprite ghost — the trigger
    /// overlay draws the box preview).</summary>
    public void ArmTriggerByIndex(int index)
    {
        if (index < 0 || index >= _triggerTypes.Count) return;
        _armedTrigger = index;
        _armedIndex = -1;
        DespawnGhost(); // triggers use the trigger-overlay box ghost, not the sprite ghost
        SetMode(EditorToolMode.Place);
        Logger.Info($"[level-editor] Palette: armed trigger '{_triggerTypes[index].Prefix}'.");
    }

    /// <summary>Disarms placement back to <see cref="EditorToolMode.SelectTransform"/> (Escape /
    /// right-click / the headless <c>palette:none</c> op) and despawns the ghost.</summary>
    public void Disarm()
    {
        EndStroke(); // commit an in-flight multi-stamp stroke before standing down
        _armedIndex = -1;
        _armedTrigger = -1;
        _armedRotation = 0f; // a fresh arm starts axis-aligned
        SetMode(EditorToolMode.SelectTransform);
        DespawnGhost();
    }

    /// <summary>The armed ghost's current orientation (radians) — what the next stamp bakes into the
    /// placed prop's <c>TransformComponent.Rotation</c>.</summary>
    public float ArmedRotation => _armedRotation;

    /// <summary>Rotates the armed ghost by <paramref name="deltaRadians"/> (island-authoring
    /// Slice 4 — the Q/E keys / the headless <c>ghost:cw</c> / <c>ghost:ccw</c> ops): the ghost
    /// preview and every subsequent stamp of the armed item land at this orientation, so straight /
    /// curve road pieces and props can be oriented at placement. Wrapped to (−π, π].</summary>
    public void RotateArmedGhost(float deltaRadians) =>
        _armedRotation = MathHelper.WrapAngle(_armedRotation + deltaRadians);

    /// <summary>Selects the layer band by name (case-insensitive; the headless
    /// <c>band:&lt;name&gt;</c> op). Returns false (loud) for an unknown band.</summary>
    public bool SelectBand(string name)
    {
        for (var i = 0; i < _bands.Count; i++)
        {
            if (!string.Equals(_bands[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            _bandIndex = i;
            return true;
        }

        Logger.Warning($"[level-editor] Palette: no layer band '{name}'.");
        return false;
    }

    /// <summary>
    /// Re-scans the asset drop folder and rebuilds the palette live (island-authoring Slice 4 —
    /// the toolbar's Refresh button / the headless <c>RefreshCatalog</c> action), so dropping a new
    /// PNG shows up without restarting the editor. Rescans the <see cref="AssetCatalog"/>,
    /// invalidates the texture cache (changed/new files re-decode), disarms (the armed index may no
    /// longer be valid), disposes + rebuilds the item rows (buttons/labels/thumbnails) from the new
    /// entries, and forces a re-layout. Bands and triggers are screen-fixed, so they are kept.
    /// </summary>
    public void Refresh()
    {
        _catalog.Rescan();
        _textures.Invalidate();

        // No chrome yet (headless / pre-first-Update): the catalog is refreshed; there are no rows
        // to rebuild, and BuildChrome will read the fresh entries on the first Update.
        if (!_built) return;

        Disarm(); // the previously-armed item may be gone after the rescan

        foreach (var item in _items)
        {
            if (item.Button.IsAlive) item.Button.Dispose();
            if (item.Label.IsAlive) item.Label.Dispose();
            if (item.Thumbnail.IsAlive) item.Thumbnail.Dispose();
        }
        _items.Clear();
        if (_emptyHint.IsAlive) _emptyHint.Dispose();

        foreach (var entry in _catalog.Entries)
        {
            var label = CreateLabel(ItemLabel(entry));
            _items.Add(new ItemButton
            {
                Entry = entry, Button = CreateButton(label), Label = label, Thumbnail = CreateThumbnail(),
            });
        }

        if (_catalog.Entries.Count == 0 && _triggerItems.Count == 0)
            _emptyHint = CreateLabel("Palette empty - drop packs into Content/Island/ (see MANIFEST.md)");

        _scroll = 0;
        _laidOutScroll = -1; // force PositionChrome to re-run next Update
        Logger.Info($"[level-editor] Palette refreshed: {_catalog.Entries.Count} item(s).");
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        if (!_built && _viewportManager != null) BuildChrome();

        // Edit-guarded: while Playing the palette neither hit-tests nor places, and the ghost
        // despawns (a click in the viewport belongs to the game). The armed selection is kept so
        // pausing resumes where the designer was; the chrome renders dimmed.
        var editing = state.RunMode == RunMode.Edit;
        (int Band, int Item) hovered = (-1, -1);

        if (editing)
        {
            // Self-heal the shared mode: Place with nothing armed (e.g. a stale state after a
            // failed headless arm) falls back to SelectTransform so no tool family is muted.
            if (GetMode() == EditorToolMode.Place && _armedIndex < 0 && _armedTrigger < 0)
                SetMode(EditorToolMode.SelectTransform);

            if (_cancelRequested?.Invoke(state) == true && (_armedIndex >= 0 || _armedTrigger >= 0))
                Disarm();

            // Ghost rotate (Slice 4): Q/E rotate the armed sprite ghost before stamping (triggers
            // are axis-aligned boxes — not rotated).
            if (_armedIndex >= 0)
            {
                if (_rotateCwRequested?.Invoke(state) == true) RotateArmedGhost(GhostRotationStep);
                if (_rotateCcwRequested?.Invoke(state) == true) RotateArmedGhost(-GhostRotationStep);
            }

            hovered = HandleInteraction(state);
            UpdateGhostAndPlace(state);
        }
        else
        {
            EndStroke(); // commit any open multi-stamp stroke before the palette goes inert
            DespawnGhost();
        }

        if (_built)
        {
            var scale = _viewportManager!.DevicePixelRatio;
            var strip = EditorChromeLayout.BottomBar(
                _viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale);
            _scroll = PaletteLayout.ClampScroll(_scroll, TotalRows(), strip, scale);
            if (_laidOutWidth != _viewportManager.ScreenWidth ||
                _laidOutHeight != _viewportManager.ScreenHeight ||
                _laidOutScroll != _scroll ||
                _laidOutScale != scale)
                PositionChrome(strip, scale);
            ReflectState(hovered, editing);
        }
    }

    // ---- Strip interaction (raw ScreenPosition, like the toolbar / systems panel) ----

    private (int Band, int Item) HandleInteraction(GameState state)
    {
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();

            // Right-click disarms from anywhere (viewport or chrome) — the standard escape hatch.
            if (input.RightButtonPressed && _armedIndex >= 0)
                Disarm();

            if (!_built) return (-1, -1);

            var scale = _viewportManager!.DevicePixelRatio;
            var strip = EditorChromeLayout.BottomBar(
                _viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale);
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            if (!strip.Contains(point)) return (-1, -1);

            if (input.ScrollWheelDelta != 0)
                _scroll = PaletteLayout.ClampScroll(
                    _scroll + PaletteLayout.ScrollRows(input.ScrollWheelDelta), TotalRows(), strip, scale);

            // Band selector row.
            for (var i = 0; i < _bandButtons.Count; i++)
            {
                var bounds = _bandButtons[i].Button.Get<SimpleButtonComponent>();
                var rect = ButtonRect(_bandButtons[i].Button, bounds);
                if (!rect.Contains(point)) continue;
                if (input.LeftButtonReleased) _bandIndex = _bandButtons[i].Index;
                return (i, -1);
            }

            // Item flow grid.
            for (var i = 0; i < _items.Count; i++)
            {
                if (!PaletteLayout.TryItemRect(strip, _items[i].Flowed, _items[i].Width, _scroll,
                        out var rect, scale)) continue;
                if (!rect.Contains(point)) continue;
                if (input.LeftButtonReleased) ArmByIndex(i);
                return (-1, i);
            }

            // Triggers section (flowed in the same grid; hovered index offset by the item count).
            for (var j = 0; j < _triggerItems.Count; j++)
            {
                if (!PaletteLayout.TryItemRect(strip, _triggerItems[j].Flowed, _triggerItems[j].Width,
                        _scroll, out var rect, scale)) continue;
                if (!rect.Contains(point)) continue;
                if (input.LeftButtonReleased) ArmTriggerByIndex(j);
                return (-1, _items.Count + j);
            }

            return (-1, -1);
        }
        return (-1, -1);
    }

    private static Rectangle ButtonRect(Entity button, in SimpleButtonComponent visual)
    {
        var position = button.Get<TransformComponent>().Position;
        return new Rectangle((int)position.X, (int)position.Y, (int)visual.Size.X, (int)visual.Size.Y);
    }

    // ---- Ghost + place ----

    private void UpdateGhostAndPlace(GameState state)
    {
        // Trigger placement (island-authoring §5.3): no sprite ghost — the trigger overlay draws
        // the box preview — so just place on a viewport click.
        if (_armedTrigger >= 0)
        {
            DespawnGhost();
            PlaceTriggerOnClick();
            return;
        }

        if (_armedIndex < 0)
        {
            DespawnGhost();
            return;
        }

        var entry = _catalog.Entries[_armedIndex];
        var band = SelectedBand;
        var texture = _textures.Load(entry.AssetKey); // lazy + memoized; magenta when missing

        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();

            EnsureGhost(entry, band, texture);
            if (input.OutsideViewport)
            {
                // The pointer is over chrome/margins: WorldPosition is stale there, so hide the
                // ghost off-screen (culling drops its VisibleComponent) instead of freezing it.
                Park(_ghost);
            }
            else
            {
                var position = PlacementPosition(input.WorldPosition);
                Place(_ghost, position);
                _ghost.Get<TransformComponent>().Rotation = _armedRotation; // ghost-rotate (Slice 4)

                // Multi-stamp hold-drag (Slice 4): the press begins a coalesced stroke stamping one
                // prop; holding + dragging stamps more at StampSpacing arc-length intervals; the
                // release commits the whole stroke as ONE undo step. A single click (press with no
                // drag) = one stamp = one undo step.
                if (input.LeftButtonPressed)
                    BeginStroke(entry, band, position, input.WorldPosition, texture);
                else if (_stamping && input.LeftButton)
                    ContinueStroke(entry, band, input.WorldPosition, texture);
            }

            // A release — or the button no longer held (e.g. it went up over the chrome margins) —
            // ends the stroke, committing the coalesced transaction even when the pointer left the
            // viewport mid-drag.
            if (_stamping && (input.LeftButtonReleased || !input.LeftButton))
                EndStroke();

            return; // single cursor
        }
    }

    private Vector2 PlacementPosition(Vector2 cursorWorld)
    {
        ref readonly var gizmo = ref GetGizmoStateEntity().Get<GizmoStateComponent>();
        return gizmo.SnapEnabled && gizmo.GridStep > 0f
            ? GizmoTransform.Snap(cursorWorld, gizmo.GridStep)
            : cursorWorld;
    }

    /// <summary>Opens a coalesced stroke (one undo step for the whole hold-drag, the gizmo's
    /// <c>BeginTransaction</c>/<c>CommitTransaction</c> pattern) and stamps the first prop at the
    /// press position.</summary>
    private void BeginStroke(AssetCatalogEntry entry, PaletteBand band, Vector2 firstPosition,
        Vector2 cursorWorld, Microsoft.Xna.Framework.Graphics.Texture2D? texture)
    {
        _history.BeginTransaction();
        _stamping = true;
        _lastPlacedSnapped = null;
        StampAt(entry, band, firstPosition, texture);
        _lastStampWorld = cursorWorld;
    }

    /// <summary>Stamps any additional props the cursor's travel since the last stamp has earned
    /// (arc-length spacing via <see cref="StrokeSampler"/>), inside the open stroke transaction. A
    /// non-positive <see cref="GizmoStateComponent.StampSpacing"/> disables multi-stamp (only the
    /// press stamp lands — the classic single-click).</summary>
    private void ContinueStroke(AssetCatalogEntry entry, PaletteBand band, Vector2 cursorWorld,
        Microsoft.Xna.Framework.Graphics.Texture2D? texture)
    {
        var spacing = GetGizmoStateEntity().Get<GizmoStateComponent>().StampSpacing;
        if (spacing <= 0f) return;
        var points = StrokeSampler.Sample(_lastStampWorld, cursorWorld, spacing);
        if (points.Count == 0) return;
        foreach (var raw in points)
            StampAt(entry, band, PlacementPosition(raw), texture);
        _lastStampWorld = points[points.Count - 1];
    }

    /// <summary>One stamp = one <see cref="CreateEntityCommand"/> pushed into the open stroke
    /// transaction (the command tags <c>SceneObjectComponent</c> + snapshots the sub-graph). Skips
    /// a stamp that snap-collapses onto the previous one (snap on + spacing &lt; grid) so identical
    /// props never stack in one cell.</summary>
    private void StampAt(AssetCatalogEntry entry, PaletteBand band, Vector2 position,
        Microsoft.Xna.Framework.Graphics.Texture2D? texture)
    {
        if (_lastPlacedSnapped.HasValue && _lastPlacedSnapped.Value == position) return;
        var created = default(Entity);
        _history.Push(new CreateEntityCommand(_world, _serializer,
            w =>
            {
                created = SpritePropFactory.Create(w, entry, band, position, texture, _armedRotation);
                return created;
            }));
        _lastPlacedSnapped = position;
        if (created.IsAlive) _lastStampCreated = created;

        Logger.Info($"[level-editor] Placed '{entry.Id}' on band '{band.Name}' at " +
                    $"({position.X:0.##}, {position.Y:0.##}).");
    }

    /// <summary>Ends the multi-stamp stroke: commit the coalesced transaction (one undo step for
    /// the whole drag) and auto-select the last stamp (selection is dormant in Place mode, so
    /// nothing fights this) so the gizmo shows its handles once the palette disarms. A no-op when
    /// no stroke is open.</summary>
    private void EndStroke()
    {
        if (!_stamping) return;
        _stamping = false;
        if (_history.InTransaction) _history.CommitTransaction();
        ClearSelection();
        if (_lastStampCreated.IsAlive) _lastStampCreated.Set(new SelectedComponent());
        _lastStampCreated = default;
        _lastPlacedSnapped = null;
    }

    /// <summary>Places a trigger zone on a viewport left-click at the snapped cursor position: one
    /// <see cref="CreateEntityCommand"/> (one undo step, auto-tagged) wrapping
    /// <see cref="TriggerFactory"/>, with a scene-unique auto-numbered identity. Repeated clicks keep
    /// placing until disarmed.</summary>
    private void PlaceTriggerOnClick()
    {
        var type = _triggerTypes[_armedTrigger];
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            if (input.OutsideViewport || !input.LeftButtonPressed) return;
            PlaceTrigger(type, PlacementPosition(input.WorldPosition));
            return; // single cursor
        }
    }

    private void PlaceTrigger(TriggerType type, Vector2 position)
    {
        var name = TriggerFactory.NextName(_world, type.Prefix);
        var created = default(Entity);
        _history.Push(new CreateEntityCommand(_world, _serializer,
            w =>
            {
                created = TriggerFactory.Create(w, type, position, name);
                return created;
            }));

        ClearSelection();
        if (created.IsAlive) created.Set(new SelectedComponent());

        Logger.Info($"[level-editor] Placed trigger '{type.Prefix}:{name}' at " +
                    $"({position.X:0.##}, {position.Y:0.##}).");
    }

    private void ClearSelection()
    {
        List<Entity>? toClear = null;
        foreach (var e in _selectedSet.GetEntities())
            (toClear ??= new List<Entity>()).Add(e);
        if (toClear == null) return;
        foreach (var e in toClear)
            if (e.IsAlive && e.Has<SelectedComponent>())
                e.Remove<SelectedComponent>();
    }

    private void EnsureGhost(AssetCatalogEntry entry, PaletteBand band,
        Microsoft.Xna.Framework.Graphics.Texture2D? texture)
    {
        if (!_ghostAlive || !_ghost.IsAlive)
        {
            _ghost = _world.CreateEntity();
            _ghost.Set(new EditorInfrastructureComponent()); // never scene content; survives Restart
            _ghost.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
            _ghost.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
            _ghost.Set(new SpriteInfoComponent());
            // NO SceneObjectComponent (never serialized) and no VisibleComponent (CullingSystem
            // owns it — the ghost is an ordinary Main-target sprite the real pipeline previews).
            _ghostAlive = true;
        }

        var source = SpritePropFactory.SourceRect(entry, texture);
        ref var sprite = ref _ghost.Get<SpriteInfoComponent>();
        sprite.SpriteSheet = texture;
        sprite.AssetKey = null; // the ghost is not a save-candidate; keep it key-less
        sprite.Source = source;
        sprite.Size = new Vector2(source.Width, source.Height);
        sprite.Color = GhostColor;
        sprite.Target = RenderTargetID.Main;
        sprite.LayerDepth = band.LayerDepth;
        sprite.YSortOffset = 0f;
        sprite.Origin = band.YSorted ? SpritePropFactory.FeetOrigin(source) : Vector2.Zero;
    }

    private void DespawnGhost()
    {
        if (_ghostAlive && _ghost.IsAlive) _ghost.Dispose();
        _ghostAlive = false;
    }

    // ---- Chrome build / layout / state ----

    private void BuildChrome()
    {
        for (var i = 0; i < _bands.Count; i++)
        {
            var label = CreateLabel(_bands[i].Name);
            _bandButtons.Add((i, CreateButton(label), label));
        }

        foreach (var entry in _catalog.Entries)
        {
            var label = CreateLabel(ItemLabel(entry));
            _items.Add(new ItemButton
            {
                Entry = entry, Button = CreateButton(label), Label = label, Thumbnail = CreateThumbnail(),
            });
        }

        // Triggers section (island-authoring §5.3): flowed in the same grid, after the sprite items.
        foreach (var type in _triggerTypes)
        {
            var label = CreateLabel(TriggerLabel(type));
            _triggerItems.Add(new TriggerButton { Type = type, Button = CreateButton(label), Label = label });
        }

        if (_catalog.Entries.Count == 0 && _triggerItems.Count == 0)
            _emptyHint = CreateLabel("Palette empty - drop packs into Content/Island/ (see MANIFEST.md)");

        _built = true;
    }

    /// <summary>A trigger button's strip label: a leading marker so the Triggers section reads at a
    /// glance amid the sprite items.</summary>
    public static string TriggerLabel(TriggerType type) => $"[T] {type.Label}";

    /// <summary>An item's strip label: <c>folder/name</c> so the folder grouping reads at a
    /// glance (entries are already sorted folder-first).</summary>
    public static string ItemLabel(AssetCatalogEntry entry) =>
        string.IsNullOrEmpty(entry.Folder) ? entry.Label : $"{entry.Folder}/{entry.Label}";

    private Entity CreateLabel(string label)
    {
        var text = _world.CreateEntity();
        text.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        text.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        text.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Editor,
            LayerDepth = EditorChromeBuilder.LabelDepth,
            TextContent = label,
            Font = _font!,
            Color = EditorChromeBuilder.LabelColor,
            Scale = EditorChromeBuilder.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = 0, // blanked while parked; the layout pass reveals it
        });
        // NOTE: no VisibleComponent — chrome rule (see EditorChromeBuilder).
        return text;
    }

    private Entity CreateButton(Entity labelEntity)
    {
        var button = _world.CreateEntity();
        button.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        button.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        button.Set(new SimpleButtonComponent
        {
            Size = Vector2.One, // the layout pass sets the real size
            LineThickness = 1f,
            Color = EditorChromeBuilder.ButtonOutline,
            FillColor = EditorChromeBuilder.ButtonFill,
            TextEntity = labelEntity,
            Target = RenderTargetID.Editor,
            LayerDepth = EditorChromeBuilder.ButtonDepth,
        });
        // NOTE: no VisibleComponent (chrome rule) and no ToolbarButtonComponent (the palette owns
        // its own hit-testing; ToolbarSystem must not dispatch for these).
        return button;
    }

    /// <summary>A sprite item's native-resolution art thumbnail on the Editor target (Slice 4). Only
    /// a <c>DrawComponent</c> is needed — the Editor render pass reads its sprite fields directly
    /// (no sprite-prep runs off the Main target), so the layout pass populates Texture/Position/…
    /// itself and blanks Texture (draws nothing) while parked or when the texture is missing.</summary>
    private Entity CreateThumbnail()
    {
        var thumb = _world.CreateEntity();
        thumb.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        thumb.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Editor,
            LayerDepth = ThumbnailDepth,
            // Texture stays null until a visible row lazy-loads it (a null-texture sprite draws
            // nothing — the label-only fallback).
        });
        return thumb;
    }

    private int TotalRows()
    {
        var rows = 0;
        foreach (var item in _items) rows = Math.Max(rows, item.Flowed.Row + 1);
        foreach (var trigger in _triggerItems) rows = Math.Max(rows, trigger.Flowed.Row + 1);
        return rows;
    }

    private void PositionChrome(Rectangle strip, float scale)
    {
        var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
        var content = PaletteLayout.ContentArea(strip, scale);

        // Band selector header row.
        var bandWidths = new int[_bandButtons.Count];
        for (var i = 0; i < _bandButtons.Count; i++)
            bandWidths[i] = PaletteLayout.ButtonWidth(_measureLabel(_bands[i].Name) * scale, scale);
        var bandRects = PaletteLayout.BandRow(strip, bandWidths, scale);
        for (var i = 0; i < _bandButtons.Count; i++)
            PlaceButton(_bandButtons[i].Button, _bandButtons[i].Label, bandRects[i], labelHeight, scale);

        // Item flow grid, scrolled by whole rows. Sprite items and trigger buttons flow TOGETHER in
        // one grid (triggers appended after the sprite items — the "Triggers section").
        var total = _items.Count + _triggerItems.Count;
        var widths = new int[total];
        for (var i = 0; i < _items.Count; i++)
        {
            // Sprite items reserve the thumbnail box (ItemWidth), so the flow layout is stable
            // whether or not the texture resolves (Slice 4).
            widths[i] = PaletteLayout.ItemWidth(
                _measureLabel(_items[i].Label.Get<DynamicTextComponent>().TextContent) * scale, scale);
            _items[i].Width = widths[i];
        }
        for (var j = 0; j < _triggerItems.Count; j++)
        {
            var w = PaletteLayout.ButtonWidth(
                _measureLabel(_triggerItems[j].Label.Get<DynamicTextComponent>().TextContent) * scale, scale);
            widths[_items.Count + j] = w;
            _triggerItems[j].Width = w;
        }
        var flow = PaletteLayout.Flow(widths, content.Width, scale);
        for (var i = 0; i < _items.Count; i++)
        {
            _items[i].Flowed = flow[i];
            if (PaletteLayout.TryItemRect(strip, flow[i], widths[i], _scroll, out var rect, scale))
                PlaceItemButton(_items[i], rect, labelHeight, scale);
            else
                ParkItem(_items[i]);
        }
        for (var j = 0; j < _triggerItems.Count; j++)
        {
            var idx = _items.Count + j;
            _triggerItems[j].Flowed = flow[idx];
            if (PaletteLayout.TryItemRect(strip, flow[idx], widths[idx], _scroll, out var rect, scale))
                PlaceButton(_triggerItems[j].Button, _triggerItems[j].Label, rect, labelHeight, scale);
            else
                ParkButton(_triggerItems[j].Button, _triggerItems[j].Label);
        }

        if (_emptyHint.IsAlive)
        {
            PlaceLabel(_emptyHint, new Vector2(content.X,
                content.Y + EditorChromeLayout.Px(PaletteLayout.HeaderHeight, scale)
                          + (EditorChromeLayout.Px(PaletteLayout.RowHeight, scale) - labelHeight) / 2f), scale);
        }

        _laidOutWidth = _viewportManager!.ScreenWidth;
        _laidOutHeight = _viewportManager.ScreenHeight;
        _laidOutScroll = _scroll;
        _laidOutScale = scale;
    }

    private void PlaceButton(Entity button, Entity label, Rectangle rect, float labelHeight, float scale)
    {
        Place(button, new Vector2(rect.X, rect.Y));
        ref var visual = ref button.Get<SimpleButtonComponent>();
        visual.Size = new Vector2(rect.Width, rect.Height);
        PlaceLabel(label, new Vector2(
            rect.X + EditorChromeLayout.Px(PaletteLayout.ButtonPaddingX, scale),
            rect.Y + (rect.Height - labelHeight) / 2f), scale);
    }

    private void ParkButton(Entity button, Entity label)
    {
        Park(button);
        Park(label);
        ref var text = ref label.Get<DynamicTextComponent>();
        text.VisibleCharacterCount = 0; // parked labels render nothing (cheaper than re-prepping)
    }

    /// <summary>Places a sprite item's button + its label (offset past the thumbnail box) + its
    /// thumbnail (Slice 4).</summary>
    private void PlaceItemButton(ItemButton item, Rectangle rect, float labelHeight, float scale)
    {
        Place(item.Button, new Vector2(rect.X, rect.Y));
        ref var visual = ref item.Button.Get<SimpleButtonComponent>();
        visual.Size = new Vector2(rect.Width, rect.Height);
        PlaceLabel(item.Label, new Vector2(
            rect.X + PaletteLayout.ItemLabelOffsetX(scale),
            rect.Y + (rect.Height - labelHeight) / 2f), scale);
        PlaceItemThumbnail(item, rect, scale);
    }

    private void ParkItem(ItemButton item)
    {
        ParkButton(item.Button, item.Label);
        // A parked thumbnail draws nothing (and we skip the lazy texture load for scrolled-out rows).
        item.Thumbnail.Get<DrawComponent>().Texture = null;
    }

    /// <summary>Lazily loads a visible item's texture and populates its thumbnail sprite to fit the
    /// row's thumbnail box (aspect-preserved, native resolution). Falls back to the text label — the
    /// thumbnail draws nothing — when the texture is missing or the magenta placeholder.</summary>
    private void PlaceItemThumbnail(ItemButton item, Rectangle itemRect, float scale)
    {
        var draw = item.Thumbnail.Get<DrawComponent>();
        var texture = _textures.Load(item.Entry.AssetKey); // lazy + memoized (only visible rows)
        if (texture == null || ReferenceEquals(texture, _textures.Placeholder))
        {
            draw.Texture = null; // fall back to the label — nothing drawn
            return;
        }

        var source = SpritePropFactory.SourceRect(item.Entry, texture);
        var box = PaletteLayout.ItemThumbnailRect(itemRect, scale);
        var dest = PaletteLayout.ThumbnailFit(box, source.Width, source.Height);

        draw.Texture = texture;
        draw.SourceRectangle = source;
        draw.Position = new Vector2(dest.X, dest.Y);
        draw.Size = new Vector2(dest.Width, dest.Height);
        draw.Origin = Vector2.Zero;
        draw.Rotation = 0f;
        draw.Color = Color.White;
        draw.LayerDepth = ThumbnailDepth;
    }

    private void PlaceLabel(Entity label, Vector2 position, float scale)
    {
        Place(label, position);
        ref var text = ref label.Get<DynamicTextComponent>();
        text.Scale = EditorChromeBuilder.LabelScale * scale;
        text.VisibleCharacterCount = int.MaxValue;
    }

    private static void Park(Entity entity) => Place(entity, SystemsPanelLayout.ParkedPosition);

    private static void Place(Entity entity, Vector2 position)
    {
        // Palette entities are standalone (no parent), so WorldPosition derives from Position.
        ref var transform = ref entity.Get<TransformComponent>();
        transform.Position = position;
        entity.NotifyChanged<TransformComponent>();
    }

    private void ReflectState((int Band, int Item) hovered, bool editing)
    {
        for (var i = 0; i < _bandButtons.Count; i++)
        {
            ref var visual = ref _bandButtons[i].Button.Get<SimpleButtonComponent>();
            visual.FillColor = _bandButtons[i].Index == _bandIndex
                ? ArmedItemFill
                : hovered.Band == i && editing
                    ? EditorChromeBuilder.ButtonHoverFill
                    : EditorChromeBuilder.ButtonFill;
        }

        for (var i = 0; i < _items.Count; i++)
        {
            ref var visual = ref _items[i].Button.Get<SimpleButtonComponent>();
            visual.FillColor = !editing
                ? EditorChromeBuilder.ButtonDisabledFill
                : i == _armedIndex
                    ? ArmedItemFill
                    : hovered.Item == i
                        ? EditorChromeBuilder.ButtonHoverFill
                        : EditorChromeBuilder.ButtonFill;
        }

        for (var j = 0; j < _triggerItems.Count; j++)
        {
            var hoverIndex = _items.Count + j;
            ref var visual = ref _triggerItems[j].Button.Get<SimpleButtonComponent>();
            visual.FillColor = !editing
                ? EditorChromeBuilder.ButtonDisabledFill
                : j == _armedTrigger
                    ? ArmedItemFill
                    : hovered.Item == hoverIndex
                        ? EditorChromeBuilder.ButtonHoverFill
                        : EditorChromeBuilder.ButtonFill;
        }
    }

    // ---- Shared gizmo-state access (mirrors GizmoSystem's fallback) ----

    private EditorToolMode GetMode() => GetGizmoStateEntity().Get<GizmoStateComponent>().Mode;

    private void SetMode(EditorToolMode mode)
    {
        ref var state = ref GetGizmoStateEntity().Get<GizmoStateComponent>();
        state.Mode = mode;
    }

    private Entity GetGizmoStateEntity()
    {
        foreach (var e in _gizmoStateSet.GetEntities())
            return e;
        var created = _world.CreateEntity();
        created.Set(new EditorInfrastructureComponent());
        created.Set(GizmoStateComponent.Default);
        return created;
    }

    public void Dispose()
    {
        EndStroke(); // never leave an open transaction on the shared history
        DespawnGhost();
        foreach (var (_, button, label) in _bandButtons)
        {
            if (button.IsAlive) button.Dispose();
            if (label.IsAlive) label.Dispose();
        }
        foreach (var item in _items)
        {
            if (item.Button.IsAlive) item.Button.Dispose();
            if (item.Label.IsAlive) item.Label.Dispose();
            if (item.Thumbnail.IsAlive) item.Thumbnail.Dispose();
        }
        foreach (var trigger in _triggerItems)
        {
            if (trigger.Button.IsAlive) trigger.Button.Dispose();
            if (trigger.Label.IsAlive) trigger.Label.Dispose();
        }
        if (_emptyHint.IsAlive) _emptyHint.Dispose();
        _bandButtons.Clear();
        _items.Clear();
        _triggerItems.Clear();
        _cursorSet.Dispose();
        _gizmoStateSet.Dispose();
        _selectedSet.Dispose();
    }
}
