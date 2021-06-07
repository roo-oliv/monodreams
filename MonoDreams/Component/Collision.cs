using DefaultEcs;

namespace MonoDreams.Component
{
    public struct Collision
    {
        public readonly Entity CollidingEntity;

        public Collision(in Entity collidingEntity)
        {
            CollidingEntity = collidingEntity;
        }
    }
}