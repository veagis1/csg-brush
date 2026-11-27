using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CsgBrush.Editor
{
    internal static class BrushSceneLightmapUVs
    {
        readonly struct Corner : System.IEquatable<Corner>
        {
            internal readonly Vector3 Position;
            internal readonly Vector3 Normal;
            internal readonly Vector2 Texture;

            internal Corner(Vector3 position, Vector3 normal, Vector2 texture)
            {
                Position = position;
                Normal = normal;
                Texture = texture;
            }

            public bool Equals(Corner other) => Position.Equals(other.Position) && Normal.Equals(other.Normal) && Texture.Equals(other.Texture);
            public override bool Equals(object other) => other is Corner corner && Equals(corner);
            public override int GetHashCode() => unchecked((Position.GetHashCode() * 397 ^ Normal.GetHashCode()) * 397 ^ Texture.GetHashCode());
        }

        // Match triangles first. Identical positions and shading can have different UVs on either
        // side of an unwrap seam, so a single UV per position would smear the baked light there.
        internal static void Transfer(Mesh original, Mesh preview)
        {
            if (original == null || preview.vertexCount == 0 || !original.HasVertexAttribute(VertexAttribute.TexCoord1)) return;

            var originalCorners = ReadCorners(original);
            var lightmap = original.uv2;
            var triangles = new Dictionary<(Corner, Corner, Corner), (Vector2, Vector2, Vector2)>();
            var cornerUVs = new Dictionary<Corner, Vector2>();
            var seams = new HashSet<Corner>();
            for (int sub = 0; sub < original.subMeshCount; sub++)
            {
                var indices = original.GetTriangles(sub);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int start = FirstCorner(originalCorners, indices, i);
                    int a = indices[i + start], b = indices[i + (start + 1) % 3], c = indices[i + (start + 2) % 3];
                    triangles[(originalCorners[a], originalCorners[b], originalCorners[c])] = (lightmap[a], lightmap[b], lightmap[c]);
                    RememberCorner(a);
                    RememberCorner(b);
                    RememberCorner(c);
                }
            }

            var previewCorners = ReadCorners(preview);
            var vertices = new List<Vector3>(preview.vertices);
            var normals = new List<Vector3>(preview.normals);
            var texture = new List<Vector2>(preview.uv);
            var transferred = new List<Vector2>(new Vector2[preview.vertexCount]);
            var assigned = new HashSet<int>();
            var duplicates = new Dictionary<(int, Vector2), int>();
            var submeshes = new int[preview.subMeshCount][];
            bool matched = false;
            for (int sub = 0; sub < preview.subMeshCount; sub++)
            {
                var indices = submeshes[sub] = preview.GetTriangles(sub);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int start = FirstCorner(previewCorners, indices, i);
                    int a = i + start, b = i + (start + 1) % 3, c = i + (start + 2) % 3;
                    var key = (previewCorners[indices[a]], previewCorners[indices[b]], previewCorners[indices[c]]);
                    if (triangles.TryGetValue(key, out var uv))
                        matched = true;
                    else if (seams.Contains(key.Item1) || seams.Contains(key.Item2) || seams.Contains(key.Item3) ||
                        !cornerUVs.TryGetValue(key.Item1, out uv.Item1) || !cornerUVs.TryGetValue(key.Item2, out uv.Item2) ||
                        !cornerUVs.TryGetValue(key.Item3, out uv.Item3))
                        uv = default;
                    else
                        matched = true;

                    indices[a] = Assign(indices[a], uv.Item1);
                    indices[b] = Assign(indices[b], uv.Item2);
                    indices[c] = Assign(indices[c], uv.Item3);
                }
            }
            if (!matched) return;
            if (vertices.Count != preview.vertexCount)
            {
                if (vertices.Count > 65000) preview.indexFormat = IndexFormat.UInt32;
                preview.SetVertices(vertices);
                preview.SetNormals(normals);
                preview.SetUVs(0, texture);
                preview.subMeshCount = submeshes.Length;
                for (int sub = 0; sub < submeshes.Length; sub++) preview.SetTriangles(submeshes[sub], sub);
            }
            preview.SetUVs(1, transferred);

            void RememberCorner(int index)
            {
                var corner = originalCorners[index];
                var uv = lightmap[index];
                if (cornerUVs.TryGetValue(corner, out var previous) && !previous.Equals(uv)) seams.Add(corner);
                cornerUVs[corner] = uv;
            }

            int Assign(int index, Vector2 uv)
            {
                if (assigned.Add(index))
                {
                    transferred[index] = uv;
                    return index;
                }
                if (transferred[index].Equals(uv)) return index;
                var key = (index, uv);
                if (duplicates.TryGetValue(key, out int duplicate)) return duplicate;
                duplicate = vertices.Count;
                vertices.Add(vertices[index]);
                normals.Add(normals[index]);
                texture.Add(texture[index]);
                transferred.Add(uv);
                duplicates.Add(key, duplicate);
                return duplicate;
            }
        }

        static Corner[] ReadCorners(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var texture = mesh.uv;
            var corners = new Corner[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                corners[i] = new Corner(vertices[i], normals[i], texture[i]);
            return corners;
        }

        static int FirstCorner(Corner[] corners, int[] indices, int start)
        {
            int first = 0;
            for (int i = 1; i < 3; i++)
            {
                var candidate = corners[indices[start + i]].Position;
                var current = corners[indices[start + first]].Position;
                if (candidate.x < current.x || candidate.x == current.x &&
                    (candidate.y < current.y || candidate.y == current.y && candidate.z < current.z)) first = i;
            }
            return first;
        }
    }
}
