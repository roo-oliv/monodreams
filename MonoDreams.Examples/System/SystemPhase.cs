using Flecs.NET.Core;

namespace MonoDreams.Examples.System;

public static class SystemPhase
{
    public static Entity InputPhase;
    public static Entity LogicPhase;
    public static Entity PhysicsPhase;
    public static Entity CameraPhase;
    public static Entity DrawPhase;
    public static Entity CullingPhase;
    public static Entity PrepDrawPhase;
    public static Entity RenderPhase;

    public static void Initialize(World world)
    {
        // Logic Phases
        InputPhase = world.Entity("InputPhase").DependsOn(Ecs.OnUpdate);
        LogicPhase = world.Entity("LogicPhase").DependsOn(InputPhase);
        PhysicsPhase = world.Entity("PhysicsPhase").DependsOn(LogicPhase);
        CameraPhase = world.Entity("CameraPhase").DependsOn(PhysicsPhase);
        
        // Draw Phases
        CullingPhase = world.Entity("CullingPhase").DependsOn(Ecs.OnUpdate);
        PrepDrawPhase = world.Entity("PrepDrawPhase").DependsOn(CullingPhase);
        RenderPhase = world.Entity("RenderPhase").DependsOn(PrepDrawPhase);
    }
}
