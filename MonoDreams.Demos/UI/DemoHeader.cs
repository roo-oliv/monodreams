using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Extension;
using MonoDreams.Renderer;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Demos.UI;

/// Shared header chrome for every demo screen. Renders three elements on a
/// single horizontal row at the top: BACK chrome at the left, the title +
/// description card centered, and EXIT chrome at the right. All on the HUD
/// target so they stay screen-anchored regardless of camera movement.
///
/// The whole row is one layout tree (with <see cref="MainAxisAlignment.SpaceBetween"/>)
/// rather than three separate root layouts — keeping them in one tree lets the
/// SpaceBetween distribution work AND avoids the screen-root vertical stacking
/// that happens when multiple root layouts coexist.
public static class DemoHeader
{
    public const string BackId = "demo.header.back";
    public const string ExitId = "demo.header.exit";

    private const float HeaderLayerDepth = 0.97f;
    private const int ChipSize = 42;

    // Text scales.
    private const float TitleScale       = 0.30f;
    private const float DescriptionScale = 0.18f;
    private const float ChromeQScale     = 0.20f;       // single-char Q
    private const float ChromeEscScale   = 0.10f;       // 3-char ESC — kept smaller to fit
    private const float ChromeLabelScale = 0.18f;

    /// Build the standard demo header. <paramref name="descriptionLines"/> renders
    /// one centered text entity per line so each row gets its own horizontal centering
    /// — the dynamic text component does not auto-wrap multi-line strings.
    public static void Build(
        World world,
        ViewportManager viewport,
        BitmapFont font,
        Texture2D squareButtonsSheet,
        string title,
        string[] descriptionLines)
    {
        var (titleCardContainer, titleCardSize) = BuildTitleCard(world, font, title, descriptionLines);
        var (backContainer, backSize) = BuildChromeKey(world, font, squareButtonsSheet,
            id: BackId, capLabel: "ESC", rowLabel: "back");
        var (exitContainer, exitSize) = BuildChromeKey(world, font, squareButtonsSheet,
            id: ExitId, capLabel: "Q", rowLabel: "exit");

        new AutoLayoutBuilder(world, viewport)
            .CreateRoot(ScreenAnchor.TopCenter, RenderTargetID.HUD)
            .Direction(LayoutDirection.Horizontal)
            .Width(viewport.VirtualWidth)
            .Padding(8 /* top */, 12 /* right */, 12 /* bottom */, 12 /* left */)
            .AlignMain(MainAxisAlignment.SpaceBetween)
            .AlignCross(CrossAxisAlignment.Start)
            .AddSlot(slot => slot.Attach(backContainer).MeasureWith(_ => backSize))
            .AddSlot(slot => slot.Attach(titleCardContainer).MeasureWith(_ => titleCardSize))
            .AddSlot(slot => slot.Attach(exitContainer).MeasureWith(_ => exitSize))
            .Build();
    }

    /// Builds a centered title + description panel with a shared DarkBgSecondary
    /// background. The container holds the bg (via SimpleButtonComponent.FillColor)
    /// plus the title text and one text entity per description line so each line
    /// is independently horizontally centered.
    private static (Entity Container, Vector2 Size) BuildTitleCard(
        World world, BitmapFont font, string title, string[] descriptionLines)
    {
        const float padX = 22f;
        const float padY = 12f;
        const float gap = 8f;

        var titleMeasured = font.MeasureString(title);
        var titleSize = new Vector2(titleMeasured.Width * TitleScale, titleMeasured.Height * TitleScale);

        var descSizes = new Vector2[descriptionLines.Length];
        for (int i = 0; i < descriptionLines.Length; i++)
        {
            var m = font.MeasureString(descriptionLines[i]);
            descSizes[i] = new Vector2(m.Width * DescriptionScale, m.Height * DescriptionScale);
        }

        var contentW = titleSize.X;
        var contentH = titleSize.Y;
        for (int i = 0; i < descSizes.Length; i++)
        {
            contentW = MathHelper.Max(contentW, descSizes[i].X);
            contentH += gap + descSizes[i].Y;
        }
        var panelSize = new Vector2(contentW + padX * 2, contentH + padY * 2);

        var container = world.CreateEntity();
        container.Set(new TransformComponent(Vector2.Zero));
        container.Set(new SimpleButtonComponent
        {
            Size = panelSize,
            LineThickness = 0f,
            Color = Color.Transparent,
            FillColor = SproutPalette.DarkBgSecondary,
            TextEntity = null,
            Target = RenderTargetID.HUD,
        });
        container.Set<VisibleComponent>();

        // Title — centered horizontally on the first row.
        var titleEntity = world.CreateEntity();
        titleEntity.Set(new TransformComponent(new Vector2(padX + (contentW - titleSize.X) / 2f, padY)));
        titleEntity.SetParent(container);
        titleEntity.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.HUD,
            LayerDepth = HeaderLayerDepth + 0.01f,
            TextContent = title,
            Font = font,
            Color = SproutPalette.TextLight,
            Scale = TitleScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        titleEntity.Set<VisibleComponent>();

        // Description — one text entity per line, each independently centered.
        var yCursor = padY + titleSize.Y + gap;
        for (int i = 0; i < descriptionLines.Length; i++)
        {
            var lineEntity = world.CreateEntity();
            lineEntity.Set(new TransformComponent(new Vector2(padX + (contentW - descSizes[i].X) / 2f, yCursor)));
            lineEntity.SetParent(container);
            lineEntity.Set(new DynamicTextComponent
            {
                Target = RenderTargetID.HUD,
                LayerDepth = HeaderLayerDepth + 0.01f,
                TextContent = descriptionLines[i],
                Font = font,
                Color = SproutPalette.TextHover,
                Scale = DescriptionScale,
                IsRevealed = true,
                VisibleCharacterCount = int.MaxValue,
            });
            lineEntity.Set<VisibleComponent>();
            yCursor += descSizes[i].Y + gap;
        }

        return (container, panelSize);
    }

    /// Builds a CreateKeyRow-style button (cap + label + DarkBgSecondary bg) for
    /// the back / exit chrome — same visual language as the sidebar rows.
    /// Returned as (container, size) for embedding into the shared header layout.
    private static (Entity Container, Vector2 Size) BuildChromeKey(
        World world, BitmapFont font, Texture2D capSheet,
        string id, string capLabel, string rowLabel)
    {
        var capStyle = new KeyCapStyle
        {
            SpriteSheet = capSheet,
            DefaultSource = SproutSquareButtons.CreamLight,
            HoverSource = SproutSquareButtons.CreamDark,
            ActiveSource = SproutSquareButtons.TanDark,
            CapPixels = ChipSize,
            CapLabelScale = capLabel.Length > 1 ? ChromeEscScale : ChromeQScale,
            CapLabelColor = SproutPalette.WarmBrown,
        };
        var rowStyle = new KeyRowStyle
        {
            LabelColor = SproutPalette.TextLight,
            HoverColor = SproutPalette.TextHover,
            ActiveColor = SproutPalette.TextSelected,
            LabelScale = ChromeLabelScale,
            Gap = 8f,
            BackgroundColor = SproutPalette.DarkBgSecondary,
            HoverBackgroundColor = SproutPalette.DarkBgSecondary,
            ActiveBackgroundColor = SproutPalette.DarkBgSecondary,
            BackgroundPaddingX = 10f,
            BackgroundPaddingY = 6f,
        };

        var (container, _, size) = world.CreateKeyRow(
            id: id, keyLabel: capLabel, rowLabel: rowLabel,
            font: font, cap: capStyle, row: rowStyle,
            layerDepth: HeaderLayerDepth, target: RenderTargetID.HUD);

        return (container, size);
    }
}
