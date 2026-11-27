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
        internal int[] PreviewTriangles;
        readonly bool[] interiorVertices;
        List<Face>[] vertexFaces;
        Dictionary<Vector2Int, List<Face>> edgeFaces;

        PlacementGeometry(Vector3[] vertices, Vector2Int[] edges, Face[] faces, bool[] interiorVertices = null)
        {
            Vertices = vertices;
            Edges = edges;
            Faces = faces;
            this.interiorVertices = interiorVertices;
        }

        internal bool IsFeatureVertex(int index) => interiorVertices == null || !interiorVertices[index];

        internal IReadOnlyList<Face> IncidentFaces(int a, int b)
        {
            if (vertexFaces == null)
            {
                vertexFaces = new List<Face>[Vertices.Length];
                edgeFaces = new Dictionary<Vector2Int, List<Face>>();
                foreach (var face in Faces)
                    foreach (var edge in face.Edges)
                    {
                        var key = Key(edge.x, edge.y);
                        if (!edgeFaces.TryGetValue(key, out var incident))
                            edgeFaces.Add(key, incident = new List<Face>(2));
                        incident.Add(face);
                        AddVertexFace(edge.x, face);
                        AddVertexFace(edge.y, face);
                    }
            }
            List<Face> result;
            if (b < 0) result = vertexFaces[a];
            else edgeFaces.TryGetValue(Key(a, b), out result);
            return result == null ? (IReadOnlyList<Face>)System.Array.Empty<Face>() : result;

            void AddVertexFace(int vertex, Face face)
            {
                var incident = vertexFaces[vertex] ??= new List<Face>(3);
                if (incident.Count == 0 || incident[incident.Count - 1] != face) incident.Add(face);
            }
        }

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
            var builder = new MeshBuilder();
            var remap = new int[positions.Length];
            for (int i = 0; i < remap.Length; i++)
                remap[i] = builder.AddVertex(positions[i]);
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
                            builder.AddTriangle(At(i), At(i + 1), At(i + 2));
                        break;
                    case MeshTopology.Quads:
                        for (int i = 0; i + 3 < indices.Length; i += 4)
                        {
                            builder.AddTriangle(At(i), At(i + 1), At(i + 2));
                            builder.AddTriangle(At(i), At(i + 2), At(i + 3));
                        }
                        break;
                    case MeshTopology.Lines:
                    case MeshTopology.LineStrip:
                        int stride = desc.topology == MeshTopology.Lines ? 2 : 1;
                        for (int i = 0; i + 1 < indices.Length; i += stride) builder.AddLine(At(i), At(i + 1));
                        break;
                }
            }
            return builder.Build();
        }

        internal sealed class MeshBuilder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly Dictionary<Vector3, int> unique = new Dictionary<Vector3, int>();
            readonly List<Triangle> triangles = new List<Triangle>();
            readonly Dictionary<Vector2Int, List<int>> neighbours = new Dictionary<Vector2Int, List<int>>();
            readonly HashSet<Vector2Int> lines = new HashSet<Vector2Int>();

            internal int AddVertex(Vector3 position)
            {
                if (unique.TryGetValue(position, out int index)) return index;
                index = vertices.Count;
                unique.Add(position, index);
                vertices.Add(position);
                return index;
            }

            internal void AddLine(int a, int b)
            {
                if (a != b) lines.Add(Key(a, b));
            }

            internal void AddTriangle(int a, int b, int c)
            {
                var cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                float area = cross.magnitude;
                if (area <= 1e-12f) return;
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

            internal PlacementGeometry Build(bool simplify = false)
            {
                var positions = vertices.ToArray();
                var features = new HashSet<Vector2Int>(lines);
                var faces = MeshFaces(positions, triangles, neighbours, features, simplify);
                var interior = new bool[positions.Length];
                if (simplify)
                {
                    SimplifyBoundaries(positions, faces, features);
                    features.Clear();
                    foreach (var face in faces) features.UnionWith(face.Edges);
                    // Generated vertices can come from triangles that collapse in float precision.
                    for (int i = 0; i < positions.Length; i++) interior[i] = true;
                }
                else
                {
                    foreach (var triangle in triangles)
                        interior[triangle.A] = interior[triangle.B] = interior[triangle.C] = true;
                }
                var edges = new Vector2Int[features.Count];
                features.CopyTo(edges);
                foreach (var edge in edges)
                    interior[edge.x] = interior[edge.y] = false;
                var result = new PlacementGeometry(positions, edges, faces, interior);
                if (!simplify)
                {
                    result.PreviewTriangles = new int[triangles.Count * 3];
                    for (int i = 0; i < triangles.Count; i++)
                    {
                        result.PreviewTriangles[3 * i] = triangles[i].A;
                        result.PreviewTriangles[3 * i + 1] = triangles[i].B;
                        result.PreviewTriangles[3 * i + 2] = triangles[i].C;
                    }
                }
                return result;
            }
        }

        static Vector2Int Key(int a, int b) => a < b ? new Vector2Int(a, b) : new Vector2Int(b, a);
    }
}
