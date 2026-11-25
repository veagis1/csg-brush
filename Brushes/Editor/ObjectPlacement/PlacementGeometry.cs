using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    internal sealed class PlacementGeometry
    {
        internal readonly Vector3[] Vertices;
        internal readonly Vector2Int[] Edges;

        PlacementGeometry(Vector3[] vertices, Vector2Int[] edges)
        {
            Vertices = vertices;
            Edges = edges;
        }

        internal static PlacementGeometry FromBrush(Brush brush) => FromPolyhedron(BrushHooks.LocalShape(brush, out _));

        internal static PlacementGeometry FromPolyhedron(BrushPolyhedron poly)
        {
            if (poly == null || !poly.IsValid) return null;
            var edges = new List<Vector2Int>();
            foreach (var (a, b) in poly.Edges()) edges.Add(new Vector2Int(a, b));
            return new PlacementGeometry(poly.vertices, edges.ToArray());
        }

        // Import seams duplicate positions. Weld these before comparing adjacent planes so a flat
        // triangulated face has its outline, without a selectable midpoint on its internal diagonal.
        internal static PlacementGeometry FromMesh(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0) return null;
            using var snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh);
            var data = snapshot[0];
            using var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
            data.GetVertices(positions);
            var vertices = new List<Vector3>();
            var unique = new Dictionary<Vector3, int>();
            var remap = new int[positions.Length];
            for (int i = 0; i < remap.Length; i++)
            {
                var p = positions[i];
                if (!unique.TryGetValue(p, out int id))
                {
                    id = vertices.Count;
                    unique.Add(p, id);
                    vertices.Add(p);
                }
                remap[i] = id;
            }

            var planes = new Dictionary<Vector2Int, Vector3>();
            var features = new HashSet<Vector2Int>();
            for (int sub = 0; sub < data.subMeshCount; sub++)
            {
                var desc = data.GetSubMesh(sub);
                using var indices = new NativeArray<int>(desc.indexCount, Allocator.Temp);
                data.GetIndices(indices, sub);
                int At(int i) => remap[indices[i]];
                switch (desc.topology)
                {
                    case MeshTopology.Triangles:
                        for (int i = 0; i + 2 < indices.Length; i += 3)
                        {
                            int a = At(i), b = At(i + 1), c = At(i + 2);
                            var normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                            if (normal.sqrMagnitude <= 1e-24f) continue;
                            normal /= normal.magnitude;
                            AddTriangleEdge(a, b, normal);
                            AddTriangleEdge(b, c, normal);
                            AddTriangleEdge(c, a, normal);
                        }
                        break;
                    case MeshTopology.Quads:
                        for (int i = 0; i + 3 < indices.Length; i += 4)
                            for (int j = 0; j < 4; j++) features.Add(Key(At(i + j), At(i + (j + 1) % 4)));
                        break;
                    case MeshTopology.Lines:
                    case MeshTopology.LineStrip:
                        int stride = desc.topology == MeshTopology.Lines ? 2 : 1;
                        for (int i = 0; i + 1 < indices.Length; i += stride) features.Add(Key(At(i), At(i + 1)));
                        break;
                }
            }
            var edges = new List<Vector2Int>(features);
            edges.RemoveAll(edge => edge.x == edge.y);
            return new PlacementGeometry(vertices.ToArray(), edges.ToArray());

            void AddTriangleEdge(int a, int b, Vector3 normal)
            {
                var key = Key(a, b);
                if (!planes.TryGetValue(key, out var first))
                {
                    planes.Add(key, normal);
                    features.Add(key);
                }
                else if (first != Vector3.zero)
                {
                    if (Mathf.Abs(Vector3.Dot(first, normal)) > 0.999999f) features.Remove(key);
                    else features.Add(key);
                    // A crease is permanent even if a third triangle shares the edge.
                    if (features.Contains(key)) planes[key] = Vector3.zero;
                }
            }
        }

        static Vector2Int Key(int a, int b) => a < b ? new Vector2Int(a, b) : new Vector2Int(b, a);
    }
}
