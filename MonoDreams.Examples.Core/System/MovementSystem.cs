using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.Dialogue;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Input;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class MovementSystem : AEntitySetSystem<GameState>
{
    private bool _dialogueActive;

    public MovementSystem(World world, IParallelRunner parallelRunner)
        : base(world.GetEntities().With<TransformComponent>().With<PlayerState>().AsSet(), parallelRunner)
    {
        world.Subscribe((in DialogueActiveMessage msg) => _dialogueActive = msg.IsActive);
    }

    protected override void Update(GameState state, in Entity entity)
    {
        if (_dialogueActive) return;

        ref var transform = ref entity.Get<TransformComponent>();

        if (InputState.Left.Pressed(state))
            transform.TranslateX(-Constants.MaxWalkVelocity * state.Time);
        if (InputState.Right.Pressed(state))
            transform.TranslateX(Constants.MaxWalkVelocity * state.Time);
        if (InputState.Up.Pressed(state))
            transform.TranslateY(-Constants.MaxWalkVelocity * state.Time);
        if (InputState.Down.Pressed(state))
            transform.TranslateY(Constants.MaxWalkVelocity * state.Time);
    }
}