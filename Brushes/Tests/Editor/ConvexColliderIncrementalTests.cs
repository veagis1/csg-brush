using System.Collections.Generic;
using System.Linq;
using CsgBrush.Editor;
using CsgBrush.Colliders;
using CsgBrush.Colliders.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// The convex collider builder reuses pieces whose planes did not change. Every scenario checks two things:
    /// the incremental result is identical to a build from scratch, and only the pieces that had to change were touched.
    /// </summary>
    public class ConvexColliderIncrementalTests
    {
        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.ClearAll();
            Undo.IncrementCurrentGroup();
            BrushSettings.instance.snapToGrid = true;
            BrushCsg.ClearCaches();
        }

        static BrushGroup Model() => Object.FindAnyObjectByType<BrushGroup>();

        static Transform Container()
        {
            var model = Model();
            return model != null ? model.transform.Find(ConvexColliderSettings.ContainerName) : null;
        }

        /// <summary>Geometry of every piece, order independent, plus the object ids so reuse can be checked.</summary>
        static List<string> Pieces()
        {
            var list = new List<string>();
            var container = Container();
            if (container == null) return list;
            foreach (Transform child in container)
            {
                string line;
                if (child.TryGetComponent<BoxCollider>(out var bc)) line = "box " + bc.center.ToString("F3") + " " + bc.size.ToString("F3") + (bc.isTrigger ? " trigger" : "");
                else
                {
                    var mc = child.GetComponent<MeshCollider>();
                    Assert.IsNotNull(mc, "piece without a collider: " + child.name);
                    var verts = mc.sharedMesh.vertices; var b = mc.sharedMesh.bounds;
                    line = "convex v=" + verts.Length + " t=" + mc.sharedMesh.triangles.Length + " b=" + b.center.ToString("F3") + "/" + b.size.ToString("F3") + (mc.isTrigger ? " trigger" : "");
                }
                var id = child.GetComponent<ConvexPiece>();
                Assert.IsNotNull(id, "piece without identity: " + child.name);
                line += " fp=" + id.fingerprint + (id.trigger ? " trigger" : "") + " " + id.brushName;
                list.Add(line);
            }
            list.Sort(string.CompareOrdinal);
            return list;
        }

        static HashSet<GameObject> PieceObjects()
        {
            var set = new HashSet<GameObject>();
            var container = Container();
            if (container != null) foreach (Transform child in container) set.Add(child.gameObject);
            return set;
        }

        /// <summary>Destroy every piece and cache and build again; the reference the incremental result must match.</summary>
        static List<string> FromScratch()
        {
            var container = Container();
            if (container != null) Object.DestroyImmediate(container.gameObject);
            BrushCsg.ClearCaches();
            var settings = Model().GetComponent<ConvexColliderSettings>();
            settings.lastGeometryHash = 0;
            BrushApi.ForceUpdate();
            return Pieces();
        }

        static void AssertSameAsFromScratch(string when)
        {
            var incremental = Pieces();
            var scratch = FromScratch();
            Assert.AreEqual(string.Join("\n", scratch), string.Join("\n", incremental), "incremental pieces differ from a build from scratch " + when);
        }

        static List<Brush> Row(int count)
        {
            var list = new List<Brush>();
            for (int i = 0; i < count; i++) list.Add(BrushApi.Create(BrushShape.Box, new Vector3(i * 3f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity, null, "box" + i));
            BrushApi.ForceUpdate();
            return list;
        }

        [Test]
        public void MovingOneBrushOnlyTouchesItsPiece()
        {
            var boxes = Row(10);
            Assert.AreEqual(10, Pieces().Count);
            var before = PieceObjects();

            BrushApi.Move(boxes[3], new Vector3(9f, 3f, 0f));
            BrushApi.ForceUpdate();

            Assert.AreEqual(9, ConvexColliderBuilder.LastReusedPieces, "untouched pieces reused");
            Assert.AreEqual(1, ConvexColliderBuilder.LastCreatedPieces, "moved piece recreated");
            Assert.AreEqual(1, ConvexColliderBuilder.LastDestroyedPieces, "old piece of the moved brush destroyed");
            Assert.AreEqual(9, BrushCsg.LastCachedSolids, "solids of untouched brushes come from the cache");
            Assert.AreEqual(1, BrushCsg.LastBuiltSolids);
            var after = PieceObjects();
            after.IntersectWith(before);
            Assert.AreEqual(9, after.Count, "the nine untouched piece objects are the same objects as before");
            AssertSameAsFromScratch("after moving a brush");
        }

        [Test]
        public void MovingADistantBoxKeepsItsColliderAligned()
        {
            var start = new Vector3(1210099f, 88.75f, 2624193f);
            var size = new Vector3(317.875f, 177.625f, 332f);
            var moved = BrushApi.Create(BrushShape.Box, start, size, Quaternion.identity, name: "Distant", snap: false);
            var independent = BrushApi.Create(BrushShape.Box, new Vector3(10f, 2f, 7f), new Vector3(2f, 4f, 6f), Quaternion.identity, name: "Independent", snap: false);
            BrushApi.ForceUpdate();
            var retained = Container().GetComponentsInChildren<ConvexPiece>().Single(piece => piece.brushName == independent.name);

            var positions = new[] { start, start + Vector3.right * 8.125f, start + Vector3.right * 20f, start + Vector3.forward * 80.25f, start };
            foreach (var position in positions)
            {
                moved.transform.position = position;
                BrushApi.ForceUpdate();
                Physics.SyncTransforms();

                var pieces = Container().GetComponentsInChildren<ConvexPiece>();
                var match = pieces.SingleOrDefault(piece => piece.brushName == moved.name);
                Assert.IsNotNull(match, $"missing collider at {position:F3} m");
                var collider = match.GetComponent<BoxCollider>();
                Assert.IsNotNull(collider);
                Assert.That(Vector3.Distance(position, collider.bounds.center), Is.LessThanOrEqualTo(0.25f), $"collider center at {position:F3} m");
                Assert.That(Vector3.Distance(size, collider.bounds.size), Is.LessThanOrEqualTo(0.5f), $"collider size at {position:F3} m");
                Assert.AreSame(retained, pieces.Single(piece => piece.brushName == independent.name));
            }
        }

        [Test]
        public void MovingADistantCutterAcrossAnOriginBoundaryMovesTheOpening()
        {
            // The cutter crosses the boundary between two 512 m origins while the recipient stays put.
            var boundary = new Vector3(1210112f, 88.75f, 2624193f);
            var left = boundary + Vector3.left * 8f;
            var right = boundary + Vector3.right * 8f;
            BrushApi.Create(BrushShape.Box, boundary, new Vector3(64f, 16f, 32f), Quaternion.identity, snap: false);
            var cut = BrushApi.Create(BrushShape.Box, left, new Vector3(6f, 32f, 48f), Quaternion.identity, snap: false);
            BrushApi.SetOperation(cut, BrushOperation.Subtract);

            var positions = new[] { left, right, left };
            foreach (var position in positions)
            {
                cut.transform.position = position;
                BrushApi.ForceUpdate();
                Physics.SyncTransforms();

                var colliders = Container().GetComponentsInChildren<Collider>();
                bool HitsSolid(Vector3 point) => colliders.Any(collider => collider.Raycast(new Ray(point + Vector3.up * 20f, Vector3.down), out _, 40f));
                Assert.IsFalse(HitsSolid(position), $"opening at {position:F3} m");
                Assert.IsTrue(HitsSolid(position == left ? right : left), "solid on the other side of the boundary");
            }
        }

        [Test]
        public void MovingADistantParentPreservesItsChildsOpening()
        {
            var start = new Vector3(1210099f, 88.75f, 2624193f);
            var moved = BrushApi.Create(BrushShape.Box, start, new Vector3(64f, 16f, 32f), Quaternion.identity, snap: false);
            var offset = Vector3.right * 8f;
            var cut = BrushApi.Create(BrushShape.Box, start + offset, new Vector3(6f, 32f, 48f), Quaternion.identity, moved.transform, snap: false);
            BrushApi.SetOperation(cut, BrushOperation.Subtract);

            var alongX = start + Vector3.right * 80f;
            var positions = new[] { start, alongX, alongX + Vector3.forward * 96f, start };
            var previous = start;
            foreach (var position in positions)
            {
                moved.transform.position = position;
                BrushApi.ForceUpdate();
                Physics.SyncTransforms();

                Assert.AreEqual(position + offset, cut.transform.position);
                var meshObject = BrushCsg.MeshObject(Model(), false);
                var mesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
                var matrix = meshObject.localToWorldMatrix;
                var colliders = Container().GetComponentsInChildren<Collider>();
                Ray FromAbove(Vector3 point) => new Ray(point + Vector3.up * 20f, Vector3.down);
                bool HitsCollider(Ray ray) => colliders.Any(collider => collider.Raycast(ray, out _, 40f));
                var throughOpening = FromAbove(cut.transform.position);
                Assert.IsFalse(BrushHooks.IntersectRayMesh(throughOpening, mesh, matrix, out _));
                Assert.IsFalse(HitsCollider(throughOpening));
                Assert.IsTrue(BrushHooks.IntersectRayMesh(FromAbove(position), mesh, matrix, out _));
                Assert.IsTrue(HitsCollider(FromAbove(position)));

                var openingWall = new Ray(cut.transform.position, Vector3.right);
                Assert.IsTrue(BrushHooks.IntersectRayMesh(openingWall, mesh, matrix, out var wallHit));
                Assert.AreSame(cut, BrushCsg.TriangleBrushes(meshObject)[wallHit.triangleIndex]);
                if (position != previous)
                {
                    Assert.IsFalse(BrushHooks.IntersectRayMesh(FromAbove(previous), mesh, matrix, out _));
                    Assert.IsFalse(HitsCollider(FromAbove(previous)));
                }
                previous = position;
            }
        }

        [Test]
        public void SubtractOnlyRebuildsThePiecesItCuts()
        {
            var boxes = Row(10);
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3(3f, 0.5f, 0f), new Vector3(1f, 1f, 1f), Quaternion.identity, null, "cutter");
            BrushApi.SetOperation(cutter, BrushOperation.Subtract);
            BrushApi.ForceUpdate();
            AssertSameAsFromScratch("after adding a subtract brush");
            int count = Pieces().Count;
            Assert.Greater(count, 10, "the cut box became several pieces");

            BrushApi.Move(cutter, new Vector3(6f, 0.5f, 0f)); // cut the next box instead
            BrushApi.ForceUpdate();
            Assert.GreaterOrEqual(ConvexColliderBuilder.LastReusedPieces, 8, "boxes away from both cutter positions keep their pieces");
            Assert.AreEqual(10, BrushCsg.LastCachedSolids);
            AssertSameAsFromScratch("after moving the subtract brush");
        }

        [Test]
        public void UndoAndRedoMatchABuildFromScratch()
        {
            var boxes = Row(4);
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3(3f, 0.5f, 0f), new Vector3(1f, 1f, 1f), Quaternion.identity, null, "cutter");
            BrushApi.SetOperation(cutter, BrushOperation.Subtract);
            BrushApi.ForceUpdate();
            var initial = string.Join("\n", Pieces());

            Undo.IncrementCurrentGroup();
            BrushApi.Move(cutter, new Vector3(6f, 0.5f, 0f));
            BrushApi.ForceUpdate();
            var moved = string.Join("\n", Pieces());
            Assert.AreNotEqual(initial, moved);

            Undo.PerformUndo(); BrushApi.ForceUpdate();
            Assert.AreEqual(initial, string.Join("\n", Pieces()), "undo restores the pieces");
            AssertSameAsFromScratch("after undo");

            Undo.PerformRedo(); BrushApi.ForceUpdate();
            Assert.AreEqual(moved, string.Join("\n", Pieces()), "redo restores the moved pieces");
            AssertSameAsFromScratch("after redo");

            Undo.IncrementCurrentGroup();
            BrushApi.Delete(boxes[1]);
            BrushApi.ForceUpdate();
            Assert.AreEqual(1, ConvexColliderBuilder.LastDestroyedPieces, "only the deleted brush's piece goes");
            AssertSameAsFromScratch("after delete");

            Undo.PerformUndo(); BrushApi.ForceUpdate();
            Assert.AreEqual(moved, string.Join("\n", Pieces()), "undoing the delete brings the piece back");
            AssertSameAsFromScratch("after undoing a delete");
        }

        static int RenderVertexCount()
        {
            int n = 0;
            foreach (var model in Object.FindObjectsByType<BrushGroup>(FindObjectsInactive.Include))
            {
                var t = BrushCsg.MeshObject(model, false); if (t == null) continue;
                var mf = t.GetComponent<MeshFilter>(); if (mf.sharedMesh != null) n += mf.sharedMesh.vertexCount;
            }
            return n;
        }

        static void PumpUntilIdle()
        {
            for (int i = 0; i < 2000 && (BrushCsg.Busy || BrushCsg.HasDirty); i++) { System.Threading.Thread.Sleep(1); BrushCsg.Pump(); }
            Assert.IsFalse(BrushCsg.Busy || BrushCsg.HasDirty, "background build finished");
        }

        /// <summary>The editor loop's background build produces the same mesh and colliders as the synchronous path, also when edits land mid-build.</summary>
        [Test]
        public void BackgroundBuildMatchesTheSynchronousBuild()
        {
            var boxes = Row(10);
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3(3f, 0.5f, 0f), new Vector3(1f, 1f, 1f), Quaternion.identity, null, "cutter");
            BrushApi.SetOperation(cutter, BrushOperation.Subtract);
            BrushApi.ForceUpdate();
            var syncPieces = string.Join("\n", Pieces()); int syncVerts = RenderVertexCount();
            Assert.Greater(syncVerts, 0);

            BrushCsg.AsyncThresholdMs = 0; // every build goes to the worker
            try
            {
                BrushCsg.ClearCaches();
                var container = Container(); if (container != null) Object.DestroyImmediate(container.gameObject);
                BrushCsg.MarkAllDirty();
                BrushCsg.Pump();
                Assert.IsTrue(BrushCsg.Busy, "a background build started");
                PumpUntilIdle();
                Assert.AreEqual(syncPieces, string.Join("\n", Pieces()), "background build: colliders");
                Assert.AreEqual(syncVerts, RenderVertexCount(), "background build: render mesh");

                // edits while a build is running: the final result must reflect the last edit
                BrushApi.Move(cutter, new Vector3(6f, 0.5f, 0f)); BrushSnap.ProcessChanged();
                BrushCsg.Pump(); Assert.IsTrue(BrushCsg.Busy);
                BrushApi.Move(cutter, new Vector3(9f, 0.5f, 0f)); BrushSnap.ProcessChanged(); // lands mid-build
                PumpUntilIdle();
                var backgroundPieces = string.Join("\n", Pieces()); int backgroundVerts = RenderVertexCount();
                BrushCsg.AsyncThresholdMs = 8.0;
                BrushApi.ForceUpdate(); // synchronous reference for the same state
                Assert.AreEqual(string.Join("\n", Pieces()), backgroundPieces, "colliders after edits during a build");
                Assert.AreEqual(RenderVertexCount(), backgroundVerts, "render mesh after edits during a build");
                AssertSameAsFromScratch("after background builds");
            }
            finally { BrushCsg.AsyncThresholdMs = 8.0; }
        }

        [Test]
        public void ChangingAModuleValueRecreatesOnlyItsPiece()
        {
            var boxes = Row(3);
            var module = BrushApi.AddModule<TestSurfaceModule>(boxes[1]); module.value = 5;
            BrushApi.ForceUpdate();
            Assert.AreEqual(1, ConvexColliderBuilder.LastCreatedPieces, "the module's piece is a new object");
            Assert.AreEqual(2, ConvexColliderBuilder.LastReusedPieces);
            Assert.IsTrue(string.Join("\n", Pieces()).Contains("fp=" + BrushCsg.PieceFingerprint(boxes[1])), "the module's fingerprint is part of the piece identity");
            AssertSameAsFromScratch("after adding a module");
        }
    }
}
