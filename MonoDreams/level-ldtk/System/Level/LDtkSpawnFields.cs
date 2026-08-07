namespace MonoDreams.System.Level;

/// <summary>
/// The <c>ldtk:</c>-prefixed <c>EntitySpawnRequest.CustomFields</c> keys the LDtk parsers publish
/// layer-derived data under. Replaces the shared LDtk-typed <c>EntitySpawnRequest.Layer</c> member
/// (issue #54): the message stays format-agnostic and <c>level-loading</c> never compiles against LDtk,
/// while a factory that cares about the layer still reads exactly what it needs.
///
/// <para>LDtk field identifiers cannot contain <c>':'</c>, so these keys can never collide with a
/// designer-authored custom field. Factories should read them defensively
/// (<c>TryGetValue</c> + a default), because a code-driven spawn — the lightweight
/// <c>(identifier, position)</c> ctor, the <c>prefab:</c> channel — carries no LDtk context at all.</para>
/// </summary>
public static class LDtkSpawnFields
{
    /// <summary>The LDtk layer's opacity (<c>LayerInstance._Opacity</c>). Value type: <see cref="float"/>.</summary>
    public const string LayerOpacity = "ldtk:layerOpacity";

    /// <summary>The LDtk layer's grid size in pixels (<c>LayerInstance._GridSize</c>). Value type: <see cref="int"/>.</summary>
    public const string GridSize = "ldtk:gridSize";
}
