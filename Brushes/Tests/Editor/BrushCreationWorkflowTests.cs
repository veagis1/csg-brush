using System.Collections.Generic;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public sealed class BrushCreationWorkflowTests
    {
        bool previousGrid;
        bool previousBasis;

        [SetUp]
        public void Open()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            previousGrid = BrushSettings.instance.snapToGrid;
            previousBasis = BrushSurfaceGrid.Local;
        }

        [TearDown]
        public void Restore()
        {
            BrushSettings.instance.snapToGrid = previousGrid;
            BrushSurfaceGrid.Local = previousBasis;
        }

        [TestCase(null, true, -0.5f, BrushOperation.Subtract)]
        [TestCase(null, true, 0.5f, BrushOperation.Add)]
        [TestCase(null, false, -0.5f, BrushOperation.Add)]
        [TestCase(BrushOperation.Add, true, -0.5f, BrushOperation.Add)]
        [TestCase(BrushOperation.Subtract, true, 0.5f, BrushOperation.Subtract)]
        public void CreationOperationFollowsSignedDepthUnlessManuallyChosen(
            BrushOperation? manual, bool surface, float depth, BrushOperation expected)
        {
            Assert.That(BrushCreateTool.ResolveOperation(manual, surface, depth), Is.EqualTo(expected));
        }

        [Test]
        public void LocalGridFollowsAnOffsetRotatedSurface()
        {
            BrushSurfaceGrid.Local = true;
            var support = BrushApi.Create(BrushShape.Box, new Vector3(0.13f, 0.27f, 0.39f),
                Vector3.one * 2f, Quaternion.Euler(10f, 33f, 0f), snap: false);
            var frame = BrushSurfaceGrid.ForSurface(support, support.transform.position + support.transform.up, support.transform.up);
            var moving = BrushApi.Create(BrushShape.Box, frame.Origin, Vector3.one, frame.Rotation, snap: false);
            moving.gridSnap = GridSnap.Pivot;
            frame.Store(moving);
            support.transform.position += new Vector3(1f, 2f, 3f);
            var requested = frame.Origin + frame.Rotation * new Vector3(0.26f, 0f, -0.76f);
            var expected = frame.Origin + frame.Rotation * new Vector3(0.5f, 0f, -1f);
            Assert.That(Vector3.Distance(expected, BrushSnap.SnapBrushPosition(moving, requested, moving.size,
                moving.transform.rotation, 0.5f)), Is.LessThan(0.0001f));
        }

        [Test]
        public void TypedWidthStaysExactOnGridAndRestoresOnUndo()
        {
            BrushSettings.instance.snapToGrid = true;
            var box = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one * 2f, Quaternion.identity, snap: false);
            box.gridSnap = GridSnap.Shape;
            BrushSnap.Accept(box);
            Vector3 FixedCorner() => box.transform.TransformPoint(BrushGeometry.Polyhedron(box).Bounds().min);
            var before = FixedCorner();
            Undo.IncrementCurrentGroup();
            Assert.That(BrushDimensionFields.TryMeters("4,2", false, out float width), Is.True);
            Assert.That(BrushDimensionFields.Resize(box, 0, width), Is.True);
            Undo.FlushUndoRecordObjects();
            BrushSnap.Snap(box);
            Assert.That(box.size.x, Is.EqualTo(4.2f).Within(0.00001f));
            Assert.That(Vector3.Distance(FixedCorner(), before), Is.LessThan(0.0001f));
            Undo.PerformUndo();
            Assert.That(box.size.x, Is.EqualTo(2f));
            Undo.PerformRedo();
            Assert.That(box.size.x, Is.EqualTo(4.2f).Within(0.00001f));
        }

        static bool Solid(Vector3 point) =>
            Physics.OverlapBox(point, Vector3.one * 0.02f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length > 0;

        static bool Visible(BrushGroup model, Ray ray)
        {
            foreach (var meshObject in BrushCsg.MeshObjects(model))
            {
                var mesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
                if (mesh != null && BrushHooks.IntersectRayMesh(ray, mesh, meshObject.localToWorldMatrix, out _))
                    return true;
            }
            return false;
        }

        [Test]
        public void AddressedCutAffectsOnlyItsTargetEvenBeforeItInTheHierarchy()
        {
            var target = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one * 2f, Quaternion.identity, snap: false);
            BrushApi.Create(BrushShape.Box, new Vector3(0f, 0f, 3f), Vector3.one * 2f, Quaternion.identity, snap: false);
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3(0f, 0f, 1.5f),
                new Vector3(1f, 1f, 5f), Quaternion.identity, snap: false);
            cutter.transform.SetAsFirstSibling();
            BrushCutTarget.FromBrush(target).Assign(cutter);

            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            var model = BrushCsg.ModelOf(target);
            var between = new Vector3(0f, 0f, 1.5f);
            Assert.That(Visible(model, new Ray(between, Vector3.back)), Is.False);
            Assert.That(Visible(model, new Ray(between, Vector3.forward)), Is.True);
            Assert.That(Solid(Vector3.zero), Is.False);
            Assert.That(Solid(new Vector3(0f, 0f, 3f)), Is.True);
            Assert.That(Solid(new Vector3(0.8f, 0f, 0f)), Is.True);
        }

        [Test]
        public void APlanWallHasAnAddressAndItsOwnMeasuredHeight()
        {
            var plan = new GameObject("План").AddComponent<FloorPlan>();
            int start = plan.AddPoint(Vector3.zero);
            int end = plan.AddPoint(Vector3.right * 4f);
            plan.AddWall(start, end, 0.3f);
            var walls = new List<FloorPlan.Wall>();
            plan.Walls(walls);
            Assert.That(BrushPlanDimensionFields.SetWallDimension(plan, walls[0], 2.7f, true), Is.True);
            Assert.That(BrushPlanDimensionFields.SetWallLength(plan, walls[0], 5.2f), Is.True);
            BrushGenerators.Update(plan);
            var generated = plan.generated[0];
            var target = BrushCutTarget.FromBrush(generated);
            Assert.That(target.TryWall(out var wall), Is.True);
            Assert.That(wall.height, Is.EqualTo(2.7f).Within(0.00001f));
            Assert.That(wall.Length, Is.EqualTo(5.2f).Within(0.00001f));
        }

        [Test]
        public void AddressedWallCutLeavesAnotherWallOfTheSamePlanIntact()
        {
            var plan = new GameObject("План").AddComponent<FloorPlan>();
            plan.AddWall(plan.AddPoint(new Vector3(-2f, 0f, 0f)), plan.AddPoint(new Vector3(2f, 0f, 0f)), 0.3f);
            plan.AddWall(plan.AddPoint(new Vector3(-2f, 0f, 2f)), plan.AddPoint(new Vector3(2f, 0f, 2f)), 0.3f);
            BrushGenerators.Update(plan);
            var target = BrushCutTarget.FromBrush(plan.generated[0]);
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 1f),
                new Vector3(1f, 1f, 4f), Quaternion.identity, plan.transform, snap: false);
            target.Assign(cutter);
            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            Assert.That(Solid(new Vector3(0f, 1f, 0f)), Is.False);
            Assert.That(Solid(new Vector3(0f, 1f, 2f)), Is.True);
        }
    }
}
