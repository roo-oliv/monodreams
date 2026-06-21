using DefaultEcs;
using MonoDreams.Util;

namespace MonoDreams.Message;

public class RigidBodyTouchMessage(Entity touchingEntity, RelativeReferential side)
{
    public Entity TouchingEntity { get; } = touchingEntity;
    public RelativeReferential Side { get; } = side;
}
