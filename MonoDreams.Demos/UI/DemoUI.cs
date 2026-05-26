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
    public static (Entity container, Entity outline, Vector2 size) CreateButton(
        World world,
        string id,
        string label,
        BitmapFont font,
        ButtonStyle style,
        float textLayerDepth,
        RenderTargetID target = RenderTargetID.Main,
        Color? activeColor = null)
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
            Color = style.DefaultColor,
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
            Color = style.BorderColor,
            TextEntity = textEntity,
            Target = target,
        });
        outline.Set(new DemoButtonComponent
        {
            Id = id,
            DefaultColor = style.DefaultColor,
            HoveredColor = style.HoveredColor,
            ActiveColor = activeColor ?? style.HoveredColor,
        });
        outline.Set<VisibleComponent>();

        return (container, outline, buttonSize);
    }

    /// Creates an icon-only clickable button (used for back / exit chrome). The
    /// container hosts a child sprite for the icon and an outline entity for
    /// hit-testing. Outline color is transparent so no border draws — interaction
    /// is signaled by recoloring the icon sprite on hover instead.
    public static (Entity container, Entity outline, Vector2 size) CreateIconButton(
        World world,
        string id,
        Texture2D iconSheet,
        Rectangle sourceRect,
        int sizePx,
        Color defaultTint,
        Color hoverTint,
        float layerDepth,
        RenderTargetID target = RenderTargetID.Main)
    {
        var buttonSize = new Vector2(sizePx, sizePx);
        var container = world.CreateEntity();
        var transform = new TransformComponent(Vector2.Zero);
        container.Set(transform);

        // Icon sprite child — sits at (0,0) inside the container, scaled to sizePx.
        var icon = world.CreateEntity();
        icon.Set(new TransformComponent(
            Vector2.Zero,
            0f,
            new Vector2((float)sizePx / sourceRect.Width, (float)sizePx / sourceRect.Height)));
        icon.SetParent(container);
        icon.Set(new SpriteInfoComponent
        {
            SpriteSheet = iconSheet,
            Source = sourceRect,
            Size = buttonSize,
            Color = defaultTint,
            Target = target,
            LayerDepth = layerDepth,
        });
        icon.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = target });
        icon.Set<VisibleComponent>();

        // Outline entity: invisible (transparent), supplies SimpleButton for hit-test +
        // DemoButton for click dispatch. TextEntity points at the icon so the interaction
        // system can recolor it on hover/active.
        var outline = world.CreateEntity();
        outline.Set(transform);
        outline.Set(new SimpleButtonComponent
        {
            Size = buttonSize,
            LineThickness = 0f,
            Color = Color.Transparent,
            TextEntity = null,
            Target = target,
        });
        outline.Set(new DemoButtonComponent
        {
            Id = id,
            DefaultColor = defaultTint,
            HoveredColor = hoverTint,
            ActiveColor = hoverTint,
        });
        outline.Set<VisibleComponent>();

        // Cross-link via a side component so a small extra system can recolor the icon.
        outline.Set(new IconRecolorTarget { Icon = icon });

        return (container, outline, buttonSize);
    }
}

/// Style data for the Sprout Lands key-cap visual: a sprite-sheet with the
/// default/hover/active button frames, the per-cap pixel size, and the
/// label scale/color.
public class KeyCapStyle
{
    public Texture2D SpriteSheet { get; init; } = null!;
    public Rectangle DefaultSource { get; init; }
    public Rectangle HoverSource { get; init; }
    public Rectangle ActiveSource { get; init; }
    public int CapPixels { get; init; } = 32;
    public float CapLabelScale { get; init; } = 0.13f;
    public Color CapLabelColor { get; init; } = Color.Black;
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

public static partial class DemoUIRowExtensions
{
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

        // Cap sprite — at left edge of row, vertically centered. Shifted by bg padding.
        var capYOffset = padY + (contentSize.Y - capSize.Y) / 2f;
        var spriteEntity = world.CreateEntity();
        spriteEntity.Set(new TransformComponent(new Vector2(padX, capYOffset)));
        spriteEntity.SetParent(container);
        spriteEntity.Set(new SpriteInfoComponent
        {
            SpriteSheet = cap.SpriteSheet,
            Source = cap.DefaultSource,
            Size = capSize,
            Color = Color.White,
            Target = target,
            LayerDepth = layerDepth,
        });
        spriteEntity.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = target });
        spriteEntity.Set<VisibleComponent>();

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
        outline.Set(new IconRecolorTarget
        {
            Icon = spriteEntity,
            DefaultSource = cap.DefaultSource,
            HoverSource = cap.HoverSource,
            ActiveSource = cap.ActiveSource,
        });
        outline.Set<VisibleComponent>();

        return (container, outline, rowSize);
    }

    /// Composite "toggle pill + label" row. Click anywhere on the row to flip
    /// the toggle. The toggle sprite swaps its source rectangle based on
    /// <see cref="ToggleSwitchComponent.On"/> via <c>ToggleSwitchSystem</c>;
    /// the screen subscribing to <see cref="DemoButtonClicked"/> flips the bool.
    public static (Entity Container, Entity Outline, Vector2 Size) CreateToggleRow(
        this World world,
        string id,
        string rowLabel,
        BitmapFont font,
        Texture2D toggleSheet,
        Rectangle offSource,
        Rectangle onSource,
        bool initiallyOn,
        Vector2 toggleSize,
        KeyRowStyle row,
        float layerDepth,
        RenderTargetID target = RenderTargetID.HUD)
    {
        var rowLabelMeasured = font.MeasureString(rowLabel);
        var rowLabelSize = new Vector2(rowLabelMeasured.Width * row.LabelScale,
                                       rowLabelMeasured.Height * row.LabelScale);
        var contentSize = new Vector2(toggleSize.X + row.Gap + rowLabelSize.X,
                                      MathHelper.Max(toggleSize.Y, rowLabelSize.Y));
        var padX = row.BackgroundPaddingX;
        var padY = row.BackgroundPaddingY;
        var rowSize = new Vector2(contentSize.X + padX * 2, contentSize.Y + padY * 2);

        var container = world.CreateEntity();
        var rowTransform = new TransformComponent(Vector2.Zero);
        container.Set(rowTransform);

        // Toggle sprite at left, vertically centered (shifted by bg padding).
        var toggleYOffset = padY + (contentSize.Y - toggleSize.Y) / 2f;
        var spriteEntity = world.CreateEntity();
        spriteEntity.Set(new TransformComponent(new Vector2(padX, toggleYOffset)));
        spriteEntity.SetParent(container);
        spriteEntity.Set(new SpriteInfoComponent
        {
            SpriteSheet = toggleSheet,
            Source = initiallyOn ? onSource : offSource,
            Size = toggleSize,
            Color = Color.White,
            Target = target,
            LayerDepth = layerDepth,
        });
        spriteEntity.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = target });
        spriteEntity.Set<VisibleComponent>();

        // Row label at right, vertically centered (shifted by bg padding).
        var rowLabelYOffset = padY + (contentSize.Y - rowLabelSize.Y) / 2f;
        var rowLabelEntity = world.CreateEntity();
        rowLabelEntity.Set(new TransformComponent(new Vector2(padX + toggleSize.X + row.Gap, rowLabelYOffset)));
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
            OffSource = offSource,
            OnSource = onSource,
            SpriteEntity = spriteEntity,
        });
        outline.Set<VisibleComponent>();

        return (container, outline, rowSize);
    }
}

/// Pointer from a button's outline entity to its sprite child entity.
/// Read by <see cref="DemoIconRecolorSystem"/> to reflect hover/active state.
/// If any of the three source rects are non-null, the system swaps the sprite's
/// source rectangle by state. Otherwise it falls back to recoloring the sprite tint.
public struct IconRecolorTarget
{
    public Entity Icon;
    public Rectangle? DefaultSource;
    public Rectangle? HoverSource;
    public Rectangle? ActiveSource;
}
