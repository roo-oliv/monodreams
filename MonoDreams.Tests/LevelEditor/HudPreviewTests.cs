using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the <b>HUD-layer preview</b> (<see cref="HudPreviewSystem"/>): a SCREEN-SPACE scene
/// layer's text members are authored in virtual-resolution HUD coordinates, and while the transport
/// is Paused (<see cref="RunMode.Edit"/>) they are re-projected INTO the camera entity's frame so
/// the HUD reads as scene content the free view pans/zooms over — never as chrome glued to the
/// editor pane.
///
/// The load-bearing claims, one test each:
/// - in Edit with a camera entity, a member lands at <c>camTopLeft + authoredVirtual / zoom</c>, its
///   target flips to <c>Main</c> (the camera-transformed pass) and its text scale divides by zoom;
/// - the projection re-derives from the STASH every frame, so repeated Edit frames never compound;
/// - flipping to Play restores position / scale / target <b>byte-identically</b> and drops the stash
///   (the real HUD pass must render exactly what the game authored);
/// - the layer's eye toggle (<c>Visible = false</c>) parks the member off-screen while KEEPING the
///   stash, so restoring later is still exact;
/// - with NO camera entity (a bare world) the member is left in its authored HUD pass untouched;
/// - a member of a non-screen-space (world) layer is never projected.
///
/// Pure logic: a <see cref="World"/>, hand-built entities, a <see cref="GameState"/> and a
/// <see cref="ViewportManager"/> built with a null <c>Game</c> (it never dereferences it — the
/// established headless pattern in this folder). No <c>GraphicsDevice</c>, no font.
/// </summary>
public class HudPreviewTests
{
    // 800x600 virtual, so a zoom of 2 halves to exact binary fractions: every expected value below
    // is representable, and the assertions can be exact equality rather than an epsilon compare.
    private const int VirtualWidth = 800;
    private const int VirtualHeight = 600;

    private static readonly Vector2 AuthoredPosition = new(100f, 40f);
    private const float AuthoredScale = 0.5f;

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static ViewportManager Vm() => new(null!, VirtualWidth, VirtualHeight);

    /// <summary>The camera entity the preview frames on: an ordinary scene entity carrying
    /// <c>EntityInfoComponent("Camera")</c> + Transform + <see cref="CameraComponent"/> (the one
    /// data model — see the level-editor premise "The editor visualizes + edits the scene camera
    /// ENTITY").</summary>
    private static Entity CameraEntity(World world, Vector2 position, float zoom)
    {
        var e = world.CreateEntity();
        e.Set(new EntityInfoComponent("Camera"));
        e.Set(new TransformComponent(position));
        e.Set(new CameraComponent { Zoom = zoom });
        return e;
    }

    /// <summary>A layer entity shaped exactly like the one the host's <c>AttachHudLayer</c> creates:
    /// named via <c>EntityInfoComponent</c>, a Transform at the origin (so a member's local position
    /// IS its virtual-space position) and the <see cref="SceneLayerComponent"/> that carries the
    /// screen-space flag. No <c>SceneObjectComponent</c> — the HUD grouping never serializes.</summary>
    private static Entity Layer(World world, bool screenSpace, bool visible = true)
    {
        var layer = world.CreateEntity();
        layer.Set(new EntityInfoComponent("Layer", "HUD"));
        layer.Set(new TransformComponent(Vector2.Zero));
        layer.Set(new SceneLayerComponent
        {
            Order = 1000,
            ScreenSpace = screenSpace,
            Visible = visible,
            Locked = true,
        });
        return layer;
    }

    /// <summary>A HUD text member of <paramref name="layer"/>: what the game authored — a
    /// HUD-target <see cref="DynamicTextComponent"/> at a virtual-resolution position.</summary>
    private static Entity HudText(World world, Entity layer)
    {
        var e = world.CreateEntity();
        e.Set(new EntityInfoComponent("Interface"));
        e.Set(new TransformComponent(AuthoredPosition));
        e.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.HUD,
            TextContent = "Score: 0",
            Color = Color.White,
            Scale = AuthoredScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        e.SetParent(layer);
        return e;
    }

    /// <summary>Asserts the member still carries exactly what the game authored, and that the
    /// editor-session stash is gone.</summary>
    private static void AssertAuthored(Entity member)
    {
        Assert.Equal(AuthoredPosition, member.Get<TransformComponent>().Position);
        Assert.Equal(AuthoredScale, member.Get<DynamicTextComponent>().Scale);
        Assert.Equal(RenderTargetID.HUD, member.Get<DynamicTextComponent>().Target);
        Assert.False(member.Has<HudPreviewStashComponent>());
    }

    // ─────────────────────────── the projection ───────────────────────────

    [Fact]
    public void EditWithCamera_ProjectsHudMemberIntoTheCameraFrame()
    {
        using var world = new World();
        CameraEntity(world, new Vector2(500f, 300f), zoom: 2f);
        var member = HudText(world, Layer(world, screenSpace: true));

        using var system = new HudPreviewSystem(world, Vm());
        system.Update(Edit());

        // The frustum covers virtual/zoom world units centred on the camera entity:
        // topLeft = (500,300) - (800,600)/(2*2) = (300,150); the member sits at
        // topLeft + authored/zoom = (300,150) + (50,20).
        Assert.Equal(new Vector2(350f, 170f), member.Get<TransformComponent>().Position);
        // Main is the camera-transformed pass — that is what makes the label pan/zoom with the world.
        Assert.Equal(RenderTargetID.Main, member.Get<DynamicTextComponent>().Target);
        // Glyphs shrink by the same factor the frustum grew by, so the on-screen size is unchanged.
        Assert.Equal(AuthoredScale / 2f, member.Get<DynamicTextComponent>().Scale);
        // The authored values are stashed for the restore.
        Assert.True(member.Has<HudPreviewStashComponent>());
        var stash = member.Get<HudPreviewStashComponent>();
        Assert.Equal(AuthoredPosition, stash.VirtualPosition);
        Assert.Equal(AuthoredScale, stash.TextScale);
        Assert.Equal(RenderTargetID.HUD, stash.Target);
    }

    [Fact]
    public void RepeatedEditFrames_DoNotCompound()
    {
        using var world = new World();
        CameraEntity(world, new Vector2(500f, 300f), zoom: 2f);
        var member = HudText(world, Layer(world, screenSpace: true));

        using var system = new HudPreviewSystem(world, Vm());
        var state = Edit();
        system.Update(state);
        var afterOne = member.Get<TransformComponent>().Position;
        var scaleAfterOne = member.Get<DynamicTextComponent>().Scale;
        system.Update(state);
        system.Update(state);

        // Every frame re-derives from the stash, never from last frame's projected value — the
        // failure mode this guards is a member marching off-screen (or a scale decaying to zero)
        // while the transport sits Paused.
        Assert.Equal(afterOne, member.Get<TransformComponent>().Position);
        Assert.Equal(scaleAfterOne, member.Get<DynamicTextComponent>().Scale);
    }

    // ─────────────────────────── the restore ───────────────────────────

    [Fact]
    public void FlippingToPlay_RestoresAuthoredValuesByteIdenticallyAndDropsTheStash()
    {
        using var world = new World();
        CameraEntity(world, new Vector2(500f, 300f), zoom: 2f);
        var member = HudText(world, Layer(world, screenSpace: true));

        using var system = new HudPreviewSystem(world, Vm());
        system.Update(Edit());
        Assert.NotEqual(AuthoredPosition, member.Get<TransformComponent>().Position); // projected
        system.Update(Play());

        // Exact equality, not an epsilon: the restore assigns the stashed values back, so the REAL
        // HUD pass renders precisely what the game authored (a lossy round-trip would drift the HUD
        // by a pixel every Play/Pause cycle).
        AssertAuthored(member);
    }

    [Fact]
    public void RestoreIsIdempotent()
    {
        using var world = new World();
        CameraEntity(world, new Vector2(500f, 300f), zoom: 2f);
        var member = HudText(world, Layer(world, screenSpace: true));

        using var system = new HudPreviewSystem(world, Vm());
        system.Update(Edit());
        var play = Play();
        system.Update(play);
        system.Update(play);
        system.Update(play);

        AssertAuthored(member);
    }

    // ─────────────────────────── the eye toggle ───────────────────────────

    [Fact]
    public void EyeOffWhilePreviewing_ParksTheMemberAndKeepsTheStash()
    {
        using var world = new World();
        CameraEntity(world, new Vector2(500f, 300f), zoom: 2f);
        var layer = Layer(world, screenSpace: true);
        var member = HudText(world, layer);

        using var system = new HudPreviewSystem(world, Vm());
        var state = Edit();
        system.Update(state);
        layer.Get<SceneLayerComponent>().Visible = false;
        system.Update(state);

        // Parked far off-screen (the same sentinel the systems panel uses for scrolled-out rows) —
        // no per-entity blanking, and the text prep keeps rebuilding it harmlessly.
        Assert.Equal(SystemsPanelLayout.ParkedPosition, member.Get<TransformComponent>().Position);
        // The stash SURVIVES the park, so re-showing (or entering Play) is still an exact restore.
        Assert.True(member.Has<HudPreviewStashComponent>());

        layer.Get<SceneLayerComponent>().Visible = true;
        system.Update(state);
        Assert.Equal(new Vector2(350f, 170f), member.Get<TransformComponent>().Position);

        system.Update(Play());
        AssertAuthored(member);
    }

    // ─────────────────────────── the degenerate cases ───────────────────────────

    [Fact]
    public void NoCameraEntity_LeavesTheMemberUntouched()
    {
        using var world = new World();
        var member = HudText(world, Layer(world, screenSpace: true)); // deliberately no camera

        using var system = new HudPreviewSystem(world, Vm());
        system.Update(Edit());

        // A bare world has no frame to project into — the HUD stays in its authored pass rather
        // than being mapped against a fabricated identity camera.
        AssertAuthored(member);
    }

    [Fact]
    public void AWorldLayerMember_IsNeverProjected()
    {
        using var world = new World();
        CameraEntity(world, new Vector2(500f, 300f), zoom: 2f);
        var member = HudText(world, Layer(world, screenSpace: false));

        using var system = new HudPreviewSystem(world, Vm());
        system.Update(Edit());

        // Only SCREEN-SPACE layers are HUD groupings; a world layer's text is already scene content.
        AssertAuthored(member);
    }
}
