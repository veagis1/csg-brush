using static CsgBrush.Editor.BrushLanguage;
using System.Collections.Generic;
using System.Linq;
using CsgBrush.Manifold;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CsgBrush.Editor
{
    internal sealed class BrushCutPreview
    {
        internal enum Result { Unavailable, Disjoint, Outline, Internal, RemovesTarget }

        readonly List<Vector3> lines = new List<Vector3>();
        int lastKey;
        bool hasResult;
        Result result;

        internal IReadOnlyList<Vector3> Lines => lines;

        sealed class Operand
        {
            internal Brush brush;
            internal BrushPolyhedron shape, inner;
            internal Matrix4x4 pose;
            internal Bounds bounds;

            internal ManifoldSolid Build()
            {
                var outer = MakeSolid(shape, pose);
                if (outer == null || inner == null) return outer;
                using var cavity = MakeSolid(inner, pose);
                if (cavity == null) return outer;
                using (outer) return ManifoldSolid.Boolean(outer, cavity, ManifoldNative.OpType.Subtract);
            }

            internal int Key
            {
                get
                {
                    unchecked
                    {
                        int key = brush != null ? brush.GetEntityId().GetHashCode() : 0;
                        key = key * 31 + shape.ContentHash();
                        key = key * 31 + (inner?.ContentHash() ?? 0);
                        return key * 31 + pose.GetHashCode();
                    }
                }
            }
        }

        sealed class Part
        {
            internal Operand solid;
            internal readonly List<Operand> cuts = new List<Operand>();
        }

        static ManifoldSolid MakeSolid(BrushPolyhedron poly, Matrix4x4 pose)
        {
            if (poly == null || !poly.IsValid) return null;
            var vertices = new Vector3[poly.vertices.Length];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = pose.MultiplyPoint3x4(poly.vertices[i]);
            return ManifoldSolid.FromFaces(vertices, poly.faces.Select(face => face.indices).ToArray(), out _);
        }

        static Operand FromBrush(Brush brush)
        {
            var shape = BrushHooks.LocalShape(brush, out var localBounds);
            if (shape == null || !shape.IsValid) return null;
            var pose = BrushGeometry.LocalToWorld(brush);
            return new Operand { brush = brush, shape = shape, inner = BrushGeometry.HollowInner(brush), pose = pose, bounds = TransformBounds(localBounds, pose) };
        }

        static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            var x = matrix.MultiplyVector(Vector3.right * bounds.extents.x);
            var y = matrix.MultiplyVector(Vector3.up * bounds.extents.y);
            var z = matrix.MultiplyVector(Vector3.forward * bounds.extents.z);
            var extent = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center), extent * 2f);
        }

        static List<Part> Gather(BrushCutTarget target, Bounds bounds, Brush ignore, out bool otherParts)
        {
            otherParts = false;
            var parts = new List<Part>();
            var model = target.Parent.GetComponentInParent<BrushGroup>(true) ?? BrushCsg.DefaultModel(target.Parent.gameObject.scene, false);
            var groups = BrushCsg.BrushesByModel(createDefaultGroups: false);
            IReadOnlyList<Brush> group = model != null && groups.TryGetValue(model, out var modelBrushes)
                ? modelBrushes : target.Parent.GetComponentsInChildren<Brush>();
            var knownCutters = new Dictionary<Brush, Operand>();
            for (int i = 0; i < group.Count; i++)
            {
                var brush = group[i];
                if (brush == ignore || brush.operation != BrushOperation.Add || brush.gameObject.layer != target.Layer || !target.Contains(brush)) continue;
                var source = FromBrush(brush);
                if (source == null) continue;
                if (!bounds.Intersects(source.bounds)) { otherParts = true; continue; }
                var part = new Part { solid = source };
                parts.Add(part);
                for (int j = 0; j < group.Count; j++)
                {
                    var cut = group[j];
                    if (cut == ignore || cut.operation != BrushOperation.Subtract || cut.gameObject.layer != target.Layer) continue;
                    if (BrushCutTarget.TryScope(cut, out var scope) ? !scope.Contains(brush) : j <= i) continue;
                    if (!knownCutters.TryGetValue(cut, out var operand))
                        knownCutters.Add(cut, operand = FromBrush(cut));
                    if (operand != null && operand.bounds.Intersects(source.bounds)) part.cuts.Add(operand);
                }
            }
            return parts;
        }

        internal Result Evaluate(BrushCutTarget target, BrushPolyhedron shape, Matrix4x4 pose, Brush ignore = null, BrushPolyhedron inner = null)
        {
            if (!target.Valid || shape == null || !shape.IsValid) { hasResult = false; lines.Clear(); return Result.Unavailable; }
            var cutter = new Operand { shape = shape, inner = inner, pose = pose, bounds = TransformBounds(shape.Bounds(), pose) };
            var parts = Gather(target, cutter.bounds, ignore, out bool otherParts);
            int key = cutter.Key;
            unchecked
            {
                key = key * 31 + (otherParts ? 1 : 0);
                foreach (var part in parts)
                {
                    key = key * 31 + part.solid.Key;
                    foreach (var cut in part.cuts) key = key * 31 + cut.Key;
                    key = key * 31 + part.cuts.Count;
                }
            }
            if (hasResult && key == lastKey) return result;
            lines.Clear();
            result = Compute(cutter, parts, otherParts);
            lastKey = key;
            hasResult = true;
            return result;
        }

        Result Compute(Operand cutter, List<Part> parts, bool otherParts)
        {
            if (parts.Count == 0) return Result.Disjoint;
            using var tool = cutter.Build();
            if (tool == null) return Result.Unavailable;
            var solids = new List<ManifoldSolid>();
            try
            {
                foreach (var part in parts)
                {
                    var solid = part.solid.Build();
                    if (solid == null) continue;
                    foreach (var cut in part.cuts)
                    {
                        using var used = cut.Build();
                        if (used == null) continue;
                        using var before = solid;
                        solid = ManifoldSolid.Boolean(before, used, ManifoldNative.OpType.Subtract);
                    }
                    solids.Add(solid);
                }
                if (solids.Count == 0) return Result.Unavailable;
                using var united = solids.Count > 1 ? ManifoldSolid.Batch(solids, ManifoldNative.OpType.Add) : null;
                var target = united ?? solids[0];
                using var remainder = ManifoldSolid.Boolean(target, tool, ManifoldNative.OpType.Subtract);
                if (remainder.Status != ManifoldNative.Error.NoError) return Result.Unavailable;
                const double minVolume = 1e-8;
                if (target.Volume - remainder.Volume < minVolume) return Result.Disjoint;
                if (remainder.IsEmpty) return otherParts ? Result.Internal : Result.RemovesTarget;
                // A hollow cutter carries more than one source. Identify its remaining face boundaries geometrically.
                var cutterSources = new HashSet<int>();
                if (cutter.inner == null) cutterSources.Add(tool.OriginalId);
                else foreach (int source in tool.ToMesh().triangleSource) cutterSources.Add(source);
                Outline(remainder.ToMesh(), cutterSources);
                return lines.Count > 0 ? Result.Outline : Result.Internal;
            }
            finally { foreach (var solid in solids) solid.Dispose(); }
        }

        void Outline(ManifoldSolid.MeshData mesh, HashSet<int> cutterSources)
        {
            var merged = new int[mesh.vertices.Length];
            for (int i = 0; i < merged.Length; i++) merged[i] = i;
            int Root(int i)
            {
                while (merged[i] != i) i = merged[i];
                return i;
            }
            for (int i = 0; i < mesh.mergeFrom.Length; i++)
                merged[Root(mesh.mergeFrom[i])] = Root(mesh.mergeTo[i]);
            var edges = new Dictionary<(int a, int b), int>();
            for (int t = 0; t < mesh.triangleSource.Length; t++)
            {
                int source = cutterSources.Contains(mesh.triangleSource[t]) ? 2 : 1;
                for (int k = 0; k < 3; k++)
                {
                    int a = Root(mesh.triangles[3 * t + k]), b = Root(mesh.triangles[3 * t + (k + 1) % 3]);
                    var edge = a < b ? (a, b) : (b, a);
                    edges.TryGetValue(edge, out int sources);
                    edges[edge] = sources | source;
                }
            }
            foreach (var edge in edges)
                if (edge.Value == 3 && edge.Key.a != edge.Key.b)
                {
                    lines.Add(mesh.vertices[edge.Key.a]);
                    lines.Add(mesh.vertices[edge.Key.b]);
                }
        }

        internal void Draw(BrushCutTarget target, BrushPolyhedron shape, Matrix4x4 pose, Brush ignore = null, BrushPolyhedron inner = null)
        {
            if (shape == null || !shape.IsValid) return;
            var state = Evaluate(target, shape, pose, ignore, inner);
            if (state != Result.Outline)
            {
                string message = state == Result.Disjoint ? Tr("Нет пересечения с целью") :
                    state == Result.Internal ? Tr("Вычет без контура на поверхности") :
                    state == Result.RemovesTarget ? Tr("Будет удалён объём цели") : Tr("Предпросмотр недоступен");
                Handles.Label(pose.MultiplyPoint3x4(shape.Bounds().center), message, EditorStyles.helpBox);
                return;
            }
            var depth = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            using (new Handles.DrawingScope(new Color(1f, 0.53f, 0.16f, 0.8f)))
                for (int i = 0; i < lines.Count; i += 2) Handles.DrawDottedLine(lines[i], lines[i + 1], 4f);
            Handles.zTest = CompareFunction.LessEqual;
            using (new Handles.DrawingScope(new Color(1f, 0.68f, 0.27f)))
                for (int i = 0; i < lines.Count; i += 2) Handles.DrawLine(lines[i], lines[i + 1], 3f);
            Handles.zTest = depth;
        }
    }
}
