using System.Linq;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public sealed class ObjectEdgePlacementTests
    {
        [SetUp]
        public void OpenEmptyScene()
        {
            StageUtility.GoToMainStage();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void CloseTestScene()
        {
            StageUtility.GoToMainStage();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TestCase(0.68f, 0.75f, 3.6f)]
        [TestCase(0.22f, 0.75f, 1.35f)]
        [TestCase(-0.60f, 0.75f, 0.60f)]
        [TestCase(1.30f, 0.75f, 4.35f)]
        [TestCase(0.42f, 0.75f, 2.10f)]
        [TestCase(0.42f, 4f, 2.10f)]
        [TestCase(0.1f, 4f, 2.10f)]
        [TestCase(0.31f, 0f, 1.55f)]
        [TestCase(-0.60f, 0f, 0f)]
        [TestCase(1.30f, 0f, 5f)]
        public void PositionAlongAnObliqueFiniteEdgeUsesWorldMetresFromTheAnchor(float cursor, float step, float distance)
        {
            var a = new Vector3(3.2f, -0.8f, 7.1f);
            var b = a + new Vector3(3f, 4f, 0f);
            var result = ObjectPlacement.PointOnEdge(a, b, 0.42f, cursor, step);
            Near(a + new Vector3(0.6f, 0.8f, 0f) * distance, result);
        }

        [Test]
        public void AShortOrCollapsedEdgeNeverExtendsPastItsEndpoints()
        {
            var start = new Vector3(1f, 2f, 3f);
            var end = start + Vector3.right * 0.08f;
            Near(Vector3.Lerp(start, end, 0.25f), ObjectPlacement.PointOnEdge(start, end, 0.25f, 1f, 0.5f));
            Near(start, ObjectPlacement.PointOnEdge(start, start, 0.25f, 1f, 0.5f));
        }

        [TestCase(false, 4)]
        [TestCase(true, 5)]
        public void MeshTopologyRemovesACoplanarTriangulationSeamAndRetainsAFold(bool fold, int edgeCount)
        {
            var surface = new GameObject("Независимая поверхность");
            var filter = surface.AddComponent<MeshFilter>();
            var mesh = new Mesh { name = "Две грани с раздельными вершинами шва" };
            try
            {
                var a = Vector3.zero;
                var b = Vector3.right * 2f;
                var c = b + Vector3.forward;
                var d = Vector3.forward + (fold ? Vector3.up : Vector3.zero);
                mesh.vertices = new[] { a, b, c, a, c, d };
                mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
                filter.sharedMesh = mesh;

                var geometry = PlacementGeometry.FromMesh(filter.sharedMesh);
                Assert.That(geometry, Is.Not.Null);
                Assert.That(geometry.Edges.Length, Is.EqualTo(edgeCount));
                Assert.That(HasEdge(geometry, a, b), Is.True);
                Assert.That(HasEdge(geometry, b, c), Is.True);
                Assert.That(HasEdge(geometry, c, d), Is.True);
                Assert.That(HasEdge(geometry, d, a), Is.True);
                Assert.That(HasEdge(geometry, a, c), Is.EqualTo(fold));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void BrushTopologyUsesAuthoredEdgesWithTheActualPivotShift()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one * 2f, Quaternion.identity, snap: false);
            BrushApi.SetPivot(brush, new Vector3(0f, 0.5f, 0.5f));
            var geometry = PlacementGeometry.FromBrush(brush);
            Assert.That(geometry, Is.Not.Null);
            Assert.That(geometry.Vertices.Length, Is.EqualTo(8));
            Assert.That(geometry.Edges.Length, Is.EqualTo(12));
            var a = new Vector3(0f, -1f, -1f);
            Assert.That(HasEdge(geometry, a, new Vector3(2f, -1f, -1f)), Is.True);
            Assert.That(HasEdge(geometry, a, new Vector3(2f, -1f, 1f)), Is.False);
        }

        [Test]
        public void SourceAndDescendantsAreExcludedButAnIndependentAncestorMeshCanBeTargeted()
        {
            var ancestor = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            var descendant = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            var separateTarget = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            source.SetParent(ancestor, false);
            descendant.SetParent(source, false);

            Assert.That(ObjectPlacement.CanMove(source), Is.True);
            Assert.That(ObjectPlacement.CanTarget(source, source), Is.False);
            Assert.That(ObjectPlacement.CanTarget(source, descendant), Is.False);
            Assert.That(ObjectPlacement.CanTarget(source, ancestor), Is.True);
            Assert.That(ObjectPlacement.CanTarget(source, separateTarget), Is.True);
        }

        [Test]
        public void PrefabIsolationRejectsSourceAndTargetFromTheMainStage()
        {
            var main = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var prefabPath = AssetDatabase.GenerateUniqueAssetPath("Assets/__ObjectEdgePlacementStage.prefab");
            try
            {
                var root = new GameObject("Группа в префабе");
                var geometry = GameObject.CreatePrimitive(PrimitiveType.Cube);
                geometry.transform.SetParent(root.transform, false);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);
                var stage = PrefabStageUtility.OpenPrefab(prefabPath);
                Assert.That(stage, Is.Not.Null);
                var stageSource = stage.prefabContentsRoot.transform;
                var stageTarget = stageSource.GetComponentInChildren<MeshFilter>().transform;

                Assert.That(ObjectPlacement.CanMove(stageSource), Is.True);
                Assert.That(ObjectPlacement.CanMove(main.transform), Is.False);
                Assert.That(ObjectPlacement.CanTarget(stageTarget, main.transform), Is.False);
            }
            finally
            {
                StageUtility.GoToMainStage();
                AssetDatabase.DeleteAsset(prefabPath);
            }
        }

        [Test]
        public void HiddenLockedAndGeneratorOwnedSourcesCannotMove()
        {
            var ordinary = new GameObject("Обычная пустая группа");
            var visibility = SceneVisibilityManager.instance;
            Assert.That(ObjectPlacement.CanMove(ordinary.transform), Is.True);
            try
            {
                visibility.Hide(ordinary, false);
                Assert.That(ObjectPlacement.CanMove(ordinary.transform), Is.False);
                visibility.Show(ordinary, false);
                visibility.DisablePicking(ordinary, false);
                Assert.That(ObjectPlacement.CanMove(ordinary.transform), Is.False);
            }
            finally
            {
                visibility.Show(ordinary, false);
                visibility.EnablePicking(ordinary, false);
            }
            var plan = new GameObject("План").AddComponent<FloorPlan>();
            var generated = new GameObject("Созданный стеной браш").AddComponent<Brush>();
            generated.transform.SetParent(plan.transform, false);
            generated.generatedBy = plan;
            Assert.That(ObjectPlacement.CanMove(generated.transform), Is.False);
        }

        [Test]
        public void HiddenCutsLeaveDoorsAndWindowsAvailableAsTargets()
        {
            var settings = BrushSettings.instance;
            bool previousShowCuts = settings.showCuts;
            int previousLayers = Tools.visibleLayers;
            try
            {
                const int layer = 0;
                Tools.visibleLayers |= 1 << layer;
                var source = new GameObject("Источник переноса").transform;
                var camera = new GameObject("Камера выбора").AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << layer;
                var cut = BrushApi.Create(BrushShape.Box, Vector3.forward * 2f, Vector3.one, Quaternion.identity, snap: false);
                var door = BrushApi.Create(BrushShape.Door, Vector3.forward * 4f, Vector3.one, Quaternion.identity, snap: false);
                var window = BrushApi.Create(BrushShape.Window, Vector3.forward * 6f, Vector3.one, Quaternion.identity, snap: false);
                cut.operation = BrushOperation.Subtract;
                cut.gameObject.layer = door.gameObject.layer = window.gameObject.layer = layer;
                var placement = new PlacementScene(source);
                var cutPart = new PlacementScene.Part(cut, new Bounds(Vector3.zero, cut.ClampedSize));
                var doorPart = new PlacementScene.Part(door, new Bounds(Vector3.zero, door.ClampedSize));
                var windowPart = new PlacementScene.Part(window, new Bounds(Vector3.zero, window.ClampedSize));

                settings.showCuts = true;
                Assert.That(placement.Available(camera, cutPart), Is.True);
                settings.showCuts = false;
                Assert.That(placement.Available(camera, cutPart), Is.False);
                Assert.That(placement.Available(camera, doorPart), Is.True);
                Assert.That(placement.Available(camera, windowPart), Is.True);
            }
            finally
            {
                settings.showCuts = previousShowCuts;
                Tools.visibleLayers = previousLayers;
            }
        }

        [Test]
        public void ZeroDisplacementLeavesThePreviousUndoActionOnTop()
        {
            var previous = new GameObject("Предыдущая операция").transform;
            var source = new GameObject("Группа для переноса").transform;
            var start = new Vector3(1.25f, 2.6f, 3.45f);
            source.position = start;
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(previous, "Изменить предыдущий объект");
            previous.position = Vector3.up;
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();

            Assert.That(ObjectPlacement.Commit(source, start), Is.False);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Near(Vector3.zero, previous.position);
            Near(start, source.position);
        }

        [TestCase(GridSnap.Shape)]
        [TestCase(GridSnap.Pivot)]
        public void NestedBrushMovesItsExistingPivotOnceWithoutChangingShapeOrGridMode(GridSnap mode)
        {
            var settings = BrushSettings.instance;
            bool originalSnap = settings.snapToGrid;
            var originalSteps = settings.gridSizes;
            int originalIndex = settings.gridIndex;
            try
            {
                settings.gridSizes = new[] { 0.5f };
                settings.gridIndex = 0;
                settings.snapToGrid = false;
                var parent = new GameObject("Масштабированный родитель").transform;
                parent.SetPositionAndRotation(new Vector3(3f, 2f, 1f), Quaternion.Euler(12f, 31f, 0f));
                parent.localScale = new Vector3(2f, 1.3f, 0.8f);
                var rotation = Quaternion.Euler(10f, 22f, 7f);
                var brush = BrushApi.Create(BrushShape.Box, new Vector3(1f, 2f, 3f), new Vector3(2.37f, 1.15f, 1.77f), rotation,
                    parent, snap: false);
                BrushApi.SetPivot(brush, new Vector3(0.25f, 0.15f, 0.85f));
                brush.gridSnap = mode;
                var child = new GameObject("Дочерний объект").transform;
                child.SetParent(brush.transform, false);
                child.localPosition = new Vector3(0.1f, 0.2f, -0.35f);
                Undo.FlushUndoRecordObjects();
                BrushSnap.Accept(brush);
                brush.transform.hasChanged = false;
                settings.snapToGrid = true;

                var start = brush.transform.position;
                var target = new Vector3(8.13f, 1.26f, -2.45f);
                var delta = target - start;
                var childStart = child.position;
                var childLocal = child.localPosition;
                var localScale = brush.transform.localScale;
                var size = brush.size;
                var pivot = brush.pivot;
                var vertices = WorldVertices(brush);
                Undo.IncrementCurrentGroup();

                Assert.That(ObjectPlacement.Commit(brush.transform, target), Is.True);
                Undo.FlushUndoRecordObjects();
                BrushApi.ForceUpdate();
                AssertPositionAndVertices(target, vertices.Select(v => v + delta).ToArray(), brush);
                Near(childStart + delta, child.position);
                Near(childLocal, child.localPosition);
                Near(localScale, brush.transform.localScale);
                Near(size, brush.size);
                Near(pivot, brush.pivot);
                Assert.That(brush.gridSnap, Is.EqualTo(mode));
                Assert.That(Quaternion.Angle(rotation, brush.transform.rotation), Is.LessThan(0.01f));
                Assert.That(ObjectPlacement.Commit(brush.transform, target), Is.False);

                Undo.PerformUndo();
                BrushApi.ForceUpdate();
                AssertPositionAndVertices(start, vertices, brush);
                Near(childStart, child.position);
                Undo.PerformRedo();
                BrushApi.ForceUpdate();
                AssertPositionAndVertices(target, vertices.Select(v => v + delta).ToArray(), brush);
            }
            finally
            {
                settings.snapToGrid = originalSnap;
                settings.gridSizes = originalSteps;
                settings.gridIndex = originalIndex;
            }
        }

        [Test]
        public void MovingAnOrdinaryGroupKeepsItsBrushChildAtTheSameLocalPose()
        {
            var settings = BrushSettings.instance;
            bool originalSnap = settings.snapToGrid;
            try
            {
                settings.snapToGrid = false;
                float step = settings.GridMeters;
                var root = new GameObject("Группа с брашем").transform;
                root.SetPositionAndRotation(new Vector3(3f, 6f, 9f), Quaternion.Euler(0f, 27f, 0f));
                var brush = BrushApi.Create(BrushShape.Box, root.position + Vector3.one, Vector3.one * (step * 2f),
                    root.rotation, root, snap: false);
                brush.gridSnap = GridSnap.Shape;
                Undo.FlushUndoRecordObjects();
                BrushSnap.Accept(brush);
                brush.transform.hasChanged = false;
                settings.snapToGrid = true;

                var sourceStart = root.position;
                var childStart = brush.transform.position;
                var childLocal = brush.transform.localPosition;
                var delta = new Vector3(0.37f, 0.51f, -0.23f) * step;
                var vertices = WorldVertices(brush);
                Undo.IncrementCurrentGroup();
                Assert.That(ObjectPlacement.Commit(root, sourceStart + delta), Is.True);
                Undo.FlushUndoRecordObjects();
                BrushApi.ForceUpdate();
                Near(sourceStart + delta, root.position);
                AssertPositionAndVertices(childStart + delta, vertices.Select(v => v + delta).ToArray(), brush);
                Near(childLocal, brush.transform.localPosition);
                Assert.That(brush.gridSnap, Is.EqualTo(GridSnap.Shape));

                Undo.PerformUndo();
                BrushApi.ForceUpdate();
                Near(sourceStart, root.position);
                AssertPositionAndVertices(childStart, vertices, brush);
            }
            finally { settings.snapToGrid = originalSnap; }
        }

        static bool HasEdge(PlacementGeometry geometry, Vector3 a, Vector3 b) =>
            geometry.Edges.Any(edge => (geometry.Vertices[edge.x] == a && geometry.Vertices[edge.y] == b) ||
                (geometry.Vertices[edge.x] == b && geometry.Vertices[edge.y] == a));

        static Vector3[] WorldVertices(Brush brush) =>
            BrushGeometry.Polyhedron(brush).vertices.Select(brush.transform.TransformPoint).ToArray();

        static void AssertPositionAndVertices(Vector3 position, Vector3[] vertices, Brush brush)
        {
            Near(position, brush.transform.position);
            var actual = WorldVertices(brush);
            Assert.That(actual.Length, Is.EqualTo(vertices.Length));
            for (int i = 0; i < vertices.Length; i++) Near(vertices[i], actual[i]);
        }

        static void Near(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.0001f),
                $"Ожидалось {expected:F5} м, получено {actual:F5} м.");
    }
}
