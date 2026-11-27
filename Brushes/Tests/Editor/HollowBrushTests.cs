using System.Collections.Generic;
using System.Linq;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public class HollowBrushTests
    {
        int m_CellMinPieces;
        Material m_Material;

        [SetUp]
        public void Open()
        {
            m_CellMinPieces = BrushCsg.CellMinPieces;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            m_Material = Object.Instantiate(AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat"));
            m_Material.name = "Hollow brush test material";
        }

        [TearDown]
        public void Close()
        {
            BrushCsg.CellMinPieces = m_CellMinPieces;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (m_Material != null) Object.DestroyImmediate(m_Material);
        }

        static Brush Box(Vector3 position, Vector3 size, Transform parent = null) =>
            BrushApi.Create(BrushShape.Box, position, size, Quaternion.identity, parent, snap: false);

        [Test]
        public void ATranslatedHollowBrushKeepsMaterialAndOwnersOnBothWalls([Values(false, true)] bool cells)
        {
            BrushCsg.CellMinPieces = cells ? 1 : int.MaxValue;
            var box = Box(Vector3.zero, new Vector3(14f, 6f, 6f));
            Box(Vector3.right * 7f, Vector3.one, box.transform);
            BrushApi.SetHollow(box, true, 0.5f);
            BrushApi.SetMaterial(box, m_Material);
            BrushApi.SetStaticFlags(box, StaticEditorFlags.OccluderStatic);
            BrushCsg.RebuildAllNow();
            var model = BrushCsg.ModelOf(box);
            if (cells) Assert.That(BrushCsg.LastBuiltIslands, Is.GreaterThan(1));
            else Assert.That(BrushCsg.LastBuiltIslands, Is.EqualTo(1));
            Verify(Vector3.zero);

            var moved = Vector3.right * 40f;
            BrushApi.Move(box, moved);
            BrushApi.ForceUpdate();
            Assert.That(BrushCsg.LastBuiltSolids, Is.Zero);
            Assert.That(BrushCsg.LastCachedIslands, Is.GreaterThan(0));
            Verify(moved);

            void Verify(Vector3 centre)
            {
                centre += new Vector3(0.1f, 0.2f, 0f);
                Assert.That(OwnerAt(model, new Ray(centre + Vector3.back * 8f, Vector3.forward)), Is.SameAs(box));
                Assert.That(OwnerAt(model, new Ray(centre, Vector3.forward)), Is.SameAs(box));
                AssertMaterial(model, box, StaticEditorFlags.OccluderStatic, m_Material);
            }
        }

        [Test]
        public void ASubtractingHollowBrushOwnsBothSidesOfItsCut([Values(false, true)] bool cells)
        {
            BrushCsg.CellMinPieces = cells ? 1 : int.MaxValue;
            var receiver = Box(Vector3.zero, Vector3.one * 12f);
            var cutter = Box(Vector3.zero, Vector3.one * 4f, receiver.transform);
            BrushApi.SetOperation(cutter, BrushOperation.Subtract);
            BrushApi.SetHollow(cutter, true, 0.5f);
            BrushApi.SetMaterial(cutter, m_Material);
            BrushApi.SetCutFaces(cutter, CutFaces.CutterMaterial);
            BrushApi.SetStaticFlags(receiver, StaticEditorFlags.OccluderStatic);
            BrushCsg.RebuildAllNow();

            var model = BrushCsg.ModelOf(receiver);
            var gap = new Vector3(0.1f, 0.2f, 1.75f);
            Assert.That(OwnerAt(model, new Ray(gap, Vector3.forward)), Is.SameAs(cutter));
            Assert.That(OwnerAt(model, new Ray(gap, Vector3.back)), Is.SameAs(cutter));
            AssertMaterial(model, cutter, StaticEditorFlags.OccluderStatic, m_Material);
        }

        [Test]
        public void ReopeningAnOlderHollowMeshRebuildsOnlyTheAffectedGroupOnce()
        {
            var box = Box(Vector3.zero, Vector3.one * 4f);
            BrushApi.SetHollow(box, true, 0.5f);
            var model = BrushCsg.ModelOf(box);
            var unaffected = new GameObject("Unaffected group").AddComponent<BrushGroup>();
            Box(Vector3.right * 8f, Vector3.one, unaffected.transform);
            BrushCsg.RebuildAllNow();

            int currentKey = model.builtKey;
            model.builtKey ^= BrushCsg.HollowSourceMeshKeyBit;
            foreach (var child in BrushCsg.MeshObjects(model))
            {
                var saved = child.GetComponent<BrushMeshSources>();
                saved.runBrushes = new Brush[saved.runBrushes.Length];
            }

            var written = new List<BrushGroup>();
            BrushCsg.MeshesWritten += written.Add;
            try
            {
                BrushCsg.ClearCachesKeepingSavedMeshes();
                BrushCsg.RebuildAllNow();
                Assert.That(written, Is.EqualTo(new[] { model }));
                Assert.That(model.builtKey, Is.EqualTo(currentKey));
                Assert.That(BrushCsg.MeshObjects(model).SelectMany(child => child.GetComponent<BrushMeshSources>().runBrushes),
                    Is.All.SameAs(box));

                written.Clear();
                BrushCsg.ClearCachesKeepingSavedMeshes();
                BrushCsg.RebuildAllNow();
                Assert.That(written, Is.Empty);
            }
            finally { BrushCsg.MeshesWritten -= written.Add; }
        }

        static Brush OwnerAt(BrushGroup model, Ray ray)
        {
            Brush closest = null;
            float distance = float.PositiveInfinity;
            foreach (var child in BrushCsg.MeshObjects(model))
            {
                var mesh = child.GetComponent<MeshFilter>().sharedMesh;
                if (!BrushHooks.IntersectRayMesh(ray, mesh, child.localToWorldMatrix, out var hit)) continue;
                float next = (hit.point - ray.origin).sqrMagnitude;
                if (next >= distance) continue;
                distance = next;
                closest = BrushCsg.TriangleBrushes(child)[hit.triangleIndex];
            }
            return closest;
        }

        static void AssertMaterial(BrushGroup model, Brush brush, StaticEditorFlags flags, Material material)
        {
            int owned = 0;
            foreach (var child in BrushCsg.MeshObjects(model))
            {
                var mesh = child.GetComponent<MeshFilter>().sharedMesh;
                var materials = child.GetComponent<MeshRenderer>().sharedMaterials;
                var owners = BrushCsg.TriangleBrushes(child);
                int triangle = 0;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    int end = triangle + (int)mesh.GetIndexCount(submesh) / 3;
                    for (; triangle < end; triangle++)
                    {
                        bool matchesBrush = owners[triangle] == brush;
                        Assert.That(materials[submesh] == material, Is.EqualTo(matchesBrush));
                        if (!matchesBrush) continue;
                        Assert.That(GameObjectUtility.GetStaticEditorFlags(child.gameObject), Is.EqualTo(flags));
                        owned++;
                    }
                }
            }
            Assert.That(owned, Is.GreaterThan(12));
        }
    }
}
