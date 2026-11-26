using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public sealed class BrushCreationWorkflowTests
    {
        bool previousGrid;
        bool previousBasis;

        [SetUp]
        public void Open()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            previousGrid = BrushSettings.instance.snapToGrid;
            previousBasis = BrushSurfaceGrid.Local;
        }

        [TearDown]
        public void Restore()
        {
            BrushSettings.instance.snapToGrid = previousGrid;
            BrushSurfaceGrid.Local = previousBasis;
        }

        [TestCase(null, true, -0.5f, BrushOperation.Subtract)]
        [TestCase(null, true, 0.5f, BrushOperation.Add)]
        [TestCase(null, false, -0.5f, BrushOperation.Add)]
        [TestCase(BrushOperation.Add, true, -0.5f, BrushOperation.Add)]
        [TestCase(BrushOperation.Subtract, true, 0.5f, BrushOperation.Subtract)]
        public void CreationOperationFollowsSignedDepthUnlessManuallyChosen(
            BrushOperation? manual, bool surface, float depth, BrushOperation expected)
        {
            Assert.That(BrushCreateTool.ResolveOperation(manual, surface, depth), Is.EqualTo(expected));
        }

        [Test]
        public void LocalGridFollowsAnOffsetRotatedSurface()
        {
            BrushSurfaceGrid.Local = true;
            var support = BrushApi.Create(BrushShape.Box, new Vector3(0.13f, 0.27f, 0.39f),
                Vector3.one * 2f, Quaternion.Euler(10f, 33f, 0f), snap: false);
            var frame = BrushSurfaceGrid.ForSurface(support, support.transform.position + support.transform.up, support.transform.up);
            var moving = BrushApi.Create(BrushShape.Box, frame.Origin, Vector3.one, frame.Rotation, snap: false);
            moving.gridSnap = GridSnap.Pivot;
            frame.Store(moving);
            support.transform.position += new Vector3(1f, 2f, 3f);
            var requested = frame.Origin + frame.Rotation * new Vector3(0.26f, 0f, -0.76f);
            var expected = frame.Origin + frame.Rotation * new Vector3(0.5f, 0f, -1f);
            Assert.That(Vector3.Distance(expected, BrushSnap.SnapBrushPosition(moving, requested, moving.size,
                moving.transform.rotation, 0.5f)), Is.LessThan(0.0001f));
        }

        [Test]
        public void FreeConstructionGridSnapsInItsPlaneWithoutAnOwner()
        {
            var point = new Vector3(1.35f, -0.8f, 2.1f);
            var normal = new Vector3(0.2f, 1f, 0.9f).normalized;
            var frame = BrushSurfaceGrid.Frame.OnPlane(point, normal);
            Assert.That(frame.Valid, Is.True);
            Assert.That(frame.Owner, Is.Null);
            Assert.That(Vector3.Distance(frame.Normal, normal), Is.LessThan(0.0001f));
            Assert.That(frame.Plane.GetDistanceToPoint(point), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(Vector3.Distance(frame.Origin, normal * Vector3.Dot(point, normal)),
                Is.LessThan(0.0001f));
            var otherPoint = point + Vector3.ProjectOnPlane(Vector3.right * 4f, normal);
            var samePlane = BrushSurfaceGrid.Frame.OnPlane(otherPoint, normal);
            Assert.That(Vector3.Distance(samePlane.Origin, frame.Origin), Is.LessThan(0.0001f));

            var cursor = frame.Origin + frame.Rotation * new Vector3(0.26f, 0f, -0.76f);
            var snapped = BrushDraw.SnapInPlane(cursor, frame.Origin, frame.Rotation, 0.5f);
            var expected = frame.Origin + frame.Rotation * new Vector3(0.5f, 0f, -1f);
            Assert.That(Vector3.Distance(snapped, expected), Is.LessThan(0.0001f));
            Assert.That(frame.Plane.GetDistanceToPoint(snapped), Is.EqualTo(0f).Within(0.0001f));
        }

        [TestCase(BrushShape.Door)]
        [TestCase(BrushShape.Window)]
        public void AFreeOpeningStartsExactlyOnTheWorldFloor(BrushShape shape)
        {
            var ray = new Ray(new Vector3(0.2f, 60.456f, -0.3f), new Vector3(17f, -1f, 11f));
            Assert.That(WallAnchors.Find(ray, shape, out var placement), Is.True);
            Assert.That(placement.drawingPoint.HasValue, Is.True);
            Assert.That(placement.drawingPoint.Value.y, Is.Zero);
            var opening = WallAnchors.Create(shape, placement);
            Assert.That(opening.transform.position.y, Is.Zero);
        }

        [Test]
        public void CreationCommandsAreRegisteredInBothMenus()
        {
            string[] names =
            {
                "Блок", "Клин", "Цилиндр", "Конус", "Сфера", "Прямая лестница",
                "Изогнутая лестница", "Винтовая лестница", "Арка", "Дверь", "Окно", "План стен", "Сплайн"
            };
            var menuPaths = typeof(BrushCreateTool).Assembly.GetTypes()
                .Where(type => type.IsSubclassOf(typeof(BrushCreateTool)) ||
                    type == typeof(CreateFloorPlanTool) || type == typeof(CreateSplineTool))
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .SelectMany(method => method.GetCustomAttributesData())
                .Where(attribute => attribute.AttributeType == typeof(MenuItem))
                .Select(attribute => (string)attribute.ConstructorArguments[0].Value)
                .ToArray();

            foreach (string name in names)
            {
                Assert.That(menuPaths, Does.Contain("CSG Brush/Создать/" + name));
                Assert.That(menuPaths, Does.Contain("Tools/CSG Brush/Создать/" + name));
            }
        }

        [Test]
        public void LegacyUnitSettingsConvertTheirLengthsOnceAndKeepOtherValues()
        {
            var settings = BrushSettings.instance;
            string saved = EditorJsonUtility.ToJson(settings);
            try
            {
                EditorJsonUtility.FromJsonOverwrite(@"{""MonoBehaviour"":{
                    ""unitsPerMeter"": 100, ""unitLabel"": ""cm"",
                    ""gridSizes"": [25,50,100], ""gridIndex"": 1,
                    ""defaultBoxSize"": { ""x"":200, ""y"":300, ""z"":400 },
                    ""newStepHeight"": 25, ""newInnerRadius"": 0, ""newStepThickness"": 10,
                    ""newWallThickness"": 30, ""newDoorWidth"": 95, ""newDoorHeight"": 210,
                    ""newWindowWidth"": 120, ""newWindowHeight"": 110, ""newWindowSill"": 85,
                    ""extrudeDistance"": -50, ""curveTolerance"": 0.1, ""rotationSnapDegrees"": 30
                }}", settings);

                Assert.That(settings.MigrateLegacyUnits(), Is.True);
                Assert.That(settings.gridSizes, Is.EqualTo(new[] { 0.25f, 0.5f, 1f }));
                Assert.That(settings.gridIndex, Is.EqualTo(1));
                Assert.That(settings.GridMeters, Is.EqualTo(0.5f));
                Assert.That(settings.defaultBoxSize, Is.EqualTo(new Vector3(2f, 3f, 4f)));
                Assert.That(settings.newStepHeight, Is.EqualTo(0.25f).Within(0.00001f));
                Assert.That(settings.newInnerRadius, Is.Zero);
                Assert.That(settings.newStepThickness, Is.EqualTo(0.1f).Within(0.00001f));
                Assert.That(settings.newWallThickness, Is.EqualTo(0.3f).Within(0.00001f));
                Assert.That(settings.newDoorWidth, Is.EqualTo(0.95f).Within(0.00001f));
                Assert.That(settings.newDoorHeight, Is.EqualTo(2.1f).Within(0.00001f));
                Assert.That(settings.newWindowWidth, Is.EqualTo(1.2f).Within(0.00001f));
                Assert.That(settings.newWindowHeight, Is.EqualTo(1.1f).Within(0.00001f));
                Assert.That(settings.newWindowSill, Is.EqualTo(0.85f).Within(0.00001f));
                Assert.That(settings.extrudeDistance, Is.EqualTo(-0.5f).Within(0.00001f));
                Assert.That(settings.curveTolerance, Is.EqualTo(0.1f));
                Assert.That(settings.rotationSnapDegrees, Is.EqualTo(30f));
                Assert.That(settings.MigrateLegacyUnits(), Is.False);
                Assert.That(settings.newDoorWidth, Is.EqualTo(0.95f).Within(0.00001f));
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(saved, settings);
            }
        }

        [Test]
        public void TypedWidthStaysExactOnGridAndRestoresOnUndo()
        {
            BrushSettings.instance.snapToGrid = true;
            var box = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one * 2f, Quaternion.identity, snap: false);
            box.gridSnap = GridSnap.Shape;
            BrushSnap.Accept(box);
            Vector3 FixedCorner() => box.transform.TransformPoint(BrushGeometry.Polyhedron(box).Bounds().min);
            var before = FixedCorner();
            Undo.IncrementCurrentGroup();
            Assert.That(BrushDimensionFields.TryMeters("4,2", false, out float width), Is.True);
            Assert.That(BrushDimensionFields.Resize(box, 0, width), Is.True);
            Undo.FlushUndoRecordObjects();
            BrushSnap.Snap(box);
            Assert.That(box.size.x, Is.EqualTo(4.2f).Within(0.00001f));
            Assert.That(Vector3.Distance(FixedCorner(), before), Is.LessThan(0.0001f));
            Undo.PerformUndo();
            Assert.That(box.size.x, Is.EqualTo(2f));
            Undo.PerformRedo();
            Assert.That(box.size.x, Is.EqualTo(4.2f).Within(0.00001f));
        }

        [Test]
        public void TypedDimensionUsesWorldMetresUnderNonUniformParentScale()
        {
            BrushSettings.instance.snapToGrid = true;
            var parent = new GameObject("Родитель").transform;
            parent.SetPositionAndRotation(new Vector3(1.5f, 2f, -0.3f), Quaternion.Euler(6f, 19f, 13f));
            parent.localScale = new Vector3(2f, 0.75f, 1.3f);
            var brush = BrushApi.Create(BrushShape.Box, parent.position, new Vector3(2f, 3f, 4f),
                Quaternion.Euler(11f, 32f, 8f), parent, snap: false);
            var transform = brush.transform;
            var bounds = BrushGeometry.Polyhedron(brush).Bounds();
            var originalFixedEnd = transform.TransformPoint(bounds.min);
            var originalOtherSpans = new Vector2(
                transform.TransformVector(Vector3.up * bounds.size.y).magnitude,
                transform.TransformVector(Vector3.forward * bounds.size.z).magnitude);

            Assert.That(BrushDimensionFields.Resize(brush, 0, 0f), Is.False);
            Assert.That(BrushDimensionFields.Resize(brush, 0, 4.23f), Is.True);
            BrushSnap.Snap(brush);

            bounds = BrushGeometry.Polyhedron(brush).Bounds();
            Assert.That(transform.TransformVector(Vector3.right * bounds.size.x).magnitude,
                Is.EqualTo(4.23f).Within(0.0001f));
            Assert.That(Vector3.Distance(originalFixedEnd, transform.TransformPoint(bounds.min)), Is.LessThan(0.0001f));
            Assert.That(transform.TransformVector(Vector3.up * bounds.size.y).magnitude,
                Is.EqualTo(originalOtherSpans.x).Within(0.0001f));
            Assert.That(transform.TransformVector(Vector3.forward * bounds.size.z).magnitude,
                Is.EqualTo(originalOtherSpans.y).Within(0.0001f));
        }

        static bool Solid(Vector3 point) =>
            Physics.OverlapBox(point, Vector3.one * 0.02f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length > 0;

        static bool Visible(BrushGroup model, Ray ray)
        {
            foreach (var meshObject in BrushCsg.MeshObjects(model))
            {
                var mesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
                if (mesh != null && BrushHooks.IntersectRayMesh(ray, mesh, meshObject.localToWorldMatrix, out _))
                    return true;
            }
            return false;
        }

        [Test]
        public void AddressedCutAffectsOnlyItsTargetEvenBeforeItInTheHierarchy()
        {
            var target = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one * 2f, Quaternion.identity, snap: false);
            BrushApi.Create(BrushShape.Box, new Vector3(0f, 0f, 3f), Vector3.one * 2f, Quaternion.identity, snap: false);
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3(0f, 0f, 1.5f),
                new Vector3(1f, 1f, 5f), Quaternion.identity, snap: false);
            cutter.transform.SetAsFirstSibling();
            BrushCutTarget.FromBrush(target).Assign(cutter);

            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            var model = BrushCsg.ModelOf(target);
            var between = new Vector3(0f, 0f, 1.5f);
            Assert.That(Visible(model, new Ray(between, Vector3.back)), Is.False);
            Assert.That(Visible(model, new Ray(between, Vector3.forward)), Is.True);
            Assert.That(Solid(Vector3.zero), Is.False);
            Assert.That(Solid(new Vector3(0f, 0f, 3f)), Is.True);
            Assert.That(Solid(new Vector3(0.8f, 0f, 0f)), Is.True);
        }

        [Test]
        public void APlanWallHasAnAddressAndItsOwnMeasuredHeight()
        {
            var plan = new GameObject("План").AddComponent<FloorPlan>();
            int start = plan.AddPoint(Vector3.zero);
            int end = plan.AddPoint(Vector3.right * 4f);
            plan.AddWall(start, end, 0.3f);
            var walls = new List<FloorPlan.Wall>();
            plan.Walls(walls);
            Assert.That(BrushPlanDimensionFields.SetWallDimension(plan, walls[0], 2.7f, true), Is.True);
            Assert.That(BrushPlanDimensionFields.SetWallLength(plan, walls[0], 5.2f), Is.True);
            BrushGenerators.Update(plan);
            var generated = plan.generated[0];
            var target = BrushCutTarget.FromBrush(generated);
            Assert.That(target.TryWall(out var wall), Is.True);
            Assert.That(wall.height, Is.EqualTo(2.7f).Within(0.00001f));
            Assert.That(wall.Length, Is.EqualTo(5.2f).Within(0.00001f));
        }

        [Test]
        public void AddressedWallCutLeavesAnotherWallOfTheSamePlanIntact()
        {
            var plan = new GameObject("План").AddComponent<FloorPlan>();
            plan.AddWall(plan.AddPoint(new Vector3(-2f, 0f, 0f)), plan.AddPoint(new Vector3(2f, 0f, 0f)), 0.3f);
            plan.AddWall(plan.AddPoint(new Vector3(-2f, 0f, 2f)), plan.AddPoint(new Vector3(2f, 0f, 2f)), 0.3f);
            BrushGenerators.Update(plan);
            var target = BrushCutTarget.FromBrush(plan.generated[0]);
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 1f),
                new Vector3(1f, 1f, 4f), Quaternion.identity, plan.transform, snap: false);
            target.Assign(cutter);
            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            Assert.That(Solid(new Vector3(0f, 1f, 0f)), Is.False);
            Assert.That(Solid(new Vector3(0f, 1f, 2f)), Is.True);
        }
    }
}
