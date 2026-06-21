using System.Collections.Generic;
using MonoDreams.Draw;

namespace MonoDreams.Component.Cursor;

/// <summary>
/// Holds the per-<see cref="CursorType"/> mesh silhouettes a mesh cursor (see
/// <c>Cursor.CreateMesh</c>) can swap between — e.g. an arrow for <see cref="CursorType.Default"/>
/// and a hand for <see cref="CursorType.Hand"/>. A cursor-hover system sets
/// <see cref="CursorControllerComponent.Type"/> and swaps the cursor entity's <c>DrawComponent</c>
/// mesh to the matching library entry when the type changes. Pure data: the library is the lookup,
/// the swap is the system's job.
/// </summary>
public struct CursorMeshLibraryComponent
{
    /// <summary>Mesh per cursor type. <see cref="CursorType.Default"/> is the resting (arrow) mesh.</summary>
    public Dictionary<CursorType, MeshData> Meshes;
}
