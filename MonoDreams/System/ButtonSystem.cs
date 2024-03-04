using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System;

public class ButtonSystem : ISystem<GameState>
{
    private readonly List<CollisionMessage> _collisions;
    private Entity? _currentlyActiveButton;
    private readonly World _world;

    public ButtonSystem(World world)
    {
        _world = world;
        world.Subscribe(this);
        _collisions = new List<CollisionMessage>();
    }

    [Subscribe]
    private void On(in CollisionMessage message)
    {
        if (message.CollidingEntity.Has<ButtonState>())
        {
            _collisions.Add(message);
        }
    }

    public bool IsEnabled { get; set; } = true;
        
    public void Dispose()
    {
        _collisions.Clear();
    }

    public void Update(GameState state)
    {
        if (_collisions.Count == 0)
        {
            ResetActiveButtonState();
            return;
        }
        _collisions.Sort((l, r) => l.ContactTime.CompareTo(r.ContactTime));
        var activeButton = _collisions.Last().CollidingEntity;
        if (activeButton == _currentlyActiveButton)
        {
            _collisions.Clear();
            return;
        }
        ResetActiveButtonState();
        ref var buttonState = ref activeButton.Get<ButtonState>();
        buttonState.Select();
        if (activeButton.Has<Text>())
        {
            activeButton.Get<Text>().Color = buttonState.SelectedColor;
        }
        _currentlyActiveButton = activeButton;
        _collisions.Clear();
    }

    private void ResetActiveButtonState()
    {
        if (!_currentlyActiveButton.HasValue)
        {
            return;
        }
        var currentlyActiveButton = _currentlyActiveButton.Value;
        currentlyActiveButton.Get<ButtonState>().Reset();
        if (currentlyActiveButton.Has<Text>())
        {
            currentlyActiveButton.Get<Text>().Color = currentlyActiveButton.Get<ButtonState>().DefaultColor;
        }
        _currentlyActiveButton = null;
    }
}