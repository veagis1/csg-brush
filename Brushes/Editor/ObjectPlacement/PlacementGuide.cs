using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    // A midpoint has one guide. At a vertex, mouse direction chooses among its incident segments.
    internal sealed class PlacementGuide
    {
        readonly PlacementScene.Feature anchor;
        readonly List<Vector2Int> edges = new List<Vector2Int>();
        readonly Vector2 clickOffset;
        internal Vector3 Origin => anchor.Position;
        internal Vector3 Position { get; private set; }
        internal Vector2Int Edge { get; private set; } = new Vector2Int(-1, -1);

        internal PlacementGuide(PlacementScene.Feature anchor, Camera camera, Vector2 click)
        {
            this.anchor = anchor;
            Position = Origin;
            clickOffset = click - (Vector2)HandleUtility.WorldToGUIPointWithDepth(camera, Origin);
            if (anchor.Midpoint) edges.Add(new Vector2Int(anchor.A, anchor.B));
            else foreach (var edge in anchor.Part.Geometry.Edges)
                if (edge.x == anchor.A || edge.y == anchor.A) edges.Add(edge);
        }

        internal void Update(Camera camera, Vector2 mouse, float step)
        {
            var projected = (Vector2)HandleUtility.WorldToGUIPointWithDepth(camera, Origin);
            bool atOrigin = (mouse - projected).sqrMagnitude <= 4f ||
                (mouse - projected - clickOffset).sqrMagnitude <= 4f;
            Position = Origin;
            float nearest = float.PositiveInfinity;
            var previous = Edge;
            Edge = new Vector2Int(-1, -1);
            foreach (var edge in edges)
            {
                var a = anchor.Part.World(edge.x);
                var b = anchor.Part.World(edge.y);
                if (!Project(camera, mouse, a, b, out float u, out float error)) continue;
                if (error > nearest || (error == nearest && previous != edge)) continue;
                nearest = error;
                Edge = edge;
                float start = anchor.Midpoint ? 0.5f : edge.x == anchor.A ? 0f : 1f;
                Position = atOrigin ? Origin : ObjectPlacement.PointOnEdge(a, b, start, u, step);
            }
        }

        internal void DrawEdges()
        {
            using (new Handles.DrawingScope(new Color(1f, 0.82f, 0.28f, 0.55f)))
                foreach (var edge in edges)
                    if (edge != Edge) Handles.DrawLine(anchor.Part.World(edge.x), anchor.Part.World(edge.y), 2f);
            if (Edge.x >= 0)
                using (new Handles.DrawingScope(Color.yellow))
                    Handles.DrawAAPolyLine(3f, anchor.Part.World(Edge.x), anchor.Part.World(Edge.y));
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
