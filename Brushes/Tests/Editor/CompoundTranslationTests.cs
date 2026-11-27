using System.Collections.Generic;
using CsgBrush.Colliders.Editor;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public class CompoundTranslationTests
    {
        bool m_Snap;
        int m_CellMinPieces;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            m_Snap = BrushSettings.instance.snapToGrid;
            m_CellMinPieces = BrushCsg.CellMinPieces;
            BrushSettings.instance.snapToGrid = false;
        }

        [TearDown]
        public void TearDown()
        {
            BrushSettings.instance.snapToGrid = m_Snap;
            BrushCsg.CellMinPieces = m_CellMinPieces;
        }

        static Brush Create(BrushShape shape, Vector3 position, Vector3 size, Transform parent = null) =>
            BrushApi.Create(shape, position, size, Quaternion.identity, parent, snap: false);

        static bool HasCollider(Vector3 point) => Physics.OverlapSphere(point, 0.02f, ~0, QueryTriggerInteraction.Ignore).Length > 0;

        static Brush FaceAt(BrushGroup group, Vector3 point)
        {
            var ray = new Ray(point + Vector3.up * 5f, Vector3.down);
            Brush source = null;
            float distance = float.PositiveInfinity;
            foreach (var child in BrushCsg.MeshObjects(group))
            {
                var mesh = child.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || !BrushHooks.IntersectRayMesh(ray, mesh, child.localToWorldMatrix, out var hit)) continue;
                float next = (ray.origin - hit.point).sqrMagnitude;
                if (next >= distance) continue;
                distance = next;
                source = BrushCsg.TriangleBrushes(child)[hit.triangleIndex];
            }
            return source;
        }

        static float Volume(BrushGroup group)
        {
            double volume = 0;
            foreach (var child in BrushCsg.MeshObjects(group))
            {
                var mesh = child.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;
                var vertices = mesh.vertices;
                var origin = vertices[0];
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var triangles = mesh.GetTriangles(s);
                    for (int i = 0; i < triangles.Length; i += 3)
                        volume += Vector3.Dot(vertices[triangles[i]] - origin,
                            Vector3.Cross(vertices[triangles[i + 1]] - origin, vertices[triangles[i + 2]] - origin));
                }
            }
            return (float)(System.Math.Abs(volume) / 6.0);
        }

        static HashSet<Brush> Sources(BrushGroup group)
        {
            var sources = new HashSet<Brush>();
            foreach (var child in BrushCsg.MeshObjects(group))
            {
                var mesh = child.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;
                var mapping = BrushCsg.TriangleBrushes(child);
                Assert.That(mapping.Length, Is.EqualTo(mesh.triangles.Length / 3));
                foreach (var source in mapping) Assert.That(source, Is.Not.Null);
                sources.UnionWith(mapping);
            }
            return sources;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TranslatedCompoundKeepsItsLocalCutsAndRecomputesExternalCuts(bool cells)
        {
            if (cells) BrushCsg.CellMinPieces = 2;
            const float initialCutX = -3.12f;
            const float distantX = 400f;
            var parent = Create(BrushShape.Box, Vector3.zero, new Vector3(12f, 2f, 8f));
            var addressed = Create(BrushShape.Cylinder, Vector3.right * initialCutX, new Vector3(1.5f, 4f, 1.5f), parent.transform);
            BrushCutTarget.FromBrush(parent).Assign(addressed);
            var nested = Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 4f, 2f), parent.transform);
            BrushApi.SetOperation(nested, BrushOperation.Subtract);
            var insert = Create(BrushShape.Box, Vector3.zero, new Vector3(1f, 3f, 1f), parent.transform);
            insert.transform.SetAsFirstSibling();
            var external = Create(BrushShape.Box, Vector3.right * 22f, new Vector3(1f, 4f, 1f));
            BrushApi.SetOperation(external, BrushOperation.Subtract);
            var group = BrushCsg.ModelOf(parent);
            BrushApi.ForceUpdate();
            if (cells) Assert.That(BrushCsg.LastBuiltIslands, Is.GreaterThan(1));

            void Verify(float x, float cutX, bool hasExternalCut)
            {
                Physics.SyncTransforms();
                Vector3 position = Vector3.right * x;
                Vector3 opening = position + Vector3.right * cutX;
                Vector3 overlap = position + Vector3.right * 2f;
                Assert.That(FaceAt(group, opening), Is.Null);
                Assert.That(HasCollider(opening), Is.False);
                Assert.That(FaceAt(group, position), Is.SameAs(insert));
                Assert.That(HasCollider(position), Is.True);
                Assert.That(HasCollider(overlap), Is.EqualTo(!hasExternalCut));
                if (hasExternalCut) Assert.That(FaceAt(group, overlap), Is.Null);
                else Assert.That(FaceAt(group, overlap), Is.SameAs(parent));
                var sources = Sources(group);
                Assert.That(sources, Does.Contain(addressed));
                Assert.That(sources, Does.Contain(nested));
                Assert.That(sources.Contains(external), Is.EqualTo(hasExternalCut));
            }

            float original = Volume(group);
            parent.transform.position = Vector3.right * distantX;
            BrushApi.ForceUpdate();
            Assert.That(BrushCsg.LastBuiltSolids, Is.Zero);
            Assert.That(BrushCsg.LastBuiltPieces, Is.Zero);
            Assert.That(BrushCsg.LastBuiltIslands, Is.Zero);
            Assert.That(ConvexColliderBuilder.LastCreatedPieces, Is.Zero);
            Assert.That(ConvexColliderBuilder.LastReusedPieces, Is.GreaterThan(0));
            Verify(distantX, initialCutX, false);
            Assert.That(Volume(group), Is.EqualTo(original).Within(0.001f));

            foreach (float x in new[] { 20f, 40f, 20f, 0f })
            {
                parent.transform.position = Vector3.right * x;
                BrushApi.ForceUpdate();
                Verify(x, initialCutX, x == 20f);
                Assert.That(Volume(group), Is.EqualTo(original - (x == 20f ? 2f : 0f)).Within(0.001f));
            }

            addressed.transform.localPosition = Vector3.left * 4.5f;
            external.transform.SetAsFirstSibling();
            parent.transform.position = Vector3.right * 20f;
            BrushApi.ForceUpdate();
            Verify(20f, -4.5f, false);
            var previousOpening = Vector3.right * (20f + initialCutX);
            Assert.That(FaceAt(group, previousOpening), Is.SameAs(parent));
            Assert.That(HasCollider(previousOpening), Is.True);

            BrushCutTarget.FromBrush(parent).Assign(external);
            BrushApi.ForceUpdate();
            Verify(20f, -4.5f, true);
            float warmVolume = Volume(group);
            var warmSources = Sources(group);

            BrushCsg.ClearCaches();
            ConvexColliderBuilder.Forget(group.transform);
            BrushApi.ForceUpdate();
            Verify(20f, -4.5f, true);
            Assert.That(Volume(group), Is.EqualTo(warmVolume).Within(0.001f));
            Assert.That(Sources(group), Is.EquivalentTo(warmSources));
        }

        [Test]
        public void AFolderCutterFollowsHierarchyOrderAcrossACompoundTranslation()
        {
            var folder = new GameObject("Сборка").transform;
            var member = Create(BrushShape.Box, Vector3.zero, new Vector3(6f, 2f, 6f), folder);
            var cut = Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 4f, 2f), folder);
            BrushApi.SetOperation(cut, BrushOperation.Subtract);
            Vector3 overlap = Vector3.right * 10f;
            var outside = Create(BrushShape.Box, overlap, new Vector3(3f, 3f, 3f));
            outside.transform.SetAsFirstSibling();
            var group = BrushCsg.ModelOf(member);
            BrushApi.ForceUpdate();
            Assert.That(FaceAt(group, overlap), Is.SameAs(outside));

            folder.position = overlap;
            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            Assert.That(FaceAt(group, overlap), Is.Null);
            Assert.That(HasCollider(overlap), Is.False);

            outside.transform.SetAsLastSibling();
            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            Assert.That(FaceAt(group, overlap), Is.SameAs(outside));
            Assert.That(HasCollider(overlap), Is.True);
            float warmVolume = Volume(group);

            BrushCsg.ClearCaches();
            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            Assert.That(FaceAt(group, overlap), Is.SameAs(outside));
            Assert.That(HasCollider(overlap), Is.True);
            Assert.That(Volume(group), Is.EqualTo(warmVolume).Within(0.001f));
        }

        [Test]
        public void ReorderingIdenticalCuttersUpdatesTheSourceOfTheirCutFaces()
        {
            var parent = Create(BrushShape.Box, Vector3.zero, new Vector3(6f, 2f, 6f));
            var first = Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 4f, 2f));
            var second = Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 4f, 2f));
            BrushApi.SetOperation(first, BrushOperation.Subtract);
            BrushApi.SetOperation(second, BrushOperation.Subtract);
            first.transform.SetAsFirstSibling();
            var group = BrushCsg.ModelOf(parent);
            BrushApi.ForceUpdate();
            Assert.That(Sources(group), Is.EquivalentTo(new[] { parent, second }));
            float volume = Volume(group);

            second.transform.SetAsFirstSibling();
            first.transform.SetAsLastSibling();
            BrushApi.ForceUpdate();
            Assert.That(Sources(group), Is.EquivalentTo(new[] { parent, first }));
            Assert.That(Volume(group), Is.EqualTo(volume).Within(0.001f));
        }
    }
}
