#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MonoDreams.Screen;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// One row of the editor's Scenes panel (UX-C). <see cref="Key"/> is the stable id the
/// <c>scenes:select</c> op matches; <see cref="Label"/> is what the panel shows (a screen's display
/// name, or a scene-file id); <see cref="ScreenName"/> is the screen a switch loads;
/// <see cref="SceneId"/> is the scene id the switch requests for that screen; <see cref="IsCurrent"/>
/// marks the active entry (highlighted + carries the dirty marker). Pure data.
/// </summary>
public readonly record struct SceneCatalogEntry(
    string Key, string Label, string ScreenName, string SceneId, bool IsCurrent);

/// <summary>What selecting a Scenes-panel entry should do — the pure gate decision (UX-C §3.3).</summary>
public enum SceneSwitchDecision
{
    /// <summary>The selected entry is already current — do nothing.</summary>
    NoOp,
    /// <summary>Clean world — switch immediately (invoke the host switch callback).</summary>
    Switch,
    /// <summary>Dirty world — open the confirm-on-switch modal first.</summary>
    Confirm,
}

/// <summary>
/// The pure assembler of the editor's Scenes panel (UX-C §3.2). It merges two sources into one
/// ordered list — <b>never reading the filesystem itself</b> (the scene-id lister is injected, exactly
/// like the deleted file browser's directory lister):
/// <list type="number">
///   <item>every registered screen with a <see cref="ScreenInfo.BoundSceneId"/> → one entry
///   (label = the screen's <see cref="ScreenInfo.DisplayName"/>), in registration order;</item>
///   <item>every <c>.mdscene</c> id under the project's levels dir <b>not claimed by a binding</b> →
///   one entry hosted by the first <see cref="ScreenInfo.HostsSceneFiles"/> screen (label = the scene
///   id), ordinal-sorted — so a dangling backup scene "opens" for free by appearing here;</item>
/// </list>
/// When the project context is <b>unresolved</b> (<paramref name="projectResolved"/> false) the file
/// half is skipped entirely — <b>screens only</b> — matching the Save guard's fail-safe: with no
/// resolved root there is nowhere to have listed scenes from.
///
/// <para>Because a file entry is only produced for a scene id NOT claimed by a binding, the scene ids
/// are disjoint across entries, so <see cref="SceneCatalogEntry.Key"/> = <see cref="SceneCatalogEntry.SceneId"/>
/// is unique. The current entry is the one whose <c>(ScreenName, SceneId)</c> equals the live
/// <paramref name="currentScreenName"/>/<paramref name="currentSceneId"/> — if the current scene has no
/// entry (e.g. a brand-new untitled scene on the host with no file yet) nothing is marked current.</para>
/// </summary>
public static class SceneCatalog
{
    public static IReadOnlyList<SceneCatalogEntry> Build(
        IReadOnlyList<(string Name, ScreenInfo Info)> screens,
        IReadOnlyList<string> sceneIds,
        string? currentScreenName,
        string? currentSceneId,
        bool projectResolved)
    {
        var entries = new List<SceneCatalogEntry>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        string? hostScreen = null;

        // 1) One entry per screen that declares a bound scene id (registration order). Remember the
        //    first scene-file host so unclaimed files below can be attributed to it.
        foreach (var (name, info) in screens ?? Array.Empty<(string, ScreenInfo)>())
        {
            if (info == null) continue;
            if (hostScreen == null && info.HostsSceneFiles) hostScreen = name;
            if (string.IsNullOrEmpty(info.BoundSceneId)) continue;

            var sceneId = info.BoundSceneId!;
            claimed.Add(sceneId);
            entries.Add(new SceneCatalogEntry(
                Key: sceneId, Label: info.DisplayName, ScreenName: name, SceneId: sceneId,
                IsCurrent: IsCurrent(name, sceneId, currentScreenName, currentSceneId)));
        }

        // 2) One entry per unclaimed .mdscene id under the levels dir, hosted by the scene-file host.
        //    Skipped entirely when the project is unresolved (screens only) or when no screen hosts
        //    scene files (there is no screen to open them on).
        if (projectResolved && hostScreen != null && sceneIds != null)
        {
            foreach (var sceneId in sceneIds
                         .Where(id => !string.IsNullOrEmpty(id) && !claimed.Contains(id))
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(id => id, StringComparer.Ordinal))
            {
                entries.Add(new SceneCatalogEntry(
                    Key: sceneId, Label: sceneId, ScreenName: hostScreen, SceneId: sceneId,
                    IsCurrent: IsCurrent(hostScreen, sceneId, currentScreenName, currentSceneId)));
            }
        }

        return entries;
    }

    /// <summary>The pure dirty gate (UX-C §3.3): a current entry is a no-op; otherwise a clean world
    /// switches immediately while a dirty one requires the confirm-on-switch modal. Both the
    /// Scenes-panel row click and the <c>scenes:select</c> op decide through this one function.</summary>
    public static SceneSwitchDecision DecideSwitch(SceneCatalogEntry entry, bool isDirty) =>
        entry.IsCurrent ? SceneSwitchDecision.NoOp
        : isDirty ? SceneSwitchDecision.Confirm
        : SceneSwitchDecision.Switch;

    private static bool IsCurrent(string screenName, string sceneId, string? curScreen, string? curScene) =>
        curScreen != null && curScene != null &&
        string.Equals(screenName, curScreen, StringComparison.Ordinal) &&
        string.Equals(sceneId, curScene, StringComparison.Ordinal);
}
