using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>World-space grid enforcement: positions, sizes and rotations stay on the grid whatever the hierarchy does.</summary>
    public class BrushSnapTests
    {
        const float kGrid = 0.5f;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.ClearAll();
            var s = BrushSettings.instance;
            s.gridSizes = new[] { 0.03125f, 0.0625f, 0.125f, 0.25f, kGrid, 1f, 2f, 4f, 8f };
            s.gridIndex = System.Array.IndexOf(s.gridSizes, kGrid);
            s.rotationSnapDegrees = 15f;
            s.snapToGrid = true;
            Assert.AreEqual(kGrid, s.GridMeters, 1e-5f);
        }

        static Vector3 MinCorner(Brush b) => b.transform.position - BrushSnap.WorldExtents(b.size, b.transform.rotation) * 0.5f;

        static void AssertOnGrid(Vector3 v, string what)
        {
            Assert.AreEqual(BrushSnap.Round(v, kGrid).ToString("F4"), v.ToString("F4"), what + " is on the grid");
        }

        [Test]
        public void SizeIsRoundedToGridMultiples()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1.3f, 0.2f, 2.1f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(1.5f, 0.5f, 2f), b.size, "sizes are grid multiples, at least one grid step");
        }

        [Test]
        public void MoveSnapsTheCornerNotThePivot()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1.5f, 1f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            b.transform.position = new Vector3(0.37f, 0.1f, -0.9f); // like a script or an unsnapped drag
            BrushApi.ForceUpdate();
            AssertOnGrid(MinCorner(b), "minimum corner");
            Assert.AreEqual(new Vector3(0.25f, 0f, -1f), b.transform.position, "pivot sits half a size away from the snapped corner");
        }

        [TestCase(GridSnap.Pivot)]
        [TestCase(GridSnap.Shape)]
        public void FreePositionIsKeptUntilTheMovedAxisSnapsToTheAbsoluteGrid(GridSnap mode)
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            brush.gridSnap = mode;
            BrushApi.ForceUpdate();
            var settings = BrushSettings.instance;
            var free = new Vector3(1.23f, -0.13f, 0.39f);
            settings.snapToGrid = false;
            brush.transform.position = free;
            BrushSnap.ProcessChanged();
            settings.snapToGrid = true;
            Assert.IsFalse(BrushSnap.Snap(brush));
            Assert.AreEqual(free, brush.transform.position);

            GUIUtility.hotControl = 1234;
            try
            {
                brush.transform.position = new Vector3(1.31f, free.y, free.z);
                BrushSnap.ProcessChanged();
                Assert.IsTrue(BrushSnap.TryGetPreview(brush, out var pose, out _));
                Assert.AreEqual(new Vector3(1.5f, free.y, free.z), pose.MultiplyPoint3x4(Vector3.zero));
                Assert.AreEqual(1.31f, brush.transform.position.x);
            }
            finally { GUIUtility.hotControl = 0; }
            BrushSnap.ProcessChanged();
            Assert.AreEqual(new Vector3(1.5f, free.y, free.z), brush.transform.position);

            foreach (float node in new[] { 0.5f, 0f, -0.5f, 0f, 1.5f, 1.5f, 0f })
            {
                brush.transform.position = new Vector3(node + 0.000002f, free.y, free.z);
                BrushSnap.ProcessChanged();
                Assert.AreEqual(new Vector3(node, free.y, free.z), brush.transform.position, "including residuals within the old tolerance");
            }
        }

        [Test]
        public void ExactSizeBoxWithWcsOnItsMaximumEdgesReturnsToWorldZero()
        {
            var settings = BrushSettings.instance;
            settings.gridIndex = System.Array.IndexOf(settings.gridSizes, 0.125f);
            settings.snapToGrid = false;
            var initial = new Vector3(-0.012491941f, -0.125f, -0.103406906f);
            var size = new Vector3(0.61250806f, 3.1009622f, 6.896593f);
            var brush = BrushApi.Create(BrushShape.Box, initial, size, Quaternion.identity, snap: false);
            brush.pivot = new Vector3(1f, 0f, 1f);
            using var serialized = new SerializedObject(brush);
            var exactSize = serialized.FindProperty("exactSizeMeters");
            exactSize.vector3Value = size;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            brush.gridSnap = GridSnap.Shape;
            brush.transform.position = initial;
            BrushSnap.Accept(brush);
            settings.snapToGrid = true;

            GUIUtility.hotControl = 1234;
            try
            {
                brush.transform.position = new Vector3(-0.002f, initial.y, 0.015f);
                BrushSnap.ProcessChanged();
                Assert.That(BrushSnap.TryGetPreview(brush, out var pose, out var shapeScale), Is.True);
                Assert.That((Vector3)pose.GetColumn(3), Is.EqualTo(new Vector3(0f, initial.y, 0f)));
                Assert.That(shapeScale, Is.EqualTo(Vector3.one));
            }
            finally { GUIUtility.hotControl = 0; }

            BrushSnap.ProcessChanged();
            Assert.That(brush.transform.position, Is.EqualTo(new Vector3(0f, initial.y, 0f)));
            Assert.That(brush.size, Is.EqualTo(size));
            serialized.Update();
            Assert.That(exactSize.vector3Value, Is.EqualTo(size));
        }

        [Test]
        public void FirstMoveImmediatelyAfterSceneLoadUsesTheLoadedPose()
        {
            var scene = EditorSceneManager.GetActiveScene();
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/BrushSnapFirstMove.unity");
            var initial = new Vector3(-0.02f, -0.125f, -0.103f);
            var brush = BrushApi.Create(BrushShape.Box, initial, Vector3.one, Quaternion.identity, snap: false);
            brush.gridSnap = GridSnap.Pivot;
            try
            {
                Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
                var loadedScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                brush = System.Array.Find(loadedScene.GetRootGameObjects(), root => root.TryGetComponent<Brush>(out _))
                    .GetComponent<Brush>();
                Assert.That(brush.transform.position, Is.EqualTo(initial));

                brush.transform.position = new Vector3(0.27f, initial.y, initial.z);
                BrushSnap.ProcessChanged();
                Assert.That(brush.transform.position, Is.EqualTo(new Vector3(0.5f, initial.y, initial.z)));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void EnablingTheMagnetBeforeTheNextUpdateKeepsTheFreePositionThroughUndo()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            brush.gridSnap = GridSnap.Pivot;
            BrushApi.ForceUpdate();
            var settings = BrushSettings.instance;
            var free = new Vector3(1.23f, 0.12f, -0.16f);
            try
            {
                settings.snapToGrid = false;
                BrushHooks.ApplyGrid();
                brush.transform.position = free;
                settings.snapToGrid = true;
                BrushHooks.ApplyGrid();
                BrushSnap.ProcessChanged();
                Assert.AreEqual(free, brush.transform.position);

                Undo.IncrementCurrentGroup();
                BrushApi.Move(brush, new Vector3(1.499998f, free.y, free.z));
                Undo.FlushUndoRecordObjects();
                BrushSnap.ProcessChanged();
                var snapped = new Vector3(1.5f, free.y, free.z);
                Assert.AreEqual(snapped, brush.transform.position);
                Undo.PerformUndo();
                Assert.AreEqual(free, brush.transform.position);
                Undo.PerformRedo();
                Assert.AreEqual(snapped, brush.transform.position);
            }
            finally { settings.snapToGrid = true; BrushHooks.ApplyGrid(); }
        }

        [Test]
        public void QuarterTurnedBoxReturnsToExactWorldNodesByItsOuterCorner()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 1f, 3f), Quaternion.Euler(0f, 90f, 0f));
            brush.gridSnap = GridSnap.Shape;
            BrushApi.ForceUpdate();
            foreach (float node in new[] { -1.5f, 0f, 1f, 0f })
            {
                brush.transform.position = new Vector3(node + 0.000002f, 0f, 0f);
                BrushSnap.ProcessChanged();
                Assert.AreEqual(new Vector3(node, 0f, 0f), brush.transform.position);
            }
        }

        [Test]
        public void RotationSnapsAndScaleIsRemoved()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1f, 1f, 1f), Quaternion.identity);
            b.transform.rotation = Quaternion.Euler(0f, 47f, 0f);
            b.transform.localScale = new Vector3(2f, 1f, 1f);
            BrushApi.ForceUpdate();
            Assert.AreEqual("(0.0, 45.0, 0.0)", b.transform.rotation.eulerAngles.ToString("F1"), "rotation snapped to 15 degree steps");
            Assert.AreEqual(Vector3.one, b.transform.localScale, "scale is never used on brushes");
            var m = b.transform.localToWorldMatrix; var min = Vector3.positiveInfinity;
            foreach (var v in BrushGeometry.Polyhedron(b).vertices) min = Vector3.Min(min, m.MultiplyPoint3x4(v));
            AssertOnGrid(min, "the bounds of a non axis-aligned brush (its outermost vertices)");
        }

        [Test]
        public void BrushUnderRotatedScaledParentStaysOnTheWorldGrid()
        {
            var parent = new GameObject("Group").transform;
            parent.position = new Vector3(0.3f, 0.2f, 0.1f);
            parent.rotation = Quaternion.Euler(0f, 30f, 0f);
            parent.localScale = new Vector3(2f, 2f, 2f);
            var b = BrushApi.Create(BrushShape.Box, new Vector3(1.1f, 0.4f, -0.7f), new Vector3(2f, 1f, 1f), Quaternion.identity, parent);
            BrushApi.ForceUpdate();
            Assert.IsTrue(BrushSnap.IsAxisAligned(b.transform.rotation), "world rotation snapped to a world axis in spite of the parent");
            Assert.AreEqual(Vector3.one.ToString("F3"), b.transform.lossyScale.ToString("F3"), "world scale is one in spite of the parent");
            AssertOnGrid(MinCorner(b), "world-space corner");
            Assert.AreEqual(parent, BrushSnap.TransformedParent(b), "lint reports the parent");
            Assert.IsFalse(BrushSnap.IsOffGrid(b));

            // moving the parent later moves the brush off the grid; the poll puts it back
            parent.position += new Vector3(0.13f, 0f, 0f);
            BrushApi.ForceUpdate();
            AssertOnGrid(MinCorner(b), "corner after the parent moved");
        }

        [Test]
        public void ResetParentsPutsEverythingBack()
        {
            var parent = new GameObject("Group").transform;
            parent.rotation = Quaternion.Euler(0f, 30f, 0f);
            parent.localScale = new Vector3(2f, 2f, 2f);
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity, parent);
            BrushApi.ForceUpdate();
            Assert.AreEqual(1, BrushSnap.TransformedParents().Count);
            BrushSnap.ResetTransformedParents();
            BrushApi.ForceUpdate();
            Assert.AreEqual(0, BrushSnap.TransformedParents().Count);
            Assert.AreEqual(Vector3.one, parent.localScale);
            Assert.IsTrue(BrushSnap.IsAxisAligned(parent.rotation));
            Assert.AreEqual(0, BrushSnap.OffGridBrushes().Count);
        }

        [Test]
        public void ScaleToolResizesTheBrush()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1f, 1f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            b.transform.localScale = new Vector3(2f, 1f, 1.3f);
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(2f, 1f, 2.5f), b.size, "scale applied to the size and rounded to the grid");
            Assert.AreEqual(Vector3.one, b.transform.localScale, "scale back to one");
        }

        [Test]
        public void SnappingWaitsForTheDragToEnd()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            BrushApi.ForceUpdate();
            GUIUtility.hotControl = 1234; // a handle is being dragged
            try
            {
                b.transform.rotation = Quaternion.Euler(0f, 7f, 0f);
                b.transform.localScale = new Vector3(1.4f, 1f, 1f);
                BrushApi.ForceUpdate();
                Assert.AreEqual("(0.0, 7.0, 0.0)", b.transform.rotation.eulerAngles.ToString("F1"), "not snapped while dragging");
                Assert.AreEqual(new Vector3(1.4f, 1f, 1f), b.transform.localScale, "scale untouched while dragging");
                b.transform.rotation = Quaternion.Euler(0f, 22f, 0f); // the drag accumulates freely
            }
            finally { GUIUtility.hotControl = 0; }
            BrushApi.ForceUpdate();
            Assert.AreEqual("(0.0, 15.0, 0.0)", b.transform.rotation.eulerAngles.ToString("F1"), "snapped on release");
            Assert.AreEqual(new Vector3(1.5f, 1f, 1f), b.size, "scale applied on release");
            Assert.AreEqual(Vector3.one, b.transform.localScale);
        }

        static Bounds RenderBounds()
        {
            var b = new Bounds(); bool first = true;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                if (mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0 || mf.name.StartsWith("‹[debug") || !mf.TryGetComponent<MeshRenderer>(out var mr) || !mr.enabled) continue;
                foreach (var v in mf.sharedMesh.vertices) { var w = mf.transform.TransformPoint(v); if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w); }
            }
            return b;
        }

        static BoxCollider SingleBox()
        {
            var boxes = Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include);
            Assert.AreEqual(1, boxes.Length, "one axis-aligned collider piece");
            return boxes[0];
        }

        [Test]
        public void GeometryPreviewsTheSnapWhileTheGizmoMovesFreely()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1.5f, 1f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            GUIUtility.hotControl = 1234;
            try
            {
                b.transform.position = new Vector3(0.37f, 0.1f, -0.9f);
                b.transform.rotation = Quaternion.Euler(0f, 7f, 0f);
                BrushApi.ForceUpdate();
                Assert.AreEqual(new Vector3(0.37f, 0.1f, -0.9f), b.transform.position, "the transform (gizmo) is free during the drag");
                Assert.IsTrue(BrushSnap.TryGetPreview(b, out _, out _), "the geometry is built at the snapped pose");
                // colliders are deferred until release; the render mesh is what previews the snap
                var mb = RenderBounds();
                Assert.AreEqual(new Vector3(0.25f, 0f, -1f).ToString("F3"), mb.center.ToString("F3"), "geometry shown at the snapped position");
                Assert.AreEqual(new Vector3(1.5f, 1f, 2f).ToString("F3"), mb.size.ToString("F3"), "geometry shown axis-aligned (snapped rotation)");
            }
            finally { GUIUtility.hotControl = 0; }
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(0.25f, 0f, -1f), b.transform.position, "transform snapped on release");
            Assert.IsFalse(BrushSnap.TryGetPreview(b, out _, out _), "no preview after the release");
            var after = SingleBox();
            Assert.AreEqual(new Vector3(0.25f, 0f, -1f).ToString("F3"), after.transform.TransformPoint(after.center).ToString("F3"), "geometry unchanged by the release");
        }

        [Test]
        public void AnUnchangedSnapPreviewDoesNotRequestAnotherBuild()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            BrushApi.ForceUpdate();
            GUIUtility.hotControl = 1234;
            try
            {
                brush.transform.position = new Vector3(0.63f, 0f, 0f);
                BrushSnap.ApplyPreview(brush);
                BrushCsg.RebuildDirtyNow();
                Assert.IsFalse(BrushCsg.HasDirty);

                BrushSnap.ApplyPreview(brush);
                Assert.IsFalse(BrushCsg.HasDirty, "an unchanged preview keeps the built geometry");

                brush.transform.position += Vector3.right * kGrid;
                BrushSnap.ApplyPreview(brush);
                Assert.IsTrue(BrushCsg.HasDirty, "a different snapped position needs a build");
            }
            finally
            {
                GUIUtility.hotControl = 0;
                BrushApi.ForceUpdate();
            }
        }

        [Test]
        public void CustomShapesAreJudgedAndSnappedByTheirVertices()
        {
            var b = BrushApi.Create(BrushShape.Box, new Vector3(1f, 0.5f, 0f), new Vector3(2f, 1f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Assert.IsTrue(BrushApi.ConvertToCustom(b));
            BrushApi.ForceUpdate();
            Assert.IsFalse(BrushSnap.IsOffGrid(b), "a converted box is on the grid, whatever its size box looks like");
            BrushSettings.instance.snapToGrid = false;
            BrushApi.MoveVertex(b, 6, b.transform.TransformPoint(b.polyhedron.vertices[6]) + new Vector3(0.13f, 0.07f, 0f));
            BrushApi.ForceUpdate();
            BrushSettings.instance.snapToGrid = true;
            Assert.IsTrue(BrushSnap.IsOffGrid(b), "a vertex off the grid is reported");
            Assert.AreEqual(1, BrushSnap.SnapAll(true), "Snap all fixes it");
            BrushApi.ForceUpdate();
            Assert.IsFalse(BrushSnap.IsOffGrid(b));
            foreach (var v in b.polyhedron.vertices) AssertOnGrid(b.transform.TransformPoint(v), "vertex");
        }

        [Test]
        public void SnappingIsUndoable()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            BrushApi.ForceUpdate();
            Undo.IncrementCurrentGroup();
            BrushApi.Move(b, new Vector3(1.37f, 0f, 0f));
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(1.5f, 0f, 0f), b.transform.position, "move landed on the grid");
            Undo.PerformUndo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(Vector3.zero, b.transform.position, "undo restores the previous snapped pose");
            Undo.PerformRedo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(1.5f, 0f, 0f), b.transform.position, "redo lands on the grid again");
        }

        /// <summary>The Scale tool (dragged) or the Inspector (typed): the scale becomes the size, and one undo takes both back.</summary>
        [Test]
        public void ScalingIsUndoable([Values(true, false)] bool dragged)
        {
            var b = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Undo.IncrementCurrentGroup();
            if (dragged) GUIUtility.hotControl = 1234; // the handle records the transform every frame of the drag
            try
            {
                for (int frame = 1; frame <= (dragged ? 4 : 1); frame++)
                {
                    Undo.RecordObject(b.transform, "Scale");
                    b.transform.localScale = new Vector3(dragged ? 1f + frame * 0.25f : 2f, 1f, 1f);
                    Undo.FlushUndoRecordObjects();
                    BrushApi.ForceUpdate();
                }
            }
            finally { GUIUtility.hotControl = 0; }
            BrushApi.ForceUpdate(); // released
            Assert.AreEqual(new Vector3(4f, 2f, 2f), b.size, "the scale became the size");
            Assert.AreEqual(Vector3.one, b.transform.localScale);
            Assert.AreEqual(4f, RenderBounds().size.x, 1e-3f, "and the mesh");

            Undo.PerformUndo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(2f, 2f, 2f), b.size, "undo gives the size back");
            Assert.AreEqual(Vector3.one, b.transform.localScale, "with no scale");
            Assert.AreEqual(2f, RenderBounds().size.x, 1e-3f, "and the mesh");

            Undo.PerformRedo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(4f, 2f, 2f), b.size, "redo");
            Assert.AreEqual(Vector3.one, b.transform.localScale);
            Assert.AreEqual(4f, RenderBounds().size.x, 1e-3f);
        }

        [Test]
        public void UnitysVisibleGridFollowsTheBrushGrid()
        {
            var s = BrushSettings.instance; int grid = s.gridIndex;
            try
            {
                s.SetGridIndex(1);
                Assert.AreEqual(s.GridMeters, UnityEditor.EditorSnapSettings.gridSize.x, 1e-5f);
                s.SetGridIndex(3);
                Assert.AreEqual(Vector3.one * s.GridMeters, UnityEditor.EditorSnapSettings.gridSize, "the Scene view grid shows the brush grid");
            }
            finally { s.SetGridIndex(grid); }
        }

        [Test]
        public void DoorsAndWindowsAreNotSnapped()
        {
            var s = BrushSettings.instance; bool snap = s.snapToGrid;
            try
            {
                s.snapToGrid = true;
                var window = BrushApi.Create(BrushShape.Window, new Vector3(0.3f, 1.5f, 0.12f), new Vector3(1.2f, 1.2f, 0.3f), Quaternion.Euler(0f, 30f, 0f));
                Assert.IsFalse(BrushSnap.Snap(window), "a window keeps its place: on a wall, or where it was put");
                Assert.AreEqual(new Vector3(1.2f, 1.2f, 0.3f), window.size, "and its own size");
                Assert.AreEqual(new Vector3(0.3f, 1.5f, 0.12f), window.transform.position);
                Assert.IsFalse(BrushSnap.IsOffGrid(window));
                BrushApi.SetSize(window, new Vector3(0.9f, 1.3f, 0.3f));
                Assert.AreEqual(new Vector3(0.9f, 1.3f, 0.3f), window.size, "a size set by hand is kept");
            }
            finally { s.snapToGrid = snap; }
        }
    }
}
