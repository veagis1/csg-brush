using System;
using System.Collections.Generic;
using System.Linq;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public sealed class BrushCreationToolTransitionTests
    {
        Brush brush;
        FloorPlan plan;
        Spline spline;
        bool wasShapeSuspended;

        static IEnumerable<TestCaseData> CreationTools()
        {
            yield return new TestCaseData("Блок", typeof(CreateBoxBrushTool));
            yield return new TestCaseData("Клин", typeof(CreateWedgeBrushTool));
            yield return new TestCaseData("Цилиндр", typeof(CreateCylinderBrushTool));
            yield return new TestCaseData("Конус", typeof(CreateConeBrushTool));
            yield return new TestCaseData("Сфера", typeof(CreateSphereBrushTool));
            yield return new TestCaseData("Прямая лестница", typeof(CreateStairsBrushTool));
            yield return new TestCaseData("Изогнутая лестница", typeof(CreateCurvedStairsBrushTool));
            yield return new TestCaseData("Винтовая лестница", typeof(CreateSpiralStairsBrushTool));
            yield return new TestCaseData("Арка", typeof(CreateArchBrushTool));
            yield return new TestCaseData("Дверь", typeof(CreateDoorBrushTool));
            yield return new TestCaseData("Окно", typeof(CreateWindowBrushTool));
            yield return new TestCaseData("План стен", typeof(CreateFloorPlanTool));
            yield return new TestCaseData("Сплайн", typeof(CreateSplineTool));
        }

        [SetUp]
        public void SetUp()
        {
            wasShapeSuspended = SessionState.GetBool(BrushShapeHandles.SuspendedKey, false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            brush = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            plan = new GameObject("План стен").AddComponent<FloorPlan>();
            spline = new GameObject("Сплайн").AddComponent<Spline>();
        }

        [TearDown]
        public void TearDown()
        {
            ToolManager.SetActiveContext<GameObjectToolContext>();
            ToolManager.RestorePreviousPersistentTool();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (wasShapeSuspended) SessionState.SetBool(BrushShapeHandles.SuspendedKey, true);
            else SessionState.EraseBool(BrushShapeHandles.SuspendedKey);
        }

        static void Enter<T>(Component component) where T : EditorToolContext
        {
            Tools.current = Tool.Move;
            Selection.activeGameObject = component.gameObject;
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            ToolManager.SetActiveContext<T>();
            Assert.That(ToolManager.activeContextType, Is.EqualTo(typeof(T)));
        }

        static void AssertCreation(Type tool)
        {
            Assert.That(ToolManager.activeContextType, Is.EqualTo(typeof(GameObjectToolContext)));
            Assert.That(ToolManager.activeToolType, Is.EqualTo(tool));
        }

        [TestCaseSource(nameof(CreationTools))]
        public void CreationCommandLeavesBrushFloorPlanAndSplineEditing(string item, Type tool)
        {
            Enter<BrushEditContext>(brush);
            Assert.That(EditorApplication.ExecuteMenuItem("CSG Brush/Создать/" + item), Is.True);
            AssertCreation(tool);

            Enter<FloorPlanEditContext>(plan);
            Assert.That(EditorApplication.ExecuteMenuItem("Tools/CSG Brush/Создать/" + item), Is.True);
            AssertCreation(tool);

            Enter<SplineEditContext>(spline);
            ToolManager.SetActiveTool(tool);
            AssertCreation(tool);
        }

        [Test]
        public void GameObjectCommandClosesComponentEditingAndCreatesImmediately()
        {
            Enter<BrushEditContext>(brush);
            Assert.That(EditorApplication.ExecuteMenuItem("GameObject/CSG Brush/Блок"), Is.True);
            Assert.That(ToolManager.activeContextType, Is.EqualTo(typeof(GameObjectToolContext)));
            var created = Selection.activeGameObject.GetComponent<Brush>();
            Assert.That(created, Is.Not.Null);
            Assert.That(created, Is.Not.SameAs(brush));

            Enter<FloorPlanEditContext>(plan);
            var drawPlan = ExecuteGraphMenu<FloorPlan>("План стен", typeof(FloorPlanMenu));
            drawPlan();
            AssertCreation(typeof(FloorPlanDrawTool));

            Enter<SplineEditContext>(spline);
            var drawSpline = ExecuteGraphMenu<Spline>("Сплайн", typeof(SplineMenu));
            drawSpline();
            AssertCreation(typeof(SplineDrawTool));
        }

        static EditorApplication.CallbackFunction ExecuteGraphMenu<T>(string item, Type menu) where T : PointGraph
        {
            var pending = EditorApplication.delayCall?.GetInvocationList() ?? Array.Empty<Delegate>();
            var previous = Selection.activeGameObject;
            Assert.That(EditorApplication.ExecuteMenuItem("GameObject/CSG Brush/" + item), Is.True);
            Assert.That(ToolManager.activeContextType, Is.EqualTo(typeof(GameObjectToolContext)));
            Assert.That(Selection.activeGameObject, Is.Not.SameAs(previous));
            Assert.That(Selection.activeGameObject.GetComponent<T>(), Is.Not.Null);

            // Batch tests can run without an Inspector update that drains delayCall.
            var draw = (EditorApplication.CallbackFunction)EditorApplication.delayCall.GetInvocationList().Except(pending)
                .Single(callback => callback.Method.DeclaringType?.DeclaringType == menu);
            EditorApplication.delayCall -= draw;
            return draw;
        }

        [Test]
        public void ADeferredDrawCannotRestoreThePreviousToolAfterStartingCreation()
        {
            Enter<FloorPlanEditContext>(plan);
            var oldDraw = ExecuteGraphMenu<FloorPlan>("План стен", typeof(FloorPlanMenu));
            ToolManager.SetActiveTool<CreateBoxBrushTool>();
            oldDraw();
            AssertCreation(typeof(CreateBoxBrushTool));
        }

        [Test]
        public void CreationTurnsOffShapeEditingForTheSessionWithoutChangingThePreference()
        {
            var settings = BrushSettings.instance;
            bool previousPreference = settings.shapeHandles;
            settings.shapeHandles = true;
            SessionState.EraseBool(BrushShapeHandles.SuspendedKey);
            try
            {
                Assert.That(BrushShapeHandles.Enabled, Is.True);
                ToolManager.SetActiveTool<CreateBoxBrushTool>();
                Assert.That(BrushShapeHandles.Enabled, Is.False);
                ToolManager.RestorePreviousPersistentTool();
                Assert.That(BrushShapeHandles.Enabled, Is.False);
                Assert.That(settings.shapeHandles, Is.True);
            }
            finally { settings.shapeHandles = previousPreference; }
        }

        [Test]
        public void CreationLeavesThePivotPickerImmediately()
        {
            Enter<BrushEditContext>(brush);
            BrushEditContext.Exit();
            ToolManager.SetActiveTool<BrushPivotTool>();
            Assert.That(ToolManager.activeToolType, Is.EqualTo(typeof(BrushPivotTool)));

            Assert.That(EditorApplication.ExecuteMenuItem("CSG Brush/Создать/Блок"), Is.True);
            AssertCreation(typeof(CreateBoxBrushTool));
        }

        [Test]
        public void CreationCancelsTheCurrentPointDragAndReleasesTheMouse()
        {
            plan.AddPoint(Vector3.zero);
            Enter<FloorPlanEditContext>(plan);
            EditModeMouse.Take(527);
            try
            {
                var step = BrushSettings.instance.GridMeters;
                FloorPlanEditState.BeginDrag(plan, new HashSet<int> { 0 });
                FloorPlanEditState.TransformPoints(plan, FloorPlanEditState.dragStart, FloorPlanEditState.dragMoving,
                    point => point + Vector3.right * step);
                Assert.That(plan.points[0], Is.EqualTo(Vector3.right * step));

                ToolManager.SetActiveTool<CreateBoxBrushTool>();
                AssertCreation(typeof(CreateBoxBrushTool));
                Assert.That(plan.points[0], Is.EqualTo(Vector3.zero));
                Assert.That(FloorPlanEditState.Dragging(plan), Is.False);
                Assert.That(GUIUtility.hotControl, Is.Zero);
            }
            finally { EditModeMouse.ReleaseOwned(); }
        }
    }
}
