using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    internal sealed partial class PlacementScene
    {
        readonly Dictionary<BrushGroup, List<Transform>> csgMeshes = new Dictionary<BrushGroup, List<Transform>>();
        readonly Dictionary<(BrushGroup model, int layer), bool> loadedCsgLayers =
            new Dictionary<(BrushGroup model, int layer), bool>();
        Dictionary<BrushGroup, List<Brush>> csgOrder;
        readonly Dictionary<Brush, Part> authoredParts = new Dictionary<Brush, Part>();
        readonly Dictionary<Brush, List<Part>> cutsByTarget = new Dictionary<Brush, List<Part>>();
        readonly Dictionary<Brush, List<Part>> wallsByCut = new Dictionary<Brush, List<Part>>();

        internal readonly struct SurfaceHit
        {
            internal readonly Part Part;
            internal readonly PlacementGeometry.Face Face;
            internal readonly Vector3 Position;
            internal readonly Vector3 Normal;
            internal readonly Feature Feature;

            internal SurfaceHit(Part part, PlacementGeometry.Face face, Vector3 position, Vector3 normal, Feature feature = default)
            {
                Part = part;
                Face = face;
                Position = position;
                Normal = normal;
                Feature = feature;
            }
        }

        internal bool TryHoverSurface(Camera camera, Vector2 mouse, out SurfaceHit hit)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var forward = camera.transform.forward;
            float directionDepth = Vector3.Dot(forward, ray.direction);
            float originDepth = Vector3.Dot(forward, ray.origin - camera.transform.position);
            hit = default;
            if (directionDepth <= 0f) return false;
            float nearDistance = Mathf.Max(0f, (camera.nearClipPlane - originDepth) / directionDepth);
            var nearRay = new Ray(ray.GetPoint(nearDistance), ray.direction);
            float maxDistance = (camera.farClipPlane - originDepth) / directionDepth - nearDistance;
            return TryRaycast(nearRay, camera.cullingMask & Tools.visibleLayers, maxDistance, out hit);
        }

        readonly List<Feature> surfaceFeatures = new List<Feature>();

        internal bool TryPickSurface(Camera camera, Vector2 mouse, out SurfaceHit hit, bool snap, bool requireFace)
        {
            bool hasSurface = TryHoverSurface(camera, mouse, out var surface);
            hit = surface;
            if (!snap) return hasSurface;
            var feature = Pick(camera, mouse, surface, requireFace);
            if (feature.Part == null) return hasSurface;
            var normal = feature.Face != null ? WorldNormal(feature.Part, feature.Face) : Vector3.zero;
            hit = new SurfaceHit(feature.Part, feature.Face, feature.Position, normal, feature);
            return true;
        }

        internal Feature Pick(Camera camera, Vector2 mouse, SurfaceHit surface = default, bool requireFace = false)
        {
            Feature feature = default;
            float nearest = MagnetRadius * MagnetRadius, depth = float.PositiveInfinity;
            var preferred = surface.Part;
            if (preferred != null) ConsiderPart(preferred);
            foreach (var part in targets)
                if (part != preferred && Available(camera, part) && MayContainPointer(camera, part, mouse))
                    ConsiderPart(part);
            return feature;

            void ConsiderPart(Part part)
            {
                surfaceFeatures.Clear();
                GetFeatures(part, surfaceFeatures);
                foreach (var candidate in surfaceFeatures)
                {
                    var projected = HandleUtility.WorldToGUIPointWithDepth(camera, candidate.Position);
                    float distance = ((Vector2)projected - mouse).sqrMagnitude;
                    if (distance > nearest || (Mathf.Approximately(distance, nearest) && projected.z >= depth))
                        continue;
                    var face = candidate.Face;
                    if (requireFace)
                    {
                        face = VisibleFace(camera, candidate, part == preferred ? surface.Face : null);
                        if (face == null) continue;
                    }
                    else if (!IsVisible(camera, candidate)) continue;
                    nearest = distance;
                    depth = projected.z;
                    feature = new Feature(part, candidate.A, candidate.B, face);
                }
            }
        }

        internal bool TryRaycast(Ray ray, out SurfaceHit hit, int visibleLayers = ~0) =>
            TryRaycast(ray, visibleLayers, float.PositiveInfinity, out hit);

        bool TryRaycast(Ray ray, int layers, float maxDistance, out SurfaceHit hit)
        {
            hit = FindSurface(ray, layers, maxDistance);
            if (hit.Part != null && ObjectPlacement.CanTarget(source, hit.Part.Owner.transform)) return true;
            hit = default;
            return false;
        }

        internal bool IsVisible(Camera camera, Part part, PlacementGeometry.Face face, Vector3 point)
        {
            int layers = camera.cullingMask & Tools.visibleLayers;
            if (!VisiblePart(part, layers) || !RayToPoint(camera, point, out var ray, out float distance)) return false;
            var toLocal = part.Owner.transform.worldToLocalMatrix;
            return (face == null || Vector3.Dot(toLocal.MultiplyVector(ray.direction), face.Normal) < 0f) &&
                (face == null || AuthoredSurfaceAvailable(part, point, WorldNormal(part, face))) &&
                FindSurface(ray, layers, distance).Part == null;
        }

        internal bool IsVisible(Camera camera, Feature feature)
        {
            if (feature.Part == null || feature.Part.Owner is Brush cutter && cutter.operation == BrushOperation.Subtract)
                return false;
            if (feature.Face != null || feature.Part.Geometry.IncidentFaces(feature.A, feature.B).Count == 0)
                return IsVisible(camera, feature.Part, feature.Face, feature.Position);
            return VisibleFace(camera, feature) != null;
        }

        PlacementGeometry.Face VisibleFace(Camera camera, Feature feature, PlacementGeometry.Face preferred = null)
        {
            var part = feature.Part;
            int layers = camera.cullingMask & Tools.visibleLayers;
            if (!VisiblePart(part, layers) || !RayToPoint(camera, feature.Position, out var ray, out float distance))
                return null;
            var localDirection = part.Owner.transform.worldToLocalMatrix.MultiplyVector(ray.direction);
            PlacementGeometry.Face face = feature.Face;
            if (face != null)
            {
                if (Vector3.Dot(localDirection, face.Normal) >= 0f) return null;
            }
            else
            {
                float strongest = 0f;
                var incident = part.Geometry.IncidentFaces(feature.A, feature.B);
                for (int i = 0; i < incident.Count; i++)
                {
                    var candidate = incident[i];
                    float facing = -Vector3.Dot(ray.direction, WorldNormal(part, candidate));
                    if (facing <= 0f) continue;
                    if (candidate == preferred)
                    {
                        face = candidate;
                        break;
                    }
                    if (facing <= strongest) continue;
                    strongest = facing;
                    face = candidate;
                }
            }
            return face != null && AuthoredSurfaceAvailable(part, feature.Position, WorldNormal(part, face)) &&
                FindSurface(ray, layers, distance).Part == null ? face : null;
        }

        static Vector3 WorldNormal(Part part, PlacementGeometry.Face face)
        {
            var normal = part.Owner.transform.worldToLocalMatrix.transpose.MultiplyVector(face.Normal);
            float length = normal.magnitude;
            return length > 0f ? normal / length : Vector3.zero;
        }

        static bool RayToPoint(Camera camera, Vector3 point, out Ray ray, out float maxDistance)
        {
            var forward = camera.transform.forward;
            var eye = camera.transform.position;
            var offset = point - eye;
            float depth = Vector3.Dot(forward, offset);
            ray = default;
            maxDistance = 0f;
            if (depth < camera.nearClipPlane || depth > camera.farClipPlane) return false;
            var near = camera.orthographic ? point - forward * (depth - camera.nearClipPlane) :
                eye + offset * (camera.nearClipPlane / depth);
            ray = new Ray(near, camera.orthographic ? forward : offset.normalized);
            float surfaceTolerance = Mathf.Max(1e-4f, point.magnitude * 2e-7f);
            maxDistance = (point - near).magnitude - surfaceTolerance;
            return true;
        }

        SurfaceHit FindSurface(Ray ray, int layers, float nearest)
        {
            SurfaceHit hit = default;
            if (nearest < 0f) return hit;
            foreach (var part in targets)
            {
                if (part.Owner == null) continue;
                var toLocal = part.Owner.transform.worldToLocalMatrix;
                var origin = toLocal.MultiplyPoint3x4(ray.origin);
                var direction = toLocal.MultiplyVector(ray.direction);
                float localScale = direction.magnitude;
                if (localScale == 0f ||
                    !part.Bounds.IntersectRay(new Ray(origin, direction / localScale), out float enter) ||
                    enter > nearest * localScale ||
                    !VisiblePart(part, layers)) continue;
                Intersect(part);
                if (hasMovingSolid && part.Owner is Brush brush && brush.operation == BrushOperation.Add &&
                    LoadCsgGeometry(brush) && AuthoredPart(brush) is Part fallback)
                    Intersect(fallback);

                void Intersect(Part candidate)
                {
                    var geometry = Read(candidate);
                    if (geometry == null) return;
                    foreach (var face in geometry.Faces)
                    {
                        if (!face.Intersect(origin, direction, nearest, out float distance, out var point)) continue;
                        if (hit.Part != null && distance >= nearest) continue;
                        var normal = WorldNormal(candidate, face);
                        if (normal.sqrMagnitude == 0f) continue;
                        var world = candidate.World(point);
                        if (!AuthoredSurfaceAvailable(candidate, world, normal)) continue;
                        nearest = distance;
                        hit = new SurfaceHit(candidate, face, world, normal);
                    }
                }
            }
            return hit;
        }

        bool AuthoredSurfaceAvailable(Part part, Vector3 point, Vector3 normal)
        {
            if (!part.Authored || !(part.Owner is Brush wall) || wall.operation != BrushOperation.Add) return true;
            float inset = Mathf.Max(2.5e-4f, point.magnitude * 5e-8f);
            return ExposedWithoutSource(wall, ExistingModel(wall), point, normal, inset);
        }

        bool LoadCsgGeometry(Brush brush)
        {
            var model = ExistingModel(brush);
            if (model == null) return false;
            int layer = brush.gameObject.layer;
            var key = (model, layer);
            if (loadedCsgLayers.TryGetValue(key, out bool available)) return available;
            if (!csgMeshes.TryGetValue(model, out var meshes))
                csgMeshes.Add(model, meshes = BrushCsg.MeshObjects(model));

            var builders = new Dictionary<Part, PlacementGeometry.MeshBuilder>();
            foreach (var meshObject in meshes)
            {
                if (meshObject == null || meshObject.gameObject.layer != layer ||
                    !meshObject.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null ||
                    !meshObject.TryGetComponent<MeshRenderer>(out var renderer)) continue;
                var mesh = BrushSceneVisibility.MeshFor(meshObject, out var owners);
                if (owners == null) continue;
                available = true;
                if (renderer.enabled && BrushSceneVisibility.CanPick(meshObject) && mesh != null && owners.Length > 0)
                    Collect(mesh, filter.transform.localToWorldMatrix, owners);
            }
            loadedCsgLayers.Add(key, available);
            foreach (var pair in builders) pair.Key.Geometry = pair.Value.Build(simplify: true);
            return available;

            void Collect(Mesh mesh, Matrix4x4 toWorld, Brush[] owners)
            {
                using var snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh);
                var data = snapshot[0];
                using var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
                data.GetVertices(vertices);
                var transforms = new Dictionary<Part, Matrix4x4>();
                int triangle = 0;
                for (int sub = 0; sub < data.subMeshCount; sub++)
                {
                    var description = data.GetSubMesh(sub);
                    if (description.topology != MeshTopology.Triangles) continue;
                    using var indices = new NativeArray<int>(description.indexCount, Allocator.Temp);
                    data.GetIndices(indices, sub);
                    for (int i = 0; i + 2 < indices.Length && triangle < owners.Length; i += 3, triangle++)
                    {
                        var owner = owners[triangle];
                        if (owner == null || source != null && owner.transform.IsChildOf(source)) continue;
                        var a = vertices[indices[i]];
                        var b = vertices[indices[i + 1]];
                        var c = vertices[indices[i + 2]];
                        if (owner.operation == BrushOperation.Subtract)
                            owner = CutWall(owner, model, toWorld, a, b, c);
                        if (owner == null || !targetBrushes.TryGetValue(owner, out var part)) continue;
                        if (!builders.TryGetValue(part, out var builder))
                            builders.Add(part, builder = new PlacementGeometry.MeshBuilder());
                        if (!transforms.TryGetValue(part, out var toOwner))
                            transforms.Add(part, toOwner = owner.transform.worldToLocalMatrix * toWorld);
                        int ia = builder.AddVertex(toOwner.MultiplyPoint3x4(a));
                        int ib = builder.AddVertex(toOwner.MultiplyPoint3x4(b));
                        int ic = builder.AddVertex(toOwner.MultiplyPoint3x4(c));
                        if (toOwner.determinant < 0f) builder.AddTriangle(ia, ic, ib);
                        else builder.AddTriangle(ia, ib, ic);
                    }
                }
            }
        }

        internal Brush CutWall(Brush cutter, BrushGroup model, Matrix4x4 toWorld, Vector3 a, Vector3 b, Vector3 c,
            Vector3? hitPosition = null)
        {
            if (!wallsByCut.TryGetValue(cutter, out var walls))
            {
                wallsByCut.Add(cutter, walls = new List<Part>());
                csgOrder ??= BrushCsg.BrushesByModel(false);
                if (csgOrder.TryGetValue(model, out var ordered))
                {
                    bool addressed = BrushCutTarget.TryScope(cutter, out var scope);
                    foreach (var wall in ordered)
                    {
                        if (!addressed && wall == cutter) break;
                        if (wall == null || wall.operation != BrushOperation.Add ||
                            wall.gameObject.layer != cutter.gameObject.layer || addressed && !scope.Contains(wall) ||
                            !BrushSceneVisibility.IsVisible(wall.transform)) continue;
                        var part = AuthoredPart(wall);
                        if (part != null) walls.Add(part);
                    }
                }
            }
            if (walls.Count == 1) return (Brush)walls[0].Owner;
            var u = toWorld.MultiplyVector(b - a);
            var v = toWorld.MultiplyVector(c - a);
            var cross = Vector3.Cross(u, v);
            float length = cross.magnitude;
            if (length == 0f) return null;
            var normal = cross / length;
            if (toWorld.determinant < 0f) normal = -normal;
            var point = hitPosition ?? toWorld.MultiplyPoint3x4(a + ((b - a) + (c - a)) / 3f);
            var inside = point - normal * Mathf.Max(2.5e-4f, point.magnitude * 2e-7f);
            foreach (var part in walls)
                if (ContainsVolume(part, inside)) return (Brush)part.Owner;
            // At coincident authored surfaces the inset can land outside a very thin wall.
            foreach (var part in walls)
                if (part.Bounds.Contains(part.Owner.transform.InverseTransformPoint(point))) return (Brush)part.Owner;
            return null;
        }

        Part AuthoredPart(Brush brush)
        {
            if (authoredParts.TryGetValue(brush, out var part)) return part;
            var shape = BrushHooks.LocalShape(brush, out var bounds);
            part = shape == null ? null : new Part(brush, bounds, authored: true)
            {
                Geometry = PlacementGeometry.FromPolyhedron(shape)
            };
            authoredParts.Add(brush, part);
            return part;
        }

        bool ExposedWithoutSource(Brush wall, BrushGroup model, Vector3 point, Vector3 normal, float inset)
        {
            // Contact with the moving solid can remove the contact face from the rendered union.
            var outside = point + normal * inset;
            bool coveredBySource = false;
            foreach (var part in moving)
            {
                if (!(part.Owner is Brush brush) || !brush.isActiveAndEnabled || brush.operation != BrushOperation.Add ||
                    brush.gameObject.layer != wall.gameObject.layer || !ContainsVolume(part, outside) ||
                    ExistingModel(brush) != model) continue;
                coveredBySource = true;
                break;
            }
            if (!coveredBySource) return false;

            var inside = point - normal * inset;
            foreach (var cut in CutsOf(wall, model))
                if (ContainsVolume(cut, inside)) return false;
            return true;
        }

        List<Part> CutsOf(Brush wall, BrushGroup model)
        {
            if (cutsByTarget.TryGetValue(wall, out var cuts)) return cuts;
            cutsByTarget.Add(wall, cuts = new List<Part>());
            csgOrder ??= BrushCsg.BrushesByModel(false);
            if (!csgOrder.TryGetValue(model, out var ordered)) return cuts;
            bool afterWall = false;
            foreach (var cutter in ordered)
            {
                if (cutter == wall) afterWall = true;
                if (cutter == null || cutter.operation != BrushOperation.Subtract ||
                    cutter.gameObject.layer != wall.gameObject.layer || cutter.transform.IsChildOf(source) || !BrushSceneVisibility.IsVisible(cutter.transform) ||
                    (BrushCutTarget.TryScope(cutter, out var scope) ? !scope.Contains(wall) : !afterWall)) continue;
                var cut = AuthoredPart(cutter);
                if (cut != null) cuts.Add(cut);
            }
            return cuts;
        }

        bool ContainsVolume(Part part, Vector3 world)
        {
            if (part.Owner == null) return false;
            var local = part.Owner.transform.InverseTransformPoint(world);
            if (!part.Bounds.Contains(local)) return false;
            var geometry = Read(part);
            if (geometry == null) return false;
            // Crossings of authored faces give a winding number for a closed, possibly concave solid.
            var ray = new Vector3(0.712f, 0.401f, 0.576f);
            int winding = 0;
            foreach (var face in geometry.Faces)
                if (face.Intersect(local, ray, float.PositiveInfinity, out _, out _, twoSided: true))
                    winding += Vector3.Dot(face.Normal, ray) > 0f ? 1 : -1;
            return winding > 0;
        }

        static BrushGroup ExistingModel(Brush brush)
        {
            var model = brush.GetComponentInParent<BrushGroup>(true);
            return model != null ? model : BrushCsg.DefaultModel(brush.gameObject.scene, false);
        }
    }
}
