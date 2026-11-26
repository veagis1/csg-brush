using static CsgBrush.Editor.BrushLanguage;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    [EditorTool("Snap WCS")]
    public sealed class BrushPivotTool : EditorTool
    {
        const float PickRadius = 12f; // GUI points
        internal const string PickLabel = "Привязать WCS";
        internal static GUIContent PickCommand => new GUIContent(Tr(PickLabel),
            Tr("Укажите вершину, середину ребра или центр грани в сцене. Туда переместится начало рабочей системы координат объекта. При привязке по форме сетка переключится на начало WCS. Esc отменяет выбор."));
        internal static GUIContent ResetCommand => new GUIContent(Tr("Сбросить WCS"),
            Tr("Вернуть начало рабочей системы координат объекта в центр его габарита. Геометрия останется на месте."));
        internal static bool Active => ToolManager.activeToolType == typeof(BrushPivotTool);
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("Mode_Vertex"), PickCommand.tooltip);
        Component source;
        PlacementGeometry brushGeometry;
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
            ObjectChangeEvents.changesPublished += InvalidateGeometry;
        }

        void InvalidateGeometry(ref ObjectChangeEventStream changes) => brushGeometry = null;

        public override void OnWillBeDeactivated()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            ObjectChangeEvents.changesPublished -= InvalidateGeometry;
            brushGeometry = null;
        }

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
            bool picked = false;
            PlacementMarkerKind pickedKind = default;
            Vector3 pickedWorld = default, pickedLocal = default;
            void Consider(Vector3 local, Vector3 world, PlacementMarkerKind kind)
            {
                var screen = HandleUtility.WorldToGUIPointWithDepth(world);
                if (screen.z < view.camera.nearClipPlane || screen.z > view.camera.farClipPlane) return;
                float d = ((Vector2)screen - e.mousePosition).sqrMagnitude;
                if (d < closest || (Mathf.Approximately(d, closest) && screen.z < depth))
                {
                    picked = true;
                    pickedKind = kind;
                    pickedLocal = local;
                    pickedWorld = world;
                    closest = d;
                    depth = screen.z;
                }
                if (e.type != EventType.Repaint) return;
                PlacementMarker.Draw(world, view.camera.transform.rotation, kind, 0.032f);
            }

            using (new Handles.DrawingScope(new Color(1f, 0.85f, 0.3f)))
            {
                if (source is PointGraph graph)
                {
                    var geometry = GeneratorWcs.Geometry(graph);
                    foreach (var p in geometry.vertices) Consider(p, graph.GraphToWorld(p), PlacementMarkerKind.Vertex);
                    foreach (var p in geometry.midpoints) Consider(p, graph.GraphToWorld(p), PlacementMarkerKind.Midpoint);
                    foreach (var p in geometry.centers) Consider(p, graph.GraphToWorld(p), PlacementMarkerKind.FaceCenter);
                }
                else
                {
                    brushGeometry ??= PlacementGeometry.FromBrush((Brush)source);
                    if (brushGeometry == null) return;
                    var matrix = source.transform.localToWorldMatrix;
                    void Local(Vector3 p, PlacementMarkerKind kind) => Consider(p, matrix.MultiplyPoint3x4(p), kind);
                    foreach (var p in brushGeometry.Vertices) Local(p, PlacementMarkerKind.Vertex);
                    foreach (var edge in brushGeometry.Edges)
                        Local((brushGeometry.Vertices[edge.x] + brushGeometry.Vertices[edge.y]) * 0.5f, PlacementMarkerKind.Midpoint);
                    foreach (var face in brushGeometry.Faces) Local(face.Center, PlacementMarkerKind.FaceCenter);
                }
            }
            if (e.type == EventType.Repaint && picked)
            {
                using (new Handles.DrawingScope(Color.yellow))
                    PlacementMarker.Draw(pickedWorld, view.camera.transform.rotation, pickedKind, 0.075f);
                Handles.Label(pickedWorld, pickedKind == PlacementMarkerKind.FaceCenter ? Tr("  Центр грани") :
                    pickedKind == PlacementMarkerKind.Midpoint ? Tr("  Середина ребра") : Tr("  Вершина"));
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
