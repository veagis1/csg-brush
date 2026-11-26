using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    internal sealed partial class PlacementScene
    {
        readonly Dictionary<BrushGroup, List<Transform>> csgMeshes = new Dictionary<BrushGroup, List<Transform>>();
        Dictionary<BrushGroup, List<Brush>> csgOrder;
        readonly Dictionary<Brush, Part> independentCuts = new Dictionary<Brush, Part>();
        readonly Dictionary<Brush, List<Part>> cutsByTarget = new Dictionary<Brush, List<Part>>();

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

        internal bool TryPickSurface(Camera camera, Vector2 mouse, out SurfaceHit hit)
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
            if (!TryRaycast(nearRay, camera.cullingMask & Tools.visibleLayers, maxDistance, out var surface))
                return false;

            Feature feature = default;
            float nearest = MagnetRadius * MagnetRadius;
            var part = surface.Part;
            var face = surface.Face;
            void Consider(int a, int b)
            {
                var candidate = new Feature(part, a, b, face);
                var world = candidate.Position;
                var projected = HandleUtility.WorldToGUIPointWithDepth(camera, world);
                float distance = ((Vector2)projected - mouse).sqrMagnitude;
                if (distance > nearest || (feature.Part != null && Mathf.Approximately(distance, nearest))) return;
                if (!IsVisible(camera, part, face, world)) return;
                nearest = distance;
                feature = candidate;
            }
            Consider(-1, -1);
            foreach (var edge in face.Edges)
            {
                Consider(edge.x, -1);
                Consider(edge.y, -1);
                Consider(edge.x, edge.y);
            }
            hit = new SurfaceHit(part, face, surface.Position, surface.Normal, feature);
            return true;
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
            if (!VisiblePart(part, layers)) return false;
            var forward = camera.transform.forward;
            var eye = camera.transform.position;
            var offset = point - eye;
            float depth = Vector3.Dot(forward, offset);
            if (depth < camera.nearClipPlane || depth > camera.farClipPlane) return false;
            var near = camera.orthographic ? point - forward * (depth - camera.nearClipPlane) :
                eye + offset * (camera.nearClipPlane / depth);
            var ray = new Ray(near, camera.orthographic ? forward : offset.normalized);
            var toLocal = part.Owner.transform.worldToLocalMatrix;
            if (Vector3.Dot(toLocal.MultiplyVector(ray.direction), face.Normal) >= 0f) return false;
            var normal = toLocal.transpose.MultiplyVector(face.Normal).normalized;
            if (!IsRenderedBrushSurface(part, point, normal)) return false;
            const float surfaceTolerance = 1e-4f;
            float maxDistance = (point - near).magnitude - surfaceTolerance;
            return FindSurface(ray, layers, maxDistance).Part == null;
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
                if (direction.sqrMagnitude == 0f ||
                    !part.Bounds.IntersectRay(new Ray(origin, direction.normalized)) ||
                    !VisiblePart(part, layers)) continue;
                var geometry = Read(part);
                if (geometry == null) continue;
                foreach (var face in geometry.Faces)
                {
                    if (!face.Intersect(origin, direction, nearest, out float distance, out var point)) continue;
                    if (hit.Part != null && distance >= nearest) continue;
                    var normal = toLocal.transpose.MultiplyVector(face.Normal);
                    float length = normal.magnitude;
                    if (length == 0f) continue;
                    normal /= length;
                    var world = part.World(point);
                    if (!IsRenderedBrushSurface(part, world, normal)) continue;
                    nearest = distance;
                    hit = new SurfaceHit(part, face, world, normal);
                }
            }
            return hit;
        }

        bool IsRenderedBrushSurface(Part part, Vector3 point, Vector3 normal)
        {
            // An additive brush can have portions swallowed by a union or removed by a cut.
            // The built triangle sources let picking keep the authored face while rejecting those portions.
            if (!(part.Owner is Brush brush) || brush.operation != BrushOperation.Add) return true;
            var model = ExistingModel(brush);
            if (model == null) return true;
            if (!csgMeshes.TryGetValue(model, out var meshes))
                csgMeshes.Add(model, meshes = BrushCsg.MeshObjects(model));

            bool hasRenderedMesh = false;
            float tolerance = Mathf.Max(1e-3f, point.magnitude * 2e-7f);
            var ray = new Ray(point + normal * tolerance, -normal);
            foreach (var meshObject in meshes)
            {
                if (meshObject == null || meshObject.gameObject.layer != brush.gameObject.layer ||
                    !meshObject.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null ||
                    !meshObject.TryGetComponent<MeshRenderer>(out var renderer)) continue;
                var owners = BrushCsg.TriangleBrushes(meshObject);
                if (owners == null || owners.Length == 0) continue;
                hasRenderedMesh = true;
                if (renderer.enabled && renderer.bounds.IntersectRay(ray, out float enter) && enter <= tolerance * 2f &&
                    BrushHooks.IntersectRayMesh(ray, filter.sharedMesh, meshObject.localToWorldMatrix, out var hit) &&
                    hit.distance <= tolerance * 2f && hit.triangleIndex >= 0 && hit.triangleIndex < owners.Length &&
                    owners[hit.triangleIndex] == brush) return true;
            }
            return !hasRenderedMesh || ExposedWithoutSource(brush, model, point, normal, tolerance * 0.25f);
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
                    cutter.gameObject.layer != wall.gameObject.layer || cutter.transform.IsChildOf(source) ||
                    (cutter.addressedCut ? !BrushCutTarget.Scope(cutter).Contains(wall) : !afterWall)) continue;
                if (!independentCuts.TryGetValue(cutter, out var cut))
                {
                    if (BrushHooks.LocalShape(cutter, out var bounds) == null) continue;
                    independentCuts.Add(cutter, cut = new Part(cutter, bounds));
                }
                cuts.Add(cut);
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
