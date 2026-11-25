using static CsgBrush.Editor.BrushLanguage;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    internal static class BrushPlanDimensionFields
    {
        internal static bool Draw(FloorPlan plan)
        {
            if (!FloorPlanEditContext.IsActive || BrushCreateTool.Active != null) return false;
            var selection = FloorPlanEditState.Sel(plan);
            string key = "csg.plan/" + plan.GetEntityId() + "/";
            var transform = plan.transform;
            float heightScale = transform.TransformVector(Vector3.up).magnitude;
            if (heightScale < 1e-6f) return false;

            if (FloorPlanEditState.Mode == BrushEditMode.Face && selection.rooms.Count == 1)
            {
                foreach (int index in selection.rooms)
                {
                    var room = FloorPlanEditState.RoomsOf(plan)[index];
                    Vector2 centre = Vector2.zero;
                    foreach (var point in room.polygon) centre += point;
                    centre /= room.polygon.Length;
                    var corner = room.polygon[0];
                    var position = transform.TransformPoint(new Vector3(corner.x, room.height * 0.5f, corner.y));
                    var interior = transform.TransformPoint(new Vector3(centre.x, room.height * 0.5f, centre.y));
                    var edge = new BrushDimensionFields.Edge(1, position, transform.TransformVector(Vector3.up), interior);
                    BrushDimensionFields.Field(key + "room/" + index, edge, Tr("Высота комнаты, м"), room.height * heightScale,
                        meters =>
                        {
                            if (plan == null) return;
                            Undo.RecordObject(plan, Tr("Изменить высоту комнаты"));
                            plan.SettingsOf(room).height = ToDimension(meters / heightScale);
                            RecordPlan(plan);
                        }, meters => DrawRoomOutline(plan, room, ToDimension(meters / heightScale), PreviewColor));
                    return true;
                }
            }
            if (FloorPlanEditState.Mode != BrushEditMode.Edge || selection.edges.Count != 1) return false;
            var walls = new List<FloorPlan.Wall>();
            plan.Walls(walls);
            foreach (int index in selection.edges)
            {
                if (index < 0 || index >= walls.Count) return false;
                var wall = walls[index];
                DrawWall(plan, wall, key + wall.startId + "/" + wall.endId, heightScale);
                return true;
            }
            return false;
        }

        static int LinkIndex(FloorPlan plan, int start, int end)
        {
            for (int i = 0; i < plan.links.Count; i++)
            {
                var link = plan.links[i];
                if ((link.start == start && link.end == end) || (link.start == end && link.end == start))
                    return i;
            }
            return -1;
        }

        static void DrawWall(FloorPlan plan, FloorPlan.Wall wall, string key, float heightScale)
        {
            var transform = plan.transform;
            wall.At(0f, out var start, out _, out var firstNormal);
            wall.At(wall.Length, out var end, out _, out var lastNormal);
            var across = new Vector3(firstNormal.x, 0f, firstNormal.y);
            float widthScale = transform.TransformVector(across).magnitude;
            wall.At(wall.Length * 0.5f, out var middle, out _, out _);
            var centre = transform.TransformPoint(new Vector3(middle.x, wall.height * 0.5f, middle.y));
            var upright = end + lastNormal * wall.outer;
            var halfHeight = transform.TransformPoint(new Vector3(upright.x, wall.height * 0.5f, upright.y));
            var cap = start + firstNormal * ((wall.inner + wall.outer) * 0.5f);
            var capTop = transform.TransformPoint(new Vector3(cap.x, wall.height, cap.y));
            var rise = new BrushDimensionFields.Edge(1, halfHeight, transform.TransformVector(Vector3.up), centre);
            BrushDimensionFields.Field(key + "/height", rise, Tr("Высота стены, м"), wall.height * heightScale,
                meters => SetWallDimension(plan, wall, meters / heightScale, true),
                meters => PreviewWall(plan, wall, Mathf.Max(wall.sharedRoomHeight, ToDimension(meters / heightScale)), wall.Thickness));
            if (widthScale >= 1e-6f)
                BrushDimensionFields.Field(key + "/width", new BrushDimensionFields.Edge(2, capTop, transform.TransformVector(across), centre),
                    Tr("Толщина поперёк стены, м"), wall.Thickness * widthScale,
                    meters => SetWallDimension(plan, wall, meters / widthScale, false),
                    meters => PreviewWall(plan, wall, wall.height, ToDimension(meters / widthScale)));

            var a = new Vector3(wall.a.x, 0f, wall.a.y);
            var b = new Vector3(wall.b.x, 0f, wall.b.y);
            float length = transform.TransformVector(b - a).magnitude;
            string title = wall.Curved ? Tr("Длина хорды стены, м") : Tr("Длина стены, м");
            var span = new BrushDimensionFields.Edge(0, transform.TransformPoint((a + b) * 0.5f), transform.TransformVector(b - a), centre);
            BrushDimensionFields.Field(key + "/length", span, title, length,
                value => SetWallLength(plan, wall, value), value => PreviewWallLength(plan, wall, value));
        }

        static readonly Color PreviewColor = new Color(0.36f, 0.86f, 1f, 1f);

        static void DrawRoomOutline(FloorPlan plan, FloorPlan.Room room, float height, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            var line = new Vector3[room.polygon.Length + 1];
            for (int i = 0; i < room.polygon.Length; i++)
                line[i] = plan.transform.TransformPoint(new Vector3(room.polygon[i].x, height, room.polygon[i].y));
            line[line.Length - 1] = line[0];
            using (new Handles.DrawingScope(color))
            {
                Handles.DrawAAPolyLine(3f, line);
                if (height > 0f)
                {
                    var rise = plan.transform.TransformVector(Vector3.up * height);
                    for (int i = 0; i < room.polygon.Length; i++) Handles.DrawLine(line[i], line[i] - rise);
                }
            }
        }

        static void PreviewWall(FloorPlan plan, FloorPlan.Wall wall, float height, float thickness)
        {
            if (wall.Thickness <= 0f) return;
            float ratio = thickness / wall.Thickness;
            var path = wall.path;
            var rise = plan.transform.TransformVector(Vector3.up * height);
            using (new Handles.DrawingScope(PreviewColor))
            {
                for (int i = 0; i + 1 < path.Length; i++)
                {
                    var delta = (path[i + 1] - path[i]).normalized;
                    var n = wall.outwardLeft ? new Vector2(-delta.y, delta.x) : new Vector2(delta.y, -delta.x);
                    Vector3 World(Vector2 p) => plan.transform.TransformPoint(new Vector3(p.x, 0f, p.y));
                    var a = World(path[i] + n * wall.inner * ratio);
                    var b = World(path[i + 1] + n * wall.inner * ratio);
                    var c = World(path[i + 1] + n * wall.outer * ratio);
                    var d = World(path[i] + n * wall.outer * ratio);
                    Handles.DrawAAPolyLine(2f, a, b, c, d, a);
                    Handles.DrawAAPolyLine(2f, a + rise, b + rise, c + rise, d + rise, a + rise);
                    foreach (var point in new[] { a, b, c, d }) Handles.DrawLine(point, point + rise);
                }
            }
        }

        static bool TryWallEnd(FloorPlan plan, FloorPlan.Wall wall, float meters, out int index, out Vector3 point)
        {
            index = plan != null ? plan.PointIndex(wall.endId) : -1;
            point = default;
            if (index < 0 || meters < 0.001f || !float.IsFinite(meters)) return false;
            int start = plan.PointIndex(wall.startId);
            if (start < 0) return false;
            var begin = plan.Point(start);
            var vector = plan.Point(index) - begin;
            float length = plan.transform.TransformVector(vector).magnitude;
            if (length < 0.001f) return false;
            point = begin + vector * (meters / length);
            return true;
        }

        internal static bool SetWallLength(FloorPlan plan, FloorPlan.Wall wall, float meters)
        {
            if (!TryWallEnd(plan, wall, meters, out int end, out var point)) return false;
            Undo.RecordObject(plan, Tr("Изменить длину стены"));
            plan.points[end] = point;
            RecordPlan(plan);
            return true;
        }

        static void PreviewWallLength(FloorPlan plan, FloorPlan.Wall wall, float meters)
        {
            if (!TryWallEnd(plan, wall, meters, out int end, out var point)) return;
            var moved = plan.transform.TransformPoint(point);
            var original = plan.transform.TransformPoint(plan.Point(end));
            using (new Handles.DrawingScope(PreviewColor))
            {
                Handles.DrawDottedLine(original, moved, 4f);
                foreach (var link in plan.links)
                {
                    int other = link.start == wall.endId ? link.end : link.end == wall.endId ? link.start : -1;
                    int index = plan.PointIndex(other);
                    if (index >= 0) Handles.DrawAAPolyLine(3f, plan.transform.TransformPoint(plan.Point(index)), moved);
                }
            }
        }

        internal static bool SetWallDimension(FloorPlan plan, FloorPlan.Wall wall, float value, bool height)
        {
            if (plan == null) return false;
            int index = LinkIndex(plan, wall.startId, wall.endId);
            if (index < 0 || !float.IsFinite(value) || value < 0f) return false;
            if (height && value > 0f && value + 1e-5f < wall.sharedRoomHeight)
            {
                float minimum = wall.sharedRoomHeight * plan.transform.TransformVector(Vector3.up).magnitude;
                SceneView.currentDrawingSceneView?.ShowNotification(new GUIContent(BrushLanguage.Format("Высота общей стены должна быть не меньше {0:0.###} м.", minimum)));
                return false;
            }
            Undo.RecordObject(plan, height ? Tr("Изменить высоту стены") : Tr("Изменить толщину стены"));
            var link = plan.links[index];
            if (height) link.height = ToDimension(value);
            else link.thickness = ToDimension(value);
            plan.links[index] = link;
            RecordPlan(plan);
            return true;
        }

        static float ToDimension(float value) => value > 0f && float.IsFinite(value) ? Mathf.Max(0.001f, value) : 0f;

        static void RecordPlan(FloorPlan plan)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(plan);
            BrushGenerators.MarkDirty(plan);
            SceneView.RepaintAll();
        }
    }
}
