using CsgBrush.Editor;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// Lit brush meshes get lightmap UVs once an edit is finished, keep them in the saved scene, and keep both when the
    /// scene opens again: a bake stays valid until the brushes are edited.
    /// </summary>
    public class LightmapUVTests
    {
        const string ScenePath = "Assets/__lightmap_uv_test.unity";
        bool m_Auto;

        [SetUp]
        public void Open() { m_Auto = BrushSettings.instance.autoLightmapUVs; BrushSettings.instance.autoLightmapUVs = true; EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }

        [TearDown]
        public void Close()
        {
            BrushSettings.instance.autoLightmapUVs = m_Auto;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(ScenePath);
        }

        static BrushGroup Group() => Object.FindAnyObjectByType<BrushGroup>(FindObjectsInactive.Include);

        static MeshFilter LitMesh() => BrushLightmapUVs.LitMeshes(Group())[0];

        static Brush Room(Transform group = null)
        {
            var floor = BrushApi.Create(BrushShape.Box, new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 8f), Quaternion.identity, group, "Floor");
            BrushApi.Create(BrushShape.Box, new Vector3(0f, 1.5f, 3.8f), new Vector3(8f, 3f, 0.4f), Quaternion.identity, group, "Wall");
            var cut = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 3.8f), new Vector3(1f, 2f, 1f), Quaternion.identity, group, "Door");
            cut.operation = BrushOperation.Subtract;
            BrushApi.ForceUpdate();
            return floor;
        }

        [Test]
        public void AFinishedEditGetsLightmapUVsAndAnotherEditDropsThem()
        {
            var floor = Room();
            Assert.IsFalse(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "not while building");
            BrushLightmapUVs.FlushPending(); // what the editor does once the edit is finished
            Assert.IsTrue(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "made once the edit is finished");
            Assert.AreEqual(0, BrushLightmapUVs.Missing(Group()).Count);
            BrushApi.Move(floor, new Vector3(0f, -0.5f, 1f));
            BrushApi.ForceUpdate();
            Assert.IsFalse(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "an edit drops them");
            BrushLightmapUVs.FlushPending();
            Assert.IsTrue(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "and they are made again");
        }

        [Test]
        public void WithoutAutoTheyAreMadeOnlyWhenAsked()
        {
            BrushSettings.instance.autoLightmapUVs = false;
            Room();
            BrushLightmapUVs.FlushPending();
            Assert.AreEqual(1, BrushLightmapUVs.Missing(Group()).Count, "missing until asked");
            Assert.AreEqual(1, BrushLightmapUVs.Generate(Group()));
            Assert.AreEqual(0, BrushLightmapUVs.Missing(Group()).Count);
        }

        [Test]
        public void OtherUnwrapSettingsMakeTheUVsAgain()
        {
            Room();
            BrushLightmapUVs.FlushPending();
            Group().rendering.lightmapPackMargin = 8f;
            Assert.AreEqual(1, BrushLightmapUVs.Missing(Group()).Count, "made with other settings");
            Assert.AreEqual(1, BrushLightmapUVs.Generate(Group()));
            Assert.AreEqual(0, BrushLightmapUVs.Missing(Group()).Count);
        }

        [Test]
        public void AReopenedSceneKeepsItsMeshesTheirLightmapUVsAndWhichBrushMadeEachTriangle()
        {
            Room();
            BrushLightmapUVs.FlushPending();
            var uv2 = LitMesh().sharedMesh.uv2; var vertices = LitMesh().sharedMesh.vertices;
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); // the editor clears its caches and builds every group
            BrushHooks.EnsureAll();
            BrushApi.ForceUpdate();
            var mesh = LitMesh().sharedMesh;
            Assert.IsTrue(BrushLightmapUVs.HasLightmapUVs(mesh), "the lightmap UVs are still there");
            CollectionAssert.AreEqual(uv2, mesh.uv2, "the same UVs");
            CollectionAssert.AreEqual(vertices, mesh.vertices, "on the same mesh: it was kept, not built again");
            var owners = BrushCsg.TriangleBrushes(LitMesh().transform);
            Assert.IsNotNull(owners, "which brush made each triangle is known");
            Assert.AreEqual(mesh.triangles.Length / 3, owners.Length);
            Assert.IsTrue(System.Array.Exists(owners, b => b != null && b.name == "Wall"), "the wall's triangles are the wall's");

            var floor = GameObject.Find("Floor").GetComponent<Brush>();
            BrushApi.Move(floor, new Vector3(0f, -0.5f, 1f));
            BrushApi.ForceUpdate();
            Assert.IsFalse(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "an edit still builds it again");
        }

        [Test]
        public void RebuildBuildsAKeptMeshAgain()
        {
            Room(new GameObject("Level").AddComponent<BrushGroup>().transform);
            BrushLightmapUVs.FlushPending();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BrushHooks.EnsureAll();
            BrushApi.ForceUpdate();
            Assert.IsTrue(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "kept");
            BrushCsg.Rebuild(Group());
            Assert.IsFalse(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "Rebuild builds it from the brushes");
        }

        static Mesh PlanarMesh(bool sloped = false)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            void Face(Vector3 origin, Vector3 u, Vector3 v, bool reverse = false)
            {
                int i = vertices.Count;
                vertices.AddRange(new[] { origin, origin + u, origin + u + v, origin + v });
                triangles.AddRange(reverse
                    ? new[] { i, i + 2, i + 1, i, i + 3, i + 2 }
                    : new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            Face(Vector3.zero, 5f * Vector3.right, 2f * Vector3.up);
            Face(new Vector3(0f, 0f, 2f), 3f * Vector3.right, Vector3.up, reverse: true);
            Face(new Vector3(6f, 0f, 0f), 2f * Vector3.right, 4f * Vector3.up);
            Face(new Vector3(0f, 5f, 0f), Vector3.left, 3f * Vector3.forward);
            var firstSubmesh = triangles.ToArray();
            triangles.Clear();
            Face(new Vector3(9f, 0f, 0f), 2f * Vector3.up, sloped ? new Vector3(1f, 0f, 2f) : 2f * Vector3.forward);

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(firstSubmesh, 0);
            mesh.SetTriangles(triangles, 1);
            mesh.RecalculateNormals();
            var uv = new Vector2[vertices.Count];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(i * 0.07f, -i * 0.03f);
            mesh.uv = uv;
            return mesh;
        }

        static float SignedUVAreaTwice(Vector2 a, Vector2 b, Vector2 c)
        {
            var ab = b - a;
            var ac = c - a;
            return ab.x * ac.y - ab.y * ac.x;
        }

        [Test]
        public void AxisAlignedChartsKeepOneScaleAndLeaveSpaceBetweenTheirBounds()
        {
            const float margin = 4f / 1024f;
            const float tolerance = 1e-5f;
            var mesh = PlanarMesh();
            try
            {
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                var firstSubmesh = mesh.GetTriangles(0);
                var secondSubmesh = mesh.GetTriangles(1);
                var uv0 = mesh.uv;
                Assert.IsTrue(BrushPlanarLightmapUVs.TryGenerate(mesh, 88f, margin));
                CollectionAssert.AreEqual(vertices, mesh.vertices);
                CollectionAssert.AreEqual(triangles, mesh.triangles, "triangle order must still match the brush source table");
                Assert.AreEqual(2, mesh.subMeshCount);
                CollectionAssert.AreEqual(firstSubmesh, mesh.GetTriangles(0));
                CollectionAssert.AreEqual(secondSubmesh, mesh.GetTriangles(1));
                CollectionAssert.AreEqual(uv0, mesh.uv);

                var uv2 = mesh.uv2;
                Assert.AreEqual(vertices.Length, uv2.Length);
                var charts = new List<Rect>();
                float scale = (uv2[1] - uv2[0]).magnitude / (vertices[1] - vertices[0]).magnitude;
                Assert.Greater(scale, 0f);
                for (int i = 0; i < uv2.Length; i += 4)
                {
                    var min = uv2[i];
                    var max = uv2[i];
                    for (int corner = 0; corner < 4; corner++)
                    {
                        var uv = uv2[i + corner];
                        Assert.That(uv.x, Is.InRange(0f, 1f));
                        Assert.That(uv.y, Is.InRange(0f, 1f));
                        min = Vector2.Min(min, uv);
                        max = Vector2.Max(max, uv);
                        int next = i + (corner + 1) % 4;
                        Assert.That(Vector2.Distance(uv, uv2[next]),
                            Is.EqualTo(Vector3.Distance(vertices[i + corner], vertices[next]) * scale).Within(tolerance));
                    }

                    foreach (var other in charts)
                    {
                        float gapX = Mathf.Max(other.xMin - max.x, min.x - other.xMax);
                        float gapY = Mathf.Max(other.yMin - max.y, min.y - other.yMax);
                        Assert.That(Mathf.Max(gapX, gapY), Is.GreaterThanOrEqualTo(margin - tolerance));
                    }
                    charts.Add(Rect.MinMaxRect(min.x, min.y, max.x, max.y));
                }

                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    float area = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude;
                    float uvArea = SignedUVAreaTwice(uv2[a], uv2[b], uv2[c]);
                    Assert.Greater(uvArea, 0f);
                    Assert.That(uvArea, Is.EqualTo(area * scale * scale).Within(tolerance));
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [TestCase(true, 88f)]
        [TestCase(false, 91f)]
        public void UnsupportedChartsLeaveMeshDataAlone(bool sloped, float hardAngle)
        {
            var mesh = PlanarMesh(sloped);
            try
            {
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                var uv0 = mesh.uv;
                var uv2 = new Vector2[mesh.vertexCount];
                for (int i = 0; i < uv2.Length; i++) uv2[i] = new Vector2(i * 0.04f, 1f - i * 0.04f);
                mesh.uv2 = uv2;

                Assert.IsFalse(BrushPlanarLightmapUVs.TryGenerate(mesh, hardAngle, 4f / 1024f));
                CollectionAssert.AreEqual(vertices, mesh.vertices);
                CollectionAssert.AreEqual(triangles, mesh.triangles);
                CollectionAssert.AreEqual(uv0, mesh.uv);
                CollectionAssert.AreEqual(uv2, mesh.uv2);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
