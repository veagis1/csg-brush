using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace CsgBrush.Tests
{
    public sealed class BrushSessionAndMenuTests
    {
        [Test]
        public void ToolPreferencesUseSessionOverridesWithoutChangingProjectSettings()
        {
            var settings = BrushSettings.instance;
            string project = EditorJsonUtility.ToJson(settings);
            bool relativeDisplacement = settings.showRelativeDisplacement;
            try
            {
                AssertSessionOverride(nameof(settings.gridIndex), settings.defaultGridIndex, settings.defaultGridIndex == 0 ? 1 : 0,
                    () => BrushSettings.instance.gridIndex, value => settings.gridIndex = value, SessionState.GetInt, SessionState.SetInt, SessionState.EraseInt);
                AssertBool(nameof(settings.snapToGrid), settings.defaultSnapToGrid, () => BrushSettings.instance.snapToGrid, value => settings.snapToGrid = value);
                AssertBool(nameof(settings.showCuts), settings.defaultShowCuts, () => BrushSettings.instance.showCuts, value => settings.showCuts = value);
                AssertBool(nameof(settings.showDimensions), settings.defaultShowDimensions, () => BrushSettings.instance.showDimensions, value => settings.showDimensions = value);
                AssertBool(nameof(settings.shapeHandles), settings.defaultShapeHandles, () => BrushSettings.instance.shapeHandles, value => settings.shapeHandles = value);
                AssertSessionOverride(nameof(settings.shapeEditSelection), (int)settings.defaultShapeEditSelection,
                    settings.defaultShapeEditSelection == ShapeEditSelection.Match ? (int)ShapeEditSelection.Offset : (int)ShapeEditSelection.Match,
                    () => (int)BrushSettings.instance.shapeEditSelection, value => settings.shapeEditSelection = (ShapeEditSelection)value,
                    SessionState.GetInt, SessionState.SetInt, SessionState.EraseInt);
                AssertSessionOverride(nameof(settings.extrudeDistance), settings.defaultExtrudeDistance, settings.defaultExtrudeDistance == 2.75f ? -1.5f : 2.75f,
                    () => BrushSettings.instance.extrudeDistance, value => settings.extrudeDistance = value,
                    SessionState.GetFloat, SessionState.SetFloat, SessionState.EraseFloat);
                AssertBool(nameof(settings.extrudeIndividual), settings.defaultExtrudeIndividual, () => BrushSettings.instance.extrudeIndividual, value => settings.extrudeIndividual = value);

                Assert.That(settings.showRelativeDisplacement, Is.EqualTo(relativeDisplacement));
                Assert.That(EditorJsonUtility.ToJson(settings), Is.EqualTo(project));
            }
            finally
            {
                if (EditorJsonUtility.ToJson(settings) != project) EditorJsonUtility.FromJsonOverwrite(project, settings);
            }
        }

        static void AssertBool(string property, bool projectValue, Func<bool> read, Action<bool> write) =>
            AssertSessionOverride(property, projectValue, !projectValue, read, write, SessionState.GetBool, SessionState.SetBool, SessionState.EraseBool);

        static void AssertSessionOverride<T>(string property, T projectValue, T editedValue, Func<T> read, Action<T> write,
            Func<string, T, T> readSession, Action<string, T> writeSession, Action<string> eraseSession)
        {
            string key = BrushSettings.SessionKey + property;
            T original = readSession(key, projectValue);
            bool hadOverride = EqualityComparer<T>.Default.Equals(original, readSession(key, editedValue));
            try
            {
                eraseSession(key);
                Assert.That(read(), Is.EqualTo(projectValue), property);
                write(editedValue);
                Assert.That(readSession(key, projectValue), Is.EqualTo(editedValue), key);
                Assert.That(read(), Is.EqualTo(editedValue), property);
                writeSession(key, projectValue);
                Assert.That(read(), Is.EqualTo(projectValue), property);
            }
            finally
            {
                if (hadOverride) writeSession(key, original);
                else eraseSession(key);
            }
        }

        [Test]
        public void CreationOptionsCopyProjectDefaultsAndResetWithoutChangingManualOperation()
        {
            const string manualKey = "CsgBrush.Create.ManualOperation";
            const int absent = int.MinValue;
            int previousManual = SessionState.GetInt(manualKey, absent);
            bool hadManualKey = previousManual == SessionState.GetInt(manualKey, int.MaxValue);
            var settings = BrushSettings.instance;
            var previousModules = settings.newModules;
            int previousSides = settings.newSides;
            string projectBeforeTest = EditorJsonUtility.ToJson(settings);
            var workingField = typeof(BrushSettings).GetField("workingOptions", BindingFlags.Instance | BindingFlags.NonPublic);
            var workingFlag = typeof(BrushSettings).GetField("hasWorkingOptions", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(workingField, Is.Not.Null);
            Assert.That(workingFlag, Is.Not.Null);
            var previousWorking = workingField.GetValue(settings);
            var previouslyEditing = workingFlag.GetValue(settings);
            try
            {
                string module = typeof(BrushTrigger).FullName;
                settings.newModules = new List<string> { module };
                settings.newSides = 17;
                string project = EditorJsonUtility.ToJson(settings);
                settings.ResetCreationOptions();
                BrushCreateTool.ManualOperation = null;
                Assert.That(BrushCreateTool.Operation, Is.EqualTo(settings.newOperation));

                foreach (var operation in new[] { BrushOperation.Add, BrushOperation.Subtract })
                {
                    BrushCreateTool.Operation = operation;
                    ref var options = ref settings.EditCreationOptions();
                    AssertMatchingProjectDefaults(settings, options);
                    Assert.That(options.newModules, Is.Not.SameAs(settings.newModules));
                    options.newModules.Clear();
                    options.newSides = 23;
                    options.newHollow = !settings.newHollow;
                    Assert.That(settings.CreationOptions.newSides, Is.EqualTo(23));
                    Assert.That(settings.CreationOptions.newHollow, Is.EqualTo(!settings.newHollow));
                    Assert.That(settings.CreationOptions.newModules, Is.Empty);
                    Assert.That(settings.newModules, Is.EqualTo(new[] { module }));
                    Assert.That(EditorJsonUtility.ToJson(settings), Is.EqualTo(project));

                    settings.ResetCreationOptions();
                    AssertMatchingProjectDefaults(settings, settings.CreationOptions);
                    Assert.That(BrushCreateTool.ManualOperation, Is.EqualTo(operation));
                    Assert.That(BrushCreateTool.Operation, Is.EqualTo(operation));
                    Assert.That(SessionState.GetInt(manualKey, absent), Is.EqualTo((int)operation));
                }
            }
            finally
            {
                settings.newModules = previousModules;
                settings.newSides = previousSides;
                if (EditorJsonUtility.ToJson(settings) != projectBeforeTest)
                {
                    EditorJsonUtility.FromJsonOverwrite(projectBeforeTest, settings);
                    settings.newModules = previousModules;
                }
                workingField.SetValue(settings, previousWorking);
                workingFlag.SetValue(settings, previouslyEditing);
                if (hadManualKey) SessionState.SetInt(manualKey, previousManual);
                else SessionState.EraseInt(manualKey);
            }
        }

        static void AssertMatchingProjectDefaults(BrushSettings settings, NewBrushOptions value)
        {
            foreach (var option in typeof(NewBrushOptions).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var projectField = typeof(BrushSettings).GetField(option.Name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(projectField, Is.Not.Null, option.Name);
                Assert.That(option.GetValue(value), Is.EqualTo(projectField.GetValue(settings)), option.Name);
            }
        }

        [Test]
        public void BrushContextMenuSelectsElementsAndTracksCheckedModes()
        {
            string[] labels = { "Выбрать всё", "Инвертировать выбор", "Снять выделение", "Вершины  1", "Рёбра  2", "Грани  3", "Завершить правку" };
            var previousMode = BrushEditState.Mode;
            var restoreSelection = CaptureEditorSelection();
            var context = ScriptableObject.CreateInstance<BrushEditContext>();
            var go = new GameObject("Brush context menu test");
            var brush = go.AddComponent<Brush>();
            try
            {
                brush.shape = BrushShape.Box;
                UnityEditor.Selection.activeGameObject = go;
                BrushEditState.Mode = BrushEditMode.Face;
                var actions = MenuActions(context, labels);
                Assert.That(actions[5].status, Is.EqualTo(DropdownMenuAction.Status.Checked));
                Assert.That(actions[3].status, Is.EqualTo(DropdownMenuAction.Status.Normal));
                actions[3].Execute();
                Assert.That(BrushEditState.Mode, Is.EqualTo(BrushEditMode.Vertex));
                actions = MenuActions(context, labels);
                Assert.That(actions[3].status, Is.EqualTo(DropdownMenuAction.Status.Checked));
                Assert.That(actions[5].status, Is.EqualTo(DropdownMenuAction.Status.Normal));
                actions[0].Execute();
                Assert.That(BrushEditState.Sel(brush).vertices.Count, Is.EqualTo(8));
                actions[1].Execute();
                Assert.That(BrushEditState.Sel(brush).vertices, Is.Empty);
                actions[4].Execute();
                Assert.That(BrushEditState.Mode, Is.EqualTo(BrushEditMode.Edge));
                actions[5].Execute();
                Assert.That(BrushEditState.Mode, Is.EqualTo(BrushEditMode.Face));
            }
            finally
            {
                BrushEditState.Mode = previousMode;
                restoreSelection();
                ForgetSelection(typeof(BrushEditState), brush);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(context);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PointGraphContextMenuTracksModesAndEnablesSelectionActions(bool spline)
        {
            string[] labels = { "Выделить всё", "Инвертировать выделение", "Снять выделение", "Удалить  ⌫", "Плавная / угловая точка  S",
                "Режим точек  1", "Режим стен  2", "Режим комнат  3", "Завершить редактирование" };
            var previousMode = FloorPlanEditState.Mode;
            var restoreSelection = CaptureEditorSelection();
            var context = (PointGraphEditContext)ScriptableObject.CreateInstance(spline ? typeof(SplineEditContext) : typeof(FloorPlanEditContext));
            var go = new GameObject("Point graph context menu test");
            var graph = (PointGraph)go.AddComponent(spline ? typeof(Spline) : typeof(FloorPlan));
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                int a = graph.AddPoint(Vector3.zero);
                int b = graph.AddPoint(Vector3.right * 2f);
                int c = graph.AddPoint(Vector3.right * 4f);
                graph.AddLink(a, b);
                graph.AddLink(b, c);
                UnityEditor.Selection.activeGameObject = go;
                FloorPlanEditState.Mode = BrushEditMode.Vertex;
                var actions = MenuActions(context, labels);
                Assert.That(actions[5].status, Is.EqualTo(DropdownMenuAction.Status.Checked));
                Assert.That(actions[3].status, Is.EqualTo(DropdownMenuAction.Status.Disabled));
                Assert.That(actions[4].status, Is.EqualTo(DropdownMenuAction.Status.Disabled));

                actions[0].Execute();
                Assert.That(FloorPlanEditState.Sel(graph).vertices, Is.EquivalentTo(new[] { 0, 1, 2 }));
                actions[1].Execute();
                Assert.That(FloorPlanEditState.Sel(graph).vertices, Is.Empty);
                FloorPlanEditState.Sel(graph).vertices.Add(0);
                actions = MenuActions(context, labels);
                Assert.That(actions[3].status, Is.EqualTo(DropdownMenuAction.Status.Normal));
                Assert.That(actions[4].status, Is.EqualTo(DropdownMenuAction.Status.Normal));
                actions[4].Execute();
                Assert.That(graph.IsSmooth(a), Is.True);

                actions[6].Execute();
                Assert.That(FloorPlanEditState.Mode, Is.EqualTo(BrushEditMode.Edge));
                actions = MenuActions(context, labels);
                Assert.That(actions[6].status, Is.EqualTo(DropdownMenuAction.Status.Checked));
                actions[7].Execute();
                Assert.That(FloorPlanEditState.Mode, Is.EqualTo(BrushEditMode.Face));
                actions = MenuActions(context, labels);
                Assert.That(actions[7].status, Is.EqualTo(DropdownMenuAction.Status.Checked));
                Assert.That(actions[3].status, Is.EqualTo(DropdownMenuAction.Status.Disabled));
                Assert.That(actions[4].status, Is.EqualTo(DropdownMenuAction.Status.Disabled));

                actions[5].Execute();
                actions = MenuActions(context, labels);
                actions[3].Execute();
                Assert.That(graph.links.Count, Is.EqualTo(1));
            }
            finally
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(undoGroup);
                FloorPlanEditState.Mode = previousMode;
                restoreSelection();
                ForgetSelection(typeof(FloorPlanEditState), graph);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(context);
            }
        }

        static DropdownMenuAction[] MenuActions(EditorToolContext context, string[] labels)
        {
            var menu = new DropdownMenu();
            context.PopulateMenu(menu);
            menu.PrepareForDisplay(null);
            var actions = menu.MenuItems().OfType<DropdownMenuAction>().ToArray();
            Assert.That(actions.Select(action => action.name), Is.EqualTo(labels.Select(BrushLanguage.Tr)));
            return actions;
        }

        static void ForgetSelection(Type state, Component component)
        {
            var field = state.GetField("s_Selection", BindingFlags.Static | BindingFlags.NonPublic);
            (field?.GetValue(null) as IDictionary)?.Remove(component);
        }

        static Action CaptureEditorSelection()
        {
            var selected = UnityEditor.Selection.objects;
            var active = UnityEditor.Selection.activeObject;
            var context = ToolManager.activeContextType;
            var tool = ToolManager.activeToolType;
            return () =>
            {
                UnityEditor.Selection.objects = selected;
                if (active != null && UnityEditor.Selection.activeObject != active)
                    UnityEditor.Selection.objects = selected.OrderByDescending(item => item == active).ToArray();
                if (context != null && ToolManager.activeContextType != context)
                {
                    ActiveEditorTracker.sharedTracker.ForceRebuild();
                    ToolManager.SetActiveContext(context);
                }
                if (tool != null && ToolManager.activeToolType != tool) ToolManager.SetActiveTool(tool);
            };
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ViewDepthUsesCameraSpaceAndKeepsObliqueHits(bool orthographic)
        {
            var go = new GameObject("Camera depth test");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = orthographic;
                camera.nearClipPlane = 0.5f;
                camera.farClipPlane = 10f;
                go.transform.SetPositionAndRotation(new Vector3(4f, 5f, 6f), Quaternion.Euler(21f, -17f, 3f));
                Vector3 Point(float depth) => go.transform.position + go.transform.forward * depth + go.transform.right * 4.8f;
                Assert.That(Vector3.Distance(Point(9f), go.transform.position), Is.GreaterThan(camera.farClipPlane));
                Assert.That(BrushDraw.WithinViewDepth(camera, Point(9f)), Is.True);
                Assert.That(BrushDraw.WithinViewDepth(camera, Point(10.5f)), Is.False);
                Assert.That(BrushDraw.WithinViewDepth(camera, Point(0.25f)), Is.False);
                Assert.That(BrushDraw.WithinViewDepth(camera, Point(-1f)), Is.False);
                Assert.That(BrushDraw.WithinViewDepth(null, Point(10.5f)), Is.True);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void BrushCreationGridRejectsAHitBeyondItsSceneCamera()
        {
            var view = ScriptableObject.CreateInstance<SceneView>();
            try
            {
                var camera = view.camera;
                Assert.That(camera, Is.Not.Null);
                view.pivot = Vector3.zero;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(25f, 0f, 0f));
                var createTool = typeof(BrushCreateTool);
                const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
                var plane = (Plane)createTool.GetMethod("GridPlane", flags).Invoke(null, new object[] { view });
                var origin = camera.transform.position;
                var point = plane.ClosestPointOnPlane(origin + camera.transform.forward * 12f + camera.transform.right * 20f);
                float depth = Vector3.Dot(point - origin, camera.transform.forward);
                Assert.That(depth, Is.GreaterThan(0f));
                var ray = new Ray(origin, (point - origin).normalized);
                var find = createTool.GetMethod("FindGridPlane", flags);
                var args = new object[] { view, ray, Vector3.zero, Vector3.zero };
                camera.nearClipPlane = depth * 0.1f;
                camera.farClipPlane = depth * 2f;
                Assert.That((bool)find.Invoke(null, args), Is.True);
                Assert.That(Vector3.Distance((Vector3)args[2], point), Is.LessThan(0.001f));
                camera.farClipPlane = depth * 0.5f;
                Assert.That((bool)find.Invoke(null, args), Is.False);
            }
            finally { Object.DestroyImmediate(view); }
        }
    }
}
