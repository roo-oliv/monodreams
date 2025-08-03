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
    private EntitySet _levelSelectionEntities = world.GetEntities().With<LevelEntity>().AsSet();
    private EntitySet _levelEntities = world.GetEntities().With<LevelTag>().AsSet();
    private bool _inLevelSelection = true;

    protected override void Update(GameState state, in Entity entity)
    {
        if (!_inLevelSelection) return;

        ref var levelSelector = ref entity.Get<LevelSelector>();
        ref var transform = ref entity.Get<Transform>();
        ref var cursor = ref world.GetEntities().With<CursorInput>().AsEnumerable().FirstOrDefault().Get<CursorInput>();

        // Check if cursor is within button bounds (white square outline)
        var buttonSize = new Vector2(200, 50); // Adjust size as needed
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
        // Clear level selection entities
        foreach (var entity in _levelSelectionEntities.GetEntities())
        {
            entity.Dispose();
        }

        _inLevelSelection = false;

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

        _inLevelSelection = true;

        // Create level selection UI
        CreateLevelSelectionUI();
    }

    private void LoadLevel(int levelIndex)
    {
        // This is the extracted code from GameJamScreen's Load method
        var content = World.Get<ContentProvider>().Content;

        // Tag the entities as level entities for easy cleanup later
        var levelTag = new LevelTag { LevelIndex = levelIndex };

        // Create the track and related entities
        var trackEntity = Track.Create(World);
        trackEntity.Set(levelTag);

        var levelBoundaryEntity = LevelBoundary.Create(World);
        levelBoundaryEntity.Set(levelTag);

        var carEntity = Car.Create(World, content.Load<Texture2D>("Characters/SportsRacingCar_0"));
        carEntity.Set(levelTag);

        var font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");
        var padding = -230f;

        var topSpeedEntity = TrackStat.Create(World, StatType.TopSpeed, font, new Vector2(-400, padding), RenderTargetID.Main, new Color(255, 201, 7));
        topSpeedEntity.Set(levelTag);

        var multiplyEntity = SimpleText.Create(World, "*", font, new Vector2(-270, padding), RenderTargetID.Main, 0.2f);
        multiplyEntity.Set(levelTag);

        var overtakingEntity = TrackStat.Create(World, StatType.OvertakingSpots, font, new Vector2(-200, padding), RenderTargetID.Main, new Color(203, 30, 75));
        overtakingEntity.Set(levelTag);

        var equalsEntity = SimpleText.Create(World, "=", font, new Vector2(0, padding), RenderTargetID.Main, 0.3f);
        equalsEntity.Set(levelTag);

        var scoreEntity = TrackStat.Create(World, StatType.Score, font, new Vector2(80, padding), RenderTargetID.Main);
        scoreEntity.Set(levelTag);

        var gradeEntity = TrackGradeDisplay.Create(World, font, new Vector2(300, 220), RenderTargetID.Main);
        gradeEntity.Set(levelTag);
    }

    private void CreateLevelSelectionUI()
    {
        var content = World.Get<ContentProvider>().Content;
        var font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");

        // Create title
        SimpleText.Create(World, "Mini Track", font, new Vector2(-200, -200), RenderTargetID.Main, 0.6f, new Color(255, 201, 7));

        // Create level button
        CreateLevelButton("01", 0, font, new Vector2(0, 0));
    }

    private Entity CreateLevelButton(string levelName, int levelIndex, BitmapFont font, Vector2 position)
    {
        var entity = World.CreateEntity();

        // Add components
        entity.Set(new Transform(position));
        entity.Set(new DynamicText
        {
            Target = RenderTargetID.Main,
            LayerDepth = 0.9f,
            TextContent = levelName,
            Font = font,
            Color = Color.White,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
            Scale = 0.3f
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
        CreateButtonOutline(entity, new Vector2(80, 60));

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
            LineThickness = 4f,
            Target = RenderTargetID.Main
        });

        entity.Set(new LevelEntity());
    }
}
