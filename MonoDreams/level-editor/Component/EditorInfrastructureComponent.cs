namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Tags an entity as part of the editor's own machinery — chrome panels/buttons/labels, systems-panel
/// rows, gizmo overlays and collider proxies, the shared gizmo-state entity, the overlay-provided
/// cursor — as opposed to the scene being edited. The one consumer is the transport's <b>Restart</b>
/// (<c>EditorTransport.Restart</c>): it disposes every entity that is NOT editor infrastructure
/// (and not the cursor pipeline / a screen-kept entity) before re-running the screen's original
/// load, so tagged entities survive a restart with their state intact.
///
/// <para>Every site that creates an editor-owned entity must set this tag — the engine has no
/// entity↔level association, so the tag IS the boundary. Pure data (an empty marker), never
/// serialized.</para>
/// </summary>
public readonly struct EditorInfrastructureComponent;
