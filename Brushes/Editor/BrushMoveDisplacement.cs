using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    // Observe Unity's Move and Rotate handles without taking a control or writing a Transform.
    [InitializeOnLoad]
    internal static class BrushMoveDisplacement
    {
        static SceneView owner;
        static Transform target;
        static Brush brush;
        static Vector3 start;
        static Quaternion rotation;
        static int axesAtPress;
        static Quaternion startRotation;
        static Vector3 rotationDegrees;
        static int rotationAxes;
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
            var tool = Tools.current;
            if (!BrushSettings.instance.showRelativeDisplacement || (tool != Tool.Move && tool != Tool.Rotate) ||
                ToolManager.activeContextType != typeof(GameObjectToolContext))
            {
                if (owner != null) Reset();
                return;
            }

            if (e.rawType == EventType.MouseDown && e.button == 0 && !e.alt && !Tools.viewToolActive)
            {
                target = Selection.activeTransform;
                if (!BrushSceneVisibility.CanPick(target)) { Reset(); return; }
                owner = view;
                target.TryGetComponent(out brush);
                start = target.position;
                rotation = Tools.handleRotation;
                if (tool == Tool.Move)
                    axesAtPress = PickAxes(view.camera, Tools.handlePosition, rotation);
                else
                {
                    startRotation = target.rotation;
                    rotationDegrees = Vector3.zero;
                    rotationAxes = 0;
                }
            }
            if (owner != view) return;
            if (!BrushSceneVisibility.CanPick(target) || e.rawType == EventType.MouseUp || e.rawType == EventType.Ignore ||
                (e.rawType == EventType.KeyDown && e.keyCode == KeyCode.Escape))
            {
                Reset();
                return;
            }
            if (tool == Tool.Rotate)
            {
                // RotateTool applies its change directly to the Transform, outside TransformManipulator.
                if (GUIUtility.hotControl != 0) UpdateRotation(view, e);
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
                    BrushDimensionFields.DrawDisplacement(view, pivot + rotation * Axes[arrow] * length, pivot,
                        BrushDimensionFields.FormatDisplacement(delta[world], world));
            }
        }

        static void UpdateRotation(SceneView view, Event e)
        {
            rotationDegrees = RelativeRotation(startRotation, target.rotation, rotation, rotationDegrees);
            int moving = DragAxes(0, rotationDegrees);
            if (moving != 0) rotationAxes = moving; // keep the ring's zero label when returning to the start
            if (e.type != EventType.Repaint || rotationAxes == 0) return;

            var degrees = rotationDegrees;
            if (brush != null && BrushSnap.TryGetPreview(brush, out var pose, out _))
                degrees = RelativeRotation(startRotation, pose.rotation, rotation, rotationDegrees);
            var pivot = Tools.handlePosition;
            float radius = HandleUtility.GetHandleSize(pivot);
            for (int axis = 0; axis < 3; axis++)
            {
                if ((rotationAxes & (1 << axis)) == 0) continue;
                var tip = HandleUtility.ClosestPointToDisc(pivot, rotation * Axes[axis], radius);
                BrushDimensionFields.DrawDisplacement(view, tip, pivot, BrushDimensionFields.FormatRotation(degrees[axis], axis));
            }
        }

        // Axis-angle of the relative orientation, expressed in the handle basis captured at mouse-down.
        // Choose the equivalent turn closest to the previous sample so crossing 180° or 360° keeps the sign.
        internal static Vector3 RelativeRotation(Quaternion start, Quaternion current, Quaternion basis, Vector3 previousDegrees)
        {
            var delta = current * Quaternion.Inverse(start);
            var vector = Quaternion.Inverse(basis) * new Vector3(delta.x, delta.y, delta.z);
            // Quaternion arithmetic can leave orthogonal components even on a single-axis ring.
            // This tolerance is below half the last displayed decimal place in degrees.
            for (int axis = 0; axis < 3; axis++)
                if (Mathf.Abs(vector[axis]) < 0.000001f) vector[axis] = 0f;
            float length = vector.magnitude;
            if (length == 0f)
                return previousDegrees.normalized * (360f * Mathf.Round(previousDegrees.magnitude / 360f));
            var direction = vector / length;
            float angle = 2f * Mathf.Atan2(length, delta.w) * Mathf.Rad2Deg;
            float turns = Mathf.Round((Vector3.Dot(previousDegrees, direction) - angle) / 360f);
            return direction * (angle + 360f * turns);
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
