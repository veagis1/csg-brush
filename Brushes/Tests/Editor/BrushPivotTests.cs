using System;
using System.Linq;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CsgBrush.Tests
{
    /// <summary>A brush's pivot moves its transform, never its shape; resizing and snapping keep the pivot where it is.</summary>
    public class BrushPivotTests
    {
        [SetUp]
        public void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        static Bounds World(Brush b)
        {
            var poly = BrushGeometry.Polyhedron(b); var m = b.transform.localToWorldMatrix;
            var bounds = new Bounds(m.MultiplyPoint3x4(poly.vertices[0]), Vector3.zero);
            foreach (var v in poly.vertices) bounds.Encapsulate(m.MultiplyPoint3x4(v));
            return bounds;
        }

        static void Same(Bounds expected, Bounds actual, string message)
        {
            Assert.That(Vector3.Distance(expected.min, actual.min), Is.LessThan(1e-4f), message + ": min " + actual.min.ToString("F3"));
            Assert.That(Vector3.Distance(expected.max, actual.max), Is.LessThan(1e-4f), message + ": max " + actual.max.ToString("F3"));
        }

        static Bounds RenderBounds()
        {
            var r = Object.FindAnyObjectByType<MeshRenderer>(); Assert.IsNotNull(r, "the brush renders"); return r.bounds;
        }

        [Test]
        public void SettingThePivotMovesTheTransformAndLeavesTheShape()
        {
            BrushSettings.instance.snapToGrid = false;
            var box = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f), Quaternion.Euler(0f, 90f, 0f));
            BrushApi.ForceUpdate();
            var before = World(box); var rendered = RenderBounds();
            Undo.IncrementCurrentGroup(); // the pivot is its own step, apart from creating the brush
            BrushApi.SetPivot(box, new Vector3(0.5f, 0f, 0f));
            BrushApi.ForceUpdate();
            Same(before, World(box), "the shape stays");
            Same(rendered, RenderBounds(), "and so does the mesh");
            Assert.That(Vector3.Distance(new Vector3(-1f, 0f, 0f), box.transform.position), Is.LessThan(1e-4f), "the transform sits on the back of the bottom: " + box.transform.position.ToString("F3"));
            Undo.PerformUndo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(0.5f, 0.5f, 0.5f), box.pivot, "undo takes the pivot back");
            Assert.That(Vector3.Distance(new Vector3(0f, 1f, 0f), box.transform.position), Is.LessThan(1e-4f), "and the transform");
            Same(before, World(box), "the shape never moved");
        }

        [Test]
        public void ABottomPivotKeepsTheBottomWhenResizedAndSnapsByItsFaces()
        {
            var s = BrushSettings.instance; bool snap = s.snapToGrid; int grid = s.gridIndex;
            try
            {
                s.snapToGrid = true;
                for (int i = 0; i < s.gridSizes.Length; i++) if (s.gridSizes[i] <= 0.5f) s.SetGridIndex(i);
                float g = s.GridMeters;
                var box = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 1f, 2f) * (g / 0.5f), Quaternion.identity);
                BrushApi.SetPivot(box, new Vector3(0.5f, 0f, 0.5f));
                BrushApi.ForceUpdate();
                Assert.AreEqual(-0.5f * box.size.y, box.transform.position.y, 1e-4f, "the transform went to the bottom");
                BrushApi.SetSize(box, new Vector3(box.size.x, box.size.y * 3f, box.size.z));
                BrushApi.ForceUpdate();
                Assert.AreEqual(-0.5f * box.size.y / 3f, World(box).min.y, 1e-4f, "it grows up from its bottom");
                box.transform.position += new Vector3(0.3f * g, 0.3f * g, 0.3f * g);
                BrushApi.ForceUpdate();
                var b = World(box);
                foreach (float v in new[] { b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z })
                    Assert.AreEqual(Mathf.Round(v / g) * g, v, 1e-4f, "its faces snap to the grid: " + b.min.ToString("F3") + " " + b.max.ToString("F3"));
            }
            finally { s.snapToGrid = snap; s.SetGridIndex(grid); }
        }

        [Test]
        public void AnAbsolutePivotStaysThatFarFromTheCornerWhenResized()
        {
            BrushSettings.instance.snapToGrid = false;
            var box = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            var before = World(box);
            BrushApi.SetPivotMode(box, PivotMode.Absolute);
            Assert.AreEqual(new Vector3(1f, 1f, 1f), box.pivot, "the same pivot, in metres");
            Same(before, World(box), "switching the mode moves nothing");
            BrushApi.SetPivot(box, new Vector3(0.5f, 0f, 1f));
            Assert.That(Vector3.Distance(new Vector3(-0.5f, -1f, 0f), box.transform.position), Is.LessThan(1e-4f), box.transform.position.ToString("F3"));
            Same(before, World(box), "the shape stays");
            BrushApi.SetSize(box, new Vector3(4f, 2f, 2f));
            Assert.AreEqual(-1f, World(box).min.x, 1e-4f, "the left face stays 0.5 m from the pivot");
            BrushApi.SetPivotMode(box, PivotMode.Normalized);
            Assert.That(Vector3.Distance(new Vector3(0.125f, 0f, 0.5f), box.pivot), Is.LessThan(1e-5f), "back to a fraction: " + box.pivot.ToString("F3"));
            Assert.AreEqual(-1f, World(box).min.x, 1e-4f, "still in place");
        }

        [Test]
        public void ACustomShapesPivotMovesItsVerticesAndSurvivesAReset()
        {
            BrushSettings.instance.snapToGrid = false;
            var box = BrushApi.Create(BrushShape.Box, new Vector3(3f, 1f, 0f), new Vector3(2f, 2f, 4f), Quaternion.identity);
            BrushApi.ConvertToCustom(box);
            var before = World(box);
            Assert.AreEqual(new Vector3(0.5f, 0.5f, 0.5f), BrushApi.PivotOf(box), "a fresh custom shape is centred");
            BrushApi.SetPivot(box, new Vector3(0.5f, 0f, 0f));
            Same(before, World(box), "the shape stays");
            Assert.AreEqual(new Vector3(0.5f, 0f, 0f), BrushApi.PivotOf(box), "the vertices moved instead");
            Assert.That(Vector3.Distance(new Vector3(3f, 0f, -2f), box.transform.position), Is.LessThan(1e-4f), box.transform.position.ToString("F3"));
            BrushApi.ResetShape(box);
            Assert.AreEqual(BrushShape.Box, box.shape);
            Assert.AreEqual(new Vector3(0.5f, 0f, 0f), box.pivot, "the reset box keeps that pivot");
            Same(before, World(box), "and its place");
        }

        [Test]
        public void ChildrenStayWhereTheyAre()
        {
            BrushSettings.instance.snapToGrid = false;
            var box = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            var child = new GameObject("Lamp").transform; child.SetParent(box.transform, false); child.localPosition = new Vector3(0f, 1f, 0f);
            BrushApi.SetPivot(box, new Vector3(0.5f, 0f, 0.5f));
            Assert.That(Vector3.Distance(new Vector3(0f, 1f, 0f), child.position), Is.LessThan(1e-4f), child.position.ToString("F3"));
        }

        [TestCase(PivotMode.Normalized, false)]
        [TestCase(PivotMode.Absolute, false)]
        [TestCase(PivotMode.Normalized, true)]
        [TestCase(PivotMode.Absolute, true)]
        public void PickingAnActualVertexMovesThePivotAndKeepsTheShape(PivotMode mode, bool custom)
        {
            var settings = BrushSettings.instance;
            bool previousSnap = settings.snapToGrid;
            settings.snapToGrid = false;
            try
            {
                var parent = new GameObject("Родитель").transform;
                parent.SetPositionAndRotation(new Vector3(1.2f, -0.5f, 2.3f), Quaternion.Euler(5f, 27f, 11f));
                parent.localScale = new Vector3(1.5f, 0.8f, 2f);
                var brush = BrushApi.Create(BrushShape.Cylinder, new Vector3(2f, 0.5f, -1f),
                    new Vector3(3f, 2f, 4f), Quaternion.Euler(18f, 37f, 6f), parent, snap: false);
                if (custom)
                    BrushApi.ConvertToCustom(brush);
                BrushApi.SetPivotMode(brush, mode);
                brush.gridSnap = GridSnap.Shape;
                var child = new GameObject("Дочерний объект").transform;
                child.SetParent(brush.transform, false);
                child.localPosition = new Vector3(0.3f, -0.4f, 0.5f);
                var childPosition = child.position;
                var originalPivot = brush.transform.position;
                var shape = BrushGeometry.Polyhedron(brush);
                var bounds = shape.Bounds();
                int index = Array.FindIndex(shape.vertices,
                    vertex => vertex.x > bounds.min.x + 0.1f && vertex.x < bounds.max.x - 0.1f);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), "Цилиндр содержит вершину вне углов габарита.");
                var originalVertices = WorldVertices(brush);

                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                Assert.That(BrushApi.SetPivotToVertex(brush, index), Is.True);
                Undo.FlushUndoRecordObjects();

                AssertPosition(originalVertices[index], brush.transform.position);
                AssertPosition(childPosition, child.position);
                AssertVertices(originalVertices, brush);
                Assert.That(brush.shape, Is.EqualTo(custom ? BrushShape.Custom : BrushShape.Cylinder));
                Assert.That(brush.pivotMode, Is.EqualTo(mode));
                Assert.That(brush.gridSnap, Is.EqualTo(GridSnap.Pivot));

                Undo.PerformUndo();
                AssertPosition(originalPivot, brush.transform.position);
                AssertVertices(originalVertices, brush);
                Assert.That(brush.gridSnap, Is.EqualTo(GridSnap.Shape));
                Undo.PerformRedo();
                AssertPosition(originalVertices[index], brush.transform.position);
                AssertVertices(originalVertices, brush);
                Assert.That(brush.gridSnap, Is.EqualTo(GridSnap.Pivot));

                brush.transform.rotation *= Quaternion.Euler(0f, 45f, 0f);
                AssertPosition(originalVertices[index], WorldVertices(brush)[index]);
                brush.transform.position += new Vector3(0.3f, 0.5f, -0.7f);
                AssertPosition(brush.transform.position, WorldVertices(brush)[index]);
                var displacedVertices = WorldVertices(brush);
                var center = brush.transform.TransformPoint(BrushGeometry.Polyhedron(brush).Bounds().center);
                BrushApi.ResetPivot(brush);
                AssertPosition(center, brush.transform.position);
                AssertVertices(displacedVertices, brush);
            }
            finally
            {
                settings.snapToGrid = previousSnap;
            }
        }

        static Vector3[] WorldVertices(Brush brush) =>
            BrushGeometry.Polyhedron(brush).vertices.Select(brush.transform.TransformPoint).ToArray();

        static void AssertVertices(Vector3[] expected, Brush brush)
        {
            var actual = WorldVertices(brush);
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
                AssertPosition(expected[i], actual[i]);
        }

        static void AssertPosition(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.0001f),
                $"Ожидалось {expected:F5} м, получено {actual:F5} м.");

        [Test]
        public void AnAuthoredEdgeMidpointIsUndoableAndADiagonalIsRejected()
        {
            var brush = BrushApi.Create(BrushShape.Box, new Vector3(2f, 1f, 3f), Vector3.one * 2f, Quaternion.Euler(14f, 26f, 5f), snap: false);
            var poly = BrushGeometry.Polyhedron(brush);
            var edges = poly.Edges();
            var (a, b) = edges[0];
            int diagonal = Enumerable.Range(a + 1, poly.vertices.Length - a - 1).First(i => !edges.Contains((a, i)));
            var before = WorldVertices(brush);
            var pivot = brush.transform.position;
            Assert.That(BrushApi.SetPivotToEdge(brush, a, diagonal), Is.False);
            AssertPosition(pivot, brush.transform.position);

            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Assert.That(BrushApi.SetPivotToEdge(brush, b, a), Is.True);
            Undo.FlushUndoRecordObjects();
            var midpoint = (before[a] + before[b]) * 0.5f;
            AssertPosition(midpoint, brush.transform.position);
            AssertVertices(before, brush);
            Undo.PerformUndo();
            AssertPosition(pivot, brush.transform.position);
            Undo.PerformRedo();
            AssertPosition(midpoint, brush.transform.position);
            AssertVertices(before, brush);

            brush.transform.Rotate(0f, 35f, 0f, Space.World);
            var rotated = WorldVertices(brush);
            AssertPosition(midpoint, (rotated[a] + rotated[b]) * 0.5f);
        }

        [TestCase(BrushShape.Box, 2f)]
        [TestCase(BrushShape.Cylinder, -2f)]
        [TestCase(BrushShape.Wedge, -2f)]
        [TestCase(BrushShape.Arch, 2f)]
        [TestCase(BrushShape.CurvedStairs, 2f)]
        [TestCase(BrushShape.SpiralStairs, -2f)]
        public void ADrawnBrushUsesTheAcceptedStartAndKeepsThePreview(BrushShape shape, float height)
        {
            const float grid = 0.5f;
            var plane = BrushDraw.SurfaceRotation(new Vector3(0.2f, 0.8f, 0.25f));
            var frame = new Vector3(1.2f, 3.1f, 4.2f);
            var start = BrushDraw.SnapInPlane(frame + plane * new Vector3(0.62f, 0f, 0.85f), frame, plane, grid);
            var end = start + plane * new Vector3(-2.5f, 0f, 3f);
            Vector3 centre, size;
            Quaternion rotation;
            if (BrushCreateTool.IsRadial(shape))
            {
                centre = start + plane * Vector3.up * Mathf.Min(0f, height);
                size = new Vector3(4f, Mathf.Abs(height), 0f);
                rotation = plane;
            }
            else if (BrushCreateTool.IsCentred(shape))
                BrushDraw.CentredPose(start, end, height, plane, grid, false, out centre, out size, out rotation);
            else if (shape == BrushShape.Arch)
                BrushDraw.SpanPose(start, end, -1f, false, height, plane, grid, 15f, out centre, out size, out rotation);
            else if (BrushCreateTool.IsDirected(shape))
                BrushDraw.DirectedPose(start, end, -1f, false, height, plane, grid, 15f, out centre, out size, out rotation);
            else
                BrushDraw.Pose(start, end, height, plane, grid, out centre, out size, out rotation);

            var settings = BrushSettings.instance;
            bool previousSnap = settings.snapToGrid;
            var previousMode = settings.newGridSnap;
            settings.snapToGrid = true;
            settings.newGridSnap = GridSnap.Shape;
            try
            {
                var parameters = BrushCreateTool.IsRadial(shape)
                    ? BrushCreateTool.ParametersForRadial(shape, size.x, size.y) : BrushCreateTool.ParametersFor(shape, size);
                var preview = BrushGeometry.ShapePolyhedron(shape, parameters).vertices.Select(vertex => centre + rotation * vertex).ToArray();
                var brush = BrushCreateTool.CreateDrawnBrush(shape, start, centre, size, rotation);
                BrushSnap.Snap(brush);

                AssertPosition(start, brush.transform.position);
                Assert.That(Quaternion.Angle(rotation, brush.transform.rotation), Is.LessThan(0.01f));
                Assert.That(brush.gridSnap, Is.EqualTo(GridSnap.Shape));
                AssertVertices(preview, brush);
            }
            finally
            {
                settings.snapToGrid = previousSnap;
                settings.newGridSnap = previousMode;
            }
        }

        [Test]
        public void DrawingPivotOutsideTheBoundsSurvivesSwitchingNumericMode()
        {
            var start = new Vector3(1f, -2f, -3f);
            var brush = BrushCreateTool.CreateDrawnBrush(BrushShape.Box, start, Vector3.zero, Vector3.one * 2f, Quaternion.identity);
            AssertPosition(start, brush.transform.position);
            AssertPosition(Vector3.zero, World(brush).center);
            BrushApi.SetPivotMode(brush, PivotMode.Absolute);
            AssertPosition(Vector3.zero, World(brush).center);
            BrushApi.SetPivotMode(brush, PivotMode.Normalized);
            AssertPosition(Vector3.zero, World(brush).center);
            BrushApi.ResetPivot(brush);
            AssertPosition(Vector3.zero, brush.transform.position);
        }

        [Test]
        public void ADrawnCutKeepsTheFirstGridPointBeyondAWallEnd()
        {
            var plan = new GameObject("План стены").AddComponent<FloorPlan>();
            int a = plan.AddPoint(Vector3.zero);
            int b = plan.AddPoint(Vector3.right * 2.3f);
            plan.AddWall(a, b, 0.2f);
            var target = new BrushCutTarget(plan, a, b);
            var start = BrushDraw.SnapInPlane(new Vector3(2.28f, 0.5f, 0f), Vector3.zero, Quaternion.identity, 0.5f);
            Assert.That(start.x, Is.EqualTo(2.5f));
            var cut = BrushCreateTool.CreateDrawnBrush(BrushShape.Box, start, start + new Vector3(-0.5f, 0f, 0f),
                new Vector3(1f, 0.5f, 0.5f), Quaternion.identity, plan.transform);
            var before = WorldVertices(cut);
            AssertPosition(start, cut.transform.position);

            target.Assign(cut);
            WallAnchors.AttachCutToWall(cut, target);
            Assert.That(cut.GetComponent<WallAnchor>().onWall, Is.True);
            AssertPosition(start, cut.transform.position);
            AssertVertices(before, cut);
            Undo.FlushUndoRecordObjects();
            BrushApi.ForceUpdate();
            AssertPosition(start, cut.transform.position);
            AssertVertices(before, cut);

            Vector3 move = Vector3.forward * 0.4f;
            plan.points[0] += move;
            plan.points[1] += move;
            WallAnchors.Layout(plan);
            AssertPosition(start + move, cut.transform.position);
            AssertVertices(before.Select(point => point + move).ToArray(), cut);
        }

        [TestCase(BrushShape.Door)]
        [TestCase(BrushShape.Window)]
        [TestCase(BrushShape.CurvedStairs)]
        [TestCase(BrushShape.SpiralStairs)]
        public void VertexPivotPreservesAnIndependentOpeningOrRadialStair(BrushShape shape)
        {
            var settings = BrushSettings.instance;
            bool previousSnap = settings.snapToGrid;
            settings.snapToGrid = false;
            try
            {
                var brush = BrushApi.Create(shape, new Vector3(1.4f, 2.1f, 3.8f), new Vector3(2f, 2f, 1f),
                    Quaternion.Euler(0f, 30f, 0f), snap: false);
                var child = new GameObject("Дочерний объект").transform;
                child.SetParent(brush.transform, false);
                child.localPosition = new Vector3(0.2f, 0.3f, 0.4f);
                var originalChildPosition = child.position;
                var originalVertices = WorldVertices(brush);
                var originalPivot = brush.transform.position;
                var originalHandlePositions = RadialHandlePositions(brush);
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();

                Assert.That(BrushApi.SetPivotToVertex(brush, 0), Is.True);
                Undo.FlushUndoRecordObjects();
                AssertPosition(originalVertices[0], brush.transform.position);
                AssertPosition(originalChildPosition, child.position);
                AssertVertices(originalVertices, brush);
                Assert.That(brush.shape, Is.EqualTo(shape));
                var positions = RadialHandlePositions(brush);
                foreach (var pair in originalHandlePositions)
                    AssertPosition(pair.Value, positions[pair.Key]);

                Undo.PerformUndo();
                AssertPosition(originalPivot, brush.transform.position);
                AssertVertices(originalVertices, brush);
                Undo.PerformRedo();
                AssertPosition(originalVertices[0], brush.transform.position);
                AssertVertices(originalVertices, brush);

                var center = brush.transform.TransformPoint(BrushGeometry.Polyhedron(brush).Bounds().center);
                BrushApi.ResetPivot(brush);
                AssertPosition(center, brush.transform.position);
                AssertPosition(originalChildPosition, child.position);
                AssertVertices(originalVertices, brush);
            }
            finally
            {
                settings.snapToGrid = previousSnap;
            }
        }

        static System.Collections.Generic.Dictionary<string, Vector3> RadialHandlePositions(Brush brush) =>
            BrushShapeHandles.HandlesOf(brush)
                .Where(handle => handle.id == "inner" || handle.id == "height")
                .ToDictionary(handle => handle.id, handle => brush.transform.TransformPoint(handle.local));

        [Test]
        public void AWindowWithShiftedVertexPivotAttachesAtItsOpeningCenter()
        {
            var settings = BrushSettings.instance;
            bool previousSnap = settings.snapToGrid;
            settings.snapToGrid = false;
            try
            {
                var plan = new GameObject("План стен").AddComponent<FloorPlan>();
                plan.AddWall(plan.AddPoint(Vector3.zero), plan.AddPoint(Vector3.right * 6f), 0.3f);
                var position = new Vector3(2.4f, 1.6f, -0.1f);
                var size = new Vector3(1.2f, 1f, 0.5f);
                var plain = BrushApi.Create(BrushShape.Window, position, size, Quaternion.identity, snap: false);
                var shifted = BrushApi.Create(BrushShape.Window, position, size, Quaternion.identity, snap: false);
                Assert.That(BrushApi.SetPivotToVertex(shifted, 0), Is.True);

                var expected = WallAnchors.Attach(plain.gameObject, plan);
                var actual = WallAnchors.Attach(shifted.gameObject, plan);
                Assert.That(actual.onWall, Is.True);
                Assert.That(actual.distance, Is.EqualTo(expected.distance).Within(0.0001f));
                Assert.That(actual.height, Is.EqualTo(expected.height).Within(0.0001f));
                AssertPosition(World(plain).center, World(shifted).center);
                Assert.That(BrushApi.SetPivotToVertex(shifted, 1), Is.False);

                plan.points[0] += Vector3.forward;
                WallAnchors.Layout(plan);
                AssertPosition(World(plain).center, World(shifted).center);
            }
            finally
            {
                settings.snapToGrid = previousSnap;
            }
        }

        [Test]
        public void DoorsWindowsAndRadialStairsKeepTheirOwnPivot()
        {
            BrushSettings.instance.snapToGrid = false;
            foreach (var shape in new[] { BrushShape.Door, BrushShape.Window, BrushShape.CurvedStairs, BrushShape.SpiralStairs })
            {
                var b = BrushApi.Create(shape, Vector3.zero, new Vector3(1f, 2f, 0.3f), Quaternion.identity);
                b.pivot = Vector3.zero;
                Assert.IsNull(BrushApi.PivotOf(b), shape + " has no pivot to set");
                Assert.AreEqual(Vector3.zero, b.PivotShift, shape + " ignores the field");
            }
        }
    }
}
