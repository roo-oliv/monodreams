using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Drives <see cref="TabBarComponent"/>s. Listens for <see cref="UIFocusActivated"/>: activating a
/// header switches its bar's <see cref="TabBarComponent.Active"/> tab. Each frame it marks the
/// active header <see cref="ButtonStateComponent.IsActive"/> and reconciles every
/// <see cref="TabContentComponent"/> entity: the active tab's bodies get <c>VisibleComponent</c>
/// and their focusables are enabled; inactive tabs are hidden and their focusables disabled (so
/// keyboard/pointer navigation stays within the visible tab).
///
/// <para>One conceptual tab bar per screen: content is matched to the (single) bar's active index.
/// A screen with several independent tab bars would need a bar id on
/// <see cref="TabContentComponent"/>; that is intentionally out of scope here.</para>
/// </summary>
public sealed class TabSystem : ISystem<GameState>
{
    private readonly EntitySet _bars;
    private readonly EntitySet _contents;

    public bool IsEnabled { get; set; } = true;

    public TabSystem(World world)
    {
        _bars = world.GetEntities().With<TabBarComponent>().AsSet();
        _contents = world.GetEntities().With<TabContentComponent>().AsSet();
        world.Subscribe(this);
    }

    [Subscribe]
    private void OnActivated(in UIFocusActivated msg)
    {
        foreach (var barEntity in _bars.GetEntities())
        {
            var bar = barEntity.Get<TabBarComponent>();
            for (var i = 0; i < bar.Tabs.Length; i++)
            {
                if (bar.Tabs[i] != msg.Focused) continue;
                bar.Active = i;
                return;
            }
        }
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var bars = _bars.GetEntities();
        if (bars.Length == 0) return;

        // Highlight the active header on every bar.
        var active = 0;
        foreach (var barEntity in bars)
        {
            var bar = barEntity.Get<TabBarComponent>();
            active = bar.Active; // single-bar assumption: last bar's active drives content
            for (var i = 0; i < bar.Tabs.Length; i++)
            {
                var header = bar.Tabs[i];
                if (header.IsAlive && header.Has<ButtonStateComponent>())
                    header.Get<ButtonStateComponent>().IsActive = i == bar.Active;
            }
        }

        // Show the active tab's bodies, hide the rest; gate their focus accordingly.
        foreach (var e in _contents.GetEntities())
        {
            var show = e.Get<TabContentComponent>().TabIndex == active;

            var hasVisible = e.Has<VisibleComponent>();
            if (show && !hasVisible) e.Set<VisibleComponent>();
            else if (!show && hasVisible) e.Remove<VisibleComponent>();

            if (e.Has<FocusableComponent>())
                e.Get<FocusableComponent>().Disabled = !show;
        }
    }

    public void Dispose()
    {
        _bars.Dispose();
        _contents.Dispose();
    }
}
