using static CsgBrush.Editor.BrushLanguage;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    [CustomEditor(typeof(SplineAnchor)), CanEditMultipleObjects]
    sealed class SplineAnchorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => BrushEditor.DrawNamedProperties(serializedObject);
    }

    [CustomEditor(typeof(SplineRepeat)), CanEditMultipleObjects]
    sealed class SplineRepeatEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => BrushEditor.DrawNamedProperties(serializedObject);
    }

    /// <summary>A Spline's Inspector: the profile's settings that apply to it, the tolerance override, Edit and Draw.</summary>
    [CustomEditor(typeof(Spline))]
    public sealed class SplineEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var spline = (Spline)target;
            serializedObject.Update();
            void Field(SerializedProperty property) => BrushEditor.DrawProperty(property, BrushEditor.PropertyLabel(typeof(Spline), property));
            var profile = serializedObject.FindProperty(nameof(Spline.profile));
            Field(profile);
            Field(serializedObject.FindProperty(nameof(Spline.size)));
            var kind = (Spline.Profile)profile.enumValueIndex;
            if (kind == Spline.Profile.Circle) Field(serializedObject.FindProperty(nameof(Spline.sides)));
            Field(serializedObject.FindProperty(nameof(Spline.half)));
            var hollow = serializedObject.FindProperty(nameof(Spline.hollow));
            Field(hollow);
            if (hollow.boolValue)
            {
                EditorGUI.indentLevel++;
                Field(serializedObject.FindProperty(nameof(Spline.thickness)));
                EditorGUI.indentLevel--;
            }
            Field(serializedObject.FindProperty(nameof(Spline.ends)));
            Field(serializedObject.FindProperty(nameof(Spline.orientation)));
            Field(serializedObject.FindProperty(nameof(Spline.material)));
            serializedObject.ApplyModifiedProperties();
            PointGraphGUI.Tolerance(serializedObject);
            GeneratorWcs.DrawInspector(spline);

            spline.EnsureGraph();
            EditorGUILayout.LabelField(Tr("Траектория"), BrushLanguage.Format("Точек {0} · звеньев {1}", spline.points.Count, spline.links.Count));
            if (TighterThanProfile(spline)) EditorGUILayout.HelpBox(Tr("Радиус поворота слишком мал для этого сечения. Внутренняя сторона профиля складывается."), MessageType.Warning);
            EditorGUILayout.BeginHorizontal();
            bool editing = PointGraphEditContext.IsActive;
            if (GUILayout.Toggle(editing, new GUIContent(Tr("Редактировать"), Tr("Перемещайте, поворачивайте и масштабируйте выделенные точки или звенья. Клавиши 1 и 2 меняют режим выделения.")), EditorStyles.miniButton) != editing)
            {
                if (editing) PointGraphEditContext.Exit(); else PointGraphEditContext.Enter();
            }
            bool drawing = ToolManager.activeToolType == typeof(SplineDrawTool);
            if (GUILayout.Toggle(drawing, new GUIContent(Tr("Рисовать"), Tr("Добавляйте точки щелчком. Escape завершает текущий штрих. Enter или повторный Escape закрывает инструмент.")), EditorStyles.miniButton) != drawing)
            {
                if (drawing) ToolManager.RestorePreviousPersistentTool();
                else { PointGraphEditContext.Exit(); ToolManager.SetActiveTool<SplineDrawTool>(); }
            }
            EditorGUILayout.EndHorizontal();
            if (editing && targets.Length == 1 && FloorPlanEditState.Mode == BrushEditMode.Vertex) DrawRoll(spline);
            if (spline.droppedPieces > 0) EditorGUILayout.HelpBox(BrushLanguage.Format("Не построено частей: {0}. Поворот слишком резкий для выбранного сечения.", spline.droppedPieces), MessageType.Warning);
        }

        /// <summary>Edit mode: the roll of the selected points, degrees (Rotate turns it in the Scene view).</summary>
        static void DrawRoll(Spline spline)
        {
            var sel = FloorPlanEditState.Sel(spline);
            if (sel.vertices.Count == 0) return;
            float first = 0f; bool mixed = false, any = false;
            foreach (var i in sel.vertices)
            {
                float r = spline.Roll(spline.pointIds[i]);
                if (!any) { first = r; any = true; } else if (!Mathf.Approximately(r, first)) mixed = true;
            }
            EditorGUILayout.LabelField(BrushLanguage.Format("Выбранные точки ({0})", sel.vertices.Count), EditorStyles.boldLabel);
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.FloatField(new GUIContent(Tr("Крен сечения"), Tr("Угол в градусах вокруг траектории в выбранных точках. Ноль сохраняет исходную ориентацию. Угол можно менять инструментом поворота в сцене.")), first);
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(spline, Tr("Изменить крен точек сплайна"));
            foreach (var i in sel.vertices) spline.SetRoll(spline.pointIds[i], value);
            BrushGenerators.MarkDirty(spline);
        }

        /// <summary>Does the path bend anywhere more tightly than half the profile's width (its inside would fold)?</summary>
        public static bool TighterThanProfile(Spline spline)
        {
            float half = Mathf.Max(spline.size.x, spline.size.y) * 0.5f;
            foreach (var run in spline.Runs())
            {
                var p = run.points; int n = p.Count;
                for (int i = 1; i + 1 < n || (run.closed && i < n); i++)
                {
                    var a = p[i - 1]; var b = p[i]; var c = p[(i + 1) % n];
                    float angle = Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad;
                    if (angle < 1e-3f) continue;
                    float radius = Mathf.Min((b - a).magnitude, (c - b).magnitude) * 0.5f / Mathf.Tan(angle * 0.5f); // the arc through the joint
                    if (radius < half && angle < 120f * Mathf.Deg2Rad) return true; // (a sharper fold is the curve's own business)
                }
            }
            return false;
        }

        void OnSceneGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (ToolManager.activeToolType == typeof(SplineDrawTool) || PointGraphEditContext.IsActive) return;
            FloorPlanTools.DrawOutline((Spline)target);
        }
    }

    /// <summary>
    /// Draw a spline: click to add a point and a link to it, on the surface under the mouse (the profile resting on it) or
    /// level with the last point. A click on a point or a link joins the stroke to it (splitting the link there) and ends
    /// the stroke. Backspace takes back the last link; Escape or a double click ends the stroke, Enter or a second Escape
    /// leaves the tool.
    /// </summary>
    [EditorTool("Draw Spline", typeof(Spline))]
    public sealed class SplineDrawTool : EditorTool
    {
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("Spline"), Tr("Рисовать сплайн"));

        enum Hover { None, Free, Point, Link }
        Hover m_Kind; Vector3 m_Hover; int m_HoverPoint = -1, m_HoverLink = -1;
        int m_Last = -1;
        readonly List<int> m_Stroke = new List<int>();

        /// <summary>The point (id) the next activation starts its first link from, or -1.</summary>
        static int s_Start = -1;

        /// <summary>
        /// Draw on the selected spline, from a point (id) when given. Unity makes a selection's component tools a little after
        /// the selection changes, so the editors are rebuilt first, else the switch fails.
        /// </summary>
        public static void Begin(int startPoint = -1)
        {
            s_Start = startPoint;
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            ToolManager.SetActiveTool<SplineDrawTool>();
            if (ToolManager.activeToolType != typeof(SplineDrawTool)) s_Start = -1; // the switch failed: no later activation may use it
        }

        public override void OnActivated()
        {
            BrushMenu.PrepareCreation();
            m_Last = -1; m_Stroke.Clear();
            int start = s_Start; s_Start = -1;
            if (target is Spline spline && start >= 0 && spline.PointIndex(start) >= 0) { m_Last = start; m_Stroke.Add(start); }
        }

        public override void OnWillBeDeactivated() => EndStroke();

        public override void OnToolGUI(EditorWindow window)
        {
            var spline = target as Spline;
            if (!(window is SceneView) || spline == null || !BrushSceneVisibility.CanPick(spline.transform)) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (m_Last >= 0 && spline.PointIndex(m_Last) < 0) { m_Last = -1; m_Stroke.Clear(); } // undone
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown || e.type == EventType.Repaint) FindHover(spline, e);
            if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == id && m_Kind != Hover.None)
            {
                e.Use();
                if (e.clickCount == 2) { EndStroke(); return; }
                Undo.RecordObject(spline, Tr("Рисовать сплайн"));
                bool joined = m_Kind != Hover.Free;
                int point = AddAt(spline, m_Kind == Hover.Point ? m_HoverPoint : -1, m_Kind == Hover.Link ? m_HoverLink : -1, m_Hover);
                bool started = m_Last >= 0;
                if (started && point != m_Last) spline.AddLink(m_Last, point);
                BrushGenerators.MarkDirty(spline);
                if (joined && started && point != m_Last) EndStroke();
                else { m_Last = point; m_Stroke.Add(point); }
            }
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace && m_Stroke.Count >= 2)
                {
                    Undo.RecordObject(spline, Tr("Удалить звено сплайна"));
                    int last = m_Stroke[m_Stroke.Count - 1], prev = m_Stroke[m_Stroke.Count - 2];
                    spline.links.RemoveAll(l => (l.start == prev && l.end == last) || (l.start == last && l.end == prev));
                    spline.RemoveLonePoints();
                    m_Stroke.RemoveAt(m_Stroke.Count - 1); m_Last = prev;
                    BrushGenerators.MarkDirty(spline); e.Use();
                }
                else if (e.keyCode == KeyCode.Escape) { if (m_Last >= 0) EndStroke(); else ToolManager.RestorePreviousPersistentTool(); e.Use(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { ToolManager.RestorePreviousPersistentTool(); e.Use(); }
            }
            if (e.type == EventType.Repaint) DrawPreview(spline);
        }

        /// <summary>The point a click makes: an existing point (by index), a new one splitting a link, or a new one (local). Returns its id.</summary>
        public static int AddAt(Spline spline, int pointIndex, int link, Vector3 local)
        {
            if (pointIndex >= 0) return spline.pointIds[pointIndex];
            if (link >= 0) return spline.SplitLink(link, local);
            return spline.AddPoint(local);
        }

        void EndStroke() { m_Last = -1; m_Stroke.Clear(); SceneView.RepaintAll(); }

        void FindHover(Spline spline, Event e)
        {
            m_Kind = Hover.None;
            int point = FloorPlanTools.PointUnderMouse(spline, e.mousePosition);
            if (point >= 0) { m_Kind = Hover.Point; m_HoverPoint = point; m_Hover = spline.points[point]; return; }
            int link = FloorPlanTools.WallUnderMouse(spline, e.mousePosition, out var onLink);
            if (link >= 0) { m_Kind = Hover.Link; m_HoverLink = link; m_Hover = onLink; return; }
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            int last = m_Last >= 0 ? spline.PointIndex(m_Last) : -1;
            if (FreePoint(spline, ray, last >= 0 ? FloorPlanTools.World(spline, spline.points[last]) : (Vector3?)null, out var world))
            {
                m_Kind = Hover.Free;
                m_Hover = spline.WorldToGraph(world);
            }
        }

        /// <summary>
        /// Where a click in the open lands, in world space: on the surface of a brush under the mouse, raised so the profile
        /// rests on it; else level with the last point (the spline's own height when there is none). On the grid across the
        /// surface when snapping is on.
        /// </summary>
        public static bool FreePoint(Spline spline, Ray ray, Vector3? last, out Vector3 world)
        {
            float g = BrushSettings.instance.snapToGrid ? BrushSettings.instance.GridMeters : 0f;
            var hit = BrushHooks.PickBrushSurface(ray, out var point, out var normal);
            if (hit != null && hit.generatedBy != spline) // never on its own pieces
            {
                float rest = spline.profile == Spline.Profile.Circle ? spline.size.x * 0.5f : spline.size.y * 0.5f;
                world = SnapAcross(point, normal, g) + normal * rest;
                return true;
            }
            float height = last.HasValue ? last.Value.y : spline.GraphToWorld(Vector3.zero).y;
            var level = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (!BrushDraw.TryGridPoint(ray, level, out point)) { world = default; return false; }
            world = SnapAcross(point, Vector3.up, g);
            return true;
        }

        /// <summary>A point on a surface moved to the grid along the surface (not off it).</summary>
        public static Vector3 SnapAcross(Vector3 p, Vector3 normal, float g)
        {
            if (g <= 0f) return p;
            var snapped = new Vector3(Mathf.Round(p.x / g) * g, Mathf.Round(p.y / g) * g, Mathf.Round(p.z / g) * g);
            var n = normal.normalized;
            return snapped - n * Vector3.Dot(snapped - p, n); // back onto the surface's plane
        }

        void DrawPreview(Spline spline)
        {
            FloorPlanTools.DrawOutline(spline);
            if (m_Kind == Hover.None) return;
            var hover = FloorPlanTools.World(spline, m_Hover);
            bool joining = m_Kind == Hover.Point || m_Kind == Hover.Link;
            Handles.color = joining ? Color.white : FloorPlanTools.Line;
            Handles.DotHandleCap(0, hover, Quaternion.identity, HandleUtility.GetHandleSize(hover) * 0.06f, EventType.Repaint);
            int last = m_Last >= 0 ? spline.PointIndex(m_Last) : -1;
            if (last < 0) return;
            Handles.color = FloorPlanTools.Line;
            Handles.DrawAAPolyLine(3f, FloorPlanTools.World(spline, spline.points[last]), hover);
        }
    }

    static class SplineMenu
    {
        [MenuItem("GameObject/CSG Brush/Сплайн", false, 41)]
        static void Create()
        {
            BrushMenu.PrepareObjectCreation();
            Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            float g = BrushSettings.instance.GridMeters;
            if (g > 0f) pivot = new Vector3(Mathf.Round(pivot.x / g) * g, 0f, Mathf.Round(pivot.z / g) * g);
            Create(pivot, BrushMenu.CreationParent(useSelection: true), false);
        }

        /// <summary>A new spline at a position, selected, drawing; with <paramref name="firstPoint"/> its first point is there.</summary>
        public static Spline Create(Vector3 position, Transform parent, bool firstPoint)
        {
            var go = new GameObject("Spline");
            GameObjectUtility.SetStaticEditorFlags(go, BrushSettings.instance.defaultModelStaticFlags); // its pieces take the spline's flags
            Undo.RegisterCreatedObjectUndo(go, Tr("Создать сплайн"));
            if (parent != null) Undo.SetTransformParent(go.transform, parent, Tr("Создать сплайн"));
            go.transform.position = position;
            var spline = Undo.AddComponent<Spline>(go);
            int first = firstPoint ? spline.AddPoint(Vector3.zero) : -1; // the draw tool starts its first link here
            Selection.activeGameObject = go;
            int toolVersion = BrushMenu.ToolVersion;
            EditorApplication.delayCall += () =>
            {
                if (BrushMenu.ToolVersion == toolVersion && go != null && !EditorApplication.isPlayingOrWillChangePlaymode &&
                    Selection.gameObjects.Length == 1 && Selection.activeGameObject == go) SplineDrawTool.Begin(first);
            };
            return spline;
        }
    }

    /// <summary>
    /// The Create tool's Spline: click to start a spline there (on a surface, the profile resting on it), then click its
    /// points. A click on a spline's point or link continues that spline from there, the selected spline first.
    /// </summary>
    [EditorTool("Spline", variantGroup = typeof(BrushCreateTool), variantPriority = 12)]
    public sealed class CreateSplineTool : EditorTool
    {
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("Spline"), Tr("Сплайн"));
        [MenuItem(BrushCommandMenu.CreateRoot + "Сплайн", false, 13)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Сплайн", false, 13)] static void Menu() => BrushMenu.ActivateCreation<CreateSplineTool>();

        Vector3 m_Hover; bool m_HoverValid;
        BrushSurfaceGrid.Frame m_Grid;
        /// <summary>The spline a click continues, and where: one of its points (index) or links (index and spot).</summary>
        Spline m_Spline; int m_Point = -1, m_Link = -1; Vector3 m_Local;
        /// <summary>The scene's splines, found again only when the hierarchy changes.</summary>
        List<Spline> m_Splines;

        public override void OnActivated() { BrushMenu.PrepareCreation(); m_Splines = null; EditorApplication.hierarchyChanged += Forget; }
        public override void OnWillBeDeactivated() => EditorApplication.hierarchyChanged -= Forget;
        void Forget() => m_Splines = null;

        void FindSpline(Vector2 mouse)
        {
            m_Spline = null; m_Point = m_Link = -1;
            var selected = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Spline>() : null;
            if (m_Splines == null)
            {
                m_Splines = new List<Spline>();
                foreach (var s in Object.FindObjectsByType<Spline>()) if (s.gameObject.scene.IsValid()) m_Splines.Add(s);
            }
            for (int i = -1; i < m_Splines.Count; i++)
            {
                var s = i < 0 ? selected : m_Splines[i]; // the selected spline first
                if (s == null || (i >= 0 && s == selected) || !BrushSceneVisibility.CanPick(s.transform)) continue;
                s.EnsureGraph();
                int point = FloorPlanTools.PointUnderMouse(s, mouse);
                if (point >= 0) { m_Spline = s; m_Point = point; return; }
                int link = FloorPlanTools.WallUnderMouse(s, mouse, out var local);
                if (link < 0) continue;
                m_Spline = s; m_Link = link; m_Local = local;
                return;
            }
        }

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
                FindSpline(e.mousePosition);
                if (m_Spline != null) { m_HoverValid = true; m_Hover = FloorPlanTools.World(m_Spline, m_Point >= 0 ? m_Spline.points[m_Point] : m_Local); }
                else m_HoverValid = StartPoint(HandleUtility.GUIPointToWorldRay(e.mousePosition), out m_Hover, out m_Grid);
                if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            }
            if (e.type == EventType.MouseDown && e.button == 0 && m_HoverValid && HandleUtility.nearestControl == id)
            {
                if (m_Spline != null)
                {
                    var s = m_Spline;
                    Undo.RecordObject(s, Tr("Рисовать сплайн"));
                    int start = SplineDrawTool.AddAt(s, m_Point, m_Point >= 0 ? -1 : m_Link, m_Local);
                    BrushGenerators.MarkDirty(s);
                    Selection.activeGameObject = s.gameObject;
                    int toolVersion = BrushMenu.ToolVersion;
                    EditorApplication.delayCall += () =>
                    {
                        if (BrushMenu.ToolVersion == toolVersion && s != null && Selection.activeGameObject == s.gameObject)
                            SplineDrawTool.Begin(start);
                    };
                }
                else SplineMenu.Create(m_Hover, BrushMenu.CreationParent(), true);
                m_HoverValid = false;
                e.Use();
            }
            if (e.type == EventType.Repaint && m_HoverValid && (m_Spline == null || BrushSceneVisibility.CanPick(m_Spline.transform)))
            {
                bool joining = m_Spline != null; // white and larger on a spline's point or link, as when drawing
                if (!joining) BrushSurfaceGrid.Draw(m_Grid, m_Hover);
                Handles.color = joining ? Color.white : FloorPlanTools.Line;
                Handles.DotHandleCap(-1, m_Hover, Quaternion.identity, HandleUtility.GetHandleSize(m_Hover) * (joining ? 0.09f : 0.06f), EventType.Repaint);
            }
        }

        /// <summary>
        /// Where a new spline starts: on the surface under the mouse, raised so a new spline's profile rests on it, else on
        /// the ground (y = 0); on the grid across the surface.
        /// </summary>
        static bool StartPoint(Ray ray, out Vector3 world, out BrushSurfaceGrid.Frame grid)
        {
            float g = BrushSettings.instance.snapToGrid ? BrushSettings.instance.GridMeters : 0f;
            float rest = Spline.DefaultSize.x * 0.5f; // a new spline's circle
            var surface = BrushHooks.PickBrushSurface(ray, out var point, out var normal);
            if (surface == null)
            {
                normal = Vector3.up;
                if (!BrushDraw.TryGridPoint(ray, new Plane(normal, Vector3.zero), out point))
                {
                    grid = default; world = default; return false;
                }
            }
            grid = surface != null && BrushSurfaceGrid.Local ? BrushSurfaceGrid.ForSurface(surface, point, normal)
                : BrushSurfaceGrid.Frame.OnPlane(point, normal);
            world = BrushDraw.SnapInPlane(point, grid.Origin, grid.Rotation, g) + normal * rest;
            return true;
        }
    }
}
