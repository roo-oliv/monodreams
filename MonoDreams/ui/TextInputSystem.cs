using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.State;

namespace MonoDreams.UI;

/// Drives <see cref="TextInputComponent"/>: while an input is focused it inserts the
/// characters typed this frame at the caret (filtered by the field's
/// <see cref="TextInputMask"/> and capped at <see cref="TextInputComponent.MaxLength"/>),
/// deletes on Backspace/Delete, moves the caret on Left/Right/Home/End, mirrors the value
/// onto the linked text entity, and publishes <see cref="TextInputChanged"/> when the value
/// actually changed. When the field has a <see cref="TextInputComponent.CaretEntity"/> it
/// also draws a vertical white caret line at the insertion point while focused.
///
/// The keyboard is diffed once per frame in <see cref="PreUpdate"/> (edge-triggered
/// against the previous state) so every focused field shares one diff. Which field
/// is focused is decided by game code — this system only reads the flag (see the
/// "focus is game-owned" premise).
[With(typeof(TextInputComponent))]
public class TextInputSystem : AEntitySetSystem<GameState>
{
    /// The caret is a thin vertical white line drawn at the insertion point while focused.
    private static readonly Color CaretColor = Color.White;
    private const float CaretThickness = 1.5f;
    /// Blink period halves: caret is shown for the first half of each full period, hidden the second.
    private const float CaretBlinkHalfPeriod = 0.5f;

    private KeyboardState _previous;
    private readonly List<char> _typed = new();
    private bool _backspace;
    private bool _delete;
    private bool _left;
    private bool _right;
    private bool _home;
    private bool _end;

    // Per-frame cursor read (shared across every focused field, like the keyboard diff). Populated in
    // PreUpdate from the single cursor entity so the caret can be placed at a click point.
    private readonly EntitySet _cursors;
    private bool _hasCursor;
    private bool _cursorClicked; // left button pressed OR released this frame
    private Vector2 _cursorWorld;
    private Vector2 _cursorVirtual;

    // The time of the most recent caret move / edit. Used to show a steady (non-blinking) caret right
    // after typing so the user sees where they are, then resume blinking.
    private float _lastCaretActivity = float.NegativeInfinity;

    /// <summary>
    /// Test/replay seam overriding the hardware keyboard read (the repo idiom — see
    /// <c>KeyChordTracker</c>, <c>EditorShortcutSystem</c>, <c>CursorInputSystem.MouseStateProvider</c>).
    /// Default <c>null</c> → <see cref="Keyboard.GetState"/>, so every existing screen is unchanged.
    /// A scripted driver sets it (<c>textInput.KeyboardStateProvider = pointerReplay.ReadKeyboard</c>)
    /// so a <c>type</c> command reaches the field through THIS system's real per-frame key diff —
    /// mask filtering, caret movement and <see cref="TextInputChanged"/> included — instead of a
    /// driver writing <see cref="TextInputComponent.Text"/> behind the system's back.
    /// </summary>
    public Func<KeyboardState> KeyboardStateProvider { get; set; }

    private KeyboardState ReadKeyboard() =>
        KeyboardStateProvider != null ? KeyboardStateProvider() : Keyboard.GetState();

    public TextInputSystem(World world) : base(world)
    {
        _previous = Keyboard.GetState();
        _cursors = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    protected override void PreUpdate(GameState state)
    {
        _typed.Clear();
        _backspace = _delete = _left = _right = _home = _end = false;

        var cursorEntities = _cursors.GetEntities();
        _hasCursor = cursorEntities.Length > 0;
        if (_hasCursor)
        {
            ref readonly var cursor = ref cursorEntities[0].Get<CursorInputComponent>();
            _cursorClicked = cursor.LeftButtonPressed || cursor.LeftButtonReleased;
            _cursorWorld = cursor.WorldPosition;
            _cursorVirtual = cursor.VirtualPosition;
        }
        else
        {
            _cursorClicked = false;
        }

        var current = ReadKeyboard();
        foreach (var key in current.GetPressedKeys())
        {
            if (_previous.IsKeyDown(key)) continue; // only keys newly pressed this frame
            switch (key)
            {
                case Keys.Back:   _backspace = true; continue;
                case Keys.Delete: _delete = true; continue;
                case Keys.Left:   _left = true; continue;
                case Keys.Right:  _right = true; continue;
                case Keys.Home:   _home = true; continue;
                case Keys.End:    _end = true; continue;
            }
            var c = KeyToChar(key);
            if (c != '\0') _typed.Add(c);
        }

        _previous = current;
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var input = ref entity.Get<TextInputComponent>();
        var text = input.Text ?? string.Empty;

        var caretBefore = input.CaretPosition;

        if (input.Focused)
        {
            ApplyEditing(entity, ref input, ref text);
            PlaceCaretFromClick(entity, ref input, text); // a click inside the field moves the caret
        }

        // Keep the caret valid even when the field isn't focused or the value was changed
        // outside the system (e.g. the game reset it).
        input.CaretPosition = Math.Clamp(input.CaretPosition, 0, text.Length);

        // Show a steady caret right after any caret move / edit, then resume blinking.
        if (input.CaretPosition != caretBefore || text != (input.Text ?? string.Empty))
            _lastCaretActivity = state.TotalTime;

        UpdateDisplayText(input, text);
        UpdateCaretVisual(state, input, text);
    }

    /// When the left button is pressed/released this frame inside the focused field's bounds, places
    /// the caret at the nearest character boundary to the click X. The field's bounds come from its
    /// <see cref="FocusableComponent.Size"/> (or <see cref="SimpleButtonComponent.Size"/>); the text
    /// world-start X is the field's WorldPosition plus the value text's local offset (it's parented
    /// under the field). Mirrors the hit-test convention used elsewhere (Rectangle(WorldPosition,
    /// Size) vs the cursor's world/virtual position by the focusable's target).
    private void PlaceCaretFromClick(in Entity entity, ref TextInputComponent input, string text)
    {
        if (!_hasCursor || !_cursorClicked) return;
        if (!entity.Has<TransformComponent>()) return;
        if (!input.TextEntity.IsAlive || !input.TextEntity.Has<DynamicTextComponent>()) return;

        var size = Vector2.Zero;
        var target = RenderTargetID.Main;
        if (entity.Has<FocusableComponent>())
        {
            ref readonly var f = ref entity.Get<FocusableComponent>();
            size = f.Size;
            target = f.Target;
        }
        else if (entity.Has<SimpleButtonComponent>())
        {
            size = entity.Get<SimpleButtonComponent>().Size;
            target = entity.Get<SimpleButtonComponent>().Target;
        }
        if (size == Vector2.Zero) return;

        var fieldWorld = entity.Get<TransformComponent>().WorldPosition;
        var cursorPos = target == RenderTargetID.Main ? _cursorWorld : _cursorVirtual;
        var bounds = new Rectangle((int)fieldWorld.X, (int)fieldWorld.Y, (int)size.X, (int)size.Y);
        if (!bounds.Contains(cursorPos)) return;

        ref readonly var display = ref input.TextEntity.Get<DynamicTextComponent>();
        if (display.Font is not { } font) return;
        var scale = display.Scale > 0 ? display.Scale : 1f;

        // The value text is a child of the field; its local X is the left padding. World start X of
        // the rendered text = field world X + text-local X.
        var textLocalX = input.TextEntity.Has<TransformComponent>()
            ? input.TextEntity.Get<TransformComponent>().Position.X
            : 0f;
        var textStartX = fieldWorld.X + textLocalX;
        var advance = cursorPos.X - textStartX;

        // Walk prefix widths and pick the caret index whose boundary is nearest the click.
        var best = 0;
        var bestDist = Math.Abs(advance); // distance to the index-0 boundary (x = 0)
        for (var i = 1; i <= text.Length; i++)
        {
            var width = font.MeasureString(text[..i]).Width * scale;
            var dist = Math.Abs(advance - width);
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        input.CaretPosition = best;
    }

    /// Mirrors the value onto the linked text entity, swapping in the placeholder (and its color,
    /// when the field opted into color management via <see cref="TextInputComponent.TextColor"/>)
    /// while the value is empty. Runs every frame so the placeholder reappears when the field is
    /// cleared and the caret still sits at index 0 over it.
    private static void UpdateDisplayText(in TextInputComponent input, string text)
    {
        if (!input.TextEntity.IsAlive || !input.TextEntity.Has<DynamicTextComponent>()) return;

        ref var display = ref input.TextEntity.Get<DynamicTextComponent>();
        var showPlaceholder = string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(input.Placeholder);

        display.TextContent = showPlaceholder ? input.Placeholder : text;

        if (input.TextColor.A > 0)
            display.Color = showPlaceholder && input.PlaceholderColor.A > 0
                ? input.PlaceholderColor
                : input.TextColor;
    }

    /// Applies this frame's caret movement and edits to <paramref name="text"/> and the
    /// field's <see cref="TextInputComponent.CaretPosition"/>, mirrors a changed value onto
    /// the linked text entity, and publishes <see cref="TextInputChanged"/>.
    private void ApplyEditing(in Entity entity, ref TextInputComponent input, ref string text)
    {
        var caret = Math.Clamp(input.CaretPosition, 0, text.Length);

        // Movement first so an arrow pressed alongside a key reads naturally.
        if (_left)  caret = Math.Max(0, caret - 1);
        if (_right) caret = Math.Min(text.Length, caret + 1);
        if (_home)  caret = 0;
        if (_end)   caret = text.Length;

        var original = text;

        if (_backspace && caret > 0)
        {
            text = text.Remove(caret - 1, 1);
            caret--;
        }
        if (_delete && caret < text.Length)
            text = text.Remove(caret, 1);

        foreach (var c in _typed)
        {
            if (input.MaxLength > 0 && text.Length >= input.MaxLength) break;
            if (!Accepts(input.Mask, c)) continue;
            text = text.Insert(caret, c.ToString());
            caret++;
        }

        // Movement alone (no value change) still has to be recorded.
        input.CaretPosition = caret;

        if (text == original) return;

        input.Text = text;
        if (input.TextEntity.IsAlive && input.TextEntity.Has<DynamicTextComponent>())
        {
            ref var display = ref input.TextEntity.Get<DynamicTextComponent>();
            display.TextContent = text;
        }

        World.Publish(new TextInputChanged(entity, text));
    }

    /// Positions and shows the caret line while the field is focused AND in the visible half of the
    /// blink cycle, or hides it (empty mesh, skipped by <c>MasterRenderSystem</c>) otherwise. The
    /// caret entity is parented under the text entity, so its local X is simply the rendered width of
    /// the text up to the caret; its height tracks the text's font line height. No-op when the field
    /// opted out of a caret. The caret shows steadily for one half-period right after an edit / move,
    /// then resumes blinking, so typing is easy to follow.
    private void UpdateCaretVisual(GameState state, in TextInputComponent input, string text)
    {
        if (!input.CaretEntity.IsAlive
            || !input.CaretEntity.Has<DrawComponent>()
            || !input.CaretEntity.Has<TransformComponent>())
            return;

        ref var draw = ref input.CaretEntity.Get<DrawComponent>();

        // Blink: visible in the first half of each ~1s period (0.5s on / 0.5s off). Recent activity
        // forces the on-phase so the caret is steady right after typing / clicking.
        var sinceActivity = state.TotalTime - _lastCaretActivity;
        var blinkOn = sinceActivity < CaretBlinkHalfPeriod
            || (state.TotalTime % (CaretBlinkHalfPeriod * 2f)) < CaretBlinkHalfPeriod;

        if (!input.Focused
            || !blinkOn
            || !input.TextEntity.IsAlive
            || !input.TextEntity.Has<DynamicTextComponent>())
        {
            ClearMesh(draw);
            return;
        }

        ref readonly var display = ref input.TextEntity.Get<DynamicTextComponent>();
        if (display.Font is not { } font)
        {
            ClearMesh(draw);
            return;
        }

        var caret = Math.Clamp(input.CaretPosition, 0, text.Length);
        var advance = caret > 0 ? font.MeasureString(text[..caret]).Width * display.Scale : 0f;
        var height = font.LineHeight * display.Scale;

        var transform = input.CaretEntity.Get<TransformComponent>();
        if (transform.Position.X != advance || transform.Position.Y != 0f)
            transform.Position = new Vector2(advance, 0f);

        // The caret silhouette is stable (height is font-derived), so build it once per focus
        // session and let the transform carry the moving X — rebuild only when it was cleared.
        if (!draw.HasValidMesh)
            draw.SetMeshData(new LineMeshGenerator(Vector2.Zero, new Vector2(0f, height), CaretThickness, CaretColor));
    }

    /// Empties the caret mesh so <c>MasterRenderSystem</c> skips it (<c>HasValidMesh</c> is
    /// false) without removing the entity or its <c>DrawComponent</c>.
    private static void ClearMesh(DrawComponent draw)
    {
        if (draw.Vertices is { Length: 0 }) return; // already hidden
        draw.Type = DrawElementType.Mesh;
        draw.Vertices = Array.Empty<VertexPositionColor>();
        draw.Indices = Array.Empty<int>();
    }

    private static bool Accepts(TextInputMask mask, char c) => mask switch
    {
        TextInputMask.Numeric => c is >= '0' and <= '9',
        _ => true,
    };

    /// Maps a key to a printable character (lowercase, no shift handling — this is a
    /// minimal field). Returns '\0' for keys that produce no character; the mask
    /// does the final filtering.
    private static char KeyToChar(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => (char)('0' + (key - Keys.D0)),
        >= Keys.NumPad0 and <= Keys.NumPad9 => (char)('0' + (key - Keys.NumPad0)),
        >= Keys.A and <= Keys.Z => (char)('a' + (key - Keys.A)),
        Keys.Space => ' ',
        _ => '\0',
    };

    public override void Dispose()
    {
        _cursors.Dispose();
        base.Dispose();
    }
}
