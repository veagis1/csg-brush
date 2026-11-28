using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>Brushes that touch nothing do not affect each other: a group is built per island of touching brushes, and only changed islands again.</summary>
    public class BrushIslandTests
    {
        [SetUp]
        public void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        static Mesh GroupMesh(Brush any) => BrushCsg.MeshObject(BrushCsg.ModelOf(any), false).GetComponent<MeshFilter>().sharedMesh;

        [Test]
        public void MovingABrushThatTouchesNothingReusesItsIsland()
        {
            for (int i = 0; i < 5; i++) BrushApi.Create(BrushShape.Box, new Vector3(i * 4f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            var loner = BrushApi.Create(BrushShape.Box, new Vector3(0f, 0f, 20f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Assert.AreEqual(6, BrushCsg.LastBuiltIslands + BrushCsg.LastCachedIslands, "six separate islands");
            int triangles = GroupMesh(loner).triangles.Length;
            BrushApi.Move(loner, new Vector3(4f, 0f, 20f));
            BrushApi.ForceUpdate();
            Assert.AreEqual(0, BrushCsg.LastBuiltIslands);
            Assert.AreEqual(6, BrushCsg.LastCachedIslands);
            var mesh = GroupMesh(loner);
            Assert.AreEqual(triangles, mesh.triangles.Length, "the mesh still holds every island");
            var child = BrushCsg.MeshObject(BrushCsg.ModelOf(loner), false);
            var ray = new Ray(loner.transform.position + Vector3.up * 5f, Vector3.down);
            Assert.IsTrue(BrushHooks.IntersectRayMesh(ray, mesh, child.localToWorldMatrix, out var hit));
            Assert.AreSame(loner, BrushCsg.TriangleBrushes(child)[hit.triangleIndex]);
        }

        [Test]
        public void TouchingBrushesAreOneIslandWithoutInnerFaces()
        {
            var a = BrushApi.Create(BrushShape.Box, new Vector3(-1f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.Create(BrushShape.Box, new Vector3(1f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity); // shares the face at x = 0
            BrushApi.ForceUpdate();
            Assert.AreEqual(1, BrushCsg.LastBuiltIslands + BrushCsg.LastCachedIslands, "touching: one island");
            var mesh = GroupMesh(a); var v = mesh.vertices;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var c = (v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f;
                    Assert.Greater(Mathf.Abs(c.x), 1e-3f, "no face left between the two boxes");
                }
            }
        }

        [Test]
        public void AMaterialChangeReachesAnUnchangedIsland()
        {
            var a = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.Create(BrushShape.Box, new Vector3(10f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            var red = new Material(BrushCsg.DefaultMaterial()) { name = "red" };
            try
            {
                a.material = red; BrushSync.Ensure(a);
                BrushApi.ForceUpdate();
                CollectionAssert.Contains(BrushCsg.MeshObject(BrushCsg.ModelOf(a), false).GetComponent<MeshRenderer>().sharedMaterials, red);
            }
            finally { Object.DestroyImmediate(red); }
        }

        [Test]
        public void ACutSpanningTwoIslandsCarvesBoth()
        {
            var a = BrushApi.Create(BrushShape.Box, new Vector3(-2f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.Create(BrushShape.Box, new Vector3(2f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            int before = GroupMesh(a).triangles.Length;
            var cut = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 0f), new Vector3(6f, 1f, 1f), Quaternion.identity);
            BrushApi.SetOperation(cut, BrushOperation.Subtract);
            BrushApi.ForceUpdate();
            Assert.AreEqual(2, BrushCsg.LastBuiltIslands, "both boxes are cut, each still its own island");
            Assert.Greater(GroupMesh(a).triangles.Length, before, "both carry the notch");
        }

        [Test]
        public void RotatingABrushThatTouchesNothingReplacesOnlyItsColliders()
        {
            for (int i = 0; i < 5; i++) BrushApi.Create(BrushShape.Box, new Vector3(i * 4f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            var loner = BrushApi.Create(BrushShape.Box, new Vector3(0f, 0f, 20f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            BrushApi.Rotate(loner, Quaternion.Euler(0f, 30f, 0f));
            BrushApi.ForceUpdate();
            Assert.AreEqual(5, CsgBrush.Colliders.Editor.ConvexColliderBuilder.LastReusedPieces, "the others keep their pieces");
            Assert.AreEqual(1, CsgBrush.Colliders.Editor.ConvexColliderBuilder.LastCreatedPieces);
            Assert.AreEqual(1, CsgBrush.Colliders.Editor.ConvexColliderBuilder.LastDestroyedPieces);
        }

        [Test]
        public void AnotherPhysicsMaterialWithTheSameNameStillReachesThePieces()
        {
            var box = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            var first = new PhysicsMaterial("Ground"); var second = new PhysicsMaterial("Ground");
            try
            {
                box.physicsMaterial = first; BrushSync.Ensure(box); BrushApi.ForceUpdate();
                box.physicsMaterial = second; BrushSync.Ensure(box); BrushApi.ForceUpdate();
                var collider = BrushCsg.ModelOf(box).GetComponentInChildren<Collider>(true);
                Assert.AreSame(second, collider.sharedMaterial);
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }
    }
}
