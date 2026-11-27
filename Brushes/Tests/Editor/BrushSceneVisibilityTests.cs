using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public sealed class BrushSceneVisibilityTests
    {
        static readonly Ray ThroughBothBrushes = new Ray(new Vector3(0.2f, 0.7f, -5f), Vector3.forward);

        bool previousGrid;
        bool previousCuts;

        [SetUp]
        public void OpenScene()
        {
            StageUtility.GoToMainStage();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            previousGrid = BrushSurfaceGrid.Local;
            previousCuts = BrushSettings.instance.showCuts;
            BrushSurfaceGrid.Local = true;
            BrushSettings.instance.showCuts = false;
        }

        [TearDown]
        public void CloseScene()
        {
            BrushSurfaceGrid.Local = previousGrid;
            BrushSettings.instance.showCuts = previousCuts;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HiddenBrushDisappearsFromTheSharedSceneMeshAndLetsTheRayReachTheBackBrush(bool changeParent)
        {
            var (front, back, moving, changed, meshObject) = CreateScene(changeParent);
            var filter = meshObject.GetComponent<MeshFilter>();
            var fullMesh = filter.sharedMesh;
            var fullTriangles = fullMesh.triangles;
            var visibility = SceneVisibilityManager.instance;
            AssertVisibleAndSelectable(front, moving, meshObject, fullMesh);

            try
            {
                visibility.Hide(changed, false);

                var drawn = BrushSceneVisibility.MeshFor(meshObject, picking: false);
                var pickable = BrushSceneVisibility.MeshFor(meshObject, picking: true);
                Assert.That(drawn, Is.Not.SameAs(fullMesh));
                Assert.That(pickable, Is.Not.SameAs(fullMesh));
                AssertRayHits(meshObject, drawn, 4f);
                AssertRayHits(meshObject, pickable, 4f);
                Assert.That(filter.sharedMesh, Is.SameAs(fullMesh));
                Assert.That(fullMesh.triangles, Is.EqualTo(fullTriangles));
                AssertRayHits(meshObject, fullMesh, 0f);
                AssertUnavailable(front, back, moving, meshObject);
            }
            finally { visibility.Show(changed, false); }

            AssertVisibleAndSelectable(front, moving, meshObject, fullMesh);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LockedBrushStaysInTheSharedSceneMeshAndLetsSelectionReachTheBackBrush(bool changeParent)
        {
            var (front, back, moving, changed, meshObject) = CreateScene(changeParent);
            var filter = meshObject.GetComponent<MeshFilter>();
            var fullMesh = filter.sharedMesh;
            var fullTriangles = fullMesh.triangles;
            var visibility = SceneVisibilityManager.instance;
            AssertVisibleAndSelectable(front, moving, meshObject, fullMesh);

            try
            {
                visibility.DisablePicking(changed, false);

                var drawn = BrushSceneVisibility.MeshFor(meshObject, picking: false);
                var pickable = BrushSceneVisibility.MeshFor(meshObject, picking: true);
                AssertRayHits(meshObject, drawn, 0f);
                Assert.That(pickable, Is.Not.SameAs(fullMesh));
                AssertRayHits(meshObject, pickable, 4f);
                Assert.That(filter.sharedMesh, Is.SameAs(fullMesh));
                Assert.That(fullMesh.triangles, Is.EqualTo(fullTriangles));
                AssertUnavailable(front, back, moving, meshObject);
            }
            finally { visibility.EnablePicking(changed, false); }

            AssertVisibleAndSelectable(front, moving, meshObject, fullMesh);
        }

        [Test]
        public void HidingABrushPreservesBakedUVsOnTheRemainingSurfaces()
        {
            var (_, _, _, changed, meshObject) = CreateScene(false);
            var fullMesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
            var positions = fullMesh.vertices;
            var fullUV = System.Array.ConvertAll(positions, BakedUV);
            fullMesh.uv2 = fullUV;
            var visibility = SceneVisibilityManager.instance;

            try
            {
                visibility.Hide(changed, false);
                var preview = BrushSceneVisibility.MeshFor(meshObject, picking: false);
                Assert.That(preview, Is.Not.SameAs(fullMesh));
                Assert.That(preview.uv2, Is.EqualTo(System.Array.ConvertAll(preview.vertices, BakedUV)));
                Assert.That(fullMesh.uv2, Is.EqualTo(fullUV));
            }
            finally { visibility.Show(changed, false); }

            Assert.That(BrushSceneVisibility.MeshFor(meshObject, picking: false), Is.SameAs(fullMesh));

            Vector2 BakedUV(Vector3 point) => new Vector2(0.4f + point.x * 0.03f, 0.4f + point.z * 0.07f);
        }

        [Test]
        public void LightmapTransferKeepsDifferentBakedUVsOnEachSideOfASeam()
        {
            var original = new Mesh();
            var preview = new Mesh();
            try
            {
                var a = Vector3.zero;
                var b = Vector3.right;
                var c = Vector3.right + Vector3.up;
                var d = Vector3.up;
                original.vertices = new[] { a, b, c, a, c, d };
                original.triangles = new[] { 0, 1, 2, 3, 4, 5 };
                original.uv = new Vector2[6];
                original.RecalculateNormals();
                var bakedUV = new[] { new Vector2(0.1f, 0.1f), new Vector2(0.3f, 0.1f), new Vector2(0.3f, 0.3f),
                    new Vector2(0.6f, 0.6f), new Vector2(0.8f, 0.8f), new Vector2(0.6f, 0.8f) };
                original.uv2 = bakedUV;

                preview.vertices = new[] { a, b, c, d };
                preview.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                preview.uv = new Vector2[4];
                preview.RecalculateNormals();

                BrushSceneLightmapUVs.Transfer(original, preview);

                Assert.That(preview.vertexCount, Is.EqualTo(6));
                var previewUV = preview.uv2;
                Assert.That(System.Array.ConvertAll(preview.triangles, index => previewUV[index]), Is.EqualTo(bakedUV));
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(original);
            }
        }

        [Test]
        public void HidingACutterFillsTheSceneViewPocketAndLockingItOnlyRemovesThePocketFromPicking()
        {
            var wall = BrushApi.CreateBox(new Vector3(-2f, 0f, 0f), new Vector3(2f, 2f, 2f), name: "Стена");
            var cutter = BrushApi.CreateBox(new Vector3(-1f, 0.2f, -1f), new Vector3(1f, 1.5f, 1.25f),
                name: "Выемка", operation: BrushOperation.Subtract);
            cutter.transform.SetParent(wall.transform, true);
            BrushCutTarget.FromBrush(wall).Assign(cutter);
            var back = Box("Браш за стеной", 4f);
            var source = new GameObject("Перемещаемый объект").transform;
            BrushApi.ForceUpdate();

            var meshObject = BrushCsg.MeshObject(BrushCsg.ModelOf(wall), false);
            var filter = meshObject.GetComponent<MeshFilter>();
            var fullMesh = filter.sharedMesh;
            var fullTriangles = fullMesh.triangles;
            var visibility = SceneVisibilityManager.instance;
            AssertPocketRestored();

            try
            {
                visibility.Hide(cutter.gameObject, false);
                AssertRayHits(meshObject, BrushSceneVisibility.MeshFor(meshObject, picking: false), 0f);
                AssertRayHits(meshObject, BrushSceneVisibility.MeshFor(meshObject, picking: true), 0f);
                Assert.That(BrushHooks.BrushUnder(ThroughBothBrushes, meshObject.gameObject), Is.SameAs(wall));
                AssertPlacement(wall, 0f);
                Assert.That(filter.sharedMesh, Is.SameAs(fullMesh));
                Assert.That(fullMesh.triangles, Is.EqualTo(fullTriangles));
                AssertRayHits(meshObject, fullMesh, 1.25f);

                visibility.Show(cutter.gameObject, false);
                AssertPocketRestored();
                visibility.DisablePicking(cutter.gameObject, false);
                AssertRayHits(meshObject, BrushSceneVisibility.MeshFor(meshObject, picking: false), 1.25f);
                // A two-sided mesh ray can hit the inside of the back wall. A Scene click and the
                // construction grid only use front-facing surfaces after the locked face is removed.
                AssertRayHits(meshObject, BrushSceneVisibility.MeshFor(meshObject, picking: true), 2f);
                Assert.That(BrushHooks.BrushUnder(ThroughBothBrushes, meshObject.gameObject), Is.SameAs(back));
                Assert.That(BrushHooks.PickConstructionSurface(ThroughBothBrushes, out var gridPoint, out _), Is.SameAs(back));
                Assert.That(gridPoint.z, Is.EqualTo(4f).Within(0.0001f));
                AssertPlacement(back, 4f);
            }
            finally
            {
                visibility.EnablePicking(cutter.gameObject, false);
                visibility.Show(cutter.gameObject, false);
            }

            AssertPocketRestored();

            void AssertPocketRestored()
            {
                AssertRayHits(meshObject, BrushSceneVisibility.MeshFor(meshObject, picking: false), 1.25f);
                AssertRayHits(meshObject, BrushSceneVisibility.MeshFor(meshObject, picking: true), 1.25f);
                Assert.That(BrushHooks.BrushUnder(ThroughBothBrushes, meshObject.gameObject), Is.SameAs(cutter));
                AssertPlacement(wall, 1.25f);
            }

            void AssertPlacement(Brush expected, float frontZ)
            {
                var placement = new PlacementScene(source);
                Assert.That(placement.TryRaycast(ThroughBothBrushes, out var hit), Is.True);
                Assert.That(hit.Part.Owner, Is.SameAs(expected));
                Assert.That(hit.Position.z, Is.EqualTo(frontZ).Within(0.0001f));
            }
        }

        static (Brush front, Brush back, Brush moving, GameObject changed, Transform meshObject) CreateScene(bool changeParent)
        {
            var parent = new GameObject("Родитель ближнего браша");
            var front = Box("Ближний браш", 0f);
            front.transform.SetParent(parent.transform, true);
            var back = Box("Дальний браш", 4f);
            var moving = BrushApi.CreateBox(new Vector3(8f, 0f, 0f), new Vector3(9f, 1f, 1f), name: "Перемещаемый браш");
            BrushApi.ForceUpdate();

            var meshObject = BrushCsg.MeshObject(BrushCsg.ModelOf(front), false);
            var triangleOwners = BrushCsg.TriangleBrushes(meshObject);
            Assert.That(triangleOwners, Does.Contain(front));
            Assert.That(triangleOwners, Does.Contain(back));
            BrushSurfaceGrid.ForSurface(front, ThroughBothBrushes.GetPoint(5f), Vector3.back).Store(moving);
            return (front, back, moving, changeParent ? parent : front.gameObject, meshObject);
        }

        static Brush Box(string name, float frontZ) => BrushApi.CreateBox(new Vector3(-2f, 0f, frontZ),
            new Vector3(2f, 2f, frontZ + 1f), name: name);

        static void AssertVisibleAndSelectable(Brush front, Brush moving, Transform meshObject, Mesh fullMesh)
        {
            Assert.That(BrushSceneVisibility.MeshFor(meshObject, picking: false), Is.SameAs(fullMesh));
            Assert.That(BrushSceneVisibility.MeshFor(meshObject, picking: true), Is.SameAs(fullMesh));
            AssertSelectionAndPlacement(front, moving, meshObject, 0f);
            Assert.That(ObjectPlacement.CanMove(front.transform), Is.True);
            Assert.That(ObjectPlacement.CanTarget(moving.transform, front.transform), Is.True);
            Assert.That(BrushSurfaceGrid.TryMoveFrame(moving, out var frame), Is.True);
            Assert.That(frame.Owner, Is.SameAs(front.transform));
        }

        static void AssertUnavailable(Brush front, Brush back, Brush moving, Transform meshObject)
        {
            AssertSelectionAndPlacement(back, moving, meshObject, 4f);
            Assert.That(ObjectPlacement.CanMove(front.transform), Is.False);
            Assert.That(ObjectPlacement.CanTarget(moving.transform, front.transform), Is.False);
            Assert.That(BrushSurfaceGrid.TryMoveFrame(moving, out _), Is.False);
            using var serialized = new SerializedObject(moving);
            Assert.That(serialized.FindProperty("workGridOwner").objectReferenceValue, Is.SameAs(front.transform));
        }

        static void AssertSelectionAndPlacement(Brush expected, Brush moving, Transform meshObject, float frontZ)
        {
            Assert.That(BrushHooks.BrushUnder(ThroughBothBrushes, meshObject.gameObject), Is.SameAs(expected));
            Assert.That(BrushHooks.BrushUnder(ThroughBothBrushes, null), Is.SameAs(expected));
            Assert.That(BrushHooks.PickConstructionSurface(ThroughBothBrushes, out var point, out var normal),
                Is.SameAs(expected));
            Assert.That(point.z, Is.EqualTo(frontZ).Within(0.0001f));
            Assert.That(Vector3.Dot(normal, Vector3.back), Is.GreaterThan(0.9999f));

            var placement = new PlacementScene(moving.transform);
            Assert.That(placement.TryRaycast(ThroughBothBrushes, out var hit), Is.True);
            Assert.That(hit.Part.Owner, Is.SameAs(expected));
            Assert.That(hit.Position.z, Is.EqualTo(frontZ).Within(0.0001f));
        }

        static void AssertRayHits(Transform meshObject, Mesh mesh, float frontZ)
        {
            Assert.That(mesh, Is.Not.Null);
            Assert.That(BrushHooks.IntersectRayMesh(ThroughBothBrushes, mesh, meshObject.localToWorldMatrix, out var hit), Is.True);
            Assert.That(hit.point.z, Is.EqualTo(frontZ).Within(0.0001f));
        }
    }
}
