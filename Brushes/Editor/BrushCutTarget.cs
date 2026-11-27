using static CsgBrush.Editor.BrushLanguage;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    internal readonly struct BrushCutTarget
    {
        internal readonly Brush Brush;
        internal readonly FloorPlan Plan;
        internal readonly int Start, End;

        internal BrushCutTarget(Brush brush)
        {
            Brush = brush;
            Plan = null;
            Start = End = 0;
        }

        internal BrushCutTarget(FloorPlan plan, int start, int end)
        {
            Brush = null;
            Plan = plan;
            Start = start;
            End = end;
        }

        internal Transform Parent => Brush != null ? Brush.transform : Plan != null ? Plan.transform : null;
        internal bool Valid => Brush != null ? Brush.isActiveAndEnabled && Brush.operation == BrushOperation.Add :
            Plan != null && Plan.isActiveAndEnabled && Start != End;
        internal int Layer => Parent.gameObject.layer;
        internal bool CanPick => Valid && BrushSceneVisibility.CanPick(Parent);

        static Brush RootOf(Brush brush)
        {
            while (brush != null && brush.compoundPart && brush.transform.parent != null &&
                brush.transform.parent.TryGetComponent<Brush>(out var parent))
                brush = parent;
            return brush;
        }

        internal static BrushCutTarget FromBrush(Brush brush)
        {
            if (brush == null || brush.operation != BrushOperation.Add) return default;
            if (!brush.IsGenerated) return new BrushCutTarget(RootOf(brush));
            if (brush.generatedBy is FloorPlan plan && brush.generatedWallStartId != brush.generatedWallEndId)
                return new BrushCutTarget(plan, brush.generatedWallStartId, brush.generatedWallEndId);
            return default;
        }

        bool SameWall(int start, int end) => Start != End &&
            ((start == Start && end == End) || (start == End && end == Start));

        internal bool Contains(Brush brush)
        {
            if (!Valid || brush == null) return false;
            if (Brush != null) return RootOf(brush) == Brush;
            return brush.generatedBy == Plan && SameWall(brush.generatedWallStartId, brush.generatedWallEndId);
        }

        internal bool TryWall(out FloorPlan.Wall wall)
        {
            wall = default;
            if (!Valid || Plan == null) return false;
            var walls = new List<FloorPlan.Wall>();
            Plan.Walls(walls);
            foreach (var candidate in walls)
                if (SameWall(candidate.startId, candidate.endId))
                {
                    wall = candidate;
                    return true;
                }
            return false;
        }

        /// <summary>A restricted cut may have no valid target. Only an unscoped cut uses hierarchy order.</summary>
        internal static bool TryScope(Brush cutter, out BrushCutTarget scope)
        {
            if (cutter.addressedCut)
            {
                scope = Scope(cutter);
                return true;
            }

            if (!cutter.TryGetComponent<BrushGroup>(out _))
                for (var parent = cutter.transform.parent; parent != null; parent = parent.parent)
                {
                    if (parent.TryGetComponent<Brush>(out var brush) && brush.operation == BrushOperation.Add)
                    {
                        scope = FromBrush(brush);
                        return true;
                    }
                    if (parent.TryGetComponent<BrushGroup>(out _)) break;
                }

            scope = default;
            return false;
        }

        internal static BrushCutTarget Scope(Brush cutter)
        {
            var plan = cutter.cutPlan;
            if (plan == null) return new BrushCutTarget(cutter.cutTarget);
            if (!cutter.TryGetComponent<WallAnchor>(out var anchor) || anchor.Plan != plan)
                return new BrushCutTarget(plan, cutter.cutWallStartId, cutter.cutWallEndId);
            var walls = new List<FloorPlan.Wall>();
            plan.Walls(walls);
            return WallAnchors.Resolve(anchor, walls, out var wall, out _, WallAnchors.Local(plan, cutter.transform))
                ? new BrushCutTarget(plan, wall.startId, wall.endId) : default;
        }

        internal void Assign(Brush cutter)
        {
            Undo.RecordObjects(new Object[] { cutter, cutter.gameObject }, Tr("Назначить цель вычета"));
            cutter.gameObject.layer = Layer;
            cutter.operation = BrushOperation.Subtract;
            cutter.addressedCut = true;
            cutter.cutTarget = Brush;
            cutter.cutPlan = Plan;
            cutter.cutWallStartId = Start;
            cutter.cutWallEndId = End;
            PrefabUtility.RecordPrefabInstancePropertyModifications(cutter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(cutter.gameObject);
            BrushCache.Forget(cutter);
            BrushCsg.MarkDirty(cutter);
        }

        internal void Draw()
        {
            if (!CanPick) return;
            foreach (var brush in CsgBrush.Brush.Active)
            {
                if (brush.operation != BrushOperation.Add || !Contains(brush) || !BrushSceneVisibility.IsVisible(brush.transform)) continue;
                var shape = BrushHooks.LocalShape(brush, out _);
                if (shape == null) continue;
                using (new Handles.DrawingScope(new Color(1f, 0.76f, 0.25f, 0.95f), BrushGeometry.LocalToWorld(brush)))
                    foreach (var edge in shape.Edges())
                        Handles.DrawLine(shape.vertices[edge.a], shape.vertices[edge.b], 2f);
            }
        }
    }
}
