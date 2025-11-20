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
        // Logic Phases (run during Update pipeline)
        InputPhase = world.Entity("InputPhase").DependsOn(Ecs.OnUpdate);
        LogicPhase = world.Entity("LogicPhase").DependsOn(InputPhase);
        PhysicsPhase = world.Entity("PhysicsPhase").DependsOn(LogicPhase);
        CameraPhase = world.Entity("CameraPhase").DependsOn(PhysicsPhase);

        // Draw Phases (run during Draw pipeline - DrawPhase is a tag, actual phases are Culling -> PrepDraw -> Render)
        DrawPhase = world.Entity("DrawPhase");
        CullingPhase = world.Entity("CullingPhase").DependsOn(Ecs.OnUpdate);
        PrepDrawPhase = world.Entity("PrepDrawPhase").DependsOn(CullingPhase);
        RenderPhase = world.Entity("RenderPhase").DependsOn(PrepDrawPhase);
    }
}
