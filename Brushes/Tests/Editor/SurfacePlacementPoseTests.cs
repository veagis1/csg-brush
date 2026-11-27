using System.Linq;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public sealed class SurfacePlacementPoseTests
    {
        static readonly Vector3 Contact = new Vector3(3.15f, -0.85f, 2.15f);
        static readonly Vector3 Normal = new Vector3(-0.4f, 0.65f, 0.3f).normalized;
        static readonly Quaternion InitialRotation = Quaternion.Euler(25f, 30f, -40f);
        const float Tolerance = 0.0001f;

        [SetUp]
        [TearDown]
        public void ResetScene()
        {
            StageUtility.GoToMainStage();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void BrushSurfacesRestOnTheTargetPlaneWithAnOffsetWorkingOrigin([Values] BrushShape shape)
        {
            var brush = BrushApi.Create(shape == BrushShape.Custom ? BrushShape.Wedge : shape,
                Vector3.one, new Vector3(2.1f, 1.8f, 3.25f), InitialRotation, snap: false);
            if (shape == BrushShape.Custom) BrushApi.ConvertToCustom(brush);
            var t = brush.transform;
            BrushApi.SetDrawingPivot(brush, t.TransformPoint(new Vector3(0.25f, 0.4f, -0.35f)));
            var vertices = BrushHooks.LocalShape(brush, out _).vertices;
            var scene = new PlacementScene(t);

            ApplyAndCheck(scene, t, () => vertices.Select(t.TransformPoint).Min(Height));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AGeneratorRestsOnTheLowestVertexOfItsGeneratedGeometry(bool spline)
        {
            var root = new GameObject("Генератор");
            PointGraph graph = spline ? (PointGraph)root.AddComponent<Spline>() : root.AddComponent<FloorPlan>();
            int start = graph.AddPoint(new Vector3(0f, -1.3f, 0f));
            int end = graph.AddPoint(new Vector3(4f, spline ? 1.2f : 0f, 2.2f));
            graph.AddLink(start, end);
            root.transform.rotation = InitialRotation;
            BrushGenerators.Update(graph, true);
            Assert.That(GeneratorWcs.SetOrigin(graph, new Vector3(0.42f, 0.33f, -0.62f)), Is.True);
            BrushGenerators.Update(graph, true);
            Assert.That(graph.generated, Is.Not.Empty);

            ApplyAndCheck(new PlacementScene(root.transform), root.transform, () => graph.generated
                .SelectMany(brush => BrushHooks.LocalShape(brush, out _).vertices.Select(brush.transform.TransformPoint))
                .Min(Height));
        }

        [Test]
        public void CarvedBlockRestsOnItsAuthoredBaseRegardlessOfCutterPicking([Values] bool lockCutter)
        {
            var block = BrushApi.CreateBox(new Vector3(-4f, 3f, -1f), new Vector3(-1f, 5f, 1f));
            var cutter = BrushApi.CreateBox(new Vector3(-3.15f, 1f, -1.5f), new Vector3(-1.85f, 4f, 0.2f),
                parent: block.transform, operation: BrushOperation.Subtract);
            var floor = new GameObject("Пол").AddComponent<BoxCollider>();
            floor.center = Vector3.down * 0.5f;
            floor.size = new Vector3(20f, 1f, 20f);
            BrushApi.ForceUpdate();
            var model = BrushCsg.ModelOf(block);
            float LowestMaterial() => BrushCsg.MeshObjects(model)
                .SelectMany(t => t.GetComponent<MeshFilter>().sharedMesh.vertices.Select(t.TransformPoint)).Min(v => v.y);

            float AuthoredBase() => BrushHooks.LocalShape(block, out _).vertices.Select(block.transform.TransformPoint).Min(v => v.y);
            Assert.That(AuthoredBase(), Is.EqualTo(3f).Within(Tolerance));
            Assert.That(LowestMaterial(), Is.EqualTo(3f).Within(Tolerance));
            if (lockCutter) SceneVisibilityManager.instance.DisablePicking(cutter.gameObject, false);
            try
            {
                var scene = new PlacementScene(block.transform);
                Assert.That(scene.TryRaycast(new Ray(new Vector3(5f, 8f, 0f), Vector3.down), out var hit), Is.True);
                Assert.That(hit.Part.Owner, Is.SameAs(floor));
                var pose = scene.SurfacePose(hit.Position, hit.Normal, block.transform.rotation);
                Assert.That(ObjectPlacement.CommitSurface(block.transform, pose), Is.True);
                BrushApi.ForceUpdate();
                Assert.That(AuthoredBase(), Is.EqualTo(0f).Within(Tolerance));
                Assert.That(LowestMaterial(), Is.EqualTo(0f).Within(Tolerance));
            }
            finally
            {
                if (lockCutter) SceneVisibilityManager.instance.EnablePicking(cutter.gameObject, false);
            }
        }

        [TestCase(-1, -1)]
        [TestCase(0, -1)]
        [TestCase(1, -1)]
        [TestCase(2, -1)]
        [TestCase(0, 2)]
        [TestCase(0, 0)]
        public void AChildMeshRestsOnThePlaneUnderARotatedNonuniformParent(int reflectedAxis, int grandparentReflection)
        {
            var parent = new GameObject("Масштабированный родитель").transform;
            if (grandparentReflection >= 0)
            {
                var grandparent = new GameObject("Второй уровень иерархии").transform;
                grandparent.SetPositionAndRotation(new Vector3(1f, -2.3f, 0.5f), Quaternion.Euler(31f, 17f, 42f));
                var grandparentScale = new Vector3(1.2f, 2.1f, 0.75f);
                grandparentScale[grandparentReflection] *= -1f;
                grandparent.localScale = grandparentScale;
                parent.SetParent(grandparent, false);
            }
            parent.SetPositionAndRotation(new Vector3(-4f, 2f, 0.3f), Quaternion.Euler(12f, -60f, 14f));
            var parentScale = new Vector3(1.5f, 0.85f, 2.3f);
            if (reflectedAxis >= 0) parentScale[reflectedAxis] *= -1f;
            parent.localScale = parentScale;
            var root = new GameObject("Переносимая фигура").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(0.13f, -0.39f, 0.19f);
            root.rotation = InitialRotation;
            root.localScale = new Vector3(0.6f, 1.7f, 1.1f);
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(child.GetComponent<BoxCollider>());
            child.transform.SetParent(root, false);
            child.transform.localPosition = new Vector3(0.3f, -1.1f, 0.7f);
            child.transform.localRotation = Quaternion.Euler(10f, 7f, 35f);
            child.transform.localScale = new Vector3(1.4f, 0.8f, 2.2f);
            var vertices = child.GetComponent<MeshFilter>().sharedMesh.vertices;
            var scaleBefore = root.localScale;
            var initialLocalPosition = root.localPosition;
            var initialLocalRotation = root.localRotation;
            Assert.That(root.localToWorldMatrix.determinant < 0f, Is.EqualTo((reflectedAxis >= 0) ^ (grandparentReflection >= 0)));

            ApplyAndCheck(new PlacementScene(root), root, () => vertices
                .Select(child.transform.TransformPoint).Min(Height));
            Assert.That(root.localScale, Is.EqualTo(scaleBefore));
            Assert.That(root.parent, Is.SameAs(parent));
            ObjectPlacement.RestoreSurfacePreview(root, initialLocalPosition, initialLocalRotation);
            Assert.That(root.localPosition, Is.EqualTo(initialLocalPosition));
            Assert.That(Quaternion.Angle(root.localRotation, initialLocalRotation), Is.LessThan(0.05f));
        }

        [Test]
        public void AConfirmedSurfacePoseHasOneUndoStepAndSurvivesGridSnapping()
        {
            var settings = BrushSettings.instance;
            bool previousSnap = settings.snapToGrid;
            try
            {
                settings.snapToGrid = true;
                var brush = BrushApi.Create(BrushShape.Wedge, Vector3.one, Vector3.one * 2f, InitialRotation, snap: false);
                brush.gridSnap = GridSnap.Shape;
                var source = brush.transform;
                Undo.FlushUndoRecordObjects();
                var original = new Pose(source.position, source.rotation);
                var pose = new PlacementScene(source).SurfacePose(Contact, Normal, original.rotation);

                Assert.That(ObjectPlacement.ApplySurfacePreview(source, pose), Is.True);
                AssertPose(source, pose);
                Assert.That(ObjectPlacement.ApplySurfacePreview(source, original), Is.True);
                AssertPose(source, original);

                Undo.IncrementCurrentGroup();
                Assert.That(ObjectPlacement.CommitSurface(source, pose), Is.True);
                Undo.FlushUndoRecordObjects();
                BrushSnap.ProcessChanged();
                AssertPose(source, pose);
                Assert.That(ObjectPlacement.CommitSurface(source, pose), Is.False);
                Undo.PerformUndo();
                AssertPose(source, original);
                Undo.PerformRedo();
                AssertPose(source, pose);
            }
            finally
            {
                settings.snapToGrid = previousSnap;
            }
        }

        [Test]
        public void AParallelForwardUsesTheOriginalRightAxisForHeading()
        {
            var source = new GameObject("Параллельное направление").transform;
            var pose = new PlacementScene(source).SurfacePose(Contact, Vector3.forward, Quaternion.identity);
            Assert.That(Vector3.Distance(pose.rotation * Vector3.up, Vector3.forward), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(pose.rotation * Vector3.right, Vector3.right), Is.LessThan(Tolerance));
            Assert.That(pose.position, Is.EqualTo(Contact));
        }

        static void ApplyAndCheck(PlacementScene scene, Transform source, System.Func<float> lowestPoint)
        {
            var pose = scene.SurfacePose(Contact, Normal, source.rotation);
            Assert.That(Vector3.Distance(pose.rotation * Vector3.up, Normal), Is.LessThan(Tolerance));
            Assert.That(Vector3.ProjectOnPlane(pose.position - Contact, Normal).magnitude, Is.LessThan(Tolerance));
            Assert.That(ObjectPlacement.ApplySurfacePreview(source, pose), Is.True);
            Assert.That(Vector3.Distance(source.up, Normal), Is.LessThan(Tolerance));
            Assert.That(lowestPoint(), Is.EqualTo(0f).Within(Tolerance));
        }

        static float Height(Vector3 point) => Vector3.Dot(point - Contact, Normal);

        static void AssertPose(Transform source, Pose pose)
        {
            Assert.That(Vector3.Distance(source.position, pose.position), Is.LessThan(Tolerance));
            Assert.That(Quaternion.Angle(source.rotation, pose.rotation), Is.LessThan(0.05f));
        }
    }
}
