using static CsgBrush.Editor.BrushLanguage;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    [InitializeOnLoad]
    internal static class BrushDimensionFields
    {
        static GUIStyle badge;
        static string activeKey, draft;
        static SceneView owner;
        static bool focus;
        static readonly List<Rect> placedLabels = new List<Rect>();
        static readonly Color Plate = new Color(0.08f, 0.10f, 0.12f, 0.94f);
        static readonly Color Accent = new Color(0.33f, 0.92f, 0.95f);
        static readonly string[] AxisLetters =
        {
            "<color=#FF7373>X</color>",
            "<color=#79DD75>Y</color>",
            "<color=#78AAFF>Z</color>"
        };

        static BrushDimensionFields()
        {
            Selection.selectionChanged += Reset;
            Undo.undoRedoPerformed += Reset;
            ToolManager.activeToolChanged += Reset;
            EditorApplication.playModeStateChanged += _ => Reset();
            SceneView.beforeSceneGui += view =>
            {
                placedLabels.Clear();
                if (owner != view || (BrushSettings.instance.showDimensions && BrushSceneVisibility.CanPick(Selection.activeTransform))) return;
                if (GUI.GetNameOfFocusedControl() == activeKey)
                {
                    GUI.FocusControl(null);
                    EditorGUIUtility.editingTextField = false;
                }
                Close();
            };
        }

        static void Reset()
        {
            Close();
        }

        internal static bool IsEditingInCurrentView => activeKey != null && owner == SceneView.currentDrawingSceneView;

        internal static void Cancel() => Close();

        static void Close()
        {
            if (activeKey == null) return;
            activeKey = null;
            owner = null;
            focus = false;
            SceneView.RepaintAll();
        }

        static GUIStyle Badge
        {
            get
            {
                if (badge != null) return badge;
                badge = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 14, richText = true };
                badge.normal.textColor = Color.white;
                return badge;
            }
        }

        internal static bool TryMeters(string value, bool signed, out float meters)
        {
            return float.TryParse(value?.Trim().Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out meters) && float.IsFinite(meters) && (signed || meters >= 0.001f);
        }

        internal readonly struct Edge
        {
            internal readonly int Axis;
            internal readonly Vector3 Midpoint, Direction, Centre;

            internal Edge(int axis, Vector3 midpoint, Vector3 direction, Vector3 centre)
            {
                Axis = axis;
                Midpoint = midpoint;
                Direction = direction;
                Centre = centre;
            }
        }

        static string Suffix(int axis) => axis < 0 ? Tr("м") : Tr("м") + " (" + AxisLetters[axis] + ")";

        internal static string FormatDimension(float meters, int axis) =>
            meters.ToString("0.00###", BrushLanguage.Culture) + " " + Suffix(axis);

        static string SignedValue(float value, int axis) =>
            AxisLetters[axis] + ": " + value.ToString("+0.000;-0.000;0.000", BrushLanguage.Culture);

        internal static string FormatDisplacement(float meters, int axis) => SignedValue(meters, axis) + Tr(" м");
        internal static string FormatRotation(float degrees, int axis) => SignedValue(degrees, axis) + "°";

        internal static void DrawDisplacement(SceneView view, Vector3 tip, Vector3 pivot, string text)
        {
            var point = HandleUtility.WorldToGUIPointWithDepth(view.camera, tip);
            if (point.z < view.camera.nearClipPlane || point.z > view.camera.farClipPlane) return;
            var label = new GUIContent(text);
            var size = new Vector2(Badge.CalcSize(label).x + 12f, 22f);
            Vector2 outward = (Vector2)point - HandleUtility.WorldToGUIPoint(pivot);
            var rect = BadgeRect(point, Vector2.zero, outward, size, placedLabels);
            placedLabels.Add(rect);
            Handles.BeginGUI();
            try
            {
                EditorGUI.DrawRect(rect, Plate);
                GUI.Label(rect, label, Badge);
            }
            finally { Handles.EndGUI(); }
        }

        // All geometry below is in GUI points, so zoom and display scaling do not change the gap.
        internal static Rect BadgeRect(Vector2 midpoint, Vector2 along, Vector2 outward, Vector2 size, List<Rect> occupied)
        {
            var normal = along.sqrMagnitude > 0.0001f ? new Vector2(-along.y, along.x).normalized :
                outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector2.down;
            if (Vector2.Dot(normal, outward) < 0f) normal = -normal;
            float extent = (Mathf.Abs(normal.x) * size.x + Mathf.Abs(normal.y) * size.y) * 0.5f;
            var rect = new Rect(midpoint + normal * (extent + 6f) - size * 0.5f, size);
            for (int i = 0; i < occupied.Count; i++)
            {
                if (!rect.Overlaps(occupied[i])) continue;
                rect.position += normal * (size.y + 2f);
                i = -1;
            }
            return rect;
        }

        public static void Readout(Edge edge, float meters) => Field(null, edge, "", meters, null);

        public static void Field(string key, Edge edge, string title, float meters, Action<float> apply,
            Action<float> preview = null, bool signed = false)
        {
            var view = SceneView.currentDrawingSceneView;
            if (view == null || view.camera == null || !float.IsFinite(meters)) return;
            var point = HandleUtility.WorldToGUIPointWithDepth(view.camera, edge.Midpoint);
            if (point.z < view.camera.nearClipPlane || point.z > view.camera.farClipPlane) return;
            var e = Event.current;
            bool editing = key != null && owner == view && activeKey == key;
            var label = new GUIContent(FormatDimension(meters, edge.Axis), title);
            float width = Badge.CalcSize(label).x + 12f;
            var direction = edge.Direction.normalized;
            // Project towards the far end to avoid crossing the camera plane on an edge pointing at the viewer.
            if (Vector3.Dot(direction, view.camera.transform.forward) < 0f) direction = -direction;
            var tip = HandleUtility.WorldToGUIPointWithDepth(view.camera, edge.Midpoint + direction);
            var centre = HandleUtility.WorldToGUIPointWithDepth(view.camera, edge.Centre);
            var outward = centre.z > 0f ? (Vector2)(point - centre) : Vector2.down;
            var r = BadgeRect(point, tip - point, outward, new Vector2(editing ? Mathf.Max(108f, width) : width, 22f), placedLabels);
            placedLabels.Add(r);
            bool hover = r.Contains(e.mousePosition);
            if (apply == null && e.type != EventType.Repaint) return;
            if (apply != null && hover && !e.alt && e.type == EventType.Layout)
                HandleUtility.AddControl(GUIUtility.GetControlID(FocusType.Passive), -1f);

            if (editing && e.type == EventType.Repaint && TryMeters(draft, signed, out float candidate))
                preview?.Invoke(candidate);
            Handles.BeginGUI();
            try
            {
                if (editing)
                {
                    if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape ||
                        e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                    {
                        var cancel = e.keyCode == KeyCode.Escape;
                        e.Use();
                        if (!cancel && !TryMeters(draft, signed, out _)) return;
                        var value = draft;
                        GUI.FocusControl(null);
                        EditorGUIUtility.editingTextField = false;
                        Close();
                        if (!cancel && TryMeters(value, signed, out float result)) apply?.Invoke(result);
                        return;
                    }
                    if (e.type == EventType.MouseDown && !hover)
                    {
                        GUI.FocusControl(null);
                        EditorGUIUtility.editingTextField = false;
                        Close();
                        return;
                    }
                    var number = r;
                    number.xMax -= edge.Axis < 0 ? 22f : 41f;
                    GUI.SetNextControlName(key);
                    draft = EditorGUI.TextField(number, draft);
                    var suffix = Rect.MinMaxRect(number.xMax, r.yMin, r.xMax, r.yMax);
                    EditorGUI.DrawRect(suffix, Plate);
                    GUI.Label(suffix, Suffix(edge.Axis), Badge);
                    if (focus)
                    {
                        EditorGUI.FocusTextInControl(key);
                        if (GUI.GetNameOfFocusedControl() == key) focus = false;
                    }
                    if (!TryMeters(draft, signed, out _))
                        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2f, r.width, 2f), Color.red);
                    return;
                }
                if (apply != null)
                {
                    EditorGUIUtility.AddCursorRect(r, MouseCursor.Text);
                    if (hover && !e.alt && e.type == EventType.MouseDown && e.button == 0)
                    {
                        activeKey = key;
                        owner = view;
                        draft = meters.ToString("0.#####", BrushLanguage.Culture);
                        focus = true;
                        e.Use();
                        view.Repaint();
                        return;
                    }
                }
                if (e.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(r, Plate);
                    if (hover && apply != null) EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2f, r.width, 2f), Accent);
                    GUI.Label(r, label, Badge);
                }
            }
            finally { Handles.EndGUI(); }
        }

        internal static bool CanResize(Brush brush, int axis) => brush != null && axis >= 0 && axis < 3 &&
            !brush.IsGenerated && !brush.HasParametricSize && !(brush.IsOpening && axis == 2) &&
            !EditorUtility.IsPersistent(brush) && brush.gameObject.scene.IsValid();

        // Preserve the negative end, except for an opening's width, which stays centred along the wall.
        static bool ResizeCandidate(Brush brush, int axis, float meters, out BrushPolyhedron shape,
            out Vector3 size, out Vector3 translation, out Matrix4x4 deformation)
        {
            shape = null; size = translation = default; deformation = Matrix4x4.identity;
            if (!CanResize(brush, axis) || !float.IsFinite(meters) || meters < 0.001f) return false;
            var original = BrushGeometry.Polyhedron(brush);
            var bounds = original.Bounds();
            var unit = Vector3.zero; unit[axis] = 1f;
            float worldSpan = brush.transform.TransformVector(unit * bounds.size[axis]).magnitude;
            if (worldSpan < 0.000001f) return false;
            float ratio = meters / worldSpan;
            float fixedEnd = bounds.min[axis];
            if (brush.shape == BrushShape.Custom)
            {
                var scale = Vector3.one; scale[axis] = ratio;
                var anchor = unit * fixedEnd;
                deformation = Matrix4x4.Translate(anchor) * Matrix4x4.Scale(scale) * Matrix4x4.Translate(-anchor);
                shape = original.Transformed(deformation);
                size = shape.Bounds().size;
                return shape.IsSound(out _);
            }
            size = brush.ClampedSize;
            size[axis] *= ratio;
            if (size[axis] < 0.001f || !float.IsFinite(size[axis])) return false;
            var parameters = BrushGeometry.ShapeParams.From(brush);
            parameters.size = size;
            parameters.shift = brush.PivotShiftFor(size);
            shape = BrushGeometry.ShapePolyhedron(brush.shape, parameters);
            var next = shape.Bounds();
            translation = brush.transform.TransformVector(unit * (fixedEnd - next.min[axis]));
            if (brush.IsOpening && axis == 0) translation = Vector3.zero;
            return true;
        }

        internal static bool Resize(Brush brush, int axis, float meters)
        {
            if (!ResizeCandidate(brush, axis, meters, out var shape, out var size, out var move, out var deformation)) return false;
            if ((size - brush.size).sqrMagnitude < 1e-12f && move.sqrMagnitude < 1e-12f) return true;
            Undo.RecordObject(brush, Tr("Изменить размер"));
            var children = new List<(Transform child, Vector3 position)>();
            if (move != Vector3.zero)
            {
                Undo.RecordObject(brush.transform, Tr("Изменить размер"));
                foreach (Transform child in brush.transform)
                {
                    if (Brush.IsGeneratedChildName(child.name)) continue;
                    children.Add((child, child.position));
                    Undo.RecordObject(child, Tr("Изменить размер"));
                }
            }
            if (brush.shape == BrushShape.Custom)
            {
                brush.polyhedron = shape;
            }
            brush.size = size;
            brush.exactSizeMeters[axis] = size[axis];
            brush.transform.position += move;
            foreach (var (child, position) in children)
            {
                child.position = position;
                PrefabUtility.RecordPrefabInstancePropertyModifications(child);
            }
            BrushSnap.Accept(brush);
            PrefabUtility.RecordPrefabInstancePropertyModifications(brush);
            PrefabUtility.RecordPrefabInstancePropertyModifications(brush.transform);
            BrushSync.Ensure(brush);
            return true;
        }

        internal static void DrawBrushField(Brush brush, Edge edge, float meters)
        {
            int axis = edge.Axis;
            string label = axis == 0 ? Tr("Ширина") : axis == 1 ? Tr("Высота") : Tr("Глубина");
            Field("csg.size." + brush.GetEntityId() + "." + axis, edge, label + Tr(" в метрах. Enter применяет, Esc отменяет."),
                meters, value => Resize(brush, axis, value), value =>
                {
                    if (ResizeCandidate(brush, axis, value, out var shape, out _, out var move, out _))
                        DrawOutline(shape, Matrix4x4.Translate(move) * brush.transform.localToWorldMatrix);
                });
        }

        static void DrawOutline(BrushPolyhedron poly, Matrix4x4 matrix)
        {
            using (new Handles.DrawingScope(Accent, matrix))
                foreach (var edge in poly.Edges()) Handles.DrawLine(poly.vertices[edge.a], poly.vertices[edge.b], 2f);
        }
    }
}
