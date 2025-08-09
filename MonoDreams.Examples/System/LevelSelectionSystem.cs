using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Level;
using MonoDreams.Examples.Objects;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;
using ButtonState = Microsoft.Xna.Framework.Input.ButtonState;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;
using DynamicText = MonoDreams.Examples.Component.Draw.DynamicText;
using SimpleText = MonoDreams.Examples.Objects.SimpleText;

namespace MonoDreams.Examples.System;

[With(typeof(LevelSelector))]
public class LevelSelectionSystem(World world) : AEntitySetSystem<GameState>(world)
{
    private EntitySet _levelEntities = world.GetEntities().With<LevelEntity>().AsSet();

    protected override void Update(GameState state, in Entity entity)
    {
        if (!entity.Has<LevelSelector>()) return;
        ref var levelSelector = ref entity.Get<LevelSelector>();
        ref var transform = ref entity.Get<Transform>();
        ref var cursor = ref world.GetEntities().With<CursorInput>().AsEnumerable().FirstOrDefault().Get<CursorInput>();

        // Check if cursor is within button bounds (white square outline)
        var buttonSize = new Vector2(40, 40); // Adjust size as needed
        var buttonBounds = new Rectangle(
            (int)(transform.CurrentPosition.X - buttonSize.X / 2),
            (int)(transform.CurrentPosition.Y - buttonSize.Y / 2),
            (int)buttonSize.X,
            (int)buttonSize.Y);

        bool isMouseOver = buttonBounds.Contains(cursor.WorldPosition);

        // Update selection state
        if (isMouseOver)
        {
            levelSelector.IsSelected = true;

            // Handle click to start level
            if (cursor.LeftButtonReleased)
            {
                StartLevel(levelSelector.LevelIndex);
            }

            // Update text color if there's a DynamicText component
            if (entity.Has<DynamicText>())
            {
                ref var text = ref entity.Get<DynamicText>();
                text.Color = levelSelector.SelectedColor;
            }
        }
        else
        {
            levelSelector.IsSelected = false;

            // Reset text color
            if (entity.Has<DynamicText>())
            {
                ref var text = ref entity.Get<DynamicText>();
                text.Color = levelSelector.DefaultColor;
            }
        }
    }

    private void StartLevel(int levelIndex)
    {
        if (levelIndex == -1) // Special case for returning to menu
        {
            ReturnToLevelSelection();
            return;
        }
        
        // Clear level selection entities
        foreach (var entity in _levelEntities.GetEntities())
        {
            entity.Dispose();
        }

        // Load the level
        LoadLevel(levelIndex);
    }

    public void ReturnToLevelSelection()
    {
        // Clear level entities
        foreach (var entity in _levelEntities.GetEntities())
        {
            entity.Dispose();
        }

        // Create level selection UI
        CreateLevelSelectionUI();
    }

    private void LoadLevel(int levelIndex)
    {
        switch (levelIndex)
        {
            case 0:
                LoadLevel0();
                break;
            case 1:
                LoadLevel1();
                break;
            case 2:
                LoadLevel2();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(levelIndex), "Invalid level index");
        }
    }
    
    private void LoadLevel0()
    {
        var content = World.Get<ContentProvider>().Content;
        var font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");

        Track.Create0(World);
        LevelBoundary.Create0(World);
        Car.Create(World, content.Load<Texture2D>("Characters/SportsRacingCar_0"));
        var padding = -230f;
        TrackStat.Create(World, StatType.TopSpeed, font, new Vector2(-400, padding), RenderTargetID.Main, new Color(255, 201, 7));
        SimpleText.Create(World, "*", font, new Vector2(-270, padding), RenderTargetID.Main, 0.2f);
        TrackStat.Create(World, StatType.OvertakingSpots, font, new Vector2(-200, padding), RenderTargetID.Main, new Color(203, 30, 75));
        SimpleText.Create(World, "=", font, new Vector2(0, padding), RenderTargetID.Main, 0.3f);
        TrackStat.Create(World, StatType.Score, font, new Vector2(80, padding), RenderTargetID.Main);
        TrackGradeDisplay.Create(World, font, new Vector2(300, 220), RenderTargetID.Main);
        CreateBackButton(font, new Vector2(400, -230));
        SimpleText.Create(World, 
            "Welcome racing track designer! Let me explain how this thing works:\n \n \n" +
            "The track has a few control points that allow you to change its shape.\n \n \n" +
            "A Yellow Pin will mark the point where top speed is reach on the track.\n \n \n" +
            "And Red Pins will appear whenever you create a good overtaking spot!\n \n" +
            "These spots appear when you force a sharp deceleration after a long high-speed straight.\n \n \n" +
            "The track cannot go out of bounds or go over a tree, otherwise X marks will\n \n" +
            "indicate the invalidation points and your score will be zeroed.\n \n \n" +
            "The score is calculated based on the number of overtaking spots and the top speed.\n \n \n" +
            "Good luck!",
            font, new Vector2(-380, padding+50), RenderTargetID.Main, 0.1f, Color.White, 15f);
    }
    private void LoadLevel1()
    {
        var content = World.Get<ContentProvider>().Content;
        var font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");

        Track.Create(World);
        LevelBoundary.Create(World);
        Car.Create(World, content.Load<Texture2D>("Characters/SportsRacingCar_0"));
        var padding = -230f;
        TrackStat.Create(World, StatType.TopSpeed, font, new Vector2(-400, padding), RenderTargetID.Main, new Color(255, 201, 7));
        SimpleText.Create(World, "*", font, new Vector2(-270, padding), RenderTargetID.Main, 0.2f);
        TrackStat.Create(World, StatType.OvertakingSpots, font, new Vector2(-200, padding), RenderTargetID.Main, new Color(203, 30, 75));
        SimpleText.Create(World, "=", font, new Vector2(0, padding), RenderTargetID.Main, 0.3f);
        TrackStat.Create(World, StatType.Score, font, new Vector2(80, padding), RenderTargetID.Main);
        TrackGradeDisplay.Create(World, font, new Vector2(300, 220), RenderTargetID.Main);
        CreateBackButton(font, new Vector2(400, -230));
    }
    
    private void LoadLevel2()
    {
        var content = World.Get<ContentProvider>().Content;
        var font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");

        Track.Create2(World);
        LevelBoundary.Create2(World);
        Car.Create(World, content.Load<Texture2D>("Characters/SportsRacingCar_0"));
        var padding = -230f;
        TrackStat.Create(World, StatType.TopSpeed, font, new Vector2(-400, padding), RenderTargetID.Main, new Color(255, 201, 7));
        SimpleText.Create(World, "*", font, new Vector2(-270, padding), RenderTargetID.Main, 0.2f);
        TrackStat.Create(World, StatType.OvertakingSpots, font, new Vector2(-200, padding), RenderTargetID.Main, new Color(203, 30, 75));
        SimpleText.Create(World, "=", font, new Vector2(0, padding), RenderTargetID.Main, 0.3f);
        TrackStat.Create(World, StatType.Score, font, new Vector2(80, padding), RenderTargetID.Main);
        TrackGradeDisplay.Create(World, font, new Vector2(300, 220), RenderTargetID.Main);
        CreateBackButton(font, new Vector2(400, -230));
    }

    private void CreateLevelSelectionUI()
    {
        var content = World.Get<ContentProvider>().Content;
        var font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");

        // Create title
        SimpleText.Create(World, "Mini Track", font, new Vector2(-200, -200), RenderTargetID.Main, 0.6f, new Color(255, 201, 7));

        // Create level button
        CreateLevelButton("00", 0, font, new Vector2(-80, 0));
        CreateLevelButton("01", 1, font, new Vector2(0, 0));
        CreateLevelButton("02", 2, font, new Vector2(80, 0));
    }
    
    private Entity CreateBackButton(BitmapFont font, Vector2 position)
    {
        var entity = World.CreateEntity();

        // Add components
        entity.Set(new Transform(position));
        entity.Set(new DynamicText
        {
            Target = RenderTargetID.Main,
            LayerDepth = 1.0f,
            TextContent = "Back",
            Font = font,
            Color = Color.White,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
            Scale = 0.15f
        });
        entity.Set(new LevelSelector 
        {
            LevelIndex = -1,
            LevelName = "Menu",
            DefaultColor = Color.White,
            SelectedColor = new Color(255, 201, 7)
        });
        entity.Set(new Visible());
        entity.Set(new CursorInput());;
        entity.Set(new LevelEntity());

        // Create outline for the button
        CreateButtonOutline(entity, new Vector2(80, 40));

        return entity;
    }

    private Entity CreateLevelButton(string levelName, int levelIndex, BitmapFont font, Vector2 position)
    {
        var entity = World.CreateEntity();

        // Add components
        entity.Set(new Transform(position));
        entity.Set(new DynamicText
        {
            Target = RenderTargetID.Main,
            LayerDepth = 0.95f,
            TextContent = levelName,
            Font = font,
            Color = Color.White,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
            Scale = 0.15f
        });
        entity.Set(new Visible());

        // Add level selector component
        entity.Set(new LevelSelector
        {
            LevelIndex = levelIndex,
            LevelName = levelName,
            DefaultColor = Color.White,
            SelectedColor = new Color(255, 201, 7)
        });
        entity.Set(new CursorInput());;
        entity.Set(new LevelEntity());

        // Create outline for the button
        CreateButtonOutline(entity, new Vector2(40, 40));

        return entity;
    }

    private void CreateButtonOutline(Entity buttonEntity, Vector2 size)
    {
        var entity = World.CreateEntity();

        // Link to button entity's position
        entity.Set(buttonEntity.Get<Transform>());

        // Add outline component
        entity.Set(new ButtonOutline
        {
            Size = size,
            Color = Color.White,
            LineThickness = 2f,
            Target = RenderTargetID.Main
        });

        entity.Set(new LevelEntity());
    }
}
