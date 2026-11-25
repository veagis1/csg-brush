using System.Collections.Generic;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEngine;

namespace CsgBrush.Tests
{
    public class BrushDimensionLabelTests
    {
        [TestCase(0, "19,75 м (X)")]
        [TestCase(1, "19,75 м (Y)")]
        [TestCase(2, "19,75 м (Z)")]
        public void DimensionIncludesItsLocalAxis(int axis, string expected)
        {
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
    }
}
