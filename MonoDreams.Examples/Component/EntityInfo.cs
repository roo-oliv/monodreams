namespace MonoDreams.Examples.Component;

public class EntityInfo(EntityType type)
{
    public EntityType Type = type;
}

public enum EntityType
{
    Car,
    Player,
    Enemy,
    Tile,
    Collectible,
    Projectile,
    Zone,
    Interface,
}
