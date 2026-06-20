using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Extension;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Demos.UI;

/// Helpers for building demo text entities and clickable menu buttons.
/// Centralizes the layout-friendly entity composition (transform + draw + outline
/// + interaction) that every demo screen would otherwise repeat.
public static class DemoUI
{
    public static Entity CreateText(
        World world,
        string text,
        BitmapFont font,
        Color color,
        float scale,
        float layerDepth,
        RenderTargetID target = RenderTargetID.Main)
    {
        var entity = world.CreateEntity();
        entity.Set(new TransformComponent(Vector2.Zero));
        entity.Set(new DynamicTextComponent
        {
            Target = target,
            LayerDepth = layerDepth,
            TextContent = text,
            Font = font,
            Color = color,
            Scale = scale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        entity.Set<VisibleComponent>();
        return entity;
    }

    public static Vector2 MeasureText(Entity entity)
    {
        if (!entity.Has<DynamicTextComponent>()) return Vector2.Zero;
        ref var text = ref entity.Get<DynamicTextComponent>();
        var measured = text.Font.MeasureString(text.TextContent);
        return new Vector2(measured.Width * text.Scale, measured.Height * text.Scale);
    }

    /// Creates a clickable menu button with a text label. Returns
    /// (container, outline, size) so the caller can attach the container to an
    /// AutoLayout slot and flip the active flag on the outline entity when the
    /// underlying selection changes.
    ///
    /// Visual = the greyscale ramp: grey outline + dark label, white fill that darkens
    /// to light/medium grey on hover/press, and a muted darker-grey fill when
    /// <paramref name="disabled"/>. The fill mesh is baked by <c>ButtonMeshPrepSystem</c> at
    /// depth 0.95, so the label is placed just above it (<paramref name="textLayerDepth"/>
    /// should sit above the fill — see the call sites).
    public static (Entity container, Entity outline, Vector2 size) CreateButton(
        World world,
        string id,
        string label,
        BitmapFont font,
        ButtonStyle style,
        float textLayerDepth,
        RenderTargetID target = RenderTargetID.Main,
        bool disabled = false)
    {
        var textSize = font.MeasureString(label) * style.TextScale;
        var buttonSize = new Vector2(
            textSize.Width + style.Padding * 2,
            textSize.Height + style.Padding * 2);

        var container = world.CreateEntity();
        var containerTransform = new TransformComponent(Vector2.Zero);
        container.Set(containerTransform);

        var textEntity = world.CreateEntity();
        textEntity.Set(new TransformComponent(new Vector2(style.Padding, style.Padding)));
        textEntity.SetParent(container);
        textEntity.Set(new DynamicTextComponent
        {
            Target = target,
            LayerDepth = textLayerDepth,
            TextContent = label,
            Font = font,
            Color = disabled ? DemoPalette.ButtonTextDisabled : DemoPalette.ButtonText,
            Scale = style.TextScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        textEntity.Set<VisibleComponent>();

        var outline = world.CreateEntity();
        outline.Set(containerTransform);
        outline.Set(new SimpleButtonComponent
        {
            Size = buttonSize,
            LineThickness = style.BorderThickness,
            Color = disabled ? DemoPalette.ButtonTextDisabled : DemoPalette.ButtonOutline,
            FillColor = disabled ? DemoPalette.ButtonFillDisabled : DemoPalette.ButtonFill,
            TextEntity = textEntity,
            Target = target,
        });
        outline.Set(new DemoButtonComponent
        {
            Id = id,
            // Outline stays a constant grey across states; the label is a constant dark
            // (TextColorOverride); only the fill moves white -> light -> darker grey.
            DefaultColor = DemoPalette.ButtonOutline,
            HoveredColor = DemoPalette.ButtonOutline,
            ActiveColor = DemoPalette.ButtonOutline,
            TextColorOverride = DemoPalette.ButtonText,
            DefaultFillColor = DemoPalette.ButtonFill,
            HoveredFillColor = DemoPalette.ButtonFillHover,
            ActiveFillColor = DemoPalette.ButtonFillActive,
            IsDisabled = disabled,
            DisabledColor = DemoPalette.ButtonTextDisabled,
            DisabledFillColor = DemoPalette.ButtonFillDisabled,
        });
        outline.Set<VisibleComponent>();

        return (container, outline, buttonSize);
    }
}

/// Style data for a key-cap visual: a small mesh square (outline + fill) with the
/// key glyph centered, plus the per-cap pixel size and label scale/color.
public class KeyCapStyle
{
    public Color FillColor { get; init; } = DemoPalette.DarkBgSecondary;
    public Color OutlineColor { get; init; } = DemoPalette.TextLight;
    public float OutlineThickness { get; init; } = 1.5f;
    public int CapPixels { get; init; } = 32;
    public float CapLabelScale { get; init; } = 0.13f;
    public Color CapLabelColor { get; init; } = DemoPalette.TextLight;
}

/// Style data for the row that follows a key cap (the wordy label).
public class KeyRowStyle
{
    public Color LabelColor { get; init; } = Color.Black;
    public Color HoverColor { get; init; } = Color.OrangeRed;
    public Color ActiveColor { get; init; } = Color.DarkGreen;
    public float LabelScale { get; init; } = 0.14f;
    public float Gap { get; init; } = 8f;

    /// Optional row background. When <see cref="BackgroundColor"/> has alpha > 0,
    /// the row's <see cref="SimpleButtonComponent.FillColor"/> is set and the hover /
    /// active states use the matching tint. The row's bounding box (and hit-test
    /// region) expand by <see cref="BackgroundPaddingX"/> / <see cref="BackgroundPaddingY"/>
    /// so the bg gets a margin around the content.
    public Color BackgroundColor { get; init; } = Color.Transparent;
    public Color HoverBackgroundColor { get; init; } = Color.Transparent;
    public Color ActiveBackgroundColor { get; init; } = Color.Transparent;
    public float BackgroundPaddingX { get; init; } = 0f;
    public float BackgroundPaddingY { get; init; } = 0f;
}

/// Style data for a number-input row: an editable box plus its label. The box's
/// border + value text recolor on hover/focus via <see cref="DemoButtonComponent"/>
/// (focus is shown by reusing the button's <c>IsActive</c> accent), while the box
/// fill stays constant.
public class NumberInputStyle
{
    public Color LabelColor { get; init; } = Color.White;
    /// Border + value-text color when idle.
    public Color AccentColor { get; init; } = Color.White;
    /// Border + value-text color on hover.
    public Color HoverColor { get; init; } = Color.Yellow;
    /// Border + value-text color while focused.
    public Color FocusColor { get; init; } = Color.Gold;
    /// Box background fill (constant across states).
    public Color FillColor { get; init; } = Color.Transparent;
    public float BorderThickness { get; init; } = 2f;
    public float LabelScale { get; init; } = 0.18f;
    public float TextScale { get; init; } = 0.2f;
    public float Gap { get; init; } = 10f;
    public Vector2 BoxSize { get; init; } = new(48, 30);
    public float BoxPadding { get; init; } = 6f;
}

public static partial class DemoUIRowExtensions
{
    /// Composite "number box + label" row. The box is a <see cref="SimpleButtonComponent"/>
    /// (border + fill + hit-test) carrying a <see cref="TextInputComponent"/>; clicking it
    /// is dispatched as a <see cref="DemoButtonClicked"/> so the screen can focus it, and
    /// <c>TextInputSystem</c> edits the value while focused. Returns the box/input entity
    /// as <c>Outline</c> — set its focus and read its <c>TextInputComponent.Text</c> there.
    public static (Entity Container, Entity Outline, Vector2 Size) CreateNumberInputRow(
        this World world,
        string id,
        string rowLabel,
        string initialValue,
        int maxLength,
        BitmapFont font,
        NumberInputStyle style,
        float layerDepth,
        RenderTargetID target = RenderTargetID.HUD)
    {
        var labelMeasured = font.MeasureString(rowLabel);
        var labelSize = new Vector2(labelMeasured.Width * style.LabelScale,
                                    labelMeasured.Height * style.LabelScale);
        var contentSize = new Vector2(style.BoxSize.X + style.Gap + labelSize.X,
                                      MathHelper.Max(style.BoxSize.Y, labelSize.Y));

        var container = world.CreateEntity();
        var rowTransform = new TransformComponent(Vector2.Zero);
        container.Set(rowTransform);

        // Value text inside the box, vertically centered, left-padded.
        var valueHeight = font.MeasureString("0").Height * style.TextScale;
        var valueText = world.CreateEntity();
        valueText.Set(new TransformComponent(new Vector2(style.BoxPadding, (style.BoxSize.Y - valueHeight) / 2f)));
        valueText.SetParent(container);
        valueText.Set(new DynamicTextComponent
        {
            Target = target,
            LayerDepth = layerDepth + 0.01f,
            TextContent = initialValue,
            Font = font,
            Color = style.AccentColor,
            Scale = style.TextScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        valueText.Set<VisibleComponent>();

        // Caret: a thin vertical line the TextInputSystem positions and shows while the box
        // is focused. Parented to the value text so it shares the text's origin — the system
        // only writes its local X (the rendered width up to the caret) and toggles its mesh.
        var caret = world.CreateEntity();
        caret.Set(new TransformComponent(Vector2.Zero));
        caret.SetParent(valueText);
        caret.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = target,
            LayerDepth = layerDepth + 0.02f, // above the value text and the box outline
        });
        caret.Set<VisibleComponent>();

        // Box outline = clickable hit area + border + fill, plus the input state.
        var outline = world.CreateEntity();
        outline.Set(rowTransform);
        outline.Set(new SimpleButtonComponent
        {
            Size = style.BoxSize,
            LineThickness = style.BorderThickness,
            Color = style.AccentColor,
            FillColor = style.FillColor,
            TextEntity = valueText,
            Target = target,
        });
        outline.Set(new DemoButtonComponent
        {
            Id = id,
            DefaultColor = style.AccentColor,
            HoveredColor = style.HoverColor,
            ActiveColor = style.FocusColor, // "active" == focused for an input box
            DefaultFillColor = style.FillColor,
            HoveredFillColor = style.FillColor,
            ActiveFillColor = style.FillColor,
        });
        outline.Set(new TextInputComponent
        {
            Text = initialValue,
            MaxLength = maxLength,
            Mask = TextInputMask.Numeric,
            Focused = false,
            TextEntity = valueText,
            CaretEntity = caret,
            CaretPosition = initialValue.Length, // start editing at the end of the pre-filled value
        });
        outline.Set<VisibleComponent>();

        // Row label to the right of the box, vertically centered.
        var labelEntity = world.CreateEntity();
        labelEntity.Set(new TransformComponent(
            new Vector2(style.BoxSize.X + style.Gap, (contentSize.Y - labelSize.Y) / 2f)));
        labelEntity.SetParent(container);
        labelEntity.Set(new DynamicTextComponent
        {
            Target = target,
            LayerDepth = layerDepth + 0.01f,
            TextContent = rowLabel,
            Font = font,
            Color = style.LabelColor,
            Scale = style.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        labelEntity.Set<VisibleComponent>();

        return (container, outline, contentSize);
    }

    /// Composite "key cap + label" row used by sidebar menus. The cap shows
    /// a small letter or digit on a sprite background; the row label is the
    /// human-readable command name. The whole row is one clickable hit-box.
    public static (Entity Container, Entity Outline, Vector2 Size) CreateKeyRow(
        this World world,
        string id,
        string keyLabel,
        string rowLabel,
        BitmapFont font,
        KeyCapStyle cap,
        KeyRowStyle row,
        float layerDepth,
        RenderTargetID target = RenderTargetID.HUD)
    {
        var capSize = new Vector2(cap.CapPixels, cap.CapPixels);
        var rowLabelMeasured = font.MeasureString(rowLabel);
        var rowLabelSize = new Vector2(rowLabelMeasured.Width * row.LabelScale,
                                       rowLabelMeasured.Height * row.LabelScale);
        var contentSize = new Vector2(capSize.X + row.Gap + rowLabelSize.X,
                                      MathHelper.Max(capSize.Y, rowLabelSize.Y));
        var padX = row.BackgroundPaddingX;
        var padY = row.BackgroundPaddingY;
        var rowSize = new Vector2(contentSize.X + padX * 2, contentSize.Y + padY * 2);

        var container = world.CreateEntity();
        var rowTransform = new TransformComponent(Vector2.Zero);
        container.Set(rowTransform);

        // Cap — a mesh key-cap square (outline + fill) at the left edge, vertically centered.
        // Drawn at `layerDepth` (above the 0.95 row background) with its glyph just above it.
        var capYOffset = padY + (contentSize.Y - capSize.Y) / 2f;
        var capEntity = world.CreateEntity();
        capEntity.Set(new TransformComponent(new Vector2(padX, capYOffset)));
        capEntity.SetParent(container);
        var capDraw = new DrawComponent { Target = target, LayerDepth = layerDepth };
        capDraw.SetMeshData(ShapeBuilder.Panel(
            new Rectangle(0, 0, cap.CapPixels, cap.CapPixels), cap.FillColor, cap.OutlineColor, cap.OutlineThickness));
        capEntity.Set(capDraw);
        capEntity.Set<VisibleComponent>();

        // Cap label — centered inside the cap.
        var capLabelMeasured = font.MeasureString(keyLabel);
        var capLabelSize = new Vector2(capLabelMeasured.Width * cap.CapLabelScale,
                                       capLabelMeasured.Height * cap.CapLabelScale);
        var capLabelOffset = new Vector2(
            padX + (cap.CapPixels - capLabelSize.X) / 2f,
            capYOffset + (cap.CapPixels - capLabelSize.Y) / 2f);
        var capLabelEntity = world.CreateEntity();
        capLabelEntity.Set(new TransformComponent(capLabelOffset));
        capLabelEntity.SetParent(container);
        capLabelEntity.Set(new DynamicTextComponent
        {
            Target = target,
            LayerDepth = layerDepth + 0.01f,
            TextContent = keyLabel,
            Font = font,
            Color = cap.CapLabelColor,
            Scale = cap.CapLabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        capLabelEntity.Set<VisibleComponent>();

        // Row label — to the right of the cap, vertically centered.
        var rowLabelYOffset = padY + (contentSize.Y - rowLabelSize.Y) / 2f;
        var rowLabelEntity = world.CreateEntity();
        rowLabelEntity.Set(new TransformComponent(new Vector2(padX + capSize.X + row.Gap, rowLabelYOffset)));
        rowLabelEntity.SetParent(container);
        rowLabelEntity.Set(new DynamicTextComponent
        {
            Target = target,
            LayerDepth = layerDepth + 0.01f,
            TextContent = rowLabel,
            Font = font,
            Color = row.LabelColor,
            Scale = row.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        rowLabelEntity.Set<VisibleComponent>();

        // Outline carries hit-test + click dispatch + recolor link.
        var outline = world.CreateEntity();
        outline.Set(rowTransform);
        outline.Set(new SimpleButtonComponent
        {
            Size = rowSize,
            LineThickness = 0f,
            Color = Color.Transparent,
            FillColor = row.BackgroundColor,
            TextEntity = rowLabelEntity,
            Target = target,
        });
        outline.Set(new DemoButtonComponent
        {
            Id = id,
            DefaultColor = row.LabelColor,
            HoveredColor = row.HoverColor,
            ActiveColor = row.ActiveColor,
            DefaultFillColor = row.BackgroundColor,
            HoveredFillColor = row.HoverBackgroundColor,
            ActiveFillColor = row.ActiveBackgroundColor,
        });
        outline.Set<VisibleComponent>();

        return (container, outline, rowSize);
    }

    /// Composite "checkbox + label" row. Click anywhere on the row to flip the checkbox.
    /// The box is a static mesh square (white outline, black fill); a white checkmark mesh
    /// shows/hides with <see cref="ToggleSwitchComponent.On"/> via <c>ToggleSwitchSystem</c>;
    /// the screen subscribing to <see cref="DemoButtonClicked"/> flips the bool.
    public static (Entity Container, Entity Outline, Vector2 Size) CreateCheckboxRow(
        this World world,
        string id,
        string rowLabel,
        BitmapFont font,
        bool initiallyOn,
        float boxSize,
        KeyRowStyle row,
        float layerDepth,
        RenderTargetID target = RenderTargetID.HUD)
    {
        var rowLabelMeasured = font.MeasureString(rowLabel);
        var rowLabelSize = new Vector2(rowLabelMeasured.Width * row.LabelScale,
                                       rowLabelMeasured.Height * row.LabelScale);
        var contentSize = new Vector2(boxSize + row.Gap + rowLabelSize.X,
                                      MathHelper.Max(boxSize, rowLabelSize.Y));
        var padX = row.BackgroundPaddingX;
        var padY = row.BackgroundPaddingY;
        var rowSize = new Vector2(contentSize.X + padX * 2, contentSize.Y + padY * 2);
        var boxRect = new Rectangle(0, 0, (int)boxSize, (int)boxSize);

        var container = world.CreateEntity();
        var rowTransform = new TransformComponent(Vector2.Zero);
        container.Set(rowTransform);

        // Checkbox box — a static mesh square (white outline, black fill) at left, centered.
        var boxYOffset = padY + (contentSize.Y - boxSize) / 2f;
        var boxEntity = world.CreateEntity();
        boxEntity.Set(new TransformComponent(new Vector2(padX, boxYOffset)));
        boxEntity.SetParent(container);
        var boxDraw = new DrawComponent { Target = target, LayerDepth = layerDepth };
        boxDraw.SetMeshData(ShapeBuilder.Panel(boxRect, Color.Black, Color.White, 1.5f));
        boxEntity.Set(boxDraw);
        boxEntity.Set<VisibleComponent>();

        // Checkmark — child of the box, shown only while On (ToggleSwitchSystem fills/empties it).
        var checkMesh = ShapeBuilder.Checkmark(boxRect, 2.5f, Color.White).Generate();
        var checkEntity = world.CreateEntity();
        checkEntity.Set(new TransformComponent(Vector2.Zero));
        checkEntity.SetParent(boxEntity);
        var checkDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = target, LayerDepth = layerDepth + 0.01f };
        if (initiallyOn) checkDraw.SetMeshData(checkMesh);
        checkEntity.Set(checkDraw);
        checkEntity.Set<VisibleComponent>();

        // Row label at right, vertically centered (shifted by bg padding).
        var rowLabelYOffset = padY + (contentSize.Y - rowLabelSize.Y) / 2f;
        var rowLabelEntity = world.CreateEntity();
        rowLabelEntity.Set(new TransformComponent(new Vector2(padX + boxSize + row.Gap, rowLabelYOffset)));
        rowLabelEntity.SetParent(container);
        rowLabelEntity.Set(new DynamicTextComponent
        {
            Target = target,
            LayerDepth = layerDepth + 0.01f,
            TextContent = rowLabel,
            Font = font,
            Color = row.LabelColor,
            Scale = row.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        rowLabelEntity.Set<VisibleComponent>();

        // Outline + hit-test + toggle state component.
        var outline = world.CreateEntity();
        outline.Set(rowTransform);
        outline.Set(new SimpleButtonComponent
        {
            Size = rowSize,
            LineThickness = 0f,
            Color = Color.Transparent,
            FillColor = row.BackgroundColor,
            TextEntity = rowLabelEntity,
            Target = target,
        });
        outline.Set(new DemoButtonComponent
        {
            Id = id,
            DefaultColor = row.LabelColor,
            HoveredColor = row.HoverColor,
            ActiveColor = row.ActiveColor,
            DefaultFillColor = row.BackgroundColor,
            HoveredFillColor = row.HoverBackgroundColor,
            ActiveFillColor = row.ActiveBackgroundColor,
        });
        outline.Set(new ToggleSwitchComponent
        {
            On = initiallyOn,
            CheckmarkEntity = checkEntity,
            CheckmarkMesh = checkMesh,
        });
        outline.Set<VisibleComponent>();

        return (container, outline, rowSize);
    }
}