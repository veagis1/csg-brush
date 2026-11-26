using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Editor
{
    // One catalogue per gesture. Read a mesh's indices only when the pointer reaches its bounds.
    internal sealed partial class PlacementScene
    {
        internal sealed class Part
        {
            internal readonly Component Owner;
            internal readonly Bounds Bounds;
            internal readonly Mesh Mesh;
            internal PlacementGeometry Geometry;
            internal int[] WireIndices;

            internal Part(Component owner, Bounds bounds, Mesh mesh = null)
            {
                Owner = owner;
                Bounds = bounds;
                Mesh = mesh;
            }

            internal Vector3 World(int vertex) => Owner.transform.TransformPoint(Geometry.Vertices[vertex]);
            internal Vector3 World(Vector3 local) => Owner.transform.TransformPoint(local);
        }

        internal readonly struct Feature
        {
            internal readonly Part Part;
            internal readonly int A, B;
            internal readonly PlacementGeometry.Face Face;
            internal Feature(Part part, int a, int b, PlacementGeometry.Face face = null)
            {
                Part = part;
                A = a;
                B = b;
                Face = face;
            }

            internal bool FaceCenter => Face != null && A < 0;
            internal bool Midpoint => B >= 0;
            internal Vector3 Position => FaceCenter ? Part.World(Face.Center) :
                Midpoint ? (Part.World(A) + Part.World(B)) * 0.5f : Part.World(A);
        }

        const float MagnetRadius = 14f; // Scene GUI points
        readonly List<Part> targets = new List<Part>();
        readonly List<Part> moving = new List<Part>();
        readonly Dictionary<Mesh, PlacementGeometry> meshes = new Dictionary<Mesh, PlacementGeometry>();
        readonly Transform source;

        internal PlacementScene(Transform source)
        {
            this.source = source;
            var stage = StageUtility.GetCurrentStageHandle();
            var occupied = new HashSet<Transform>();
            void Add(Component owner, Bounds bounds, Mesh mesh = null)
            {
                if (owner == null || occupied.Contains(owner.transform)) return;
                bool carried = source != null && owner.transform.IsChildOf(source);
                if (!ObjectPlacement.IsSelectableGeometry(owner.transform)) return;
                occupied.Add(owner.transform);
                (carried ? moving : targets).Add(new Part(owner, bounds, mesh));
            }
            foreach (var brush in stage.FindComponentsOfType<Brush>())
                if (brush.isActiveAndEnabled && BrushHooks.LocalShape(brush, out var bounds) != null) Add(brush, bounds);
            foreach (var filter in stage.FindComponentsOfType<MeshFilter>())
                if (filter.sharedMesh != null && !(filter.TryGetComponent<Renderer>(out var renderer) && !renderer.enabled))
                    Add(filter, filter.sharedMesh.bounds, filter.sharedMesh);
            foreach (var skin in stage.FindComponentsOfType<SkinnedMeshRenderer>())
                if (skin.enabled && skin.sharedMesh != null) Add(skin, skin.localBounds);
            foreach (var collider in stage.FindComponentsOfType<MeshCollider>())
                if (collider.enabled && collider.sharedMesh != null) Add(collider, collider.sharedMesh.bounds, collider.sharedMesh);
            foreach (var box in stage.FindComponentsOfType<BoxCollider>())
                if (box.enabled) Add(box, new Bounds(box.center, box.size));
        }

        PlacementGeometry Read(Part part)
        {
            if (part.Geometry != null) return part.Geometry;
            if (part.Owner is Brush brush) return part.Geometry = PlacementGeometry.FromBrush(brush);
            if (part.Mesh != null)
            {
                if (!meshes.TryGetValue(part.Mesh, out var data))
                    meshes.Add(part.Mesh, data = PlacementGeometry.FromMesh(part.Mesh));
                return part.Geometry = data;
            }
            if (part.Owner is SkinnedMeshRenderer skin)
            {
                var baked = new Mesh();
                try
                {
                    skin.BakeMesh(baked);
                    return part.Geometry = PlacementGeometry.FromMesh(baked);
                }
                finally { Object.DestroyImmediate(baked); }
            }
            if (part.Owner is BoxCollider box)
                return part.Geometry = PlacementGeometry.FromPolyhedron(BrushPolyhedron.Box(box.size)
                    .Transformed(Matrix4x4.Translate(box.center)));
            return null;
        }

        internal Feature Pick(Camera camera, Vector2 mouse)
        {
            Feature best = default;
            float nearest = MagnetRadius * MagnetRadius, depth = float.PositiveInfinity;
            foreach (var part in targets)
            {
                if (part.Owner == null || !MayContainPointer(camera, part, mouse) || !Available(camera, part)) continue;
                var geometry = Read(part);
                if (geometry == null) continue;
                var matrix = part.Owner.transform.localToWorldMatrix;
                void Consider(Vector3 local, int a, int b, PlacementGeometry.Face face = null)
                {
                    var world = matrix.MultiplyPoint3x4(local);
                    var p = HandleUtility.WorldToGUIPointWithDepth(camera, world);
                    if (p.z < camera.nearClipPlane || p.z > camera.farClipPlane) return;
                    float distance = ((Vector2)p - mouse).sqrMagnitude;
                    if (distance > nearest || (Mathf.Approximately(distance, nearest) && p.z >= depth)) return;
                    if (face != null && !IsVisible(camera, part, face, world)) return;
                    best = new Feature(part, a, b, face);
                    nearest = distance;
                    depth = p.z;
                }
                var vertices = geometry.Vertices;
                for (int i = 0; i < vertices.Length; i++)
                    if (geometry.IsFeatureVertex(i)) Consider(vertices[i], i, -1);
                foreach (var edge in geometry.Edges)
                    Consider((vertices[edge.x] + vertices[edge.y]) * 0.5f, edge.x, edge.y);
                foreach (var face in geometry.Faces)
                    Consider(face.Center, -1, -1, face);
            }
            return best;
        }

        internal bool Available(Camera camera, Part part) =>
            VisiblePart(part, camera.cullingMask & Tools.visibleLayers) && ObjectPlacement.CanTarget(source, part.Owner.transform);

        static bool VisiblePart(Part part, int layers)
        {
            if (part.Owner == null || (layers & (1 << part.Owner.gameObject.layer)) == 0 ||
                !ObjectPlacement.IsSelectableGeometry(part.Owner.transform))
                return false;
            if (part.Owner is Brush brush && (!brush.isActiveAndEnabled ||
                (brush.operation == BrushOperation.Subtract && !brush.IsOpening && !BrushSettings.instance.showCuts))) return false;
            return true;
        }

        static bool MayContainPointer(Camera camera, Part part, Vector2 mouse)
        {
            var min = Vector2.positiveInfinity;
            var max = Vector2.negativeInfinity;
            var bounds = part.Bounds;
            var matrix = part.Owner.transform.localToWorldMatrix;
            bool behind = false, inFront = false;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                var point = HandleUtility.WorldToGUIPointWithDepth(camera, matrix.MultiplyPoint3x4(corner));
                if (point.z < camera.nearClipPlane) { behind = true; continue; }
                inFront = true;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            if (!inFront) return false;
            if (behind) return true; // The projection changes sign across the near plane.
            return mouse.x >= min.x - MagnetRadius && mouse.x <= max.x + MagnetRadius &&
                mouse.y >= min.y - MagnetRadius && mouse.y <= max.y + MagnetRadius;
        }

        internal bool DrawPreview(Component candidate, Vector3 destination, Color color, out Vector3 workingOrigin)
        {
            var rootMatrix = Matrix4x4.Translate(destination - source.position) * source.localToWorldMatrix;
            workingOrigin = destination;
            Brush window = null;
            Vector3 previewSize = default;
            if (ObjectPlacement.IsWallWindow(source))
            {
                if (!ObjectPlacement.TryWindowPlacement(source, candidate, destination, out var placement)) return false;
                var plan = placement.plan.transform;
                var inverseRotation = Quaternion.Inverse(plan.rotation);
                rootMatrix = plan.localToWorldMatrix * Matrix4x4.TRS(plan.InverseTransformPoint(placement.position),
                    inverseRotation * placement.rotation, placement.localScale);
                workingOrigin = placement.position;
                window = source.GetComponent<Brush>();
                previewSize = placement.size;
            }
            DrawMovingWire(rootMatrix, color, window, previewSize);
            return true;
        }

        void DrawMovingWire(Matrix4x4 rootMatrix, Color color, Brush window = null, Vector3 previewSize = default)
        {
            foreach (var part in moving)
            {
                if (part.Owner == null || !part.Owner.transform.IsChildOf(source)) continue;
                var data = Read(part);
                if (data == null) continue;
                if (part.WireIndices == null)
                {
                    part.WireIndices = new int[data.Edges.Length * 2];
                    for (int i = 0; i < data.Edges.Length; i++)
                    {
                        part.WireIndices[i * 2] = data.Edges[i].x;
                        part.WireIndices[i * 2 + 1] = data.Edges[i].y;
                    }
                }
                var matrix = rootMatrix * PartToSource(part.Owner.transform);
                if (window != null && part.Owner == window)
                {
                    var pivotShift = window.PivotShift;
                    float depthScale = previewSize.z / window.ClampedSize.z;
                    matrix *= Matrix4x4.Translate(pivotShift) * Matrix4x4.Scale(new Vector3(1f, 1f, depthScale)) *
                        Matrix4x4.Translate(-pivotShift);
                }
                using (new Handles.DrawingScope(color, matrix))
                    Handles.DrawLines(data.Vertices, part.WireIndices);
            }
        }

        Matrix4x4 PartToSource(Transform part)
        {
            var matrix = Matrix4x4.identity;
            for (var item = part; item != source; item = item.parent)
                matrix = Matrix4x4.TRS(item.localPosition, item.localRotation, item.localScale) * matrix;
            return matrix;
        }
    }
}
