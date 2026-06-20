using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
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

    private KeyboardState _previous;
    private readonly List<char> _typed = new();
    private bool _backspace;
    private bool _delete;
    private bool _left;
    private bool _right;
    private bool _home;
    private bool _end;

    public TextInputSystem(World world) : base(world)
    {
        _previous = Keyboard.GetState();
    }

    protected override void PreUpdate(GameState state)
    {
        _typed.Clear();
        _backspace = _delete = _left = _right = _home = _end = false;

        var current = Keyboard.GetState();
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

        if (input.Focused)
            ApplyEditing(entity, ref input, ref text);

        // Keep the caret valid even when the field isn't focused or the value was changed
        // outside the system (e.g. the game reset it).
        input.CaretPosition = Math.Clamp(input.CaretPosition, 0, text.Length);

        UpdateCaretVisual(input, text);
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

    /// Positions and shows the caret line while the field is focused, or hides it (empty mesh,
    /// skipped by <c>MasterRenderSystem</c>) otherwise. The caret entity is parented under the
    /// text entity, so its local X is simply the rendered width of the text up to the caret;
    /// its height tracks the text's font line height. No-op when the field opted out of a caret.
    private static void UpdateCaretVisual(in TextInputComponent input, string text)
    {
        if (!input.CaretEntity.IsAlive
            || !input.CaretEntity.Has<DrawComponent>()
            || !input.CaretEntity.Has<TransformComponent>())
            return;

        ref var draw = ref input.CaretEntity.Get<DrawComponent>();

        if (!input.Focused
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
}
