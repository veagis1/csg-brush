using System.Collections.Generic;
using CsgBrush.Colliders;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Pool;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Scene-view feedback for Custom shapes: the edges, and when the shape is concave the concave edges in orange
    /// and the cut faces (where the convex parts meet) as dashed lines. Nothing is drawn for a convex shape beyond
    /// its edges, so the feedback appears exactly when a student needs it.
    /// </summary>
    public static class BrushShapeDrawing
    {
        public static readonly Color EdgeColor = new Color(0.3f, 0.8f, 1f, 0.9f);

        internal static void DrawDimensions(Bounds bounds, Matrix4x4 toWorld, bool withHeight = true, Brush editable = null)
        {
            void Label(Vector3 position, Vector3 edge, int axis)
            {
                float meters = toWorld.MultiplyVector(edge).magnitude;
                if (meters <= 1e-4f) return;
                var world = toWorld.MultiplyPoint3x4(position);
                if (BrushDimensionFields.CanResize(editable, axis)) BrushDimensionFields.DrawBrushField(editable, axis, world, meters);
                else BrushDimensionFields.Readout(world, meters);
            }
            var min = bounds.min;
            var center = bounds.center;
            Label(new Vector3(center.x, min.y, min.z), Vector3.right * bounds.size.x, 0);
            Label(new Vector3(bounds.max.x, min.y, center.z), Vector3.forward * bounds.size.z, 2);
            if (withHeight) Label(new Vector3(min.x, center.y, min.z), Vector3.up * bounds.size.y, 1);
        }

        internal static void DrawSelectionDimensions()
        {
            var stage = StageUtility.GetCurrentStageHandle();
            var selected = Selection.gameObjects;
            if (selected.Length > 1 && System.Array.TrueForAll(selected, go => go.TryGetComponent<Brush>(out _)))
            {
                if (Event.current.type != EventType.Repaint) return;
                var frame = Matrix4x4.TRS(Selection.activeTransform.position, Tools.handleRotation, Vector3.one);
                var inverse = frame.inverse;
                Bounds? combined = null;
                foreach (var go in selected)
                {
                    if (!stage.Contains(go) || SceneVisibilityManager.instance.IsHidden(go)) continue;
                    var brush = go.GetComponent<Brush>();
                    var shape = BrushHooks.LocalShape(brush, out _);
                    if (shape == null) continue;
                    var local = inverse * BrushGeometry.LocalToWorld(brush);
                    foreach (var vertex in shape.vertices)
                    {
                        var point = local.MultiplyPoint3x4(vertex);
                        if (combined is Bounds bounds) { bounds.Encapsulate(point); combined = bounds; }
                        else combined = new Bounds(point, Vector3.zero);
                    }
                }
                if (combined is Bounds size) DrawDimensions(size, frame);
                return;
            }
            bool interactive = selected.Length == 1 && BrushCreateTool.Active == null &&
                !BrushEditContext.IsActive && !PointGraphEditContext.IsActive && BrushShapeHandles.WorksWith(Tools.current);
            foreach (var go in selected)
            {
                if (!go.scene.IsValid() || !stage.Contains(go) || SceneVisibilityManager.instance.IsHidden(go)) continue;
                if (go.TryGetComponent<Brush>(out var brush))
                {
                    if (BrushHooks.LocalShape(brush, out var bounds) != null)
                        DrawDimensions(bounds, BrushGeometry.LocalToWorld(brush), true, interactive ? brush : null);
                    continue;
                }
                if (selected.Length == 1 && go.TryGetComponent<FloorPlan>(out var plan) && BrushPlanDimensionFields.Draw(plan))
                    continue;
                if (Event.current.type != EventType.Repaint) continue;
                if (go.TryGetComponent<PointGraph>(out var graph))
                    DrawPathLengths(graph);
                DrawObjectBounds(go);
            }
        }

        static void DrawPathLengths(PointGraph graph)
        {
            for (int link = 0; link < graph.links.Count; link++)
            {
                var path = FloorPlanTools.WorldPath(graph, link);
                float meters = PointGraph.PathLength(path);
                if (meters <= 1e-4f) continue;
                PointGraph.PathPoint(path, meters * 0.5f, out var midpoint, out _);
                Handles.Label(midpoint, BrushSettings.FormatMeters(meters), EditorStyles.whiteMiniLabel);
            }
        }

        // Project each part into the selected object's axes so rotating the whole assembly keeps its dimensions.
        static void DrawObjectBounds(GameObject go)
        {
            var root = go.transform;
            var toLocal = root.worldToLocalMatrix;
            Bounds? combined = null;
            void Include(Vector3 point)
            {
                if (combined is Bounds bounds) { bounds.Encapsulate(point); combined = bounds; }
                else combined = new Bounds(point, Vector3.zero);
            }

            using var rendererLease = ListPool<Renderer>.Get(out var renderers);
            go.GetComponentsInChildren(false, renderers);
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
                var bounds = renderer.localBounds;
                var matrix = toLocal * renderer.localToWorldMatrix;
                var min = bounds.min;
                var max = bounds.max;
                for (int corner = 0; corner < 8; corner++)
                    Include(matrix.MultiplyPoint3x4(new Vector3((corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z)));
            }

            // A plan or spline owns its generated brushes, even when a different GameObject renders their CSG mesh.
            if (!combined.HasValue || go.GetComponent<BrushGenerator>() != null)
            {
                using var brushLease = ListPool<Brush>.Get(out var brushes);
                go.GetComponentsInChildren(false, brushes);
                foreach (var part in brushes)
                {
                    if (!part.enabled || part.operation != BrushOperation.Add) continue;
                    var poly = BrushHooks.LocalShape(part, out _);
                    if (poly == null) continue;
                    var matrix = toLocal * BrushGeometry.LocalToWorld(part);
                    foreach (var vertex in poly.vertices) Include(matrix.MultiplyPoint3x4(vertex));
                }
            }
            if (combined is Bounds result) DrawDimensions(result, root.localToWorldMatrix);
        }

        /// <summary>Convex parts and cut faces of a brush's shape (cached per polyhedron instance and edit).</summary>
        public static int Parts(Brush brush, List<Vector3[]> cutFaces)
        {
            var pieces = new List<ConvexPolytope>();
            if (brush.shape != BrushShape.Custom || brush.polyhedron == null || !brush.polyhedron.IsValid) { cutFaces?.Clear(); return 1; }
            ConvexDecomposition.Decompose(brush.polyhedron, pieces, cutFaces);
            return pieces.Count;
        }

        static readonly Color CutFill = new Color(1f, 0.3f, 0.25f, 0.16f), CutEdge = new Color(1f, 0.35f, 0.3f, 0.9f), CutSelectedFill = new Color(1f, 0.45f, 0.3f, 0.3f);

        /// <summary>
        /// Every active subtract brush as a translucent red volume with its edges (the Cuts toggle in the Brushes
        /// overlay): a cut that has carved everything away has no surface of its own to see or click otherwise.
        /// </summary>
        public static void DrawCuts()
        {
            var selected = new HashSet<GameObject>(Selection.gameObjects);
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            var stageScene = stage != null ? stage.scene : default;
            var active = Brush.Active;
            for (int b = 0; b < active.Count; b++)
            {
                var brush = active[b];
                if (brush == null || brush.operation != BrushOperation.Subtract) continue;
                var scene = brush.gameObject.scene;
                if (stage != null ? scene != stageScene : UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene)) continue; // only what the Scene view shows
                var poly = BrushGeometry.Polyhedron(brush);
                if (poly == null || !poly.IsValid) continue;
                bool isSelected = selected.Contains(brush.gameObject);
                using (new Handles.DrawingScope(BrushGeometry.LocalToWorld(brush)))
                {
                    Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                    foreach (var face in poly.faces)
                    {
                        var pts = new Vector3[face.indices.Length];
                        for (int i = 0; i < pts.Length; i++) pts[i] = poly.vertices[face.indices[i]];
                        Handles.color = isSelected ? CutSelectedFill : CutFill;
                        Handles.DrawAAConvexPolygon(pts);
                        Handles.color = CutEdge;
                        for (int i = 0; i < pts.Length; i++) Handles.DrawLine(pts[i], pts[(i + 1) % pts.Length], isSelected ? 2.5f : 1f);
                    }
                    Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                }
            }
        }

        public static void Draw(Brush brush, bool drawEdges)
        {
            if (brush == null || brush.shape != BrushShape.Custom) return;
            var poly = brush.polyhedron;
            if (poly == null || !poly.IsValid) return;
            var m = Matrix4x4.TRS(brush.transform.position, brush.transform.rotation, Vector3.one);
            if (!string.IsNullOrEmpty(brush.problem))
            {
                Handles.color = Color.red;
                foreach (var face in poly.faces)
                    for (int i = 0; i < face.indices.Length; i++)
                        Handles.DrawLine(m.MultiplyPoint3x4(poly.vertices[face.indices[i]]), m.MultiplyPoint3x4(poly.vertices[face.indices[(i + 1) % face.indices.Length]]), 3f);
                Handles.Label(m.MultiplyPoint3x4(poly.Bounds().max), brush.problem, EditorStyles.helpBox);
            }
            using (new Handles.DrawingScope(m))
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                if (drawEdges)
                {
                    Handles.color = EdgeColor;
                    foreach (var face in poly.faces)
                        for (int i = 0; i < face.indices.Length; i++)
                            Handles.DrawLine(poly.vertices[face.indices[i]], poly.vertices[face.indices[(i + 1) % face.indices.Length]], 1.5f);
                }
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            }
        }
    }
}
