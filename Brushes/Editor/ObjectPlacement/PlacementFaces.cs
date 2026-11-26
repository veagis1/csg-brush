using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush.Editor
{
    internal sealed partial class PlacementGeometry
    {
        internal sealed class Face
        {
            const float PointTolerance = 1e-5f;
            readonly Vector3[] vertices;
            readonly int droppedAxis;
            readonly Vector3 planePoint;

            internal readonly Vector2Int[] Edges;
            internal readonly Vector3 Center;
            internal readonly Vector3 Normal;

            internal Face(Vector3[] vertices, Vector2Int[] edges, Vector3 center, Vector3 normal,
                Vector3 planePoint, Vector3? interior = null)
            {
                this.vertices = vertices;
                this.planePoint = planePoint;
                Edges = edges;
                Normal = normal;
                var abs = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
                droppedAxis = abs.x >= abs.y && abs.x >= abs.z ? 0 : abs.y >= abs.z ? 1 : 2;
                center -= normal * Vector3.Dot(center - planePoint, normal);
                if (Contains(center)) Center = center;
                else if (interior.HasValue)
                    Center = interior.Value - normal * Vector3.Dot(interior.Value - planePoint, normal);
                else
                {
                    // A concave polygon can have its centroid in empty space.
                    float closest = float.PositiveInfinity;
                    Center = vertices[edges[0].x];
                    foreach (var edge in edges)
                    {
                        var a = vertices[edge.x];
                        var d = vertices[edge.y] - a;
                        if (d.sqrMagnitude == 0f) continue;
                        var p = a + d * Mathf.Clamp01(Vector3.Dot(center - a, d) / d.sqrMagnitude);
                        float distance = (p - center).sqrMagnitude;
                        if (distance >= closest) continue;
                        closest = distance;
                        Center = p;
                    }
                }
            }

            internal bool Contains(Vector3 point)
            {
                if (Mathf.Abs(Vector3.Dot(point - planePoint, Normal)) > PointTolerance) return false;
                var p = Project(point);
                bool inside = false;
                foreach (var edge in Edges)
                {
                    var a = Project(vertices[edge.x]);
                    var b = Project(vertices[edge.y]);
                    var direction = b - a;
                    float lengthSquared = direction.sqrMagnitude;
                    if (lengthSquared == 0f) continue;
                    float fraction = Mathf.Clamp01(Vector2.Dot(p - a, direction) / lengthSquared);
                    if ((p - a - direction * fraction).sqrMagnitude <= PointTolerance * PointTolerance) return true;
                    if ((a.y > p.y) != (b.y > p.y) &&
                        p.x < a.x + (p.y - a.y) * direction.x / direction.y)
                        inside = !inside;
                }
                return inside;
            }

            internal bool Intersect(Vector3 origin, Vector3 direction, float maxDistance, out float distance,
                out Vector3 point, bool twoSided = false)
            {
                distance = 0f;
                point = default;
                float denominator = Vector3.Dot(direction, Normal);
                if (denominator == 0f || (!twoSided && denominator > 0f)) return false;
                distance = Vector3.Dot(planePoint - origin, Normal) / denominator;
                if (!float.IsFinite(distance) || distance < 0f || distance > maxDistance) return false;
                point = origin + direction * distance;
                point -= Normal * Vector3.Dot(point - planePoint, Normal);
                return Contains(point);
            }

            internal Vector3 GuideEnd(Vector3[] localVertices, Vector2Int edge)
            {
                var end = (localVertices[edge.x] + localVertices[edge.y]) * 0.5f;
                var start2D = Project(Center);
                var direction = Project(end) - start2D;
                float lengthSquared = direction.sqrMagnitude;
                if (lengthSquared == 0f) return Center;

                var crossings = new List<float> { 0f, 1f };
                foreach (var side in Edges)
                {
                    var a = Project(localVertices[side.x]) - start2D;
                    var b = Project(localVertices[side.y]) - start2D;
                    var segment = b - a;
                    float denominator = Cross(direction, segment);
                    if (denominator != 0f)
                    {
                        float along = Cross(a, segment) / denominator;
                        float across = Cross(a, direction) / denominator;
                        if (along > 0f && along < 1f && across >= 0f && across <= 1f) crossings.Add(along);
                    }
                    else if (Mathf.Abs(Cross(a, direction)) <= PointTolerance * Mathf.Sqrt(lengthSquared))
                    {
                        crossings.Add(Mathf.Clamp01(Vector2.Dot(a, direction) / lengthSquared));
                        crossings.Add(Mathf.Clamp01(Vector2.Dot(b, direction) / lengthSquared));
                    }
                }
                crossings.Sort();
                for (int i = 1; i < crossings.Count; i++)
                {
                    float from = crossings[i - 1], to = crossings[i];
                    if (to > from && !Contains(Vector3.Lerp(Center, end, (from + to) * 0.5f)))
                        return Vector3.Lerp(Center, end, from);
                }
                return end;
            }

            Vector2 Project(Vector3 point) => droppedAxis == 0 ? new Vector2(point.y, point.z) :
                droppedAxis == 1 ? new Vector2(point.x, point.z) : new Vector2(point.x, point.y);

            static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        }

        readonly struct Triangle
        {
            internal readonly int A, B, C;
            internal readonly Vector3 Normal;
            internal readonly float Area;

            internal Triangle(int a, int b, int c, Vector3 normal, float area)
            {
                A = a;
                B = b;
                C = c;
                Normal = normal;
                Area = area;
            }

            internal Vector2Int Edge(int side) => side == 0 ? Key(A, B) : side == 1 ? Key(B, C) : Key(C, A);
        }

        static Face[] MeshFaces(Vector3[] vertices, List<Triangle> triangles,
            Dictionary<Vector2Int, List<int>> neighbours, HashSet<Vector2Int> features)
        {
            const float planeTolerance = 1e-5f;
            var groups = new int[triangles.Count];
            var members = new List<int>();
            var boundary = new List<Vector2Int>();
            var faces = new List<Face>();
            for (int i = 0; i < triangles.Count; i++)
            {
                if (groups[i] != 0) continue;
                int group = i + 1;
                members.Clear();
                boundary.Clear();
                var seed = triangles[i];
                var origin = vertices[seed.A];
                float area = 0f, largestArea = 0f;
                Vector3 moment = default, interior = default;
                members.Add(i);
                groups[i] = group;
                for (int j = 0; j < members.Count; j++)
                {
                    int id = members[j];
                    var triangle = triangles[id];
                    var center = ((vertices[triangle.A] - origin) + (vertices[triangle.B] - origin) +
                        (vertices[triangle.C] - origin)) / 3f;
                    area += triangle.Area;
                    moment += center * triangle.Area;
                    if (triangle.Area > largestArea)
                    {
                        largestArea = triangle.Area;
                        interior = origin + center;
                    }

                    for (int side = 0; side < 3; side++)
                    {
                        var edge = triangle.Edge(side);
                        var attached = neighbours[edge];
                        if (attached.Count == 2)
                        {
                            int otherId = attached[0] == id ? attached[1] : attached[0];
                            var other = triangles[otherId];
                            if (Vector3.Dot(seed.Normal, other.Normal) > 0.999999f &&
                                OnPlane(other.A) && OnPlane(other.B) && OnPlane(other.C))
                            {
                                if (groups[otherId] == 0)
                                {
                                    groups[otherId] = group;
                                    members.Add(otherId);
                                }
                                if (groups[otherId] == group) continue;
                            }
                        }
                        boundary.Add(edge);
                        features.Add(edge);
                    }
                }
                if (boundary.Count > 0)
                    faces.Add(new Face(vertices, boundary.ToArray(), origin + moment / area, seed.Normal, origin, interior));

                bool OnPlane(int vertex) => Mathf.Abs(Vector3.Dot(vertices[vertex] - origin, seed.Normal)) <= planeTolerance;
            }
            return faces.ToArray();
        }
    }
}
