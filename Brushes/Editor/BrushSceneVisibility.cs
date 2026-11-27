using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CsgBrush.Editor
{
    // A generated renderer can contain brushes from unrelated Hierarchy branches.
    // Keep its saved mesh and owners separate from the meshes used by the Scene camera and mouse.
    [InitializeOnLoad]
    internal static class BrushSceneVisibility
    {
        internal sealed class Preview
        {
            internal readonly Mesh Mesh = new Mesh { name = "CSG Scene View", hideFlags = HideFlags.HideAndDontSave };
            internal Material[] Materials;
            internal Brush[] Sources;
        }

        sealed class PickGeometry
        {
            readonly Vector3[] vertices;
            readonly int[] indices;
            readonly Brush[] sources;

            internal PickGeometry(Mesh mesh, Brush[] sources)
            {
                vertices = mesh.vertices;
                indices = mesh.triangles;
                this.sources = sources;
            }

            // Trace front faces of the rendered CSG result. A blind pocket has a wall surface
            // to select even when the cutter is locked. A through opening has no such triangle.
            internal bool Hit(Transform child, Ray ray, bool construction, out Brush brush, out RaycastHit hit)
            {
                var toLocal = child.worldToLocalMatrix;
                var origin = toLocal.MultiplyPoint3x4(ray.origin);
                var direction = toLocal.MultiplyVector(ray.direction);
                brush = null;
                hit = new RaycastHit { distance = float.PositiveInfinity };
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var a = vertices[indices[i]];
                    var ab = vertices[indices[i + 1]] - a;
                    var ac = vertices[indices[i + 2]] - a;
                    var p = Vector3.Cross(direction, ac);
                    float det = Vector3.Dot(ab, p);
                    if (det <= 0f) continue;
                    var fromA = origin - a;
                    float u = Vector3.Dot(fromA, p);
                    var q = Vector3.Cross(fromA, ab);
                    float v = Vector3.Dot(direction, q);
                    if (u < 0f || v < 0f || u + v > det) continue;
                    float t = Vector3.Dot(ac, q) / det;
                    if (t < 0f || t >= hit.distance) continue;
                    var owner = i / 3 < sources.Length ? sources[i / 3] : null;
                    // Interpolate on the triangle. Reconstructing the hit from the ray distance
                    // introduces a camera-dependent height even when all three vertices have Y = 0.
                    var point = child.TransformPoint(a + ab * (u / det) + ac * (v / det));
                    if (owner != null && owner.operation == BrushOperation.Subtract && (construction || locked.Contains(owner)))
                    {
                        var group = child.parent.GetComponent<BrushGroup>();
                        owner = WallResolver.CutWall(owner, group, child.localToWorldMatrix,
                            a, vertices[indices[i + 1]], vertices[indices[i + 2]], point);
                    }
                    if (owner == null || locked.Contains(owner) || hidden.Contains(owner)) continue;
                    var faceNormal = toLocal.transpose.MultiplyVector(Vector3.Cross(ab, ac));
                    hit.normal = faceNormal / faceNormal.magnitude; // The determinant above excludes degenerate faces.
                    hit.distance = t;
                    hit.point = point;
                    brush = owner;
                }
                return brush != null;
            }
        }

        static readonly Dictionary<Transform, Preview> visible = new Dictionary<Transform, Preview>();
        static readonly Dictionary<Transform, PickGeometry> pickGeometry = new Dictionary<Transform, PickGeometry>();
        static readonly Dictionary<BrushGroup, List<Brush>> groupBrushes = new Dictionary<BrushGroup, List<Brush>>();
        static readonly HashSet<Brush> hidden = new HashSet<Brush>();
        static readonly HashSet<Brush> locked = new HashSet<Brush>();
        static readonly HashSet<Brush> builtBrushes = new HashSet<Brush>();
        static readonly List<Transform> meshObjects = new List<Transform>();
        static PlacementScene wallResolver;
        static PlacementScene WallResolver => wallResolver ??= new PlacementScene();
        static bool dirty = true;

        static BrushSceneVisibility()
        {
            SceneVisibilityManager.visibilityChanged += Invalidate;
            SceneVisibilityManager.pickingChanged += Invalidate;
            EditorApplication.hierarchyChanged += Invalidate;
            BrushCsg.MeshesWritten += _ => Invalidate();
            EditorApplication.playModeStateChanged += _ => Clear();
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
            Camera.onPreCull += camera => { if (GraphicsSettings.currentRenderPipeline == null) BeginCamera(camera); };
            Camera.onPostRender += camera => { if (GraphicsSettings.currentRenderPipeline == null) EndCamera(camera); };
            RenderPipelineManager.beginCameraRendering += (_, camera) => BeginCamera(camera);
            RenderPipelineManager.endCameraRendering += (_, camera) => EndCamera(camera);
        }

        internal enum Access { Pickable, Locked, Hidden }

        internal static bool IsVisible(Transform item) => AccessOf(item, false) != Access.Hidden;
        internal static bool CanPick(Transform item) => AccessOf(item) == Access.Pickable;

        internal static Access AccessOf(Transform item, bool checkPicking = true)
        {
            if (item == null) return Access.Hidden;
            var manager = SceneVisibilityManager.instance;
            var access = Access.Pickable;
            for (var t = item; t != null; t = t.parent)
            {
                if (manager.IsHidden(t.gameObject)) return Access.Hidden;
                if (checkPicking && access == Access.Pickable && manager.IsPickingDisabled(t.gameObject))
                    access = Access.Locked;
            }
            return access;
        }

        static void Invalidate()
        {
            dirty = true;
            SceneView.RepaintAll();
        }

        static void Clear()
        {
            RestoreRenderers();
            foreach (var preview in visible.Values) Object.DestroyImmediate(preview.Mesh);
            visible.Clear();
            pickGeometry.Clear();
            groupBrushes.Clear();
            hidden.Clear();
            locked.Clear();
            builtBrushes.Clear();
            meshObjects.Clear();
            wallResolver = null;
            dirty = true;
        }

        static void Refresh()
        {
            if (!dirty) return;
            Clear();
            var stage = StageUtility.GetCurrentStageHandle();
            foreach (var (group, brushes) in BrushCsg.BrushesByModel(false))
            {
                if (!stage.Contains(group.gameObject)) continue;
                groupBrushes.Add(group, brushes);
                HashSet<int> hiddenLayers = null;
                for (int i = 0; i < brushes.Count; i++)
                {
                    var brush = brushes[i];
                    var access = AccessOf(brush.transform);
                    if (access == Access.Hidden)
                    {
                        hidden.Add(brush);
                        (hiddenLayers ??= new HashSet<int>()).Add(brush.gameObject.layer);
                    }
                    if (access == Access.Locked) locked.Add(brush);
                }
                if (hiddenLayers != null)
                    BrushCsg.BuildScenePreview(group, brushes.FindAll(b => !hidden.Contains(b) && hiddenLayers.Contains(b.gameObject.layer)), hiddenLayers, visible);
                var builtLayers = new HashSet<int>();
                foreach (var child in BrushCsg.MeshObjects(group))
                {
                    meshObjects.Add(child);
                    if (MeshFor(child, out var owners) != null && owners != null)
                        builtLayers.Add(child.gameObject.layer);
                }
                foreach (var brush in brushes)
                    if (builtLayers.Contains(brush.gameObject.layer)) builtBrushes.Add(brush);
            }
            dirty = false;
        }

        internal static void SyncForPicking()
        {
            // In a Scene GUI event Unity already delivered its Hierarchy notifications. Scripts and
            // synchronous Editor commands can query before the notification. Check once per query.
            if (!dirty && SceneView.currentDrawingSceneView == null)
                foreach (var brushes in groupBrushes.Values)
                    if (FlagsChanged(brushes)) { dirty = true; break; }
            Refresh();
        }

        internal static bool HasBuiltMesh(Brush brush) => builtBrushes.Contains(brush);
        internal static IReadOnlyList<Transform> MeshObjects => meshObjects;

        internal static Mesh MeshFor(Transform child)
        {
            SyncForPicking();
            return MeshFor(child, out _);
        }

        internal static Mesh MeshFor(Transform child, out Brush[] sources)
        {
            var canonical = child.GetComponent<MeshFilter>();
            visible.TryGetValue(child, out var preview);
            var mesh = preview != null ? preview.Mesh : canonical != null ? canonical.sharedMesh : null;
            sources = preview != null ? preview.Sources : BrushCsg.TriangleBrushes(child);
            return mesh;
        }

        internal static bool PickMesh(Transform child, Ray ray, bool construction, out Brush brush, out RaycastHit hit)
        {
            brush = null;
            hit = default;
            var mesh = MeshFor(child, out var sources);
            if (mesh == null || mesh.vertexCount == 0) return false;
            var toLocal = child.worldToLocalMatrix;
            if (!mesh.bounds.IntersectRay(new Ray(toLocal.MultiplyPoint3x4(ray.origin), toLocal.MultiplyVector(ray.direction)))) return false;
            if (construction || locked.Count != 0 && sources != null)
            {
                if (sources == null) return false;
                if (!pickGeometry.TryGetValue(child, out var geometry))
                    pickGeometry.Add(child, geometry = new PickGeometry(mesh, sources));
                return geometry.Hit(child, ray, construction, out brush, out hit);
            }
            if (!BrushHooks.IntersectRayMesh(ray, mesh, child.localToWorldMatrix, out hit)) return false;
            int triangle = hit.triangleIndex;
            if (sources != null && triangle >= 0 && triangle < sources.Length) brush = sources[triangle];
            return true;
        }

        static bool FlagsChanged(List<Brush> brushes)
        {
            foreach (var brush in brushes)
            {
                if (brush == null) return true;
                var previous = hidden.Contains(brush) ? Access.Hidden : locked.Contains(brush) ? Access.Locked : Access.Pickable;
                if (AccessOf(brush.transform) != previous) return true;
            }
            return false;
        }

        // Temporarily substitute the mesh on the existing renderer so materials, layer, shadows and
        // Scene view draw modes follow the normal render path. Game cameras and scene saves use the original.
        static readonly List<(MeshFilter filter, Mesh mesh, MeshRenderer renderer, Material[] materials)> originals =
            new List<(MeshFilter, Mesh, MeshRenderer, Material[])>();
        static Camera sceneCamera;

        static void BeginCamera(Camera camera)
        {
            if (camera.cameraType != CameraType.SceneView || EditorApplication.isPlayingOrWillChangePlaymode) return;
            RestoreRenderers();
            Refresh();
            sceneCamera = camera;
            foreach (var (child, preview) in visible)
            {
                if (child == null || !child.TryGetComponent<MeshFilter>(out var filter) ||
                    !child.TryGetComponent<MeshRenderer>(out var renderer)) continue;
                originals.Add((filter, filter.sharedMesh, renderer, renderer.sharedMaterials));
                filter.sharedMesh = preview.Mesh;
                renderer.sharedMaterials = preview.Materials;
            }
        }

        static void EndCamera(Camera camera)
        {
            if (camera == sceneCamera) RestoreRenderers();
        }

        static void RestoreRenderers()
        {
            foreach (var (filter, mesh, renderer, materials) in originals)
            {
                if (filter != null) filter.sharedMesh = mesh;
                if (renderer != null) renderer.sharedMaterials = materials;
            }
            originals.Clear();
            sceneCamera = null;
        }
    }
}
