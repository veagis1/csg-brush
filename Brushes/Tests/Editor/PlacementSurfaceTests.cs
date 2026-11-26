using System.Linq;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public sealed class PlacementSurfaceTests
    {
        [SetUp]
        public void EmptyScene()
        {
            StageUtility.GoToMainStage();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void CloseScene() => EmptyScene();

        [Test]
        public void AuthoredBoxExposesFaceCentersAndGuidesToTheActualEdges()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(4f, 2f, 6f), Quaternion.identity,
                snap: false);
            var geometry = PlacementGeometry.FromBrush(brush);
            Assert.That(geometry.Faces.Length, Is.EqualTo(6));
            var face = geometry.Faces.Single(candidate => candidate.Normal == Vector3.up);
            Near(new Vector3(0f, 1f, 0f), face.Center);
            Assert.That(face.Edges.Length, Is.EqualTo(4));
            foreach (var edge in face.Edges)
                Near((geometry.Vertices[edge.x] + geometry.Vertices[edge.y]) * 0.5f, face.GuideEnd(geometry.Vertices, edge));
        }

        [Test]
        public void TriangulationFanLeavesTheCenterAvailableAsAFaceCenter()
        {
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[]
                {
                    new Vector3(-2f, -1f), new Vector3(2f, -1f), new Vector3(2f, 1f), new Vector3(-2f, 1f),
                    Vector3.zero
                };
                mesh.triangles = new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4 };
                var geometry = PlacementGeometry.FromMesh(mesh);
                var face = geometry.Faces.Single();
                int centerIndex = System.Array.IndexOf(geometry.Vertices, Vector3.zero);

                Assert.That(geometry.IsFeatureVertex(centerIndex), Is.False);
                Assert.That(face.Edges.Length, Is.EqualTo(4));
                Near(Vector3.zero, face.Center);
                foreach (var edge in face.Edges)
                {
                    Assert.That(geometry.IsFeatureVertex(edge.x), Is.True);
                    var guide = face.GuideEnd(geometry.Vertices, edge);
                    var direction = geometry.Vertices[edge.y] - geometry.Vertices[edge.x];
                    Assert.That(Vector3.Dot(guide - face.Center, direction), Is.EqualTo(0f).Within(1e-5f));
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void WeldedCoplanarTrianglesRespectAnOpeningAndClipTheGuideAtItsBoundary()
        {
            var mesh = new Mesh();
            try
            {
                var corners = new[]
                {
                    new Vector3(-2f, -2f), new Vector3(2f, -2f), new Vector3(2f, 2f), new Vector3(-2f, 2f)
                };
                var vertices = new Vector3[16];
                var triangles = new int[24];
                for (int i = 0; i < 4; i++)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % 4];
                    int v = 4 * i, t = 6 * i;
                    vertices[v] = a;
                    vertices[v + 1] = b;
                    vertices[v + 2] = b * 0.5f;
                    vertices[v + 3] = a * 0.5f;
                    triangles[t] = v;
                    triangles[t + 1] = v + 1;
                    triangles[t + 2] = v + 2;
                    triangles[t + 3] = v;
                    triangles[t + 4] = v + 2;
                    triangles[t + 5] = v + 3;
                }
                mesh.vertices = vertices;
                mesh.triangles = triangles;

                var geometry = PlacementGeometry.FromMesh(mesh);
                var face = geometry.Faces.Single();
                Assert.That(face.Edges.Length, Is.EqualTo(8));
                Assert.That(face.Contains(face.Center), Is.True);
                Assert.That(face.Contains(Vector3.zero), Is.False);
                Assert.That(face.Intersect(Vector3.forward, Vector3.back, 2f, out _, out _), Is.False);
                var opposite = face.Edges.Single(edge => geometry.Vertices[edge.x].y == 2f && geometry.Vertices[edge.y].y == 2f);
                var end = face.GuideEnd(geometry.Vertices, opposite);
                Assert.That(end.y, Is.EqualTo(-1f).Within(1e-5f));
                Assert.That(face.Contains(end), Is.True);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void SurfaceRayChoosesNearestPartOutsideMovingHierarchyAndHonorsLayerMask()
        {
            var source = new GameObject("Перемещаемый объект").transform;
            var child = Box("Дочерняя геометрия", Vector3.zero);
            child.transform.SetParent(source);
            var obstacle = Box("Ближнее препятствие", Vector3.forward * 2f);
            obstacle.gameObject.layer = 8;
            var target = Box("Дальняя цель", Vector3.forward * 5f);
            var scene = new PlacementScene(source);
            var ray = new Ray(new Vector3(0.2f, 0.3f, -3f), Vector3.forward);

            Assert.That(scene.TryRaycast(ray, out var hit), Is.True);
            Assert.That(hit.Part.Owner, Is.SameAs(obstacle));
            Near(new Vector3(0.2f, 0.3f, 1f), hit.Position);
            Near(Vector3.back, hit.Normal);
            Assert.That(scene.TryRaycast(ray, out hit, ~(1 << obstacle.gameObject.layer)), Is.True);
            Assert.That(hit.Part.Owner, Is.SameAs(target));
            Near(new Vector3(0.2f, 0.3f, 4f), hit.Position);
        }

        [Test]
        public void SurfaceRayIgnoresBackFaces()
        {
            var source = new GameObject("Перемещаемый объект").transform;
            var surface = new GameObject("Обратная сторона");
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { new Vector3(-2f, -2f), new Vector3(2f, -2f), new Vector3(0f, 2f) };
                mesh.triangles = new[] { 0, 1, 2 };
                surface.AddComponent<MeshFilter>().sharedMesh = mesh;
                var target = Box("Цель за обратной стороной", Vector3.forward * 4f);
                var scene = new PlacementScene(source);

                Assert.That(scene.TryRaycast(new Ray(Vector3.back * 3f, Vector3.forward), out var hit), Is.True);
                Assert.That(hit.Part.Owner, Is.SameAs(target));
                Assert.That(scene.TryRaycast(new Ray(Vector3.forward, Vector3.back), out hit), Is.True);
                Assert.That(hit.Part.Owner, Is.EqualTo(surface.GetComponent<MeshFilter>()));
                Near(Vector3.forward, hit.Normal);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void SurfaceRayPassesThroughARebuiltCsgOpening()
        {
            var source = new GameObject("Перемещаемый объект").transform;
            var wall = BrushApi.CreateBox(new Vector3(-3f, 0f, 0f), new Vector3(3f, 3f, 1f), name: "Стена");
            BrushApi.CreateBox(new Vector3(-0.5f, 0f, -1f), new Vector3(0.5f, 2f, 2f),
                name: "Проём", operation: BrushOperation.Subtract);
            var target = Box("Цель за проёмом", new Vector3(0f, 1f, 4f));
            bool previousCuts = BrushSettings.instance.showCuts;
            try
            {
                BrushSettings.instance.showCuts = false;
                BrushApi.ForceUpdate();
                var scene = new PlacementScene(source);

                Assert.That(scene.TryRaycast(new Ray(new Vector3(0f, 1f, -4f), Vector3.forward), out var hit), Is.True);
                Assert.That(hit.Part.Owner, Is.EqualTo(target));
                Assert.That(scene.TryRaycast(new Ray(new Vector3(2f, 1f, -4f), Vector3.forward), out hit), Is.True);
                Assert.That(hit.Part.Owner, Is.EqualTo(wall));
                Near(new Vector3(2f, 1f, 0f), hit.Position);
            }
            finally { BrushSettings.instance.showCuts = previousCuts; }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RebuildingCsgAtContactDoesNotLetTheMovingSolidCoverItsTarget(bool independentOpening)
        {
            var wall = BrushApi.CreateBox(new Vector3(-3f, 0f, 0f), new Vector3(3f, 3f, 1f), name: "Стена");
            var source = BrushApi.CreateBox(new Vector3(-1f, 0f, -2f), new Vector3(1f, 2f, -1f), name: "Стержень");
            var targetBehind = Box("Цель за проёмом", new Vector3(0f, 1f, 4f));
            if (independentOpening)
                BrushApi.CreateBox(new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 2f, 2f),
                    name: "Независимый проём", operation: BrushOperation.Subtract);
            bool previousCuts = BrushSettings.instance.showCuts;
            try
            {
                BrushSettings.instance.showCuts = false;
                BrushApi.ForceUpdate();
                var scene = new PlacementScene(source.transform);
                source.transform.position += Vector3.forward;
                BrushSnap.Accept(source);
                BrushSync.NotifyTransformChanged(source);
                BrushApi.ForceUpdate();

                var nearContact = new Ray(new Vector3(0f, 1f, -0.01f), Vector3.forward);
                var generated = BrushCsg.MeshObjects(BrushCsg.ModelOf(wall));
                Assert.That(generated, Is.Not.Empty);
                Assert.That(generated.Any(meshObject =>
                {
                    var mesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
                    return BrushHooks.IntersectRayMesh(nearContact, mesh, meshObject.localToWorldMatrix, out var meshHit) &&
                        meshHit.distance < 0.02f;
                }), Is.False, "В сгенерированном меше отсутствует прежняя поверхность в точке касания.");
                var ray = new Ray(new Vector3(0f, 1f, -4f), Vector3.forward);
                Assert.That(scene.TryRaycast(ray, out var hit), Is.True);
                Assert.That(hit.Part.Owner, Is.EqualTo(independentOpening ? (Component)targetBehind : wall));
                Near(new Vector3(0f, 1f, independentOpening ? 3f : 0f), hit.Position);
            }
            finally { BrushSettings.instance.showCuts = previousCuts; }
        }

        [Test]
        public void MirroredBoxColliderReturnsItsOutwardWorldNormal()
        {
            var source = new GameObject("Перемещаемый объект").transform;
            var box = Box("Отражённая цель", new Vector3(3f, 7f, 2f));
            box.transform.localScale = new Vector3(-2f, 3f, 0.5f);
            box.transform.rotation = Quaternion.Euler(20f, 31f, 9f);
            var expectedPoint = box.transform.TransformPoint(new Vector3(1f, 0.2f, 0.3f));
            var expectedNormal = -box.transform.right;
            var ray = new Ray(expectedPoint + expectedNormal * 5f, -expectedNormal);

            var scene = new PlacementScene(source);
            Assert.That(scene.TryRaycast(ray, out var hit), Is.True);
            Assert.That(hit.Part.Owner, Is.SameAs(box));
            Near(expectedPoint, hit.Position);
            Near(expectedNormal, hit.Normal);
            Near(Vector3.right, hit.Face.Normal);
        }

        static BoxCollider Box(string name, Vector3 position)
        {
            var box = new GameObject(name).AddComponent<BoxCollider>();
            box.size = Vector3.one * 2f;
            box.transform.position = position;
            return box;
        }

        static void Near(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-5f), $"{expected} != {actual}");
    }
}
