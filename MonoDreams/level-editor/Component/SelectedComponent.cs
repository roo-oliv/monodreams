namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Tags the entity the designer has currently selected in <see cref="MonoDreams.State.RunMode.Edit"/>.
/// A pure tag (no fields) — ECS purity: the selection decision is made by
/// <c>SelectionSystem</c> and recorded as this tag; the gizmo / overlay systems (Wave 4b)
/// query <c>[With(SelectedComponent)]</c> to know what to draw handles around and what to
/// mutate. Single-select for Wave A: the selection system clears it from the previous
/// selection before tagging the new one (marquee / multi-select is a documented later extension).
///
/// <para>This tag is set on <b>game</b> entities (the thing being edited), never on overlay
/// entities. It is not serialized — it is transient editor state, deliberately absent from the
/// component-serializer registry (like <c>VisibleComponent</c>).</para>
/// </summary>
public struct SelectedComponent
{
}
