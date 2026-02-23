#if DEBUG
using System.Numerics;
using DefaultEcs;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.State;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;

namespace MonoDreams.Examples.Inspector;

public class DebugInspector
{
    private bool _visible;
    private KeyboardState _previousKeyboardState;
    private readonly ComponentIntrospector _introspector = new();
    private List<EntitySnapshot> _rootSnapshots = new();
    private Dictionary<Entity, EntitySnapshot> _allSnapshots = new();
    private int _totalEntityCount;
    private int _rootCount;
    private readonly WatcherState _watcherState = new();
    private World _lastWorld;
    private int _frameCounter;
    private string _filterText = "";
    private const int ScanInterval = 15; // ~4x/sec at 60fps
    private int _nextNodeId;

    public bool WantsKeyboard => _visible && ImGui.GetIO().WantCaptureKeyboard;
    public bool WantsMouse => _visible && ImGui.GetIO().WantCaptureMouse;

    public void HandleInput()
    {
        var currentState = Keyboard.GetState();
        if (currentState.IsKeyDown(Keys.F1) && _previousKeyboardState.IsKeyUp(Keys.F1))
        {
            _visible = !_visible;
        }
        _previousKeyboardState = currentState;
    }

    public void Draw(World world)
    {
        if (!_visible || world == null) return;

        // Detect world change (screen transition)
        if (world != _lastWorld)
        {
            _rootSnapshots.Clear();
            _allSnapshots.Clear();
            _watcherState.Clear();
            _lastWorld = world;
            _frameCounter = 0;
        }

        // Throttled entity scan
        if (_frameCounter++ % ScanInterval == 0)
        {
            RefreshEntitySnapshots(world);
        }

        DrawWorldHierarchyPanel();
        DrawWatchersPanel();
    }

    private void RefreshEntitySnapshots(World world)
    {
        _allSnapshots.Clear();
        _rootSnapshots.Clear();
        _totalEntityCount = 0;

        // Build snapshot for every entity
        foreach (var entity in world.GetEntities().AsEnumerable())
        {
            if (!entity.IsAlive) continue;
            _totalEntityCount++;

            string displayName;
            if (entity.Has<EntityInfo>())
            {
                var info = entity.Get<EntityInfo>();
                displayName = info.Name != null
                    ? $"{info.Name} ({info.Type}) #{entity.GetHashCode():X}"
                    : $"{info.Type} #{entity.GetHashCode():X}";
            }
            else
            {
                displayName = $"Entity #{entity.GetHashCode():X}";
            }

            _allSnapshots[entity] = new EntitySnapshot
            {
                Entity = entity,
                DisplayName = displayName,
                Components = _introspector.ReadEntity(entity)
            };
        }

        // Read hierarchy from world resource (set by HierarchySystem)
        var hierarchy = world.Has<EntityHierarchy>() ? world.Get<EntityHierarchy>() : null;

        if (hierarchy != null)
        {
            // Wire up parent-child snapshot tree
            foreach (var (entity, snapshot) in _allSnapshots)
            {
                var parent = hierarchy.GetParent(entity);
                if (parent.HasValue && _allSnapshots.TryGetValue(parent.Value, out var parentSnapshot))
                {
                    parentSnapshot.Children.Add(snapshot);
                }
                else
                {
                    _rootSnapshots.Add(snapshot);
                }
            }
        }
        else
        {
            // No hierarchy available — flat list
            _rootSnapshots.AddRange(_allSnapshots.Values);
        }

        _rootCount = _rootSnapshots.Count;
    }

    private void DrawWorldHierarchyPanel()
    {
        ImGui.Begin("World Hierarchy");
        ImGui.Text($"Entities: {_totalEntityCount} ({_rootCount} roots)");
        ImGui.InputTextWithHint("##filter", "Filter...", ref _filterText, 256);
        ImGui.Separator();

        _nextNodeId = 0;
        for (var i = 0; i < _rootSnapshots.Count; i++)
        {
            DrawEntityNode(_rootSnapshots[i]);
        }

        ImGui.End();
    }

    private void DrawEntityNode(EntitySnapshot snapshot)
    {
        var nodeId = _nextNodeId++;

        var hasFilter = _filterText.Length > 0;

        // When filtering, check if this entity or any descendant matches
        if (hasFilter && !MatchesFilter(snapshot))
            return;

        var entityPath = $"Entity:{snapshot.DisplayName}";
        var hasChildren = snapshot.Children.Count > 0;

        DrawTriStateCheckbox(entityPath, snapshot);
        ImGui.SameLine();

        var flags = hasChildren ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.Leaf;
        if (ImGui.TreeNodeEx($"{snapshot.DisplayName}##e{nodeId}", flags))
        {
            // Draw components
            for (var c = 0; c < snapshot.Components.Count; c++)
            {
                var comp = snapshot.Components[c];
                var compPath = $"{entityPath}/{comp.TypeName}";
                DrawTriStateCheckbox(compPath, snapshot, comp);
                ImGui.SameLine();

                if (ImGui.TreeNode($"{comp.TypeName}##e{nodeId}c{c}"))
                {
                    DrawComponentFields(comp, compPath);
                    ImGui.TreePop();
                }
            }

            // Draw children recursively
            for (var i = 0; i < snapshot.Children.Count; i++)
            {
                DrawEntityNode(snapshot.Children[i]);
            }

            ImGui.TreePop();
        }
    }

    private bool MatchesFilter(EntitySnapshot snapshot)
    {
        if (snapshot.DisplayName.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var child in snapshot.Children)
        {
            if (MatchesFilter(child))
                return true;
        }

        return false;
    }

    private void DrawComponentFields(ComponentSnapshot comp, string compPath)
    {
        foreach (var field in comp.Fields)
        {
            var fieldPath = $"{compPath}/{field.Name}";
            DrawLeafCheckbox(fieldPath);
            ImGui.SameLine();

            var value = field.GetValue(comp.BoxedValue);
            DrawFormattedValue(field.Name, value, fieldPath);
        }
        foreach (var prop in comp.Properties)
        {
            var fieldPath = $"{compPath}/{prop.Name}";
            DrawLeafCheckbox(fieldPath);
            ImGui.SameLine();

            try
            {
                var value = prop.GetValue(comp.BoxedValue);
                DrawFormattedValue(prop.Name, value, fieldPath);
            }
            catch
            {
                ImGui.Text($"{prop.Name}: <error>");
            }
        }
    }

    private static void DrawFormattedValue(string name, object value, string id)
    {
        if (value is XnaColor c)
        {
            ImGui.Text($"{name}:");
            ImGui.SameLine();
            var colorVec = new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
            ImGui.ColorButton($"##color{id}", colorVec, ImGuiColorEditFlags.None, new Vector2(14, 14));
            ImGui.SameLine();
            ImGui.Text($"({c.R}, {c.G}, {c.B}, {c.A})");
        }
        else
        {
            ImGui.Text($"{name}: {FormatValue(value)}");
        }
    }

    private static string FormatValue(object value)
    {
        if (value == null) return "null";
        return value switch
        {
            XnaVector2 v => $"({v.X:F1}, {v.Y:F1})",
            XnaVector3 v => $"({v.X:F1}, {v.Y:F1}, {v.Z:F1})",
            XnaRectangle r => $"[{r.X}, {r.Y}, {r.Width}x{r.Height}]",
            XnaColor c => $"({c.R}, {c.G}, {c.B}, {c.A})",
            bool b => b ? "true" : "false",
            float f => f.ToString("F2"),
            double d => d.ToString("F2"),
            Enum e => e.ToString(),
            string s => $"\"{s}\"",
            global::System.Collections.IEnumerable enumerable => FormatEnumerable(enumerable),
            _ => value.ToString() ?? "null"
        };
    }

    private static string FormatEnumerable(global::System.Collections.IEnumerable enumerable)
    {
        const int maxItems = 8;
        var items = new List<string>();
        var count = 0;
        foreach (var item in enumerable)
        {
            if (count < maxItems)
                items.Add(FormatValue(item));
            count++;
        }
        var result = string.Join(", ", items);
        if (count > maxItems)
            result += ", ...";
        return $"[{result}]";
    }

    private void DrawTriStateCheckbox(string prefix, EntitySnapshot snapshot, ComponentSnapshot comp = null)
    {
        var leafPaths = GetLeafPaths(prefix, snapshot, comp);
        var state = _watcherState.GetPrefixState(prefix, leafPaths);

        // ImGui doesn't have native tri-state: we render it manually
        var isChecked = state == true;
        if (ImGui.Checkbox($"##chk{prefix}", ref isChecked))
        {
            _watcherState.TogglePrefix(prefix, leafPaths);
        }

        // Draw intermediate dash overlay
        if (state == null) // some checked
        {
            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var midY = (min.Y + max.Y) * 0.5f;
            var dashColor = ImGui.GetColorU32(ImGuiCol.CheckMark);
            drawList.AddLine(
                new Vector2(min.X + 4, midY),
                new Vector2(max.X - 4, midY),
                dashColor, 2f);
        }
    }

    private void DrawLeafCheckbox(string leafPath)
    {
        var isChecked = _watcherState.IsChecked(leafPath);
        if (ImGui.Checkbox($"##chk{leafPath}", ref isChecked))
        {
            _watcherState.ToggleLeaf(leafPath);
        }
    }

    private static List<string> GetLeafPaths(string prefix, EntitySnapshot snapshot, ComponentSnapshot comp)
    {
        var paths = new List<string>();
        if (comp != null)
        {
            foreach (var field in comp.Fields)
                paths.Add($"{prefix}/{field.Name}");
            foreach (var prop in comp.Properties)
                paths.Add($"{prefix}/{prop.Name}");
        }
        else
        {
            foreach (var c in snapshot.Components)
            {
                var compPath = $"{prefix}/{c.TypeName}";
                foreach (var field in c.Fields)
                    paths.Add($"{compPath}/{field.Name}");
                foreach (var prop in c.Properties)
                    paths.Add($"{compPath}/{prop.Name}");
            }
        }
        return paths;
    }

    private void DrawWatchersPanel()
    {
        ImGui.Begin("Watchers");

        if (!_watcherState.HasAnyChecked())
        {
            ImGui.TextDisabled("No items watched. Check items in World Hierarchy to pin them here.");
            ImGui.End();
            return;
        }

        var i = 0;
        foreach (var snapshot in _allSnapshots.Values)
        {
            var entityPath = $"Entity:{snapshot.DisplayName}";
            var entityLeafPaths = GetLeafPaths(entityPath, snapshot, null);

            // Skip entities with no checked leaves
            if (!_watcherState.HasAnyCheckedUnder(entityPath, entityLeafPaths))
            {
                i++;
                continue;
            }

            DrawTriStateCheckbox(entityPath, snapshot);
            ImGui.SameLine();

            if (ImGui.TreeNodeEx($"{snapshot.DisplayName}##we{i}", ImGuiTreeNodeFlags.DefaultOpen))
            {
                for (var c = 0; c < snapshot.Components.Count; c++)
                {
                    var comp = snapshot.Components[c];
                    var compPath = $"{entityPath}/{comp.TypeName}";
                    var compLeafPaths = GetLeafPaths(compPath, snapshot, comp);

                    if (!_watcherState.HasAnyCheckedUnder(compPath, compLeafPaths))
                        continue;

                    DrawTriStateCheckbox(compPath, snapshot, comp);
                    ImGui.SameLine();

                    if (ImGui.TreeNodeEx($"{comp.TypeName}##we{i}c{c}", ImGuiTreeNodeFlags.DefaultOpen))
                    {
                        DrawWatcherComponentFields(comp, compPath);
                        ImGui.TreePop();
                    }
                }
                ImGui.TreePop();
            }
            i++;
        }

        ImGui.End();
    }

    private void DrawWatcherComponentFields(ComponentSnapshot comp, string compPath)
    {
        foreach (var field in comp.Fields)
        {
            var fieldPath = $"{compPath}/{field.Name}";
            if (!_watcherState.IsChecked(fieldPath)) continue;

            DrawLeafCheckbox(fieldPath);
            ImGui.SameLine();
            var value = field.GetValue(comp.BoxedValue);
            DrawFormattedValue(field.Name, value, $"w{fieldPath}");
        }
        foreach (var prop in comp.Properties)
        {
            var fieldPath = $"{compPath}/{prop.Name}";
            if (!_watcherState.IsChecked(fieldPath)) continue;

            DrawLeafCheckbox(fieldPath);
            ImGui.SameLine();
            try
            {
                var value = prop.GetValue(comp.BoxedValue);
                DrawFormattedValue(prop.Name, value, $"w{fieldPath}");
            }
            catch
            {
                ImGui.Text($"{prop.Name}: <error>");
            }
        }
    }
}
#endif
