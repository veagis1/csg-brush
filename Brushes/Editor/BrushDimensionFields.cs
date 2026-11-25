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
        static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

        static BrushDimensionFields()
        {
            Selection.selectionChanged += Reset;
            Undo.undoRedoPerformed += Reset;
            ToolManager.activeToolChanged += Reset;
            EditorApplication.playModeStateChanged += _ => Reset();
            SceneView.beforeSceneGui += _ => placedLabels.Clear();
        }

        static void Reset()
        {
            Close();
        }

        static void Close()
        {
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
                badge = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
                badge.normal.textColor = Color.white;
                return badge;
            }
        }

        internal static bool TryMeters(string value, bool signed, out float meters)
        {
            return float.TryParse(value?.Trim().Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out meters) && float.IsFinite(meters) && (signed || meters >= 0.001f);
        }

        public static void Readout(Vector3 world, float meters) => Field(null, world, "", meters, null);

        public static void Field(string key, Vector3 world, string title, float meters, Action<float> apply,
            Action<float> preview = null, bool signed = false)
        {
            var view = SceneView.currentDrawingSceneView;
            if (view == null || view.camera == null || !float.IsFinite(meters)) return;
            var point = HandleUtility.WorldToGUIPointWithDepth(view.camera, world);
            if (point.z < view.camera.nearClipPlane || point.z > view.camera.farClipPlane) return;
            var e = Event.current;
            bool editing = key != null && owner == view && activeKey == key;
            var label = new GUIContent(meters.ToString("0.00###", Russian) + " м", title);
            float width = editing ? 94f : Badge.CalcSize(label).x + 12f;
            var r = new Rect(point.x - width / 2f, point.y - 11f, width, 22f);
            // In a front or top view several dimensions can project to the same pixel.
            for (int i = 0; i < placedLabels.Count; i++)
                if (r.Overlaps(placedLabels[i])) { r.y = placedLabels[i].yMax + 2f; i = -1; }
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
                    GUI.SetNextControlName(key);
                    draft = EditorGUI.TextField(r, draft);
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
                        draft = meters.ToString("0.#####", Russian);
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
            Undo.RecordObject(brush, "Изменить размер");
            var children = new List<(Transform child, Vector3 position)>();
            if (move != Vector3.zero)
            {
                Undo.RecordObject(brush.transform, "Изменить размер");
                foreach (Transform child in brush.transform)
                {
                    if (Brush.IsGeneratedChildName(child.name)) continue;
                    children.Add((child, child.position));
                    Undo.RecordObject(child, "Изменить размер");
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

        internal static void DrawBrushField(Brush brush, int axis, Vector3 world, float meters)
        {
            string label = axis == 0 ? "Ширина" : axis == 1 ? "Высота" : "Глубина";
            Field("csg.size." + brush.GetEntityId() + "." + axis, world, label + " в метрах. Enter применяет, Esc отменяет.",
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
