using System.Collections.Generic;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Tests
{
    public class BrushDimensionLabelTests
    {
        [TestCase(1.25f, 0, "<color=#FF7373>X</color>: +1.25 m")]
        [TestCase(-0.5f, 1, "<color=#79DD75>Y</color>: -0.5 m")]
        [TestCase(-0.0000001f, 2, "<color=#78AAFF>Z</color>: 0 m")]
        public void DisplacementHasAWorldAxisAndSignedMetres(float meters, int axis, string expected)
        {
            if (!BrushLanguage.English) expected = expected.Replace(".", ",").Replace(" m", " м");
            Assert.AreEqual(expected, BrushDimensionFields.FormatDisplacement(meters, axis));
        }

        [Test]
        public void PlaneDragKeepsBothLabelsWhenOneComponentIsZero()
        {
            const int xz = 5;
            Assert.That(BrushMoveDisplacement.DragAxes(xz, Vector3.right), Is.EqualTo(xz));
            Assert.That(BrushMoveDisplacement.DragAxes(xz, Vector3.zero), Is.EqualTo(xz));
            Assert.That(BrushMoveDisplacement.ArrowForWorldAxis(xz, Quaternion.identity, 1), Is.EqualTo(-1));
            for (int i = 0; i < 3; i++)
                Assert.That(BrushMoveDisplacement.ArrowForWorldAxis(7, Quaternion.identity, i), Is.EqualTo(i));
        }

        [Test]
        public void LocalMoveLabelsFollowTheDraggedArrowWithWorldCoordinates()
        {
            var basis = Quaternion.Euler(0, 90, 0);
            Assert.That(BrushMoveDisplacement.ArrowForWorldAxis(1, basis, 0), Is.EqualTo(-1));
            Assert.That(BrushMoveDisplacement.ArrowForWorldAxis(1, basis, 2), Is.EqualTo(0));
        }

        [TestCase(0, "19,75 м (<color=#FF7373>X</color>)")]
        [TestCase(1, "19,75 м (<color=#79DD75>Y</color>)")]
        [TestCase(2, "19,75 м (<color=#78AAFF>Z</color>)")]
        public void OnlyTheLocalAxisLetterUsesItsAxisColor(int axis, string expected)
        {
            if (BrushLanguage.English) expected = expected.Replace("19,75 м", "19.75 m");
            Assert.AreEqual(expected, BrushDimensionFields.FormatDimension(19.75f, axis));
        }

        [TestCase(1f, 0f)]
        [TestCase(0f, 1f)]
        [TestCase(-1f, 2f)]
        public void EntireBadgeClearsTheEdgeOnTheOutside(float x, float y)
        {
            var along = new Vector2(x, y);
            var normal = new Vector2(y, -x).normalized;
            var midpoint = new Vector2(240f, 165f);
            var size = new Vector2(104f, 22f);
            var placed = new List<Rect>();
            var rect = BrushDimensionFields.BadgeRect(midpoint, along, normal * 30f, size, placed);
            float halfExtent = (Mathf.Abs(normal.x) * size.x + Mathf.Abs(normal.y) * size.y) * 0.5f;
            Assert.That(Vector2.Dot(rect.center - midpoint, normal) - halfExtent, Is.EqualTo(6f).Within(0.001f));

            placed.Add(rect);
            var next = BrushDimensionFields.BadgeRect(midpoint, along, normal, size, placed);
            Assert.IsFalse(rect.Overlaps(next));
            Assert.Greater(Vector2.Dot(next.center - rect.center, normal), 0f);
        }

        [Test]
        public void EdgeViewedEndOnStillHasASeparatedBadge()
        {
            var rect = BrushDimensionFields.BadgeRect(Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(100f, 22f), new List<Rect>());
            Assert.That(rect.yMax, Is.EqualTo(-6f).Within(0.001f));
        }

        [Test]
        public void AWallLengthBadgeShowsMetresWithoutAWorldAxis()
        {
            Assert.That(BrushDimensionFields.FormatDimension(19.75f, -1),
                Is.EqualTo(BrushLanguage.English ? "19.75 m" : "19,75 м"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StraightWallLengthKeepsTheEndNearestWcsAndUndoesInOneStep(bool reverseLink)
        {
            var plan = new GameObject("Plan").AddComponent<FloorPlan>();
            try
            {
                plan.transform.SetPositionAndRotation(new Vector3(33f, -4f, 17f), Quaternion.Euler(19f, 42f, 0f));
                plan.transform.localScale = new Vector3(2f, 1f, 0.5f);
                var nearPoint = new Vector3(5f, 0f, 3f);
                int nearId = plan.AddPoint(nearPoint);
                int farId = plan.AddPoint(new Vector3(1f, 0f, 1f));
                int cornerId = plan.AddPoint(new Vector3(6f, 0f, 5f));
                plan.AddWall(reverseLink ? farId : nearId, reverseLink ? nearId : farId);
                plan.AddWall(farId, cornerId);
                Undo.IncrementCurrentGroup();
                Assert.That(GeneratorWcs.SetOrigin(plan, nearPoint + Vector3.up * 2f), Is.True);
                var wcs = GeneratorWcs.World(plan);
                var walls = new List<FloorPlan.Wall>();
                plan.Walls(walls);
                var original = plan.points.ToArray();
                int near = plan.PointIndex(nearId), far = plan.PointIndex(farId);

                const float meters = 9.37f;
                Assert.That(BrushPlanDimensionFields.TryWallEnd(plan, walls[0], meters, out int moved, out var point), Is.True);
                Assert.That(moved, Is.EqualTo(far));
                Assert.That(Vector3.Distance(plan.GraphToWorld(original[near]), plan.GraphToWorld(point)),
                    Is.EqualTo(meters).Within(1e-5f));
                CollectionAssert.AreEqual(original, plan.points, "the preview must leave the authoring points alone");

                Undo.IncrementCurrentGroup();
                Assert.That(BrushPlanDimensionFields.SetWallLength(plan, walls[0], meters), Is.True);
                Assert.That(plan.points[near], Is.EqualTo(original[near]));
                Assert.That(plan.points[far], Is.EqualTo(point));
                Assert.That(plan.points[plan.PointIndex(cornerId)], Is.EqualTo(original[2]));
                Assert.That(plan.links[1].start, Is.EqualTo(farId), "the adjacent wall still shares the moved endpoint");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                CollectionAssert.AreEqual(original, plan.points);
                Assert.That(GeneratorWcs.World(plan), Is.EqualTo(wcs));
                Undo.PerformRedo();
                Assert.That(plan.points[near], Is.EqualTo(original[near]));
                Assert.That(plan.points[far], Is.EqualTo(point));
            }
            finally { Object.DestroyImmediate(plan.gameObject); }
        }

        [Test]
        public void CurvedWallLengthIsReadOnly()
        {
            var plan = new GameObject("Plan").AddComponent<FloorPlan>();
            try
            {
                int start = plan.AddPoint(Vector3.zero);
                int end = plan.AddPoint(Vector3.right * 4f);
                int next = plan.AddPoint(new Vector3(4f, 0f, 4f));
                plan.AddWall(start, end);
                plan.AddWall(end, next);
                plan.SetSmooth(end, true);
                var walls = new List<FloorPlan.Wall>();
                plan.Walls(walls);
                var original = plan.points.ToArray();
                Assert.That(walls[0].Curved, Is.True);
                Assert.That(BrushPlanDimensionFields.TryWallEnd(plan, walls[0], 6f, out _, out _), Is.False);
                Assert.That(BrushPlanDimensionFields.SetWallLength(plan, walls[0], 6f), Is.False);
                CollectionAssert.AreEqual(original, plan.points);
            }
            finally { Object.DestroyImmediate(plan.gameObject); }
        }
    }
}
