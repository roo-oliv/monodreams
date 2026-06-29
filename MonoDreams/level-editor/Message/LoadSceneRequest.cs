namespace MonoDreams.LevelEditor.Message;

/// <summary>
/// Requests loading a <b>native MonoDreams scene</b> (the editor's own format) from a path.
///
/// <para>This message is deliberately <b>separate</b> from <c>LoadLevelRequest</c>. The LDtk
/// handler on <c>LoadLevelRequest</c> (<c>LevelLoadRequestSystem</c>) unconditionally runs
/// <c>content.Load&lt;LDtkLevel&gt;</c> and sets / removes the <c>CurrentLevelComponent</c> singleton —
/// driving the LDtk tile + entity parsers. If the native scene loader shared that message, a
/// native-scene load would also trigger (and on failure, clobber) the LDtk pipeline. A dedicated
/// message keeps the two load paths independent: <c>LoadSceneRequest</c> drives only the
/// <c>SceneReaderSystem</c>, which reconstructs entities from serialized components — never via the
/// LDtk content path.</para>
/// </summary>
/// <param name="path">
/// Path to the scene JSON. Read through the content stream (<c>TitleContainer</c>) when it names a
/// built/copied content asset, or through <c>IPlatformServices</c> when it names host-filesystem
/// user data — the <c>SceneReaderSystem</c> decides based on <see cref="FromContent"/>.
/// </param>
/// <param name="fromContent">
/// <c>true</c> to resolve <paramref name="path"/> as a content asset (works on web — served over
/// HTTP via <c>TitleContainer</c>); <c>false</c> to resolve it through <c>IPlatformServices</c>
/// (a host-filesystem read; empty on web). Defaults to <c>true</c> so a shipped scene loads on
/// every backend.
/// </param>
public readonly struct LoadSceneRequest(string path, bool fromContent = true)
{
    /// <summary>Path to the scene JSON file.</summary>
    public readonly string Path = path;

    /// <summary>Whether to resolve <see cref="Path"/> as a content asset (vs. host-filesystem user data).</summary>
    public readonly bool FromContent = fromContent;
}
