using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using static CsgBrush.Editor.BrushLanguage;

namespace CsgBrush.Editor
{
    internal static class GeneratorWcs
    {
        // Markers and bounds stay in graph space. Moving the pivot never invalidates this geometry.
        internal sealed class Features
        {
            internal readonly List<Vector3> vertices = new List<Vector3>();
            internal readonly List<Vector3> midpoints = new List<Vector3>();
            internal readonly List<Vector3> centers = new List<Vector3>();
            internal Bounds bounds;
            internal int key, pointCount;
        }

        static readonly ConditionalWeakTable<PointGraph, Features> s_Features = new ConditionalWeakTable<PointGraph, Features>();

        internal static Vector3 World(Component component) => component.transform.position;

        internal static Features Geometry(PointGraph graph)
        {
            if (s_Features.TryGetValue(graph, out var result) && result.key == graph.generatedKey && result.pointCount == graph.points.Count)
                return result;
            graph.EnsureGraph();
            result = new Features { key = graph.generatedKey, pointCount = graph.points.Count };
            var vertices = new HashSet<Vector3>();
            var midpoints = new HashSet<Vector3>();
            var centers = new HashSet<Vector3>();
            void Vertex(Vector3 point)
            {
                if (!vertices.Add(point)) return;
                if (result.vertices.Count == 0) result.bounds = new Bounds(point, Vector3.zero);
                else result.bounds.Encapsulate(point);
                result.vertices.Add(point);
            }
            void Midpoint(Vector3 point)
            {
                if (midpoints.Add(point)) result.midpoints.Add(point);
            }
            for (int i = 0; i < graph.points.Count; i++) Vertex(graph.Point(i));
            for (int i = 0; i < graph.links.Count; i++)
            {
                var path = graph.Curve(i);
                if (path.Length < 2) continue;
                PointGraph.PathPoint(path, PointGraph.PathLength(path) * 0.5f, out var point, out _);
                Midpoint(point);
            }
            foreach (var brush in graph.generated)
            {
                var poly = brush != null ? brush.polyhedron : null;
                if (poly == null || !poly.IsValid) continue;
                var geometry = PlacementGeometry.FromPolyhedron(poly);
                foreach (var point in geometry.Vertices) Vertex(point);
                foreach (var edge in geometry.Edges)
                    Midpoint((geometry.Vertices[edge.x] + geometry.Vertices[edge.y]) * 0.5f);
                foreach (var face in geometry.Faces)
                    if (centers.Add(face.Center)) result.centers.Add(face.Center);
            }
            s_Features.Remove(graph);
            s_Features.Add(graph, result);
            return result;
        }

        // A pivot edit changes local frames, not the path, sill heights, mesh, or children's world poses.
        internal static bool SetOrigin(PointGraph graph, Vector3 graphPoint)
        {
            var move = graph.graphOrigin + graphPoint;
            if (!float.IsFinite(move.sqrMagnitude) || move.sqrMagnitude < 1e-12f) return false;
            var root = graph.transform;
            var records = new Object[2 + root.childCount];
            records[0] = graph;
            records[1] = root;
            for (int i = 0; i < root.childCount; i++) records[i + 2] = root.GetChild(i);
            Undo.RegisterCompleteObjectUndo(records, Tr("Задать начало WCS"));
            root.position = root.TransformPoint(move);
            graph.graphOrigin = -graphPoint;
            foreach (Transform child in root) child.localPosition -= move;
            foreach (var item in records) PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            BrushGenerators.MarkDirty(graph);
            SceneView.RepaintAll();
            return true;
        }

        internal static void DrawInspector(PointGraph graph)
        {
            using var disabled = new EditorGUI.DisabledScope(EditorUtility.IsPersistent(graph) || EditorApplication.isPlayingOrWillChangePlaymode);
            var geometry = Geometry(graph);
            using var empty = new EditorGUI.DisabledScope(geometry.vertices.Count == 0);
            var bounds = geometry.bounds;
            var mode = BrushEditor.EnumPopup(new GUIContent(Tr("Режим WCS")), graph.wcsMode);
            if (mode != graph.wcsMode)
            {
                Undo.RecordObject(graph, Tr("Задать режим WCS"));
                graph.wcsMode = mode;
                PrefabUtility.RecordPrefabInstancePropertyModifications(graph);
            }
            bool relative = mode == PivotMode.Normalized;
            var distance = -graph.graphOrigin - bounds.min;
            var size = bounds.size;
            var value = relative ? new Vector3(Fraction(distance.x, size.x), Fraction(distance.y, size.y), Fraction(distance.z, size.z)) : distance;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent(relative ? Tr("Начало WCS") : Tr("Начало WCS (м)"),
                    Tr("Начало WCS от минимального угла габарита. В режиме метров 0 соответствует этому углу, в долях 1 соответствует противоположной границе.")));
                var width = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 14f;
                EditorGUI.BeginChangeCheck();
                var picked = new Vector3(EditorGUILayout.DelayedFloatField("X", value.x), EditorGUILayout.DelayedFloatField("Y", value.y),
                    EditorGUILayout.DelayedFloatField("Z", value.z));
                EditorGUIUtility.labelWidth = width;
                if (EditorGUI.EndChangeCheck())
                {
                    // A flat axis has no fraction. Editing another axis must preserve its current offset.
                    for (int axis = 0; axis < 3; axis++)
                        if (picked[axis] != value[axis]) distance[axis] = relative ? picked[axis] * size[axis] : picked[axis];
                    SetOrigin(graph, bounds.min + distance);
                }
            }
            BrushPivotTool.DrawButtons(graph);
        }

        static float Fraction(float value, float size) => size > 1e-6f ? value / size : 0.5f;
    }
}
