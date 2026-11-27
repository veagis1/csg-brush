using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static CsgBrush.Editor.BrushLanguage;

namespace CsgBrush.Editor
{
    // Each rail starts at the chosen feature. A face centre has one rail per boundary edge.
    internal sealed class PlacementGuide
    {
        readonly PlacementScene.Feature anchor;
        readonly List<Vector3> ends = new List<Vector3>();
        readonly Vector2 clickOffset;
        int selected;
        float distance;
        bool enteredDistance;
        internal Vector3 Origin => anchor.Position;
        Vector3 End(int index) => anchor.Part.Owner.transform.TransformPoint(ends[index]);
        internal Vector3 Position => ends.Count == 0 ? Origin : Vector3.MoveTowards(Origin, End(selected), distance);

        internal PlacementGuide(PlacementScene.Feature anchor, Camera camera, Vector2 click)
        {
            this.anchor = anchor;
            clickOffset = click - (Vector2)HandleUtility.WorldToGUIPointWithDepth(camera, Origin);
            var geometry = anchor.Part.Geometry;
            if (anchor.FaceCenter)
            {
                foreach (var edge in anchor.Face.Edges)
                    ends.Add(anchor.Face.GuideEnd(geometry.Vertices, edge));
            }
            else if (anchor.Midpoint)
            {
                ends.Add(geometry.Vertices[anchor.A]);
                ends.Add(geometry.Vertices[anchor.B]);
            }
            else
            {
                foreach (var edge in anchor.Face?.Edges ?? geometry.Edges)
                {
                    if (edge.x == anchor.A) ends.Add(geometry.Vertices[edge.y]);
                    if (edge.y == anchor.A) ends.Add(geometry.Vertices[edge.x]);
                }
            }
        }

        internal void Update(Camera camera, Vector2 mouse, float step)
        {
            if (enteredDistance) return;
            var origin = Origin;
            var projected = (Vector2)HandleUtility.WorldToGUIPointWithDepth(camera, origin);
            distance = 0f;
            if ((mouse - projected).sqrMagnitude <= 4f || (mouse - projected - clickOffset).sqrMagnitude <= 4f) return;
            float nearest = float.PositiveInfinity;
            int previous = selected;
            for (int i = 0; i < ends.Count; i++)
            {
                var end = End(i);
                if (!Project(camera, mouse, origin, end, out float u, out float error)) continue;
                if (error > nearest || (error == nearest && previous != i)) continue;
                nearest = error;
                selected = i;
                var point = ObjectPlacement.PointOnEdge(origin, end, 0f, u, step);
                distance = Vector3.Distance(origin, point);
            }
        }

        internal void DrawEdges()
        {
            for (int i = 0; i < ends.Count; i++)
                using (new Handles.DrawingScope(i == selected ? Color.yellow : new Color(1f, 0.82f, 0.28f, 0.55f)))
                    Handles.DrawLine(Origin, End(i), i == selected ? 3f : 2f);
        }

        // Process the panel before scene input so editing a distance cannot confirm a scene click.
        internal bool Slider(SceneView view, out bool apply)
        {
            var rect = new Rect(8f, view.position.height - 100f, Mathf.Min(470f, view.position.width - 16f), 30f);
            bool over = rect.Contains(Event.current.mousePosition);
            Handles.BeginGUI();
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            var label = new GUIContent(Tr("Отступ (м)"), Tr("Расстояние от выбранной точки по подсвеченной направляющей. Ноль совмещает точки. Предел — конец направляющей."));
            var field = new Rect(rect.x + 6f, rect.y + 5f, rect.width - 129f, 18f);
            float maximum = ends.Count == 0 ? 0f : Vector3.Distance(Origin, End(selected));
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 76f;
            EditorGUI.BeginChangeCheck();
            float value = EditorGUI.Slider(field, label, distance, 0f, maximum);
            if (EditorGUI.EndChangeCheck())
            {
                distance = Mathf.Clamp(value, 0f, maximum);
                enteredDistance = true;
            }
            EditorGUIUtility.labelWidth = labelWidth;
            var next = new Rect(field.xMax + 4f, field.y, 28f, 18f);
            using (new EditorGUI.DisabledScope(ends.Count < 2))
                if (GUI.Button(next, new GUIContent("↻", Tr("Выбрать следующую направляющую для точного отступа.")), EditorStyles.miniButton))
                {
                    selected = (selected + 1) % ends.Count;
                    distance = Mathf.Min(distance, Vector3.Distance(Origin, End(selected)));
                    enteredDistance = true;
                }
            apply = GUI.Button(new Rect(next.xMax + 3f, field.y, 82f, 18f), Tr("Применить"), EditorStyles.miniButton);
            Handles.EndGUI();
            return over;
        }

        // The closest point in GUI space must be converted through perspective before measuring metres.
        static bool Project(Camera camera, Vector2 mouse, Vector3 a, Vector3 b, out float fraction, out float error)
        {
            fraction = error = 0f;
            float z0 = Vector3.Dot(a - camera.transform.position, camera.transform.forward);
            float z1 = Vector3.Dot(b - camera.transform.position, camera.transform.forward);
            if (Mathf.Max(z0, z1) < camera.nearClipPlane || Mathf.Min(z0, z1) > camera.farClipPlane) return false;
            float start = 0f, end = 1f;
            if (Mathf.Abs(z1 - z0) > 1e-7f)
            {
                float near = (camera.nearClipPlane - z0) / (z1 - z0);
                float far = (camera.farClipPlane - z0) / (z1 - z0);
                start = Mathf.Clamp01(Mathf.Min(near, far));
                end = Mathf.Clamp01(Mathf.Max(near, far));
            }
            var first = HandleUtility.WorldToGUIPointWithDepth(camera, Vector3.Lerp(a, b, start));
            var last = HandleUtility.WorldToGUIPointWithDepth(camera, Vector3.Lerp(a, b, end));
            var line = (Vector2)(last - first);
            if (line.sqrMagnitude < 0.01f) return false;
            float t = Mathf.Clamp01(Vector2.Dot(mouse - (Vector2)first, line) / line.sqrMagnitude);
            error = (mouse - ((Vector2)first + t * line)).sqrMagnitude;
            float u = camera.orthographic ? t : t * first.z / Mathf.Lerp(last.z, first.z, t);
            fraction = Mathf.Lerp(start, end, u);
            return true;
        }
    }
}
