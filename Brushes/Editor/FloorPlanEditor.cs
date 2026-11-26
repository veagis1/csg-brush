using static CsgBrush.Editor.BrushLanguage;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>Floor plan geometry for the tools: points on the plan's floor, snapped to the world grid.</summary>
    public static class FloorPlanTools
    {
        public static readonly Color Line = new Color(1f, 0.78f, 0.2f, 1f);

        static float Grid => BrushSettings.instance.snapToGrid ? BrushSettings.instance.GridMeters : 0f;

        /// <summary>The plan's floor as a world plane.</summary>
        public static Plane Floor(PointGraph plan) => new Plane(plan.transform.up, plan.GraphToWorld(Vector3.zero));

        public static Vector3 World(PointGraph plan, Vector3 local) => plan.GraphToWorld(plan.Flat ? new Vector3(local.x, 0f, local.z) : local);

        /// <summary>A world point on the plan's floor, snapped to the grid in the plan's own axes, as a local point.</summary>
        public static Vector3 SnapLocal(PointGraph plan, Vector3 world)
        {
            var local = plan.WorldToGraph(world);
            if (plan.Flat) local.y = 0f;
            float g = Grid;
            if (g > 0f) { local.x = Mathf.Round(local.x / g) * g; local.z = Mathf.Round(local.z / g) * g; }
            return local;
        }

        /// <summary>
        /// The next point from <paramref name="from"/> toward the mouse: along a multiple of 45 degrees with a length in grid
        /// steps (diagonal steps land on grid points too), unless <paramref name="free"/>, which takes the nearest grid point.
        /// </summary>
        public static Vector3 Constrain(Vector3 from, Vector3 toward, bool free)
        {
            if (free) return toward;
            var d = new Vector2(toward.x - from.x, toward.z - from.z);
            if (d.sqrMagnitude < 1e-8f) return from;
            float angle = Mathf.Round(Mathf.Atan2(d.y, d.x) / (Mathf.PI / 4f)) * (Mathf.PI / 4f);
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            dir.x = Mathf.Round(dir.x); dir.y = Mathf.Round(dir.y); // exact axes and diagonals
            float g = Grid > 0f ? Grid : 0.01f;
            float steps = Mathf.Max(1f, Mathf.Round(Vector2.Dot(d, dir.normalized) / (g * dir.magnitude))); // a diagonal step is g along both axes
            return new Vector3(from.x + dir.x * g * steps, 0f, from.z + dir.y * g * steps);
        }

        public static bool MouseOnFloor(PointGraph plan, Vector2 mouse, out Vector3 world)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            if (Floor(plan).Raycast(ray, out float t) && t > 0f) { world = ray.GetPoint(t); return true; }
            world = default; return false;
        }

        public static void DrawOutline(PointGraph plan)
        {
            plan.EnsureGraph();
            Handles.color = Line;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            for (int w = 0; w < plan.links.Count; w++) Handles.DrawAAPolyLine(3f, WorldPath(plan, w));
        }

        /// <summary>A link's path (a curve's pieces) in world space.</summary>
        public static Vector3[] WorldPath(PointGraph plan, int link)
        {
            var path = plan.Curve(link); var world = new Vector3[path.Length];
            for (int i = 0; i < path.Length; i++) world[i] = World(plan, path[i]);
            return world;
        }

        /// <summary>A link's path on screen.</summary>
        public static Vector2[] ScreenPath(PointGraph plan, int link)
        {
            var path = plan.Curve(link); var screen = new Vector2[path.Length];
            for (int i = 0; i < path.Length; i++) screen[i] = HandleUtility.WorldToGUIPoint(World(plan, path[i]));
            return screen;
        }

        /// <summary>How far a screen point is from a screen path, in pixels.</summary>
        public static float DistanceToPath(Vector2 p, IList<Vector2> path)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < path.Count; i++) best = Mathf.Min(best, HandleUtility.DistancePointLine(p, path[i], path[i + 1]));
            return best;
        }

        /// <summary>The point nearest the mouse within 12 pixels, or -1.</summary>
        public static int PointUnderMouse(PointGraph plan, Vector2 mouse)
        {
            int best = -1; float bestD = 12f;
            for (int i = 0; i < plan.points.Count; i++) { float d = (HandleUtility.WorldToGUIPoint(World(plan, plan.points[i])) - mouse).magnitude; if (d < bestD) { bestD = d; best = i; } }
            return best;
        }

        /// <summary>The wall nearest the mouse within 10 pixels, or -1, with the spot on it (local, on the grid along the wall when snapping).</summary>
        public static int WallUnderMouse(PointGraph plan, Vector2 mouse, out Vector3 local)
        {
            local = default;
            int best = -1; float bestD = 10f;
            for (int w = 0; w < plan.links.Count; w++)
            {
                float d = DistanceToPath(mouse, ScreenPath(plan, w));
                if (d < bestD) { bestD = d; best = w; }
            }
            if (best < 0) return -1;
            if (!plan.Flat)
            {
                // a path in the air: the place on it nearest the mouse on screen
                var path = plan.Curve(best); float bd = float.MaxValue;
                for (int i = 0; i + 1 < path.Length; i++)
                {
                    Vector2 sa = HandleUtility.WorldToGUIPoint(World(plan, path[i])), sb = HandleUtility.WorldToGUIPoint(World(plan, path[i + 1]));
                    var sab = sb - sa; float u = sab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(mouse - sa, sab) / sab.sqrMagnitude);
                    float dd = (sa + sab * u - mouse).sqrMagnitude;
                    if (dd < bd) { bd = dd; local = Vector3.Lerp(path[i], path[i + 1], u); }
                }
                return best;
            }
            if (!MouseOnFloor(plan, mouse, out var world)) return -1;
            var curve = plan.Curve(best);
            if (curve.Length > 2)
            {
                // a curved wall: the nearest place on its pieces, as it is (a grid point would leave the curve)
                var m3 = plan.WorldToGraph(world); if (plan.Flat) m3.y = 0f;
                float bestDist = float.MaxValue;
                for (int i = 0; i + 1 < curve.Length; i++)
                {
                    var seg = curve[i + 1] - curve[i]; float l2 = seg.sqrMagnitude;
                    float u = l2 < 1e-12f ? 0f : Mathf.Clamp01(Vector3.Dot(m3 - curve[i], seg) / l2);
                    var q = curve[i] + seg * u; float dd = (q - m3).sqrMagnitude;
                    if (dd < bestDist) { bestDist = dd; local = q; }
                }
                return best;
            }
            plan.LinkPoints(best, out int ia, out int ib);
            Vector3 pa = plan.points[ia], pb = plan.points[ib], ab = pb - pa;
            float len = ab.magnitude;
            var m = plan.WorldToGraph(world); m.y = 0f;
            float t = Mathf.Clamp(Vector3.Dot(m - pa, ab) / len, 0f, len);
            float g = Grid;
            if (g > 0f) t = Mathf.Round(t / g) * g;
            t = Mathf.Clamp(t, 0f, len);
            local = pa + ab / len * t;
            return best;
        }
    }

    /// <summary>Inspector parts every point graph (floor plan, spline) shows.</summary>
    public static class PointGraphGUI
    {
        /// <summary>The curve tolerance override: off uses the project's (Project Settings > Brushes).</summary>
        public static void Tolerance(SerializedObject so)
        {
            so.Update();
            var on = so.FindProperty("overrideTolerance"); var value = so.FindProperty("tolerance");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(on, new GUIContent(Tr("Задать свой допуск"), Tr("Использует допуск этого объекта. При отключении берётся допуск кривых из настроек проекта.")));
            if (EditorGUI.EndChangeCheck() && on.boolValue) value.floatValue = PointGraph.ProjectTolerance; // starts from the project's
            if (on.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(value, new GUIContent(Tr("Допуск (м)"), Tr("Максимальное отклонение прямых сегментов от кривой в метрах. Меньше — плавнее контур и больше сегментов. Минимум 0,01 м.")));
                if (value.floatValue < PointGraph.MinTolerance) value.floatValue = PointGraph.MinTolerance;
                EditorGUI.indentLevel--;
            }
            so.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// A selected floor plan shows its outline; Edit (the Edit Floor Plan context) selects and transforms its points and
    /// walls, Draw adds walls.
    /// </summary>
    [CustomEditor(typeof(FloorPlan))]
    public sealed class FloorPlanEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var plan = (FloorPlan)target;
            BrushEditor.DrawNamedProperties(serializedObject);
            PointGraphGUI.Tolerance(serializedObject);
            GeneratorWcs.DrawInspector(plan);
            plan.EnsureGraph();
            EditorGUILayout.LabelField(Tr("План стен"), BrushLanguage.Format("Точек {0} · стен {1} · комнат {2}", plan.points.Count, plan.links.Count, FloorPlanEditState.RoomsOf(plan).Count));
            EditorGUILayout.BeginHorizontal();
            bool editing = FloorPlanEditContext.IsActive;
            if (GUILayout.Toggle(editing, new GUIContent(Tr("Редактировать"), Tr("Перемещайте, поворачивайте и масштабируйте выбранные точки или стены. Клавиши 1 и 2 меняют режим выделения.")), EditorStyles.miniButton) != editing)
            {
                if (editing) FloorPlanEditContext.Exit(); else FloorPlanEditContext.Enter();
            }
            bool drawing = ToolManager.activeToolType == typeof(FloorPlanDrawTool);
            if (GUILayout.Toggle(drawing, new GUIContent(Tr("Рисовать"), Tr("Щёлкайте по полу, чтобы добавить стены. Enter или Escape завершает рисование.")), EditorStyles.miniButton) != drawing)
            {
                if (drawing) ToolManager.RestorePreviousPersistentTool();
                else { FloorPlanEditContext.Exit(); ToolManager.SetActiveTool<FloorPlanDrawTool>(); }
            }
            EditorGUILayout.EndHorizontal();
            if (editing && targets.Length == 1) DrawSelection(plan);
        }

        /// <summary>Edit mode: the thickness of the selected walls, or the floor and ceiling of the selected rooms.</summary>
        static void DrawSelection(FloorPlan plan)
        {
            var sel = FloorPlanEditState.Sel(plan);
            if (FloorPlanEditState.Mode == BrushEditMode.Edge && sel.edges.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(Tr("Выбранные стены (") + sel.edges.Count + ")", EditorStyles.boldLabel);
                float first = -1f; bool mixed = false;
                foreach (var w in sel.edges) { float t = plan.links[w].thickness; if (first < 0f) first = t; else if (!Mathf.Approximately(first, t)) mixed = true; }
                EditorGUI.showMixedValue = mixed;
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.FloatField(new GUIContent(Tr("Толщина (м)"), Tr("Толщина выбранных стен в метрах. При 0 используется толщина наружных или внутренних стен из плана.")), first);
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(plan, Tr("Изменить толщину стен"));
                    foreach (var w in sel.edges) { var link = plan.links[w]; link.thickness = Mathf.Max(0f, value); plan.links[w] = link; }
                    BrushGenerators.MarkDirty(plan);
                }
            }
            else if (FloorPlanEditState.Mode == BrushEditMode.Face && sel.rooms.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(Tr("Выбранные комнаты (") + sel.rooms.Count + ")", EditorStyles.boldLabel);
                var rooms = new List<FloorPlan.Room>(FloorPlanEditState.RoomsOf(plan));
                var chosen = new List<FloorPlan.Room>(); foreach (var r in sel.rooms) if (r < rooms.Count) chosen.Add(rooms[r]);
                if (chosen.Count == 0) return;
                var shown = chosen[0];
                bool Mixed<T>(System.Func<FloorPlan.Room, T> get) { foreach (var c in chosen) if (!Equals(get(c), get(shown))) return true; return false; }
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = Mixed(r => r.floor);
                bool floor = EditorGUILayout.Toggle(new GUIContent(Tr("Пол"), Tr("Создавать плиту пола под комнатой.")), shown.floor);
                EditorGUI.showMixedValue = Mixed(r => r.floorMaterial);
                var floorMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent(Tr("Материал пола"), Tr("Пустое поле использует материал проекта по умолчанию.")), shown.floorMaterial, typeof(Material), false);
                EditorGUI.showMixedValue = Mixed(r => r.ceiling);
                bool ceiling = EditorGUILayout.Toggle(new GUIContent(Tr("Потолок"), Tr("Создавать плиту потолка над комнатой.")), shown.ceiling);
                EditorGUI.showMixedValue = Mixed(r => r.ceilingMaterial);
                var ceilingMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent(Tr("Материал потолка"), Tr("Пустое поле использует материал проекта по умолчанию.")), shown.ceilingMaterial, typeof(Material), false);
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(plan, Tr("Изменить параметры комнат"));
                    foreach (var room in chosen)
                    {
                        var s = plan.SettingsOf(room);
                        if (floor != shown.floor) s.floor = floor;
                        if (floorMaterial != shown.floorMaterial) s.floorMaterial = floorMaterial;
                        if (ceiling != shown.ceiling) s.ceiling = ceiling;
                        if (ceilingMaterial != shown.ceilingMaterial) s.ceilingMaterial = ceilingMaterial;
                    }
                    BrushGenerators.MarkDirty(plan);
                }
                if (GUILayout.Button(new GUIContent(Tr("Взять настройки плана"), Tr("Восстановить для выбранных комнат пол, потолок и материалы из настроек плана."))))
                {
                    Undo.RecordObject(plan, Tr("Изменить параметры комнат"));
                    foreach (var room in chosen) if (room.settings >= 0 && room.settings < plan.rooms.Count) plan.rooms[room.settings] = null;
                    plan.rooms.RemoveAll(r => r == null);
                    BrushGenerators.MarkDirty(plan);
                }
            }
        }

        void OnSceneGUI()
        {
            // the draw tool and edit mode show their own
            if (ToolManager.activeToolType == typeof(FloorPlanDrawTool) || FloorPlanEditContext.IsActive) return;
            FloorPlanTools.DrawOutline((FloorPlan)target);
        }
    }

    /// <summary>
    /// Draw walls: click on the floor to add a point and a wall to it, snapped to the grid and to 45 degree steps (Shift: any
    /// grid point). A click on a point or a wall joins the new wall to it (splitting the wall there); a stroke that joins
    /// one ends, ready for the next. Backspace takes back the last wall; Escape or a double click ends the stroke, Enter or
    /// a second Escape leaves the tool.
    /// </summary>
    [EditorTool("Draw Floor Plan", typeof(FloorPlan))]
    public sealed class FloorPlanDrawTool : EditorTool
    {
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("FloorPlan"), Tr("Рисовать план стен"));

        enum Hover { None, Free, Point, Wall }
        Hover m_Kind; Vector3 m_Hover; int m_HoverPoint = -1, m_HoverWall = -1;
        /// <summary>The point the next wall starts from (id), or -1 between strokes.</summary>
        int m_Last = -1;
        readonly List<int> m_Stroke = new List<int>();

        /// <summary>
        /// Draw on the selected plan. Unity makes a selection's component tools a little after the selection changes, so a
        /// plan selected just now has no draw tool yet: the editors are rebuilt first, else the switch fails and the Create
        /// tool stays active, starting a new plan at every click.
        /// </summary>
        public static void Begin(int startPoint = -1)
        {
            s_Start = startPoint;
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            ToolManager.SetActiveTool<FloorPlanDrawTool>();
            if (ToolManager.activeToolType != typeof(FloorPlanDrawTool)) s_Start = -1; // the switch failed: no later activation may use it
        }

        /// <summary>The point (id) the next activation starts its first wall from, or -1.</summary>
        static int s_Start = -1;

        /// <summary>The point the next wall starts from, or -1 between strokes (for tests).</summary>
        public int LastPoint => m_Last;

        public override void OnActivated()
        {
            BrushMenu.PrepareCreation();
            m_Last = -1; m_Stroke.Clear();
            int start = s_Start; s_Start = -1;
            if (target is PointGraph plan)
            {
                plan.EnsureGraph();
                if (start >= 0 && plan.PointIndex(start) >= 0) { m_Last = start; m_Stroke.Add(start); }
                else if (plan.links.Count == 0 && plan.points.Count == 1) { m_Last = plan.pointIds[0]; m_Stroke.Add(m_Last); }
            }
        }

        public override void OnWillBeDeactivated() => EndStroke();

        public override void OnToolGUI(EditorWindow window)
        {
            var plan = target as PointGraph;
            if (plan == null || !(window is SceneView)) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (m_Last >= 0 && plan.PointIndex(m_Last) < 0) { m_Last = -1; m_Stroke.Clear(); } // undone
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown || e.type == EventType.Repaint) FindHover(plan, e);
            if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == id && m_Kind != Hover.None)
            {
                e.Use();
                if (e.clickCount == 2) { EndStroke(); return; }
                Undo.RecordObject(plan, Tr("Рисовать план стен"));
                int point; bool joined = m_Kind != Hover.Free;
                if (m_Kind == Hover.Point) point = plan.pointIds[m_HoverPoint];
                else if (m_Kind == Hover.Wall) point = plan.SplitLink(m_HoverWall, m_Hover);
                else point = plan.AddPoint(m_Hover);
                bool started = m_Last >= 0;
                if (started && point != m_Last) plan.AddLink(m_Last, point);
                BrushGenerators.MarkDirty(plan);
                if (joined && started && point != m_Last) EndStroke(); // joined the plan: this stroke is done
                else { m_Last = point; m_Stroke.Add(point); }
            }
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace && m_Stroke.Count >= 2)
                {
                    Undo.RecordObject(plan, Tr("Удалить стену из плана"));
                    int last = m_Stroke[m_Stroke.Count - 1], prev = m_Stroke[m_Stroke.Count - 2];
                    plan.links.RemoveAll(w => (w.start == prev && w.end == last) || (w.start == last && w.end == prev));
                    plan.RemoveLonePoints();
                    m_Stroke.RemoveAt(m_Stroke.Count - 1); m_Last = prev;
                    BrushGenerators.MarkDirty(plan); e.Use();
                }
                else if (e.keyCode == KeyCode.Escape) { if (m_Last >= 0) EndStroke(); else Finish(); e.Use(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Finish(); e.Use(); }
            }
            if (e.type == EventType.Repaint) DrawPreview(plan);
        }

        void EndStroke()
        {
            var plan = target as PointGraph;
            // a lone start point (clicked, then ended) is not kept
            if (plan != null && m_Stroke.Count == 1) { int lone = m_Stroke[0]; if (!plan.links.Exists(w => w.start == lone || w.end == lone)) { Undo.RecordObject(plan, Tr("Рисовать план стен")); plan.RemoveLonePoints(); } }
            m_Last = -1; m_Stroke.Clear(); SceneView.RepaintAll();
        }

        void FindHover(PointGraph plan, Event e)
        {
            m_Kind = Hover.None; m_HoverPoint = m_HoverWall = -1;
            int point = FloorPlanTools.PointUnderMouse(plan, e.mousePosition);
            if (point >= 0) { m_Kind = Hover.Point; m_HoverPoint = point; m_Hover = plan.points[point]; return; }
            int wall = FloorPlanTools.WallUnderMouse(plan, e.mousePosition, out var onWall);
            if (wall >= 0)
            {
                plan.LinkPoints(wall, out int a, out int b);
                if ((onWall - plan.points[a]).sqrMagnitude < 1e-8f) { m_Kind = Hover.Point; m_HoverPoint = a; m_Hover = plan.points[a]; return; }
                if ((onWall - plan.points[b]).sqrMagnitude < 1e-8f) { m_Kind = Hover.Point; m_HoverPoint = b; m_Hover = plan.points[b]; return; }
                m_Kind = Hover.Wall; m_HoverWall = wall; m_Hover = onWall; return;
            }
            if (!FloorPlanTools.MouseOnFloor(plan, e.mousePosition, out var floor)) return;
            m_Kind = Hover.Free;
            m_Hover = FloorPlanTools.SnapLocal(plan, floor);
            int last = m_Last >= 0 ? plan.PointIndex(m_Last) : -1;
            if (last >= 0) m_Hover = FloorPlanTools.Constrain(plan.points[last], m_Hover, e.shift);
        }

        void DrawPreview(PointGraph plan)
        {
            FloorPlanTools.DrawOutline(plan);
            if (m_Kind == Hover.None) return;
            var hover = FloorPlanTools.World(plan, m_Hover);
            bool joining = m_Kind != Hover.Free;
            Handles.color = joining ? Color.white : FloorPlanTools.Line;
            Handles.DotHandleCap(-1, hover, Quaternion.identity, HandleUtility.GetHandleSize(hover) * (joining ? 0.09f : 0.06f), EventType.Repaint);
            int last = m_Last >= 0 ? plan.PointIndex(m_Last) : -1;
            if (last < 0) return;
            var from = FloorPlanTools.World(plan, plan.points[last]);
            // the wall being drawn: solid and bold over a dark edge, so it shows on a light floor as on a dark one
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = new Color(0f, 0f, 0f, 0.6f);
            Handles.DrawAAPolyLine(6f, from, hover);
            Handles.color = FloorPlanTools.Line;
            Handles.DrawAAPolyLine(3f, from, hover);
            if (BrushSettings.instance.showDimensions) BrushPlanDimensionFields.DrawLengthReadout(from, hover);
        }

        void Finish() => ToolManager.RestorePreviousPersistentTool();
    }

    static class FloorPlanMenu
    {
        [MenuItem("GameObject/CSG Brush/План стен", false, 40)]
        static void Create()
        {
            BrushMenu.PrepareObjectCreation();
            var s = BrushSettings.instance;
            Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            float g = s.GridMeters;
            if (g > 0f) pivot = new Vector3(Mathf.Round(pivot.x / g) * g, 0f, Mathf.Round(pivot.z / g) * g);
            Create(pivot, BrushMenu.CreationParent(useSelection: true), false);
        }

        /// <summary>A new plan at a position, selected, drawing; with <paramref name="firstPoint"/> its first point is there.</summary>
        public static FloorPlan Create(Vector3 position, Transform parent, bool firstPoint)
        {
            var go = new GameObject("Floor Plan");
            GameObjectUtility.SetStaticEditorFlags(go, BrushSettings.instance.defaultModelStaticFlags); // its walls take the plan's flags
            Undo.RegisterCreatedObjectUndo(go, Tr("Создать план стен"));
            if (parent != null) Undo.SetTransformParent(go.transform, parent, Tr("Создать план стен"));
            go.transform.position = position;
            var plan = Undo.AddComponent<FloorPlan>(go);
            if (firstPoint) plan.AddPoint(Vector3.zero); // the draw tool starts its first wall here
            Selection.activeGameObject = go;
            int toolVersion = BrushMenu.ToolVersion;
            EditorApplication.delayCall += () =>
            {
                if (BrushMenu.ToolVersion == toolVersion && plan != null && !EditorApplication.isPlayingOrWillChangePlaymode &&
                    Selection.gameObjects.Length == 1 && Selection.activeGameObject == go) FloorPlanDrawTool.Begin();
            };
            return plan;
        }
    }

    /// <summary>
    /// The Create tool's Floor Plan: click on a floor to start a plan there, then click its corners. A click on a plan's
    /// point or wall continues that plan from there (the selected plan first), and a click on the selected plan's floor
    /// adds walls to it, so new walls join its walls as when it was first drawn.
    /// </summary>
    [EditorTool("Floor Plan", variantGroup = typeof(BrushCreateTool), variantPriority = 11)]
    public sealed class CreateFloorPlanTool : EditorTool
    {
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("FloorPlan"), Tr("План стен"));
        [MenuItem(BrushCommandMenu.CreateRoot + "План стен", false, 12)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "План стен", false, 12)] static void Menu() => BrushMenu.ActivateCreation<CreateFloorPlanTool>();

        Vector3 m_Hover; bool m_HoverValid;
        /// <summary>The plan a click continues, and where: one of its points (index), one of its walls (index and spot), or its floor (spot).</summary>
        FloorPlan m_Plan; int m_PlanPoint = -1, m_PlanWall = -1; Vector3 m_PlanLocal;
        /// <summary>The scene's plans, found again only when the hierarchy changes: the pointer looks for them at every move.</summary>
        List<FloorPlan> m_Plans;

        public override void OnActivated() { BrushMenu.PrepareCreation(); m_Plans = null; EditorApplication.hierarchyChanged += ForgetPlans; }
        public override void OnWillBeDeactivated() => EditorApplication.hierarchyChanged -= ForgetPlans;
        void ForgetPlans() => m_Plans = null;

        /// <summary>Which plan a press continues: one whose point or wall is under the pointer, the selected first; else the selected plan, pressed on its floor.</summary>
        void FindPlan(Vector2 mouse, bool onFloor, Vector3 floor)
        {
            m_Plan = null; m_PlanPoint = m_PlanWall = -1;
            var selected = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<FloorPlan>() : null;
            if (m_Plans == null)
            {
                m_Plans = new List<FloorPlan>();
                foreach (var plan in Object.FindObjectsByType<FloorPlan>()) if (plan.gameObject.scene.IsValid()) m_Plans.Add(plan);
            }
            for (int i = -1; i < m_Plans.Count; i++)
            {
                var plan = i < 0 ? selected : m_Plans[i]; // the selected plan first
                if (plan == null || (i >= 0 && plan == selected)) continue;
                plan.EnsureGraph();
                int point = FloorPlanTools.PointUnderMouse(plan, mouse);
                if (point >= 0) { m_Plan = plan; m_PlanPoint = point; return; }
                int wall = FloorPlanTools.WallUnderMouse(plan, mouse, out var local);
                if (wall < 0) continue;
                plan.LinkPoints(wall, out int a, out int b);
                m_Plan = plan;
                if ((local - plan.points[a]).sqrMagnitude < 1e-8f) m_PlanPoint = a;
                else if ((local - plan.points[b]).sqrMagnitude < 1e-8f) m_PlanPoint = b;
                else { m_PlanWall = wall; m_PlanLocal = local; }
                return;
            }
            if (selected != null && onFloor && Mathf.Abs(FloorPlanTools.Floor(selected).GetDistanceToPoint(floor)) < 0.01f)
            {
                m_Plan = selected; m_PlanLocal = FloorPlanTools.SnapLocal(selected, floor);
            }
        }

        /// <summary>Continue <see cref="m_Plan"/> from the found spot: select it and draw on it from there.</summary>
        void ContinuePlan()
        {
            var plan = m_Plan;
            Undo.RecordObject(plan, Tr("Рисовать план стен"));
            int start = m_PlanPoint >= 0 ? plan.pointIds[m_PlanPoint] : m_PlanWall >= 0 ? plan.SplitLink(m_PlanWall, m_PlanLocal) : plan.AddPoint(m_PlanLocal);
            BrushGenerators.MarkDirty(plan);
            Selection.activeGameObject = plan.gameObject;
            int toolVersion = BrushMenu.ToolVersion;
            EditorApplication.delayCall += () =>
            {
                if (BrushMenu.ToolVersion == toolVersion && plan != null && Selection.activeGameObject == plan.gameObject)
                    FloorPlanDrawTool.Begin(start);
            };
        }

        static float Grid => BrushSettings.instance.snapToGrid ? BrushSettings.instance.GridMeters : 0f;

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView) || BrushSurfaceGrid.Picking) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(id);
            if (e.alt || Tools.viewToolActive) return;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { ToolManager.RestorePreviousPersistentTool(); e.Use(); return; }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            {
                // a floor only: a plan's walls stand up from where it is started
                m_HoverValid = BrushCreateTool.FindPlane(e.mousePosition, out var p, out var n) && n.y > 0.7f;
                if (m_HoverValid) m_Hover = BrushDraw.SnapOnPlane(p, Vector3.up, Grid);
                FindPlan(e.mousePosition, m_HoverValid, p);
                if (m_Plan != null)
                {
                    m_HoverValid = true;
                    m_Hover = FloorPlanTools.World(m_Plan, m_PlanPoint >= 0 ? m_Plan.points[m_PlanPoint] : m_PlanLocal);
                }
                if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            }
            if (e.type == EventType.MouseDown && e.button == 0 && m_HoverValid && HandleUtility.nearestControl == id)
            {
                if (m_Plan != null) ContinuePlan();
                else FloorPlanMenu.Create(m_Hover, BrushMenu.CreationParent(), true);
                m_HoverValid = false;
                e.Use();
            }
            if (e.type == EventType.Repaint && m_HoverValid)
            {
                bool joining = m_Plan != null && (m_PlanPoint >= 0 || m_PlanWall >= 0); // white and larger on a plan's point or wall, as when drawing
                BrushSurfaceGrid.Draw(m_Plan != null ? new BrushSurfaceGrid.Frame(m_Plan.transform, Vector3.zero, Quaternion.identity)
                    : BrushSurfaceGrid.Frame.OnPlane(m_Hover, Vector3.up), m_Hover);
                Handles.color = joining ? Color.white : FloorPlanTools.Line;
                Handles.DotHandleCap(-1, m_Hover, Quaternion.identity, HandleUtility.GetHandleSize(m_Hover) * (joining ? 0.09f : 0.06f), EventType.Repaint);
            }
        }
    }
}
