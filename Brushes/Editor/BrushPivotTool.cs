using static CsgBrush.Editor.BrushLanguage;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    [EditorTool("Snap WCS to Vertex")]
    public sealed class BrushPivotTool : EditorTool
    {
        const float PickRadius = 12f; // GUI points
        internal const string PickLabel = "Привязать WCS к вершине";
        internal static GUIContent PickCommand => new GUIContent(Tr(PickLabel),
            Tr("Укажите вершину или середину ребра в сцене. Туда переместится начало рабочей системы координат объекта. При привязке по форме сетка переключится на начало WCS. Esc отменяет выбор."));
        internal static GUIContent ResetCommand => new GUIContent(Tr("Сбросить WCS"),
            Tr("Вернуть начало рабочей системы координат объекта в центр его габарита. Геометрия останется на месте."));
        internal static bool Active => ToolManager.activeToolType == typeof(BrushPivotTool);
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("Mode_Vertex"), PickCommand.tooltip);
        Component source;
        public override bool IsAvailable() => CanEdit(Selected());

        static Component Selected()
        {
            var go = Selection.activeGameObject;
            if (go == null || Selection.gameObjects.Length != 1) return null;
            return go.TryGetComponent<PointGraph>(out var graph) ? (Component)graph : go.GetComponent<Brush>();
        }

        static bool CanEdit(Component item) => item != null && item.gameObject.scene.IsValid() &&
            !EditorApplication.isPlayingOrWillChangePlaymode && !EditorUtility.IsPersistent(item) &&
            (item is PointGraph graph && graph.points.Count > 0 || item is Brush brush && BrushApi.CanPickPivot(brush));

        public override void OnActivated()
        {
            source = Selected();
            Selection.selectionChanged += OnSelectionChanged;
        }

        public override void OnWillBeDeactivated() => Selection.selectionChanged -= OnSelectionChanged;

        void OnSelectionChanged()
        {
            if (Active && source != Selected()) ToolManager.RestorePreviousPersistentTool();
        }

        internal static void DrawButtons(Component item)
        {
            using (new EditorGUI.DisabledScope(!CanEdit(item)))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(PickCommand)) Schedule(item, reset: false);
                if (GUILayout.Button(ResetCommand)) Schedule(item, reset: true);
            }
        }

        internal static void DrawMenu()
        {
            if (!GUILayout.Button(new GUIContent("WCS ▾", Tr("Выберите один браш, план этажа или сплайн, чтобы задать или сбросить начало WCS.")),
                    EditorStyles.miniButton, GUILayout.ExpandWidth(false), GUILayout.Height(18))) return;
            var item = Selected();
            var menu = new GenericMenu();
            if (CanEdit(item))
            {
                menu.AddItem(PickCommand, false, () => Schedule(item, reset: false));
                menu.AddItem(ResetCommand, false, () => Schedule(item, reset: true));
            }
            else
            {
                menu.AddDisabledItem(PickCommand);
                menu.AddDisabledItem(ResetCommand);
            }
            menu.ShowAsContext();
        }

        internal static void Schedule(Component item, bool reset)
        {
            // A tool/context change can rebuild the active editor. Finish the Inspector's IMGUI pass first.
            int toolVersion = BrushMenu.ToolVersion;
            EditorApplication.delayCall += () =>
            {
                if (BrushMenu.ToolVersion != toolVersion || item != Selected() || !CanEdit(item)) return;
                if (reset)
                {
                    if (item is PointGraph graph) GeneratorWcs.SetOrigin(graph, GeneratorWcs.Geometry(graph).bounds.center);
                    else BrushApi.ResetPivot((Brush)item);
                    MoveFromPivot();
                }
                else Begin();
            };
        }

        static void Begin()
        {
            BrushEditContext.Exit();
            PointGraphEditContext.Exit();
            ToolManager.SetActiveTool<BrushPivotTool>();
            SceneView.lastActiveSceneView?.Focus();
            SceneView.RepaintAll();
        }

        internal static void MoveFromPivot()
        {
            BrushEditContext.Exit();
            PointGraphEditContext.Exit();
            Tools.current = Tool.Move;
            Tools.pivotMode = UnityEditor.PivotMode.Pivot;
            Tools.pivotRotation = PivotRotation.Local;
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView view) || !CanEdit(source)) return;
            var e = Event.current;
            if (e.alt || Tools.viewToolActive) return;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                ToolManager.RestorePreviousPersistentTool();
                e.Use();
                return;
            }

            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            if (e.type == EventType.MouseMove) view.Repaint();
            if (e.type != EventType.Repaint && !(e.type == EventType.MouseDown && e.button == 0)) return;

            float closest = PickRadius * PickRadius, depth = float.PositiveInfinity;
            bool picked = false, pickedEdge = false;
            Vector3 pickedWorld = default, pickedLocal = default;
            void Consider(Vector3 local, Vector3 world, bool edge)
            {
                var screen = HandleUtility.WorldToGUIPointWithDepth(world);
                if (screen.z < view.camera.nearClipPlane || screen.z > view.camera.farClipPlane) return;
                float d = ((Vector2)screen - e.mousePosition).sqrMagnitude;
                if (d < closest || (Mathf.Approximately(d, closest) && screen.z < depth))
                {
                    picked = true;
                    pickedEdge = edge;
                    pickedLocal = local;
                    pickedWorld = world;
                    closest = d;
                    depth = screen.z;
                }
                if (e.type != EventType.Repaint) return;
                float size = HandleUtility.GetHandleSize(world);
                if (!edge) Handles.DotHandleCap(0, world, Quaternion.identity, size * 0.035f, EventType.Repaint);
                else Handles.RectangleHandleCap(0, world, view.camera.transform.rotation, size * 0.027f, EventType.Repaint);
            }

            var vertexColor = new Color(0.35f, 0.92f, 0.86f);
            var edgeColor = new Color(0.60f, 0.72f, 1f);
            if (source is PointGraph graph)
            {
                var geometry = GeneratorWcs.Geometry(graph);
                using (new Handles.DrawingScope(vertexColor))
                    foreach (var point in geometry.vertices) Consider(point, graph.GraphToWorld(point), false);
                using (new Handles.DrawingScope(edgeColor))
                    foreach (var point in geometry.midpoints) Consider(point, graph.GraphToWorld(point), true);
            }
            else
            {
                var poly = BrushHooks.LocalShape((Brush)source, out _);
                if (poly == null) return;
                var matrix = source.transform.localToWorldMatrix;
                using (new Handles.DrawingScope(vertexColor))
                    foreach (var point in poly.vertices) Consider(point, matrix.MultiplyPoint3x4(point), false);
                using (new Handles.DrawingScope(edgeColor))
                    foreach (var (a, b) in poly.Edges())
                    {
                        var point = (poly.vertices[a] + poly.vertices[b]) * 0.5f;
                        Consider(point, matrix.MultiplyPoint3x4(point), true);
                    }
            }
            if (e.type == EventType.Repaint && picked)
            {
                using (new Handles.DrawingScope(Color.yellow))
                    Handles.SphereHandleCap(0, pickedWorld, Quaternion.identity, HandleUtility.GetHandleSize(pickedWorld) * 0.12f, EventType.Repaint);
                Handles.Label(pickedWorld, pickedEdge ? Tr("  Середина ребра") : Tr("  Вершина"));
            }
            if (e.type == EventType.MouseDown && HandleUtility.nearestControl == control)
            {
                e.Use();
                if (!picked) return;
                if (source is PointGraph path) GeneratorWcs.SetOrigin(path, pickedLocal);
                else BrushApi.SetPickedPivot((Brush)source, pickedLocal);
                MoveFromPivot();
            }
        }
    }
}
