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
            Vector3[] vertices;
            int[] indices;

            // The native mesh hit is double-sided. Once locked triangles have been removed,
            // its nearest hit can be the inside of the same wall rather than the visible object behind it.
            internal int FrontTriangle(Ray ray, Matrix4x4 toLocal, out float distance)
            {
                vertices ??= Mesh.vertices;
                indices ??= Mesh.triangles;
                var origin = toLocal.MultiplyPoint3x4(ray.origin);
                var direction = toLocal.MultiplyVector(ray.direction);
                int best = -1;
                distance = float.PositiveInfinity;
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
                    if (t < 0f || t >= distance) continue;
                    distance = t;
                    best = i / 3;
                }
                return best;
            }
        }

        static readonly Dictionary<Transform, Preview> visible = new Dictionary<Transform, Preview>();
        static readonly Dictionary<Transform, Preview> selectable = new Dictionary<Transform, Preview>();
        static readonly Dictionary<BrushGroup, List<Brush>> groupBrushes = new Dictionary<BrushGroup, List<Brush>>();
        static readonly HashSet<Brush> hidden = new HashSet<Brush>();
        static readonly HashSet<Brush> locked = new HashSet<Brush>();
        static readonly HashSet<Brush> builtWithRestrictions = new HashSet<Brush>();
        static PlacementScene construction;
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

        enum Access { Pickable, Locked, Hidden }

        internal static bool IsVisible(Transform item) => AccessOf(item, false) != Access.Hidden;
        internal static bool CanPick(Transform item) => AccessOf(item) == Access.Pickable;

        static Access AccessOf(Transform item, bool checkPicking = true)
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
            foreach (var preview in selectable.Values) if (preview != null) Object.DestroyImmediate(preview.Mesh);
            visible.Clear();
            selectable.Clear();
            groupBrushes.Clear();
            hidden.Clear();
            locked.Clear();
            builtWithRestrictions.Clear();
            construction = null;
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
                HashSet<int> restrictedLayers = null;
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
                    if (access != Access.Pickable) (restrictedLayers ??= new HashSet<int>()).Add(brush.gameObject.layer);
                }
                if (hiddenLayers != null)
                    BrushCsg.BuildScenePreview(group, brushes.FindAll(b => !hidden.Contains(b) && hiddenLayers.Contains(b.gameObject.layer)), hiddenLayers, visible);
                if (restrictedLayers != null && BrushCsg.MeshObjects(group).Count != 0)
                    foreach (var brush in brushes)
                        if (restrictedLayers.Contains(brush.gameObject.layer)) builtWithRestrictions.Add(brush);
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

        internal static bool HasRestrictedMesh(Brush brush) => builtWithRestrictions.Contains(brush);

        internal static Brush RestrictedConstructionSurface(Ray ray, int layers, out Vector3 point, out Vector3 normal)
        {
            point = default;
            normal = Vector3.up;
            if (builtWithRestrictions.Count == 0) return null;
            construction ??= PlacementScene.ForConstruction(builtWithRestrictions);
            return construction.ConstructionSurface(ray, layers, out point, out normal);
        }

        internal static Mesh MeshFor(Transform child, bool picking)
        {
            SyncForPicking();
            return MeshFor(child, picking, out _);
        }

        internal static Mesh MeshFor(Transform child, bool picking, out Brush[] sources)
        {
            var canonical = child.GetComponent<MeshFilter>();
            visible.TryGetValue(child, out var preview);
            var mesh = preview != null ? preview.Mesh : canonical != null ? canonical.sharedMesh : null;
            sources = preview != null ? preview.Sources : BrushCsg.TriangleBrushes(child);
            if (!picking || mesh == null || sources == null || locked.Count == 0) return mesh;
            if (!selectable.TryGetValue(child, out var filtered))
            {
                filtered = FilterLocked(mesh, sources);
                selectable.Add(child, filtered);
            }
            if (filtered == null) return mesh;
            sources = filtered.Sources;
            return filtered.Mesh;
        }

        internal static bool PickMesh(Transform child, Ray ray, out Brush brush, out float distance)
        {
            brush = null;
            distance = float.PositiveInfinity;
            var mesh = MeshFor(child, true, out var sources);
            if (mesh == null || mesh.vertexCount == 0) return false;
            var toLocal = child.worldToLocalMatrix;
            if (!mesh.bounds.IntersectRay(new Ray(toLocal.MultiplyPoint3x4(ray.origin), toLocal.MultiplyVector(ray.direction)))) return false;
            int triangle;
            if (selectable.TryGetValue(child, out var filtered) && filtered != null)
            {
                triangle = filtered.FrontTriangle(ray, toLocal, out distance);
                if (triangle < 0) return false;
            }
            else
            {
                if (!BrushHooks.IntersectRayMesh(ray, mesh, child.localToWorldMatrix, out var hit)) return false;
                triangle = hit.triangleIndex;
                distance = hit.distance;
            }
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

        static Preview FilterLocked(Mesh mesh, Brush[] sources)
        {
            if (!System.Array.Exists(sources, locked.Contains)) return null;
            var filtered = new Preview();
            var kept = new List<Brush>();
            filtered.Mesh.indexFormat = mesh.indexFormat;
            filtered.Mesh.vertices = mesh.vertices;
            filtered.Mesh.subMeshCount = mesh.subMeshCount;
            int triangle = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var indices = mesh.GetTriangles(sub);
                var keptIndices = new List<int>(indices.Length);
                for (int i = 0; i < indices.Length; i += 3, triangle++)
                {
                    var owner = triangle < sources.Length ? sources[triangle] : null;
                    if (locked.Contains(owner)) continue;
                    kept.Add(owner);
                    keptIndices.Add(indices[i]);
                    keptIndices.Add(indices[i + 1]);
                    keptIndices.Add(indices[i + 2]);
                }
                filtered.Mesh.SetTriangles(keptIndices, sub);
            }
            filtered.Sources = kept.ToArray();
            return filtered;
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
