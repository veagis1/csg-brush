using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>Isometric UV charts for rectilinear CSG surfaces. The generated mesh already splits hard edges.</summary>
    internal static class BrushPlanarLightmapUVs
    {
        sealed class Chart
        {
            public int root;
            public Vector2 min, max, size, atlasOrigin;
            public bool rotated;
        }

        internal static bool TryGenerate(Mesh mesh, float hardAngle, float packMargin)
        {
            if (hardAngle <= 0 || hardAngle > 90 || packMargin < 0 || packMargin >= 1) return false;
            for (int s = 0; s < mesh.subMeshCount; s++)
                if (mesh.GetTopology(s) != MeshTopology.Triangles) return false;
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            if (triangles.Length == 0) return false;
            var parent = new int[vertices.Length];
            var direction = new int[vertices.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            int Root(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }

            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                int face = FaceDirection(vertices[a], vertices[b], vertices[c]);
                if (face == 0) return false;
                for (int corner = t; corner < t + 3; corner++)
                {
                    int v = triangles[corner];
                    if (direction[v] != 0 && direction[v] != face) return false;
                    direction[v] = face;
                }
                int root = Root(a);
                parent[Root(b)] = root;
                parent[Root(c)] = root;
            }

            var charts = new List<Chart>();
            var chartOf = new Chart[vertices.Length];
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                if (direction[i] == 0) continue;
                Vector2 point = Project(vertices[i], direction[i]);
                uv[i] = point;
                int root = Root(i);
                if (chartOf[root] == null)
                {
                    chartOf[root] = new Chart { root = root, min = point, max = point };
                    charts.Add(chartOf[root]);
                }
                var chart = chartOf[root];
                chart.min = Vector2.Min(chart.min, point);
                chart.max = Vector2.Max(chart.max, point);
                chartOf[i] = chart;
            }

            float longest = 0;
            foreach (var chart in charts)
            {
                var size = chart.max - chart.min;
                chart.rotated = size.y > size.x;
                chart.size = chart.rotated ? new Vector2(size.y, size.x) : size;
                longest = Mathf.Max(longest, chart.size.x);
            }
            charts.Sort((a, b) =>
            {
                int height = b.size.y.CompareTo(a.size.y);
                if (height != 0) return height;
                int width = b.size.x.CompareTo(a.size.x);
                return width != 0 ? width : a.root.CompareTo(b.root);
            });
            // One scale for all charts preserves relative texel density, including the sides of narrow cuts.
            float low = 0, high = 1f / longest;
            for (int i = 0; i < 18; i++)
            {
                float scale = (low + high) * 0.5f;
                if (PackRows(charts, scale, packMargin)) low = scale;
                else high = scale;
            }
            if (low == 0 || !PackRows(charts, low, packMargin)) return false;
            for (int i = 0; i < uv.Length; i++)
            {
                var chart = chartOf[i];
                if (chart == null) continue;
                var local = uv[i] - chart.min;
                if (chart.rotated) local = new Vector2(local.y, chart.size.y - local.x);
                uv[i] = chart.atlasOrigin + local * low;
            }
            // Atlas offsets can round the edges of a very thin sliver onto the same float.
            for (int t = 0; t < triangles.Length; t += 3)
            {
                var origin = uv[triangles[t]];
                var ab = uv[triangles[t + 1]] - origin;
                var ac = uv[triangles[t + 2]] - origin;
                if (!(ab.x * ac.y > ab.y * ac.x)) return false;
            }
            mesh.uv2 = uv;
            return true;
        }

        // Require exact coplanarity. Slopes, shared hard-edge vertices and degenerate faces use Unity's unwrapper.
        static int FaceDirection(Vector3 a, Vector3 b, Vector3 c)
        {
            int axis;
            if (a.x == b.x && a.x == c.x) axis = 0;
            else if (a.y == b.y && a.y == c.y) axis = 1;
            else if (a.z == b.z && a.z == c.z) axis = 2;
            else return 0;
            float area = Vector3.Cross(b - a, c - a)[axis];
            return area > 0 ? axis + 1 : area < 0 ? -(axis + 1) : 0;
        }

        static Vector2 Project(Vector3 p, int direction)
        {
            int axis = Mathf.Abs(direction);
            Vector2 point = axis == 1 ? new Vector2(p.y, p.z) : axis == 2 ? new Vector2(p.z, p.x) : new Vector2(p.x, p.y);
            if (direction < 0) point.x = -point.x;
            return point;
        }

        static bool PackRows(List<Chart> charts, float scale, float margin)
        {
            float edge = margin * 0.5f, x = edge, y = edge, height = 0;
            foreach (var chart in charts)
            {
                Vector2 size = chart.size * scale;
                if (size.x > 1f - margin) return false;
                if (x + size.x > 1f - edge)
                {
                    x = edge;
                    y += height + margin;
                    height = 0;
                }
                if (y + size.y > 1f - edge) return false;
                chart.atlasOrigin = new Vector2(x, y);
                x += size.x + margin;
                height = Mathf.Max(height, size.y);
            }
            return true;
        }
    }
}
