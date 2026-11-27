using static CsgBrush.Editor.BrushLanguage;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Objects on the walls of floor plans (see <see cref="WallAnchor"/>). Doors and windows cut through their wall; anything
    /// else (a prefab dropped on a wall, a brush drawn on one, an object attached from the menu) sits against a face. The
    /// plan places them all after every change of its walls; a moved one takes the nearest wall (undoable with the move);
    /// one whose wall is gone, or dragged away from every wall, stays where it is.
    /// </summary>
    [InitializeOnLoad]
    public static class WallAnchors
    {
        static WallAnchors()
        {
            BrushGenerators.Updated += g => { if (g is FloorPlan plan) Layout(plan); };
            WallAnchor.Changed = a => { var plan = a != null ? a.Plan : null; if (plan != null) BrushGenerators.MarkDirty(plan); };
        }

        /// <summary>Where a new door or window goes and how big it is.</summary>
        public struct Placement
        {
            public Vector3 position; public Quaternion rotation; public Vector3 size;
            internal BrushSurfaceGrid.Frame grid;
            internal Vector3? drawingPoint;
            internal Transform surface;
            internal Vector3 localScale;
            /// <summary>The plan whose wall it is on, or null.</summary>
            public FloorPlan plan;
            public FloorPlan.Wall wall; public float distance, sill;
        }

        /// <summary>How near an object's closest part must stay to a face (or a door to its wall) to stay on it, in metres.</summary>
        const float CatchMeters = 0.3f;

        static float Grid => BrushSettings.instance.snapToGrid ? BrushSettings.instance.GridMeters : 0f;
        static float Snap(float v) { float g = Grid; return g > 0f ? Mathf.Round(v / g) * g : v; }

        // ------------------------------------------------------------------ doors and windows

        /// <summary>A new door or window under the mouse: on a plan's wall, into a brush's side, else standing on the surface or the grid.</summary>
        public static bool Find(Ray ray, BrushShape shape, out Placement p)
        {
            var s = BrushSettings.instance;
            var opening = s.NewOpeningSize(shape); float sill = s.NewOpeningSill(shape);
            p = default;
            var hit = BrushHooks.PickBrushSurface(ray, out var point, out var normal);
            p.surface = hit != null ? hit.transform : null;
            p.grid = hit != null
                ? BrushSurfaceGrid.Frame.OnPlane(point, normal)
                : BrushSurfaceGrid.Frame.OnPlane(Vector3.zero, Vector3.up);
            if (hit != null && hit.generatedBy is FloorPlan plan && Mathf.Abs(normal.y) < 0.5f)
            {
                var local = plan.WorldToGraph(point);
                if (NearestWall(plan, new Vector2(local.x, local.z), out var wall, out float along, out _))
                {
                    p.plan = plan; p.wall = wall; p.sill = sill;
                    p.grid = BrushSurfaceGrid.ForSurface(hit, point, normal);
                    p.distance = ClampOpening(Snap(along), wall.Length, opening.x * Mathf.Abs(BrushSnap.CounterScaleUnder(plan.transform).x));
                    p.drawingPoint = BrushDraw.SnapInPlane(point, p.grid.Origin, p.grid.Rotation, Grid);
                    OpeningPose(plan, wall, p.distance, sill, opening, out p.position, out p.rotation, out p.size);
                    return true;
                }
            }
            if (hit != null && !hit.IsOpening && Mathf.Abs(normal.y) < 0.5f)
            {
                // into the side of a brush: through its thickness, standing on its bottom
                var n = new Vector3(normal.x, 0f, normal.z).normalized;
                float thickness = Through(hit, point, -n);
                float bottom = Bottom(hit);
                var along = Vector3.Cross(Vector3.up, n);
                float g = Grid;
                var centre = point - n * (thickness * 0.5f);
                var shift = g > 0f ? along * (Mathf.Round(Vector3.Dot(centre, along) / g) * g - Vector3.Dot(centre, along)) : Vector3.zero;
                centre += shift; // on a grid line along the face
                centre.y = bottom + sill + opening.y * 0.5f;
                p.drawingPoint = point + shift;
                p.position = centre; p.rotation = Quaternion.LookRotation(n, Vector3.up);
                p.size = new Vector3(opening.x, opening.y, thickness + 2f * BrushSettings.OpeningMarginMeters);
                return true;
            }
            // on a floor or the grid: standing there, facing the view along a world axis
            if (hit == null)
            {
                var ground = new Plane(Vector3.up, Vector3.zero);
                if (!BrushDraw.TryGridPoint(ray, ground, out point)) return false;
            }
            var f = -ray.direction; f.y = 0f;
            var facing = Mathf.Abs(f.x) > Mathf.Abs(f.z) ? new Vector3(Mathf.Sign(f.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(f.z == 0f ? 1f : f.z));
            var at = new Vector3(Snap(point.x), point.y, Snap(point.z));
            p.drawingPoint = at;
            p.position = at + Vector3.up * (sill + opening.y * 0.5f);
            p.rotation = Quaternion.LookRotation(facing, Vector3.up);
            p.size = new Vector3(opening.x, opening.y, BrushSettings.DefaultOpeningDepthMeters);
            return true;
        }

        /// <summary>Make the door or window of a placement (undoable), anchored when it is on a plan's wall.</summary>
        public static Brush Create(BrushShape shape, in Placement p)
        {
            int group = Undo.GetCurrentGroup();
            var parent = p.plan != null ? p.plan.transform : BrushMenu.CreationParent();
            var brush = BrushApi.Create(shape, p.position, p.size, p.rotation, parent);
            if (p.drawingPoint is Vector3 start) BrushApi.SetDrawingPivot(brush, start);
            if (p.plan != null)
            {
                // Finish the pre-anchor transform edit before the Undo hook can treat it as a move of the anchor.
                Undo.FlushUndoRecordObjects();
                var a = Undo.AddComponent<WallAnchor>(brush.gameObject);
                a.face = WallFace.Centered;
                a.startId = p.wall.startId; a.endId = p.wall.endId; a.distance = p.distance; a.height = p.sill; a.wallLength = p.wall.Length;
                Layout(p.plan);
            }
            Undo.CollapseUndoOperations(group);
            return brush;
        }

        /// <summary>A door or window on a plan's wall (by index), its middle a distance from the wall's first corner.</summary>
        public static Brush Place(FloorPlan plan, BrushShape shape, int wallIndex, float distance)
        {
            var walls = new List<FloorPlan.Wall>(); plan.Walls(walls);
            var s = BrushSettings.instance;
            var p = new Placement { plan = plan, wall = walls[wallIndex], distance = distance, sill = s.NewOpeningSill(shape) };
            OpeningPose(plan, p.wall, distance, p.sill, s.NewOpeningSize(shape), out p.position, out p.rotation, out p.size);
            return Create(shape, p);
        }

        // ------------------------------------------------------------------ anything else

        /// <summary>
        /// Put an object on the nearest wall of a plan (the nearest plan when null), keeping where it is and lined up with the
        /// wall: it becomes the plan's child and follows the wall from now on. Undoable. Null when there is no plan.
        /// </summary>
        public static WallAnchor Attach(GameObject go, FloorPlan plan = null) => Attach(go, plan, false);

        /// <summary>
        /// <see cref="Attach(GameObject, FloorPlan)"/>; the object lines up with the wall (its front out of the face, up is up)
        /// unless <paramref name="keepRotation"/>, as a brush drawn on the wall keeps the pose it was drawn with.
        /// </summary>
        public static WallAnchor Attach(GameObject go, FloorPlan plan, bool keepRotation)
        {
            if (go == null) return null;
            go.TryGetComponent<Brush>(out var brush);
            if (plan == null) plan = NearestPlan(ReferencePosition(go.transform, brush));
            if (plan == null || go.GetComponent<FloorPlan>() != null) return null;
            if (go.transform.parent != plan.transform) Undo.SetTransformParent(go.transform, plan.transform, Tr("Привязать к стене"));
            if (!go.TryGetComponent<WallAnchor>(out var a)) a = Undo.AddComponent<WallAnchor>(go);
            Undo.RecordObject(a, Tr("Привязать к стене"));
            a.startId = a.endId = WallAnchor.Unplaced;
            if (brush != null && brush.IsOpening) a.face = WallFace.Centered;
            else if (a.face == WallFace.Centered) a.face = WallFace.Inside; // the side it is on decides
            a.MarkBrushes(true);
            if (keepRotation) s_KeepPose.Add(a);
            Layout(plan);
            s_KeepPose.Remove(a);
            return a;
        }

        internal static void AttachCutToWall(Brush brush, BrushCutTarget target)
        {
            if (!target.TryWall(out var wall)) return;
            // Recording the drawing pivot is part of creation. Flush it while the object still has no anchor.
            Undo.FlushUndoRecordObjects();
            var plan = target.Plan;
            var t = brush.transform;
            wall.Project(Local(plan, t), out float distance, out float across);
            distance = Mathf.Clamp(distance, 0f, wall.Length);
            var face = across < (wall.inner + wall.outer) * 0.5f ? WallFace.Inside : WallFace.Outside;
            Frame(plan, wall, face, distance, out var origin, out var rotation);
            var inverse = Quaternion.Inverse(rotation);
            var offset = inverse * (t.position - origin);
            var anchor = Undo.AddComponent<WallAnchor>(brush.gameObject);
            Set(anchor, wall, face, distance, offset.y, offset.z, inverse * t.rotation, offset.x);
            anchor.MarkBrushes(true);
            Layout(plan);
        }

        /// <summary>Take an object off its wall: it leaves the plan where it is, and stays there. Undoable.</summary>
        public static void Detach(GameObject go)
        {
            if (go == null || !go.TryGetComponent<WallAnchor>(out var a)) return;
            var plan = a.Plan;
            int group = Undo.GetCurrentGroup();
            if (plan != null) Undo.SetTransformParent(go.transform, plan.transform.parent, Tr("Отвязать от стены"));
            a.MarkBrushes(false);
            Undo.DestroyObjectImmediate(a);
            Undo.CollapseUndoOperations(group);
        }

        // ------------------------------------------------------------------ keeping them on their walls

        static readonly HashSet<WallAnchor> s_Moved = new HashSet<WallAnchor>();
        static readonly List<FloorPlan.Wall> s_Walls = new List<FloorPlan.Wall>();
        static readonly HashSet<WallAnchor> s_ExplicitEdits = new HashSet<WallAnchor>();

        /// <summary>An anchored object was moved by hand: it takes the nearest wall at the plan's next update.</summary>
        public static void Moved(WallAnchor a)
        {
            if (a == null || s_ExplicitEdits.Contains(a)) return;
            s_Moved.Add(a);
            var plan = a.Plan;
            if (plan != null) BrushGenerators.MarkDirty(plan);
        }

        internal static void WithoutMoveTracking(WallAnchor anchor, System.Action edit)
        {
            if (s_ExplicitEdits.Contains(anchor)) { edit(); return; }
            Undo.FlushUndoRecordObjects();
            s_ExplicitEdits.Add(anchor);
            try { edit(); }
            finally
            {
                // The Undo hook must see these edits while tracking by transform is suspended.
                try { Undo.FlushUndoRecordObjects(); }
                finally { s_ExplicitEdits.Remove(anchor); }
            }
        }

        /// <summary>Put everything anchored to the plan on its wall (the plan calls this after building its walls).</summary>
        public static void Layout(FloorPlan plan)
        {
            if (plan == null) return;
            plan.Walls(s_Walls);
            var container = BrushGenerators.Container(plan, false);
            if (container != null && container.GetSiblingIndex() != 0) container.SetAsFirstSibling(); // walls first: cuts come after them
            foreach (Transform child in plan.transform)
            {
                child.TryGetComponent<Brush>(out var brush);
                bool opening = brush != null && brush.IsOpening;
                if (!child.TryGetComponent<WallAnchor>(out var a))
                {
                    if (!opening) continue; // an object under the plan rides on a wall only when attached
                    a = child.gameObject.AddComponent<WallAnchor>(); // a door put under the plan by hand
                    a.face = WallFace.Centered;
                }
                if (a.startId == WallAnchor.Unplaced) { s_Moved.Remove(a); Reanchor(plan, a, opening, true); }
                else if (s_Moved.Remove(a)) Reanchor(plan, a, opening, false);
                // the anchor's saved values are only changed by the user (move, Pick Wall), never here: an undo of a plan edit
                // must find the object's wall again from the same values
                if (!Resolve(a, s_Walls, out var wall, out float distance, Local(plan, ReferencePosition(child, brush)))) { a.onWall = false; continue; }
                a.onWall = true;
                // its wall is whole: points added from now on are new (derived from the plan, so kept out of Undo)
                bool whole = (wall.startId == a.startId && wall.endId == a.endId) || (wall.startId == a.endId && wall.endId == a.startId);
                if (whole && a.newPointsFrom != plan.nextPointId) a.newPointsFrom = plan.nextPointId;
                Vector3 position; Quaternion rotation;
                if (opening)
                {
                    OpeningLayoutPose(plan, wall, distance, a.height, brush, child.localScale, out position, out rotation, out var size);
                    if (Mathf.Abs(brush.size.z - size.z) > 1e-5f) { brush.size = new Vector3(brush.size.x, brush.size.y, size.z); BrushSync.Ensure(brush); }
                }
                else ObjectPose(plan, wall, a, Mathf.Clamp(distance, 0f, wall.Length), out position, out rotation);
                var t = child;
                if ((t.position - position).sqrMagnitude <= 1e-10f && Quaternion.Angle(t.rotation, rotation) <= 1e-3f) continue;
                t.SetPositionAndRotation(position, rotation);
                a.MarkBrushes(true);
                foreach (var carried in child.GetComponentsInChildren<Brush>()) BrushSync.NotifyTransformChanged(carried);
            }
        }

        /// <summary>
        /// The wall an anchor is on, and how far along: the wall between its two ends; a wall split by new points (the piece
        /// it is on); or, when one end is gone, the wall that kept the other. False when its wall is gone.
        /// </summary>
        /// <summary>An object's place on its plan's floor.</summary>
        // Openings attach by the centre of the cut. A pivot picked before attaching is a transform offset.
        static Vector3 ReferencePosition(Transform t, Brush brush) =>
            brush != null && brush.IsOpening ? t.TransformPoint(brush.PivotShift) : t.position;

        static Vector3 OpeningOffset(FloorPlan plan, Quaternion worldRotation, Vector3 localScale, Vector3 localOffset)
        {
            var scaled = Vector3.Scale(localScale, localOffset);
            var parent = plan.transform;
            var localRotation = Quaternion.Inverse(parent.rotation) * worldRotation;
            return parent.TransformVector(localRotation * scaled);
        }

        public static Vector2 Local(FloorPlan plan, Transform t) => Local(plan, ReferencePosition(t, t.GetComponent<Brush>()));

        static Vector2 Local(FloorPlan plan, Vector3 world)
        {
            var point = plan.WorldToGraph(world);
            return new Vector2(point.x, point.z);
        }

        /// <summary>Does a point lie on a wall (within its thickness and a little more)?</summary>
        static bool OnWall(FloorPlan.Wall w, Vector2 p, float reach)
        {
            w.Project(p, out float t, out float across);
            if (t < -CatchMeters || t > w.Length + CatchMeters) return false;
            return across > w.inner - reach && across < w.outer + reach;
        }

        public static bool Resolve(WallAnchor a, List<FloorPlan.Wall> walls, out FloorPlan.Wall wall, out float distance, Vector2? at = null)
        {
            wall = default; distance = a.distance;
            if (a.startId < 0) return false;
            Vector2? ps = null, pe = null;
            foreach (var w in walls)
            {
                if (w.startId == a.startId && w.endId == a.endId) { wall = w; return true; }
                if (w.startId == a.endId && w.endId == a.startId) { wall = w; distance = w.Length - a.distance; return true; } // the same wall, the other way
                if (w.startId == a.startId) ps = w.a; else if (w.endId == a.startId) ps = w.b;
                if (w.startId == a.endId) pe = w.a; else if (w.endId == a.endId) pe = w.b;
            }
            if (ps.HasValue && pe.HasValue && (pe.Value - ps.Value).sqrMagnitude > 1e-8f)
            {
                // split by new points: walk the straight run from its first point to its last, the object on the piece its distance reaches
                var dir = (pe.Value - ps.Value).normalized;
                int current = a.startId; float before = 0f;
                var used = new HashSet<int>();
                for (int step = 0; step < walls.Count; step++)
                {
                    int next = -1; bool forward = true;
                    for (int i = 0; i < walls.Count && next < 0; i++)
                    {
                        if (used.Contains(i)) continue;
                        var w = walls[i];
                        if (w.startId == current && Vector2.Dot(w.direction, dir) > 0.999f) { next = i; forward = true; }
                        else if (w.endId == current && Vector2.Dot(-w.direction, dir) > 0.999f) { next = i; forward = false; }
                    }
                    if (next < 0) break;
                    used.Add(next);
                    var piece = walls[next]; float len = piece.Length;
                    int far = forward ? piece.endId : piece.startId;
                    if (a.distance <= before + len || far == a.endId)
                    {
                        wall = piece; float along = a.distance - before;
                        distance = forward ? along : len - along;
                        return true;
                    }
                    before += len; current = far;
                }
                if (FollowBentSplit(a, walls, out wall, out distance)) return true;
            }
            if (ps.HasValue && pe.HasValue) return false; // both points are there but no wall joins them: the wall was deleted
            // one of its points is gone (a point between two straight walls was deleted, say): a wall from the other point,
            // only one the object stands on, keeping the distance from that point
            bool Fits(FloorPlan.Wall w) => !at.HasValue || OnWall(w, at.Value, CatchMeters + Mathf.Abs(a.offset)); // a shelf stands off the face by its offset
            foreach (var w in walls) if (w.startId == a.startId && Fits(w)) { wall = w; return true; }
            foreach (var w in walls) if (w.endId == a.startId && Fits(w)) { wall = w; distance = w.Length - a.distance; return true; }
            foreach (var w in walls) if (w.endId == a.endId && Fits(w)) { wall = w; distance = w.Length - (a.wallLength - a.distance); return true; }
            foreach (var w in walls) if (w.startId == a.endId && Fits(w)) { wall = w; distance = a.wallLength - a.distance; return true; }
            return false;
        }

        /// <summary>
        /// A wall split by new points that no longer line up (a + dragged off the line): the shortest run of walls from its
        /// first point to its last through points added since (their ids are newer than both ends), the object on the piece
        /// its distance reaches along the run. False when there is no such run.
        /// </summary>
        static bool FollowBentSplit(WallAnchor a, List<FloorPlan.Wall> walls, out FloorPlan.Wall wall, out float distance)
        {
            wall = default; distance = a.distance;
            var plan = a.Plan;
            if (plan == null || !plan.FollowSplit(a.startId, a.endId, a.distance, a.newPointsFrom, out int link, out float along) || link >= walls.Count) return false;
            wall = walls[link]; distance = along; // the walls are in the order of the plan's links
            return true;
        }

        /// <summary>
        /// Take the wall nearest the object (undoable with the move that caused it). A door stays on a wall it is near; anything
        /// else keeps its place relative to the face of the side it is on, while some part of it is near that face (or its
        /// offset from its own wall). Forced (a new anchor), it takes the nearest wall wherever it is, lined up with it.
        /// </summary>
        static void Reanchor(FloorPlan plan, WallAnchor a, bool opening, bool force)
        {
            var t = a.transform;
            var brush = opening ? a.GetComponent<Brush>() : null;
            var local = plan.WorldToGraph(ReferencePosition(t, brush));
            var p2 = new Vector2(local.x, local.z);
            if (!NearestWall(plan, p2, out var wall, out float along, out float across)) return;
            if (opening)
            {
                if (!force && across > (wall.outer - wall.inner) * 0.5f + CatchMeters) { SetFree(a); return; } // off every wall: a free cut
                var toPlan = plan.transform.worldToLocalMatrix * t.localToWorldMatrix;
                var y = toPlan.GetRow(1);
                var half = brush.ClampedSize * 0.5f;
                float halfHeight = Mathf.Abs(y.x) * half.x + Mathf.Abs(y.y) * half.y + Mathf.Abs(y.z) * half.z;
                float sill = Mathf.Max(0f, (local.y - halfHeight) * MetresPerUnitNormal(plan, Vector3.up));
                Set(a, wall, WallFace.Centered, Snap(along), sill, 0f, Quaternion.identity);
                return;
            }
            float middle = (wall.inner + wall.outer) * 0.5f;
            wall.Project(p2, out _, out float side);
            var face = a.face == WallFace.Centered ? WallFace.Centered : side - middle >= 0f ? WallFace.Outside : WallFace.Inside;
            float distance = Mathf.Clamp(Snap(along), 0f, wall.Length);
            Frame(plan, wall, face, distance, out var origin, out var frame);
            var inv = Quaternion.Inverse(frame);
            var rel = inv * (t.position - origin);
            // off the wall: a free object, or one taken to another wall, takes it only against it; one on its own wall is
            // freed only when pulled further out than its offset (a typed offset never frees it)
            bool sameWall = Resolve(a, s_Walls, out var own, out _) && own.startId == wall.startId && own.endId == wall.endId; // its wall, split or not
            bool pulled = !sameWall || face != a.face || rel.z - a.offset > CatchMeters;
            if (!force && pulled && Gap(t, origin, inv, rel.z) > CatchMeters) { SetFree(a); return; }
            // a new anchor lines up with the wall (unless it keeps the pose it was drawn with); one turned by hand on its own
            // wall keeps that turn; one taken to another wall or side keeps its rotation relative to the wall
            var rotation = force ? (s_KeepPose.Remove(a) ? inv * t.rotation : Quaternion.identity) : sameWall && face == a.face ? inv * t.rotation : a.rotation;
            Set(a, wall, face, distance, rel.y, rel.z, rotation);
        }

        /// <summary>New anchors that keep their object's rotation instead of lining up with the wall: brushes drawn on it.</summary>
        static readonly HashSet<WallAnchor> s_KeepPose = new HashSet<WallAnchor>();

        static void Set(WallAnchor a, FloorPlan.Wall wall, WallFace face, float distance, float height, float offset, Quaternion rotation, float alongOffset = 0f)
        {
            if (a.startId == wall.startId && a.endId == wall.endId && a.face == face && Mathf.Approximately(a.distance, distance) && Mathf.Approximately(a.height, height)
                && Mathf.Approximately(a.offset, offset) && Mathf.Approximately(a.alongOffset, alongOffset) && Quaternion.Angle(a.rotation, rotation) < 1e-3f) return;
            Undo.RecordObject(a, Tr("Переместить вдоль стены"));
            a.startId = wall.startId; a.endId = wall.endId; a.face = face; a.distance = distance; a.height = height; a.offset = offset; a.rotation = rotation; a.wallLength = wall.Length;
            a.alongOffset = alongOffset;
        }

        static void SetFree(WallAnchor a)
        {
            if (a.startId == WallAnchor.Free) return;
            Undo.RecordObject(a, Tr("Переместить за пределы стены"));
            a.startId = a.endId = WallAnchor.Free;
        }

        /// <summary>How far the object's nearest part is from the face, along the face's normal (negative: into the wall).</summary>
        static float Gap(Transform t, Vector3 origin, Quaternion inverseFrame, float pivotOffset)
        {
            var b = LocalBounds(t, out bool any);
            if (!any) return pivotOffset;
            float gap = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                gap = Mathf.Min(gap, (inverseFrame * (t.TransformPoint(corner) - origin)).z);
            }
            return gap;
        }

        /// <summary>The wall nearest a point on the plan's floor: how far along it and how far from its middle line.</summary>
        public static bool NearestWall(FloorPlan plan, Vector2 point, out FloorPlan.Wall nearest, out float along, out float across)
        {
            var walls = new List<FloorPlan.Wall>(); plan.Walls(walls);
            nearest = default; along = 0f; across = float.MaxValue;
            foreach (var w in walls)
            {
                w.Project(point, out float t, out _);
                t = Mathf.Clamp(t, 0f, w.Length);
                w.At(t, out var onLine, out _, out var normal);
                var middle = onLine + normal * ((w.inner + w.outer) * 0.5f);
                float d = (point - middle).magnitude;
                if (d < across) { across = d; nearest = w; along = t; }
            }
            return walls.Count > 0;
        }

        /// <summary>The floor plan with a wall nearest a world point.</summary>
        static FloorPlan NearestPlan(Vector3 world)
        {
            FloorPlan best = null; float bestD = float.MaxValue;
            var active = BrushGenerator.Active;
            for (int i = 0; i < active.Count; i++)
            {
                if (!(active[i] is FloorPlan plan)) continue;
                var local = plan.WorldToGraph(world);
                if (NearestWall(plan, new Vector2(local.x, local.z), out _, out _, out float across) && across < bestD) { bestD = across; best = plan; }
            }
            return best;
        }

        /// <summary>The middle of a door kept on the wall: a whole opening fits between the corners when it can.</summary>
        static float ClampOpening(float distance, float length, float width)
        {
            float half = width * 0.5f;
            return length <= width ? length * 0.5f : Mathf.Clamp(distance, half, length - half);
        }

        /// <summary>A place on a wall's face (or its middle) on the floor, and the face's frame: z out of the face, y up.</summary>
        public static void Frame(FloorPlan plan, FloorPlan.Wall wall, WallFace face, float distance, out Vector3 origin, out Quaternion rotation)
        {
            float off = face == WallFace.Inside ? wall.inner : face == WallFace.Outside ? wall.outer : (wall.inner + wall.outer) * 0.5f;
            wall.At(distance, out var onLine, out _, out var outward); // along a curved wall: its direction there
            var n = face == WallFace.Inside ? -outward : outward;
            var p = onLine + outward * off;
            origin = plan.GraphToWorld(new Vector3(p.x, 0f, p.y));
            rotation = plan.transform.rotation * Quaternion.LookRotation(new Vector3(n.x, 0f, n.y), Vector3.up);
        }

        static void ObjectPose(FloorPlan plan, FloorPlan.Wall wall, WallAnchor a, float distance, out Vector3 position, out Quaternion rotation)
        {
            Frame(plan, wall, a.face, distance, out var origin, out var frame);
            position = origin + frame * new Vector3(a.alongOffset, a.height, a.offset);
            rotation = frame * a.rotation;
        }

        /// <summary>The world pose and size of a door or window on a wall: through the wall's thickness, facing across it.</summary>
        public static void OpeningPose(FloorPlan plan, FloorPlan.Wall wall, float distance, float sill, Vector2 opening,
            out Vector3 position, out Quaternion rotation, out Vector3 size) =>
            OpeningPose(plan, wall, distance, sill, opening, BrushSnap.CounterScaleUnder(plan.transform), out position, out rotation, out size);

        static void OpeningPose(FloorPlan plan, FloorPlan.Wall wall, float distance, float sill, Vector2 opening, Vector3 localScale,
            out Vector3 position, out Quaternion rotation, out Vector3 size)
        {
            Frame(plan, wall, WallFace.Centered, distance, out var origin, out rotation);
            float height = sill / MetresPerUnitNormal(plan, Vector3.up) + opening.y * Mathf.Abs(localScale.y) * 0.5f;
            position = origin + plan.transform.TransformVector(Vector3.up * height);
            wall.At(distance, out _, out _, out var outward);
            float margin = BrushSettings.OpeningMarginMeters / MetresPerUnitNormal(plan, new Vector3(outward.x, 0f, outward.y));
            float depthScale = Mathf.Abs(localScale.z) > 1e-6f ? Mathf.Abs(localScale.z) : 1f;
            size = new Vector3(opening.x, opening.y, (wall.Thickness + 2f * margin) / depthScale);
        }

        // A floor or wall is a plane in the graph's frame. Transform its normal as a covector to
        // convert signed distances to that plane even under a non-uniformly scaled ancestry.
        static float MetresPerUnitNormal(FloorPlan plan, Vector3 normal)
        {
            float inverse = plan.transform.worldToLocalMatrix.transpose.MultiplyVector(normal).magnitude;
            return float.IsFinite(inverse) && inverse > 1e-6f ? 1f / inverse : 1f;
        }

        static void OpeningLayoutPose(FloorPlan plan, FloorPlan.Wall wall, float distance, float sill, Brush brush, Vector3 scale,
            out Vector3 position, out Quaternion rotation, out Vector3 size)
        {
            var box = brush.ClampedSize;
            distance = ClampOpening(distance, wall.Length, box.x * Mathf.Abs(scale.x));
            OpeningPose(plan, wall, distance, sill, new Vector2(box.x, box.y), scale, out position, out rotation, out size);
            position -= OpeningOffset(plan, rotation, scale, brush.PivotShift);
        }

        internal static bool CanEditWindowPivot(Brush brush) => brush != null && brush.isActiveAndEnabled &&
            brush.shape == BrushShape.Window && !brush.IsGenerated &&
            brush.TryGetComponent<WallAnchor>(out var anchor) && anchor.isActiveAndEnabled && anchor.Plan != null;

        internal static bool TryWindowPlacement(Brush window, BrushCutTarget target, Vector3 point, out Placement placement)
        {
            placement = default;
            if (!CanEditWindowPivot(window) || !float.IsFinite(point.sqrMagnitude) || !target.TryWall(out var wall)) return false;
            var plan = target.Plan;
            var anchor = window.GetComponent<WallAnchor>();
            wall.Project(Local(plan, point), out float along, out _);
            along = Mathf.Clamp(along, 0f, wall.Length);
            var source = window.transform;
            var sourceScale = source.localScale;
            var pivotShift = window.PivotShift;
            float openingWidth = window.ClampedSize.x;
            float worldX = source.TransformVector(Vector3.right).magnitude;
            float worldY = source.TransformVector(Vector3.up).magnitude;
            float centre = 0f, error = float.PositiveInfinity;
            Vector3 scale = sourceScale;

            void Consider(float first, float last, Vector2 direction, Vector2 outward)
            {
                var localRotation = Quaternion.LookRotation(new Vector3(outward.x, 0f, outward.y), Vector3.up);
                float targetX = plan.transform.TransformVector(localRotation * Vector3.right).magnitude;
                float targetY = plan.transform.TransformVector(localRotation * Vector3.up).magnitude;
                var candidateScale = new Vector3(
                    targetX > 1e-6f ? Mathf.Sign(sourceScale.x) * worldX / targetX : sourceScale.x,
                    targetY > 1e-6f ? Mathf.Sign(sourceScale.y) * worldY / targetY : sourceScale.y,
                    sourceScale.z);
                var offset = localRotation * Vector3.Scale(candidateScale, pivotShift);
                // The anchor stores its centre station. Convert the selected WCS station to the
                // corresponding centre, using the rotation and scale on this piece of wall.
                float wanted = along + Vector2.Dot(direction, new Vector2(offset.x, offset.z));
                float width = openingWidth * Mathf.Abs(candidateScale.x);
                // At a shared endpoint Wall.At still uses the piece ending there.
                float start = first > 0f ? first + Mathf.Min(Mathf.Max(1e-5f, first * 2e-7f), (last - first) * 0.25f) : first;
                float min = Mathf.Max(start, ClampOpening(0f, wall.Length, width));
                float max = Mathf.Min(last, ClampOpening(wall.Length, wall.Length, width));
                if (min > max) return;
                float candidate = Mathf.Clamp(wanted, min, max);
                float miss = Mathf.Abs(candidate - wanted);
                if (miss >= error) return;
                error = miss;
                centre = candidate;
                scale = candidateScale;
            }

            if (wall.Curved)
            {
                float first = 0f;
                for (int i = 1; i < wall.path.Length; i++)
                {
                    var delta = wall.path[i] - wall.path[i - 1];
                    float length = delta.magnitude;
                    if (length < 1e-9f) continue;
                    var direction = delta / length;
                    var right = new Vector2(direction.y, -direction.x);
                    Consider(first, first + length, direction, wall.outwardLeft ? -right : right);
                    first += length;
                }
            }
            else Consider(0f, wall.Length, wall.direction, wall.outward);

            if (!float.IsFinite(error)) return false;
            placement = new Placement { plan = plan, wall = wall, distance = centre, sill = anchor.height, localScale = scale };
            OpeningLayoutPose(plan, wall, centre, anchor.height, window, scale,
                out placement.position, out placement.rotation, out placement.size);
            return true;
        }

        internal static bool MoveWindow(Brush window, Placement placement)
        {
            if (!CanEditWindowPivot(window) || placement.plan == null) return false;
            var anchor = window.GetComponent<WallAnchor>();
            var oldPlan = anchor.Plan;
            var newPlan = placement.plan;
            var wall = placement.wall;
            var t = window.transform;
            if (oldPlan == newPlan && anchor.startId == wall.startId && anchor.endId == wall.endId &&
                Mathf.Approximately(anchor.distance, placement.distance) && anchor.alongOffset == 0f &&
                t.localScale == placement.localScale) return false;
            int group = Undo.GetCurrentGroup();
            var name = Tr("Переместить вдоль стены");
            WithoutMoveTracking(anchor, () =>
            {
                s_Moved.Remove(anchor);
                if (oldPlan != newPlan)
                {
                    Undo.SetTransformParent(t, newPlan.transform, name);
                    if (t.parent != newPlan.transform) return;
                }
                Undo.RecordObject(t, name);
                t.localScale = placement.localScale;
                Undo.RecordObject(window, name);
                Set(anchor, wall, WallFace.Centered, placement.distance, anchor.height, 0f, Quaternion.identity);
                Layout(newPlan);
                PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                PrefabUtility.RecordPrefabInstancePropertyModifications(window);
                BrushGenerators.MarkDirty(oldPlan);
            });
            Undo.CollapseUndoOperations(group);
            return anchor.Plan == newPlan;
        }

        // ------------------------------------------------------------------ choosing the wall

        /// <summary>
        /// Put an anchored object on a wall by index (<see cref="FloorPlan.Walls"/>) of its plan, or of another plan (it moves
        /// under that plan), or free it with -1. It keeps its height, face, offset and rotation; its distance is kept within
        /// the wall. Undoable.
        /// </summary>
        public static void SetWall(WallAnchor a, FloorPlan plan, int wallIndex)
        {
            if (a == null || plan == null) return;
            var old = a.Plan;
            if (old != plan) Undo.SetTransformParent(a.transform, plan.transform, Tr("Сменить стену"));
            var walls = new List<FloorPlan.Wall>(); plan.Walls(walls);
            Undo.RecordObject(a, Tr("Сменить стену"));
            a.alongOffset = 0f;
            if (wallIndex < 0 || wallIndex >= walls.Count) a.startId = a.endId = WallAnchor.Free;
            else
            {
                var w = walls[wallIndex];
                a.startId = w.startId; a.endId = w.endId; a.wallLength = w.Length;
                a.distance = Mathf.Clamp(a.distance, 0f, w.Length);
            }
            a.MarkBrushes(true);
            Layout(plan);
            if (old != null && old != plan) BrushGenerators.MarkDirty(old);
        }

        public static void SetWall(WallAnchor a, int wallIndex) => SetWall(a, a != null ? a.Plan : null, wallIndex);

        /// <summary>The index of the wall an anchor is on now, or -1.</summary>
        public static int WallIndex(WallAnchor a, List<FloorPlan.Wall> walls)
        {
            var plan = a.Plan;
            if (!Resolve(a, walls, out var wall, out _, plan != null ? Local(plan, a.transform) : (Vector2?)null)) return -1;
            return wall.index;
        }

        // ------------------------------------------------------------------ shapes

        /// <summary>The bounds of an object in its own space: its meshes and brushes, children included.</summary>
        public static Bounds LocalBounds(Transform root, out bool any)
        {
            var b = new Bounds(); bool found = false;
            var toRoot = root.worldToLocalMatrix;
            void Add(Vector3 p) { if (!found) { b = new Bounds(p, Vector3.zero); found = true; } else b.Encapsulate(p); }
            void AddBox(Matrix4x4 m, Bounds local)
            {
                for (int i = 0; i < 8; i++) Add(m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? local.min.x : local.max.x, (i & 2) == 0 ? local.min.y : local.max.y, (i & 4) == 0 ? local.min.z : local.max.z)));
            }
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && (mf.gameObject.hideFlags & HideFlags.HideInHierarchy) == 0) AddBox(toRoot * mf.transform.localToWorldMatrix, mf.sharedMesh.bounds);
            foreach (var sr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (sr.sharedMesh != null) AddBox(toRoot * sr.transform.localToWorldMatrix, sr.sharedMesh.bounds);
            foreach (var brush in root.GetComponentsInChildren<Brush>(true))
            {
                var poly = BrushGeometry.Polyhedron(brush);
                if (poly == null) continue;
                var m = toRoot * brush.transform.localToWorldMatrix;
                foreach (var v in poly.vertices) Add(m.MultiplyPoint3x4(v));
            }
            any = found;
            return b;
        }

        /// <summary>How far a ray from a point on a brush's surface travels through it (its thickness there).</summary>
        static float Through(Brush brush, Vector3 point, Vector3 direction)
        {
            var poly = BrushGeometry.Polyhedron(brush);
            var t = brush.transform;
            var lo = t.InverseTransformPoint(point + direction * 1e-3f); var ld = t.InverseTransformDirection(direction);
            float best = float.MaxValue;
            for (int f = 0; f < poly.faces.Length; f++)
            {
                var pl = poly.Plane(f); var n = new Vector3(pl.x, pl.y, pl.z);
                float denom = Vector3.Dot(n, ld);
                if (denom <= 1e-6f) continue; // only faces the ray leaves through
                float tt = -(Vector3.Dot(n, lo) + pl.w) / denom;
                if (tt > 0f && tt < best) best = tt;
            }
            return best == float.MaxValue ? BrushSettings.DefaultOpeningDepthMeters : best + 1e-3f;
        }

        /// <summary>The lowest point of a brush in the world: where a door in its side stands.</summary>
        static float Bottom(Brush brush)
        {
            var poly = BrushGeometry.Polyhedron(brush);
            float y = float.MaxValue;
            foreach (var v in poly.vertices) y = Mathf.Min(y, brush.transform.TransformPoint(v).y);
            return y == float.MaxValue ? brush.transform.position.y : y;
        }

        // ------------------------------------------------------------------ menu

        [MenuItem("GameObject/CSG Brush/Привязать к стене", false, 45)]
        static void AttachSelected()
        {
            int group = Undo.GetCurrentGroup();
            foreach (var go in Selection.gameObjects) if (CanAttach(go)) Attach(go);
            Undo.CollapseUndoOperations(group);
        }

        [MenuItem("GameObject/CSG Brush/Привязать к стене", true)]
        static bool CanAttachSelected() { foreach (var go in Selection.gameObjects) if (CanAttach(go)) return true; return false; }

        static bool CanAttach(GameObject go) => go != null && !EditorUtility.IsPersistent(go) && go.GetComponent<FloorPlan>() == null && go.GetComponent<WallAnchor>() == null
            && !(go.TryGetComponent<Brush>(out var b) && b.IsGenerated) && NearestPlan(go.transform.position) != null;

        [MenuItem("GameObject/CSG Brush/Отвязать от стены", false, 46)]
        static void DetachSelected()
        {
            int group = Undo.GetCurrentGroup();
            foreach (var go in Selection.gameObjects) Detach(go);
            Undo.CollapseUndoOperations(group);
        }

        [MenuItem("GameObject/CSG Brush/Отвязать от стены", true)]
        static bool CanDetachSelected() { foreach (var go in Selection.gameObjects) if (go.GetComponent<WallAnchor>() != null) return true; return false; }
    }
}
