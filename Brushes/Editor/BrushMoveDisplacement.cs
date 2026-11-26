using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    // Observe the native Move handle. The readout never takes a control or writes a Transform.
    [InitializeOnLoad]
    internal static class BrushMoveDisplacement
    {
        static SceneView owner;
        static Transform target;
        static Brush brush;
        static Vector3 start;
        static Quaternion rotation;
        static int axesAtPress;
        static bool shift;
        static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };

        // Unity exposes the current tool and hotControl, but a shape face can also own hotControl
        // while Move is selected. Only its built-in manipulator knows it actually took the press.
        static readonly Func<bool> NativeDrag = (Func<bool>)typeof(Tools).Assembly
            .GetType("UnityEditor.TransformManipulator")?.GetProperty("active",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true)
            ?.CreateDelegate(typeof(Func<bool>));

        static BrushMoveDisplacement()
        {
            Selection.selectionChanged += Reset;
            ToolManager.activeToolChanged += Reset;
            Undo.undoRedoPerformed += Reset;
            EditorApplication.playModeStateChanged += _ => Reset();
        }

        static void Reset()
        {
            owner = null;
            target = null;
            brush = null;
        }

        internal static void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            if (!BrushSettings.instance.showRelativeDisplacement || Tools.current != Tool.Move ||
                ToolManager.activeContextType != typeof(GameObjectToolContext))
            {
                if (owner != null) Reset();
                return;
            }

            if (e.rawType == EventType.MouseDown && e.button == 0 && !e.alt && !Tools.viewToolActive)
            {
                target = Selection.activeTransform;
                if (target == null) { Reset(); return; }
                owner = view;
                target.TryGetComponent(out brush);
                start = target.position;
                rotation = Tools.handleRotation;
                axesAtPress = PickAxes(view.camera, Tools.handlePosition, rotation);
            }
            if (owner != view) return;
            if (target == null || e.rawType == EventType.MouseUp || e.rawType == EventType.Ignore ||
                (e.rawType == EventType.KeyDown && e.keyCode == KeyCode.Escape))
            {
                Reset();
                return;
            }
            // Repaint events may have no modifier flags of their own.
            if (e.rawType == EventType.MouseDown || e.rawType == EventType.MouseDrag ||
                e.rawType == EventType.KeyDown || e.rawType == EventType.KeyUp)
                shift = (e.keyCode == KeyCode.LeftShift || e.keyCode == KeyCode.RightShift)
                    ? e.rawType == EventType.KeyDown : e.shift;
            if (GUIUtility.hotControl == 0 || NativeDrag?.Invoke() != true) return;
            if (e.rawType == EventType.KeyDown || e.rawType == EventType.KeyUp) view.Repaint();
            if (e.type != EventType.Repaint) return;

            var rawDelta = target.position - start;
            var delta = rawDelta;
            if (brush != null && BrushSnap.TryGetPreview(brush, out var pose, out _))
                delta = (Vector3)pose.GetColumn(3) - start;
            int axes = shift ? 7 : DragAxes(axesAtPress, Quaternion.Inverse(rotation) * rawDelta);
            Vector3 pivot = Tools.handlePosition;
            float length = HandleUtility.GetHandleSize(pivot);
            for (int world = 0; world < 3; world++)
            {
                int arrow = ArrowForWorldAxis(axes, rotation, world);
                if (arrow >= 0)
                    BrushDimensionFields.DrawDisplacement(view, pivot + rotation * Axes[arrow] * length, pivot, delta[world], world);
            }
        }

        // The native plane squares extend one quarter of a handle into the camera-facing octant.
        // Remember the pressed plane even when the mouse subsequently moves parallel to just one axis.
        internal static int PickAxes(Camera camera, Vector3 pivot, Quaternion basis)
        {
            var cameraDirection = camera.orthographic ? -camera.transform.forward : camera.transform.position - pivot;
            var facing = Quaternion.Inverse(basis) * cameraDirection.normalized;
            float length = HandleUtility.GetHandleSize(pivot);
            int result = 0;
            float nearest = float.PositiveInfinity;
            // Use the same GUI-point distances as Unity's rectangle, shaft and cone caps.
            // A plane remains pickable within the editor's hit radius outside the drawn square.
            for (int a = 0; a < 3; a++)
            {
                int b = (a + 1) % 3, normal = (a + 2) % 3;
                if (AxisHidden(facing[a]) || AxisHidden(facing[b])) continue;
                float half = length * 0.125f;
                var offset = Axes[a] * Mathf.Sign(facing[a] + 0.01f) + Axes[b] * Mathf.Sign(facing[b] + 0.01f);
                var orientation = basis * Quaternion.LookRotation(Axes[normal], Axes[b]);
                float distance = HandleUtility.DistanceToRectangle(pivot + basis * offset * half, orientation, half);
                if (distance <= nearest) { nearest = distance; result = (1 << a) | (1 << b); }
            }
            for (int axis = 0; axis < 3; axis++)
            {
                if (AxisHidden(facing[axis])) continue;
                var direction = basis * Axes[axis];
                var shaft = HandleUtility.DistanceToLine(pivot, pivot + direction * (length * 0.9f));
                var cone = HandleUtility.DistanceToCone(pivot + direction * length, Quaternion.LookRotation(direction), length * 0.2f);
                float distance = Mathf.Min(shaft, cone);
                if (distance <= nearest) { nearest = distance; result = 1 << axis; }
            }
            return result;
        }

        // The standard Move handle suppresses nearly end-on axes and their planes at 60% camera fade.
        static bool AxisHidden(float towardCamera) => towardCamera < -0.9421f || towardCamera > 0.9916f;

        internal static int DragAxes(int pressed, Vector3 localDelta)
        {
            float maximum = Mathf.Max(Mathf.Abs(localDelta.x), Mathf.Max(Mathf.Abs(localDelta.y), Mathf.Abs(localDelta.z)));
            int moving = 0;
            for (int axis = 0; axis < 3; axis++)
                if (Mathf.Abs(localDelta[axis]) > maximum * 0.0001f) moving |= 1 << axis;
            return (moving & ~pressed) == 0 ? pressed : moving;
        }

        // A local arrow at an angle can change multiple world coordinates. Keep their signed world
        // values together at that arrow. At a quarter turn, only the corresponding world axis appears.
        internal static int ArrowForWorldAxis(int selectedAxes, Quaternion basis, int world)
        {
            int result = -1;
            float largest = 0.0001f;
            for (int axis = 0; axis < 3; axis++)
            {
                if ((selectedAxes & (1 << axis)) == 0) continue;
                float component = Mathf.Abs((basis * Axes[axis])[world]);
                if (component <= largest) continue;
                largest = component;
                result = axis;
            }
            return result;
        }
    }
}
