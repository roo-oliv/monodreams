#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.Renderer;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Builds the engine-native editor toolbar (Wave 4b): a fixed row of labelled buttons on the
/// <b>HUD</b> render target (screen-space, never Main), one per <see cref="EditorToolbarAction"/>.
/// Each button reuses the engine's <c>SimpleButtonComponent</c> + <c>ButtonMeshPrepSystem</c> for its
/// outline+fill mesh and a <c>DynamicTextComponent</c> for its label, and carries a
/// <see cref="ToolbarButtonComponent"/> binding the click to its action and recording its
/// screen-space <see cref="ToolbarButtonComponent.Bounds"/> for <c>ToolbarSystem</c>'s hit-test.
/// Web-capable (it is all engine UI components — no ImGui).
///
/// <para><b>Why fixed layout, not the auto-layout solver, drives the bounds.</b> The hit-test needs
/// each button's screen-space rectangle to be known the moment the toolbar is built (deterministic,
/// testable). The auto-layout system only resolves positions after it runs a frame, so this builder
/// lays the row out itself at fixed offsets and writes the exact bounds onto each
/// <see cref="ToolbarButtonComponent"/>. An <see cref="AutoLayoutBuilder"/> root is still created to
/// anchor the toolbar group on the HUD target, keeping it a first-class engine-UI citizen.</para>
/// </summary>
public sealed class EditorToolbarBuilder
{
    private const float ButtonHeight = 22f;
    private const float ButtonGap = 6f;
    private const float Margin = 8f;
    private const float TextScale = 0.12f;
    private const float Padding = 6f;

    private readonly World _world;
    private readonly BitmapFont _font;
    private readonly float _layerDepth;

    public EditorToolbarBuilder(World world, BitmapFont font, float layerDepth = 0.9f)
    {
        _world = world;
        _font = font;
        _layerDepth = layerDepth;
    }

    /// <summary>The default toolbar contents: tool select (move/rotate/scale), Save, Load, Undo,
    /// Redo, and the snap toggle — the item-14 button set.</summary>
    public static readonly (EditorToolbarAction action, string label)[] DefaultButtons =
    {
        (EditorToolbarAction.ToolMove, "Move"),
        (EditorToolbarAction.ToolRotate, "Rotate"),
        (EditorToolbarAction.ToolScale, "Scale"),
        (EditorToolbarAction.Save, "Save"),
        (EditorToolbarAction.Load, "Load"),
        (EditorToolbarAction.Undo, "Undo"),
        (EditorToolbarAction.Redo, "Redo"),
        (EditorToolbarAction.ToggleSnap, "Snap"),
    };

    /// <summary>
    /// Builds the toolbar row and returns the created button entities (in order). The toolbar group
    /// is anchored top-left on the HUD render target. Bounds are written onto each button so the
    /// <c>ToolbarSystem</c> can hit-test immediately.
    /// </summary>
    public List<Entity> Build((EditorToolbarAction action, string label)[]? buttons = null,
        ViewportManager? viewportManager = null)
    {
        buttons ??= DefaultButtons;

        // Anchor an AutoLayoutBuilder root on the HUD target so the toolbar is a first-class engine-UI
        // group (not on Main). The button bounds are computed by this builder (see the class doc).
        if (viewportManager != null)
            new AutoLayoutBuilder(_world, viewportManager)
                .CreateRoot(ScreenAnchor.TopLeft, RenderTargetID.HUD)
                .Name("EditorToolbar")
                .Build();

        var created = new List<Entity>(buttons.Length);
        var x = Margin;
        var y = Margin;

        foreach (var (action, label) in buttons)
        {
            var size = MeasureButton(label);
            var bounds = new Rectangle((int)x, (int)y, (int)size.X, (int)size.Y);
            created.Add(CreateButton(action, label, bounds));
            x += size.X + ButtonGap;
        }

        return created;
    }

    private Vector2 MeasureButton(string label)
    {
        var textSize = _font.MeasureString(label) * TextScale;
        return new Vector2(textSize.Width + Padding * 2f, ButtonHeight);
    }

    private Entity CreateButton(EditorToolbarAction action, string label, Rectangle bounds)
    {
        var container = _world.CreateEntity();
        var transform = new TransformComponent(new Vector2(bounds.X, bounds.Y));
        container.Set(transform);

        // Label text, parented (transform-only) by sharing an offset transform.
        var textEntity = _world.CreateEntity();
        textEntity.Set(new TransformComponent(new Vector2(bounds.X + Padding, bounds.Y + Padding * 0.5f)));
        textEntity.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.HUD,
            LayerDepth = _layerDepth + 0.02f,
            TextContent = label,
            Font = _font,
            Color = Color.White,
            Scale = TextScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        textEntity.Set(new VisibleComponent());

        // The outline + fill mesh (engine button rendering), drawn by ButtonMeshPrepSystem.
        container.Set(new SimpleButtonComponent
        {
            Size = new Vector2(bounds.Width, bounds.Height),
            LineThickness = 1.5f,
            Color = new Color(220, 220, 220),
            FillColor = new Color(40, 40, 48, 220),
            TextEntity = textEntity,
            Target = RenderTargetID.HUD,
            LayerDepth = _layerDepth,
        });
        container.Set(new ToolbarButtonComponent
        {
            Action = action,
            Bounds = bounds,
            IsHovered = false,
            IsActive = false,
        });
        container.Set(new VisibleComponent());

        return container;
    }
}
