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
            internal readonly bool Authored;
            internal PlacementGeometry Geometry;
            internal int[] WireIndices;

            internal Part(Component owner, Bounds bounds, Mesh mesh = null, bool authored = false)
            {
                Owner = owner;
                Bounds = bounds;
                Mesh = mesh;
                Authored = authored;
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
        readonly List<(Transform item, BrushSceneVisibility.Access access)> hierarchyAccess =
            new List<(Transform, BrushSceneVisibility.Access)>();
        readonly Dictionary<Brush, Part> targetBrushes = new Dictionary<Brush, Part>();
        readonly Dictionary<Mesh, PlacementGeometry> meshes = new Dictionary<Mesh, PlacementGeometry>();
        readonly Transform source;
        readonly bool hasMovingSolid;

        internal PlacementScene(Transform source)
        {
            BrushSceneVisibility.SyncForPicking();
            this.source = source;
            var stage = StageUtility.GetCurrentStageHandle();
            var occupied = new HashSet<Transform>();
            void Add(Component owner, Bounds bounds, Mesh mesh = null)
            {
                if (owner == null || !occupied.Add(owner.transform) || !ObjectPlacement.IsAuthoredGeometry(owner.transform)) return;
                var access = BrushSceneVisibility.AccessOf(owner.transform);
                hierarchyAccess.Add((owner.transform, access));
                if (access != BrushSceneVisibility.Access.Pickable) return;
                bool carried = source != null && owner.transform.IsChildOf(source);
                var part = new Part(owner, bounds, mesh, authored: carried);
                (carried ? moving : targets).Add(part);
                if (!carried && owner is Brush brush) targetBrushes.Add(brush, part);
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
            hasMovingSolid = moving.Exists(part => part.Owner is Brush brush && brush.operation == BrushOperation.Add);
        }

        // Ray picking only needs the cut-to-wall lookup; no moving object or target catalogue.
        internal PlacementScene() { }

        internal bool HierarchyAccessChanged()
        {
            foreach (var (item, access) in hierarchyAccess)
                if (item == null || BrushSceneVisibility.AccessOf(item) != access) return true;
            return false;
        }

        PlacementGeometry Read(Part part)
        {
            if (part.Geometry != null) return part.Geometry;
            if (part.Owner is Brush brush)
            {
                if (!part.Authored && brush.operation == BrushOperation.Subtract) return null;
                if (!part.Authored && LoadCsgGeometry(brush)) return part.Geometry;
                return part.Geometry = PlacementGeometry.FromBrush(brush);
            }
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

        internal void GetFeatures(Part part, List<Feature> features)
        {
            if (part.Owner is Brush cutter && cutter.operation == BrushOperation.Subtract) return;
            var geometry = Read(part);
            if (geometry == null) return;
            for (int i = 0; i < geometry.Vertices.Length; i++)
                if (geometry.IsFeatureVertex(i)) features.Add(new Feature(part, i, -1));
            foreach (var edge in geometry.Edges)
                features.Add(new Feature(part, edge.x, edge.y));
            foreach (var face in geometry.Faces)
                features.Add(new Feature(part, -1, -1, face));
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
            DrawMovingGeometry(rootMatrix, color, window, previewSize);
            return true;
        }

        void DrawMovingGeometry(Matrix4x4 rootMatrix, Color color, Brush window = null, Vector3 previewSize = default)
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
                DrawPreviewFill(part, data, matrix, color);
                using (new Handles.DrawingScope(color, matrix))
                    Handles.DrawLines(data.Vertices, part.WireIndices);
            }
        }

        static void DrawPreviewFill(Part part, PlacementGeometry data, Matrix4x4 matrix, Color color)
        {
            if (data.PreviewTriangles == null)
            {
                BrushPolyhedron poly = null;
                if (part.Owner is Brush brush) poly = BrushHooks.LocalShape(brush, out _);
                else if (part.Owner is BoxCollider box) poly = BrushPolyhedron.Box(box.size);
                var indices = new List<int>();
                if (poly != null && poly.IsValid)
                    for (int i = 0; i < poly.faces.Length; i++)
                        foreach (var triangle in ConvexDecomposition.Triangulate(poly, i)) indices.AddRange(triangle);
                data.PreviewTriangles = indices.ToArray();
            }
            if (data.PreviewTriangles.Length == 0 || !BrushSurfaceGrid.UseOverlayMaterial()) return;
            GL.PushMatrix();
            try
            {
                GL.MultMatrix(matrix);
                GL.Begin(GL.TRIANGLES);
                GL.Color(new Color(color.r, color.g, color.b, 0.22f));
                foreach (var index in data.PreviewTriangles) GL.Vertex(data.Vertices[index]);
                GL.End();
            }
            finally { GL.PopMatrix(); }
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
