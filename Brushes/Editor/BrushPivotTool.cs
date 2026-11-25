using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    [EditorTool("Привязать гизмо к вершине", typeof(Brush))]
    public sealed class BrushPivotTool : EditorTool
    {
        const float PickRadius = 12f; // GUI points
        internal static bool Active => ToolManager.activeToolType == typeof(BrushPivotTool);
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("Mode_Vertex"), "Привязать гизмо к вершине");
        public override bool IsAvailable() => targets != null && Selection.gameObjects.Length == 1 && BrushApi.CanPickPivot(target as Brush);

        internal static void Begin(Brush brush)
        {
            Selection.activeGameObject = brush.gameObject;
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
            if (!(window is SceneView view) || !BrushApi.CanPickPivot(target as Brush)) return;
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
            int picked = -1;
            using (new Handles.DrawingScope(new Color(0.35f, 0.92f, 0.86f)))
            {
                for (int i = 0; i < poly.vertices.Length; i++)
                {
                    var world = matrix.MultiplyPoint3x4(poly.vertices[i]);
                    var screen = HandleUtility.WorldToGUIPointWithDepth(world);
                    if (screen.z < view.camera.nearClipPlane || screen.z > view.camera.farClipPlane) continue;
                    float d = ((Vector2)screen - e.mousePosition).sqrMagnitude;
                    if (d < closest || (Mathf.Approximately(d, closest) && screen.z < depth))
                    {
                        picked = i;
                        closest = d;
                        depth = screen.z;
                    }
                    if (e.type == EventType.Repaint)
                        Handles.DotHandleCap(0, world, Quaternion.identity, HandleUtility.GetHandleSize(world) * 0.035f, EventType.Repaint);
                }
            }
            if (e.type == EventType.Repaint && picked >= 0)
            {
                var world = matrix.MultiplyPoint3x4(poly.vertices[picked]);
                using (new Handles.DrawingScope(Color.yellow))
                    Handles.SphereHandleCap(0, world, Quaternion.identity, HandleUtility.GetHandleSize(world) * 0.12f, EventType.Repaint);
                Handles.Label(world, "  Точка привязки");
            }
            if (e.type == EventType.MouseDown && HandleUtility.nearestControl == control)
            {
                if (picked >= 0 && BrushApi.SetPivotToVertex(brush, picked)) MoveFromPivot();
                e.Use();
            }
        }
    }
}
