using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    internal sealed partial class PlacementGeometry
    {
        internal readonly Vector3[] Vertices;
        internal readonly Vector2Int[] Edges;
        internal readonly Face[] Faces;
        readonly bool[] interiorVertices;

        PlacementGeometry(Vector3[] vertices, Vector2Int[] edges, Face[] faces, bool[] interiorVertices = null)
        {
            Vertices = vertices;
            Edges = edges;
            Faces = faces;
            this.interiorVertices = interiorVertices;
        }

        internal bool IsFeatureVertex(int index) => interiorVertices == null || !interiorVertices[index];

        internal static PlacementGeometry FromBrush(Brush brush) => FromPolyhedron(BrushHooks.LocalShape(brush, out _));

        internal static PlacementGeometry FromPolyhedron(BrushPolyhedron poly)
        {
            if (poly == null || !poly.IsValid) return null;
            var edges = new List<Vector2Int>();
            foreach (var (a, b) in poly.Edges()) edges.Add(new Vector2Int(a, b));
            var faces = new List<Face>();
            foreach (var face in poly.faces)
            {
                var indices = face.indices;
                if (indices == null || indices.Length < 3) continue;
                var origin = poly.vertices[indices[0]];
                Vector3 areaNormal = default;
                for (int i = 1; i + 1 < indices.Length; i++)
                    areaNormal += Vector3.Cross(poly.vertices[indices[i]] - origin, poly.vertices[indices[i + 1]] - origin);
                float area = areaNormal.magnitude;
                if (area <= 1e-12f) continue;
                var normal = areaNormal / area;
                Vector3 moment = default;
                var boundary = new Vector2Int[indices.Length];
                for (int i = 0; i < indices.Length; i++)
                {
                    int a = indices[i], b = indices[(i + 1) % indices.Length];
                    boundary[i] = new Vector2Int(a, b);
                    var u = poly.vertices[a] - origin;
                    var v = poly.vertices[b] - origin;
                    moment += (u + v) * Vector3.Dot(Vector3.Cross(u, v), normal);
                }
                faces.Add(new Face(poly.vertices, boundary, origin + moment / (3f * area), normal, origin));
            }
            return new PlacementGeometry(poly.vertices, edges.ToArray(), faces.ToArray());
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

            var triangles = new List<Triangle>();
            var neighbours = new Dictionary<Vector2Int, List<int>>();
            var features = new HashSet<Vector2Int>();
            var interiorVertices = new bool[vertices.Count];
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
                            AddTriangle(At(i), At(i + 1), At(i + 2));
                        break;
                    case MeshTopology.Quads:
                        for (int i = 0; i + 3 < indices.Length; i += 4)
                        {
                            AddTriangle(At(i), At(i + 1), At(i + 2));
                            AddTriangle(At(i), At(i + 2), At(i + 3));
                        }
                        break;
                    case MeshTopology.Lines:
                    case MeshTopology.LineStrip:
                        int stride = desc.topology == MeshTopology.Lines ? 2 : 1;
                        for (int i = 0; i + 1 < indices.Length; i += stride) features.Add(Key(At(i), At(i + 1)));
                        break;
                }
            }
            var positionsArray = vertices.ToArray();
            var faces = MeshFaces(positionsArray, triangles, neighbours, features);
            features.RemoveWhere(edge => edge.x == edge.y);
            var edges = new Vector2Int[features.Count];
            features.CopyTo(edges);
            foreach (var edge in edges)
                interiorVertices[edge.x] = interiorVertices[edge.y] = false;
            return new PlacementGeometry(positionsArray, edges, faces, interiorVertices);

            void AddTriangle(int a, int b, int c)
            {
                var cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                float area = cross.magnitude;
                if (area <= 1e-12f) return;
                interiorVertices[a] = interiorVertices[b] = interiorVertices[c] = true;
                int id = triangles.Count;
                triangles.Add(new Triangle(a, b, c, cross / area, area));
                AddTriangleEdge(a, b, id);
                AddTriangleEdge(b, c, id);
                AddTriangleEdge(c, a, id);
            }

            void AddTriangleEdge(int a, int b, int id)
            {
                var key = Key(a, b);
                if (!neighbours.TryGetValue(key, out var list))
                    neighbours.Add(key, list = new List<int>(2));
                list.Add(id);
            }
        }

        static Vector2Int Key(int a, int b) => a < b ? new Vector2Int(a, b) : new Vector2Int(b, a);
    }
}
