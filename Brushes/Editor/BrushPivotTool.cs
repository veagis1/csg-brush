using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    [EditorTool(BrushPivotTool.PickLabel, typeof(Brush))]
    public sealed class BrushPivotTool : EditorTool
    {
        const float PickRadius = 12f; // GUI points
        internal const string PickLabel = "Привязать WCS к вершине";
        internal static readonly GUIContent PickCommand = new GUIContent(PickLabel,
            "Укажите вершину или середину ребра в сцене. Туда переместится начало рабочей системы координат объекта. При привязке по форме сетка переключится на начало WCS. Esc отменяет выбор.");
        internal static readonly GUIContent ResetCommand = new GUIContent("Сбросить WCS",
            "Вернуть начало рабочей системы координат объекта в центр его габарита. Геометрия останется на месте.");
        internal static bool Active => ToolManager.activeToolType == typeof(BrushPivotTool);
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("Mode_Vertex"), PickCommand.tooltip);
        public override bool IsAvailable() => IsSelectedBrush(target as Brush);

        static bool IsSelectedBrush(Brush brush) => brush != null && brush.gameObject.scene.IsValid() &&
            Selection.activeGameObject == brush.gameObject && Selection.gameObjects.Length == 1 &&
            !EditorApplication.isPlayingOrWillChangePlaymode && !EditorUtility.IsPersistent(brush) && BrushApi.CanPickPivot(brush);

        internal static void DrawMenu()
        {
            if (!GUILayout.Button(new GUIContent("WCS ▾", "WCS — рабочая система координат объекта. Выберите один редактируемый браш, чтобы задать или сбросить начало WCS."),
                    EditorStyles.miniButton, GUILayout.ExpandWidth(false), GUILayout.Height(18))) return;
            var brush = Selection.activeGameObject?.GetComponent<Brush>();
            var menu = new GenericMenu();
            if (IsSelectedBrush(brush))
            {
                menu.AddItem(PickCommand, false, () => Schedule(brush, reset: false));
                menu.AddItem(ResetCommand, false, () => Schedule(brush, reset: true));
            }
            else
            {
                menu.AddDisabledItem(PickCommand);
                menu.AddDisabledItem(ResetCommand);
            }
            menu.ShowAsContext();
        }

        internal static void Schedule(Brush brush, bool reset)
        {
            // A tool/context change can rebuild the active editor. Finish the Inspector's IMGUI pass first.
            int toolVersion = BrushMenu.ToolVersion;
            EditorApplication.delayCall += () =>
            {
                if (BrushMenu.ToolVersion != toolVersion || !IsSelectedBrush(brush)) return;
                if (reset)
                {
                    BrushApi.ResetPivot(brush);
                    MoveFromPivot();
                }
                else Begin();
            };
        }

        static void Begin()
        {
            BrushEditContext.Exit();
            ToolManager.SetActiveTool<BrushPivotTool>();
            SceneView.lastActiveSceneView?.Focus();
            SceneView.RepaintAll();
        }

        internal static void MoveFromPivot()
        {
            BrushEditContext.Exit();
            Tools.current = Tool.Move;
            Tools.pivotMode = UnityEditor.PivotMode.Pivot;
            Tools.pivotRotation = PivotRotation.Local;
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView view) || !IsSelectedBrush(target as Brush)) return;
            var brush = (Brush)target;
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

            var poly = BrushHooks.LocalShape(brush, out _);
            if (poly == null) return;
            var matrix = brush.transform.localToWorldMatrix;
            float closest = PickRadius * PickRadius, depth = float.PositiveInfinity;
            int picked = -1, edgeEnd = -1;
            Vector3 pickedWorld = default;
            void Consider(Vector3 local, int a, int b = -1)
            {
                var world = matrix.MultiplyPoint3x4(local);
                var screen = HandleUtility.WorldToGUIPointWithDepth(world);
                if (screen.z < view.camera.nearClipPlane || screen.z > view.camera.farClipPlane) return;
                float d = ((Vector2)screen - e.mousePosition).sqrMagnitude;
                if (d < closest || (Mathf.Approximately(d, closest) && screen.z < depth))
                {
                    picked = a;
                    edgeEnd = b;
                    pickedWorld = world;
                    closest = d;
                    depth = screen.z;
                }
                if (e.type != EventType.Repaint) return;
                float size = HandleUtility.GetHandleSize(world);
                if (b < 0) Handles.DotHandleCap(0, world, Quaternion.identity, size * 0.035f, EventType.Repaint);
                else Handles.RectangleHandleCap(0, world, view.camera.transform.rotation, size * 0.027f, EventType.Repaint);
            }

            using (new Handles.DrawingScope(new Color(0.35f, 0.92f, 0.86f)))
                for (int i = 0; i < poly.vertices.Length; i++)
                    Consider(poly.vertices[i], i);
            using (new Handles.DrawingScope(new Color(0.60f, 0.72f, 1f)))
            {
                foreach (var (a, b) in poly.Edges())
                    Consider((poly.vertices[a] + poly.vertices[b]) * 0.5f, a, b);
            }
            if (e.type == EventType.Repaint && picked >= 0)
            {
                using (new Handles.DrawingScope(Color.yellow))
                    Handles.SphereHandleCap(0, pickedWorld, Quaternion.identity, HandleUtility.GetHandleSize(pickedWorld) * 0.12f, EventType.Repaint);
                Handles.Label(pickedWorld, edgeEnd < 0 ? "  Вершина" : "  Середина ребра");
            }
            if (e.type == EventType.MouseDown && HandleUtility.nearestControl == control)
            {
                e.Use();
                if (picked < 0) return;
                bool applied = edgeEnd < 0 ? BrushApi.SetPivotToVertex(brush, picked) : BrushApi.SetPivotToEdge(brush, picked, edgeEnd);
                if (applied) MoveFromPivot();
            }
        }
    }
}
