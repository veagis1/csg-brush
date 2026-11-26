using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    internal enum PlacementMarkerKind { Vertex, Midpoint, FaceCenter }

    internal static class PlacementMarker
    {
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
