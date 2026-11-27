using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    internal enum PlacementMarkerKind { Vertex, Midpoint, FaceCenter }

    // Moving the pointer across one object only changes the highlighted label.
    internal sealed class PlacementPointDisplay
    {
        readonly List<PlacementScene.Feature> visible = new List<PlacementScene.Feature>();
        PlacementScene cachedScene;
        PlacementScene.Part cachedPart;
        Matrix4x4 viewProjection;
        int layers;
        bool cuts;

        internal void Invalidate()
        {
            cachedScene = null;
            cachedPart = null;
            visible.Clear();
        }

        internal void Draw(PlacementScene scene, Camera camera, PlacementScene.Part part, PlacementScene.Feature snapped, bool showAll)
        {
            var rotation = camera.transform.rotation;
            if (showAll)
            {
                UpdateVisible(scene, camera, part);
                PlacementScene.Feature hover = default;
                float nearest = 12f * 12f, depth = float.PositiveInfinity;
                using (new Handles.DrawingScope(PlacementMarker.PointColor))
                    foreach (var point in visible)
                    {
                        var world = point.Position;
                        PlacementMarker.Draw(world, rotation, PlacementMarker.Kind(point), PlacementMarker.PointScale);
                        var screen = HandleUtility.WorldToGUIPointWithDepth(camera, world);
                        float d = ((Vector2)screen - Event.current.mousePosition).sqrMagnitude;
                        if (d > nearest || Mathf.Approximately(d, nearest) && screen.z >= depth) continue;
                        nearest = d;
                        depth = screen.z;
                        hover = point;
                    }
                if (hover.Part != null)
                    PlacementMarker.Highlight(hover.Position, rotation, PlacementMarker.Kind(hover));
            }
            if (snapped.Part != null && scene.IsVisible(camera, snapped))
                using (new Handles.DrawingScope(Color.yellow))
                    PlacementMarker.Draw(snapped.Position, rotation, PlacementMarker.Kind(snapped), 0.065f);
        }

        void UpdateVisible(PlacementScene scene, Camera camera, PlacementScene.Part part)
        {
            var projection = camera.projectionMatrix * camera.worldToCameraMatrix;
            int mask = camera.cullingMask & Tools.visibleLayers;
            bool showCuts = BrushSettings.instance.showCuts;
            if (scene == cachedScene && part == cachedPart && projection.Equals(viewProjection) &&
                mask == layers && cuts == showCuts) return;
            cachedScene = scene;
            cachedPart = part;
            viewProjection = projection;
            layers = mask;
            cuts = showCuts;
            visible.Clear();
            scene.GetFeatures(part, visible);
            visible.RemoveAll(point => !scene.IsVisible(camera, point));
        }
    }

    internal static class PlacementMarker
    {
        internal static readonly Color PointColor = new Color(1f, 0.85f, 0.3f);
        internal const float PointScale = 0.032f;

        internal static void Highlight(Vector3 point, Quaternion view, PlacementMarkerKind kind)
        {
            using (new Handles.DrawingScope(Color.yellow))
                Draw(point, view, kind, 0.075f);
            var label = kind == PlacementMarkerKind.FaceCenter ? "  Центр грани" :
                kind == PlacementMarkerKind.Midpoint ? "  Середина ребра" : "  Вершина";
            Handles.Label(point, BrushLanguage.Tr(label));
        }

        internal static PlacementMarkerKind Kind(PlacementScene.Feature point) =>
            point.FaceCenter ? PlacementMarkerKind.FaceCenter :
            point.Midpoint ? PlacementMarkerKind.Midpoint : PlacementMarkerKind.Vertex;

        internal static void Draw(Vector3 point, Quaternion view, PlacementMarkerKind kind, float scale)
        {
            float size = HandleUtility.GetHandleSize(point) * scale;
            switch (kind)
            {
                case PlacementMarkerKind.FaceCenter:
                    Handles.RectangleHandleCap(0, point, view * Quaternion.Euler(0f, 0f, 45f), size, EventType.Repaint);
                    Handles.DotHandleCap(0, point, view, size * 0.32f, EventType.Repaint);
                    break;
                case PlacementMarkerKind.Midpoint:
                    Handles.RectangleHandleCap(0, point, view, size, EventType.Repaint);
                    break;
                default:
                    Handles.DotHandleCap(0, point, view, size, EventType.Repaint);
                    break;
            }
        }
    }
}
