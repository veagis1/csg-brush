using CsgBrush.Colliders.Editor;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// A large island is built in cells and an edit builds again only the cells it reaches; a brush's colliders are built
    /// again only where its parts or the cuts reaching them changed.
    /// </summary>
    public class IncrementalBuildTests
    {
        int m_CellMinPieces;

        [SetUp]
        public void NewScene() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); m_CellMinPieces = BrushCsg.CellMinPieces; }

        [TearDown]
        public void Restore() => BrushCsg.CellMinPieces = m_CellMinPieces;

        static float Area(Brush any)
        {
            var mesh = BrushCsg.MeshObject(BrushCsg.ModelOf(any), false).GetComponent<MeshFilter>().sharedMesh;
            var v = mesh.vertices; float area = 0f;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3) area += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * 0.5f;
            }
            return area;
        }

        /// <summary>A winding chain of overlapping boxes, 40 m long, with a few cuts: one island of many pieces.</summary>
        static Brush[] Chain()
        {
            var boxes = new Brush[20];
            for (int i = 0; i < boxes.Length; i++)
                boxes[i] = BrushApi.Create(BrushShape.Box, new Vector3(i * 2f, Mathf.Sin(i * 0.7f), Mathf.Cos(i * 0.4f) * 3f), new Vector3(3f, 2f, 3f), Quaternion.Euler(0f, i * 13f, 0f));
            for (int i = 0; i < 4; i++)
            {
                var cut = BrushApi.Create(BrushShape.Box, new Vector3(5f + i * 9f, 0.5f, Mathf.Cos((2.5f + i * 4.5f) * 0.4f) * 3f), new Vector3(1.5f, 1.5f, 6f), Quaternion.identity);
                cut.operation = BrushOperation.Subtract;
            }
            BrushApi.ForceUpdate();
            return boxes;
        }

        [Test]
        public void ALargeIslandBuiltInCellsHasTheSameSurfaceAsBuiltWhole()
        {
            var boxes = Chain();
            BrushCsg.ClearCaches(); BrushCsg.RebuildAllNow();
            Assert.Greater(BrushCsg.LastBuiltIslands, 1, "built in cells");
            float cells = Area(boxes[0]);
            BrushCsg.CellMinPieces = int.MaxValue;
            BrushCsg.ClearCaches(); BrushCsg.RebuildAllNow();
            Assert.AreEqual(1, BrushCsg.LastBuiltIslands, "built whole");
            float whole = Area(boxes[0]);
            Assert.AreEqual(whole, cells, whole * 1e-4f, "the same surface: the cells' own walls are left out, nothing else");
        }

        [Test]
        public void AnEditToALargeIslandBuildsAgainOnlyTheCellsItReaches()
        {
            var boxes = Chain();
            BrushApi.Move(boxes[boxes.Length - 1], boxes[boxes.Length - 1].transform.position + new Vector3(0f, 0.5f, 0f));
            BrushApi.ForceUpdate();
            Assert.Greater(BrushCsg.LastBuiltIslands, 0, "the cells round the moved box");
            Assert.Greater(BrushCsg.LastCachedIslands, BrushCsg.LastBuiltIslands, "most cells kept");
        }

        [Test]
        public void DraggingASplineToTheHorizonBuildsTheIslandWholeAndReturnsToCells()
        {
            Chain();
            var spline = new GameObject("Spline").AddComponent<Spline>();
            GameObjectUtility.SetStaticEditorFlags(spline.gameObject, BrushSettings.instance.defaultModelStaticFlags);
            spline.profile = Spline.Profile.Rectangle;
            spline.size = new Vector2(1f, 0.5f);
            var near = new Vector3(10f, 0f, 3f);
            int start = spline.AddPoint(new Vector3(0f, 0f, 3f));
            int end = spline.AddPoint(near);
            spline.AddLink(start, end);
            BrushGenerators.Update(spline, true);
            BrushApi.ForceUpdate();
            Assert.That(BrushCsg.LastBuiltIslands + BrushCsg.LastCachedIslands, Is.GreaterThan(1));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            spline.points[1] = new Vector3(100000f, 0f, 30000f);
            BrushGenerators.Update(spline, false);
            BrushApi.ForceUpdate();
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(10000));
            Assert.That(BrushCsg.Busy || BrushCsg.HasDirty, Is.False);
            Assert.That(BrushCsg.LastBuiltIslands, Is.EqualTo(1));
            Assert.That(BrushCsg.LastCachedIslands, Is.Zero);

            spline.points[1] = near;
            BrushGenerators.Update(spline, false);
            BrushApi.ForceUpdate();
            Assert.That(BrushCsg.LastBuiltIslands + BrushCsg.LastCachedIslands, Is.GreaterThan(1));
        }

        [Test]
        public void MovingADistantBrushUpdatesItsRenderMeshAndTriangleSources()
        {
            var start = new Vector3(1210099f, 88.75f, 2624193f);
            var size = new Vector3(317.875f, 177.625f, 332f);
            var independentPosition = new Vector3(10f, 2f, 7f);
            var independentSize = new Vector3(2f, 4f, 6f);
            var moved = BrushApi.Create(BrushShape.Box, start, size, Quaternion.identity, snap: false);
            var independent = BrushApi.Create(BrushShape.Box, independentPosition, independentSize, Quaternion.identity, snap: false);
            var material = Object.Instantiate(AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat"));

            try
            {
                BrushApi.SetMaterial(moved, material);
                var positions = new[] { start, start + Vector3.right * 8.125f, start + Vector3.forward * 17.25f, start };
                foreach (var position in positions)
                {
                    moved.transform.position = position;
                    BrushApi.ForceUpdate();

                    var meshObject = BrushCsg.MeshObject(BrushCsg.ModelOf(moved), false);
                    Assert.IsNotNull(meshObject);
                    var mesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
                    var sources = BrushCsg.TriangleBrushes(meshObject);
                    var materials = meshObject.GetComponent<MeshRenderer>().sharedMaterials;
                    Assert.IsNotNull(sources);
                    Assert.AreEqual(mesh.triangles.Length / 3, sources.Length);

                    var movedBounds = new Bounds(position, Vector3.zero);
                    var independentBounds = new Bounds(independentPosition, Vector3.zero);
                    var vertices = mesh.vertices;
                    int triangle = 0, movedTriangles = 0, independentTriangles = 0;
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    {
                        var indices = mesh.GetTriangles(submesh);
                        for (int i = 0; i < indices.Length; i += 3, triangle++)
                        {
                            bool isMoved = sources[triangle] == moved;
                            Assert.AreSame(isMoved ? moved : independent, sources[triangle]);
                            Assert.AreEqual(isMoved, materials[submesh] == material);
                            for (int corner = 0; corner < 3; corner++)
                            {
                                var vertex = meshObject.TransformPoint(vertices[indices[i + corner]]);
                                if (isMoved) movedBounds.Encapsulate(vertex);
                                else independentBounds.Encapsulate(vertex);
                            }
                            if (isMoved) movedTriangles++;
                            else independentTriangles++;
                        }
                    }

                    Assert.AreEqual(12, movedTriangles);
                    Assert.AreEqual(12, independentTriangles);
                    Assert.That(Vector3.Distance(position, movedBounds.center), Is.LessThanOrEqualTo(0.25f), $"render mesh center at {position:F3} m");
                    Assert.That(Vector3.Distance(size, movedBounds.size), Is.LessThanOrEqualTo(0.5f), $"render mesh size at {position:F3} m");
                    Assert.AreEqual(independentPosition, independentBounds.center);
                    Assert.AreEqual(independentSize, independentBounds.size);
                }
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ACutNearOneEndOfALongBrushBuildsAgainOnlyTheCollidersThere()
        {
            // a long Rectangle run through Smooth points: one brush of many convex parts, upright so its parts stay put
            var s = new GameObject("Spline").AddComponent<Spline>();
            s.profile = Spline.Profile.Rectangle; s.size = new Vector2(2f, 1f); s.orientation = Spline.Orientation.Upright;
            int last = -1;
            for (int i = 0; i <= 6; i++) { int id = s.AddPoint(new Vector3(i * 5f, 0f, (i % 2) * 3f)); if (last >= 0) s.AddLink(last, id); s.SetSmooth(id, i > 0 && i < 6); last = id; }
            BrushGenerators.Update(s, true);
            var cut = BrushApi.Create(BrushShape.Box, new Vector3(1f, 0f, 0f), new Vector3(1f, 3f, 3f), Quaternion.identity);
            cut.operation = BrushOperation.Subtract;
            BrushApi.ForceUpdate();
            BrushApi.Move(cut, new Vector3(1.5f, 0f, 0f));
            BrushApi.ForceUpdate();
            Assert.Greater(ConvexColliderBuilder.LastReusedPieces, 0, "the run's colliders far from the cut are kept");
            Assert.Less(ConvexColliderBuilder.LastCreatedPieces, ConvexColliderBuilder.LastReusedPieces, "only those near it are made again");
        }
    }
}
