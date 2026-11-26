using System;
using System.Linq;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CsgBrush.Tests
{
    /// <summary>
    /// Clicks in the Scene view, sent as real mouse events: a click on the level selects the brush under the pointer only
    /// where Unity's own selection would take the click, so every tool keeps the clicks it claims, over brushes too.
    /// </summary>
    public class SceneClickTests
    {
        SceneView m_View; Vector2 m_Offset, m_Seen, m_TargetGui; Func<Vector3> m_Target;
        Brush m_Floor, m_Tower;
        UnityEditor.PivotMode m_PreviousPivotMode;
        PivotRotation m_PreviousPivotRotation;

        [SetUp]
        public void Open()
        {
            m_PreviousPivotMode = Tools.pivotMode;
            m_PreviousPivotRotation = Tools.pivotRotation;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            m_Floor = BrushApi.Create(BrushShape.Box, new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f), Quaternion.identity, null, "Floor");
            m_Tower = BrushApi.Create(BrushShape.Box, new Vector3(4f, 3f, 4f), new Vector3(4f, 6f, 4f), Quaternion.identity, null, "Tower");
            BrushApi.ForceUpdate();
            Selection.activeGameObject = null;
            m_View = EditorWindow.GetWindow<SceneView>();
            m_View.position = new Rect(0f, 0f, 1000f, 700f);
            m_View.LookAt(new Vector3(-1f, 1.5f, 0f), Quaternion.Euler(25f, 30f, 0f), 14f, false, true);
            SceneView.duringSceneGui += Observe;
            m_View.SendEvent(new Event { type = EventType.Repaint });
            m_View.SendEvent(new Event { type = EventType.MouseMove, mousePosition = new Vector2(300f, 300f) });
            m_Offset = m_Seen - new Vector2(300f, 300f);
        }

        [TearDown]
        public void Close()
        {
            SceneView.duringSceneGui -= Observe;
            GUIUtility.hotControl = 0;
            Tools.current = Tool.Move;
            Tools.pivotMode = m_PreviousPivotMode;
            Tools.pivotRotation = m_PreviousPivotRotation;
            Selection.activeGameObject = null;
        }

        void Observe(SceneView view)
        {
            var e = Event.current;
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown) m_Seen = e.mousePosition;
            if (m_Target != null && (e.type == EventType.Layout || e.type == EventType.Repaint)) m_TargetGui = HandleUtility.WorldToGUIPoint(m_Target());
        }

        Vector2 Gui(Vector3 world) => Gui(() => world);

        Vector2 Gui(Func<Vector3> world)
        {
            m_Target = world;
            m_View.SendEvent(new Event { type = EventType.Repaint });
            m_Target = null;
            return m_TargetGui - m_Offset;
        }

        void Send(EventType type, Vector2 at)
        {
            m_View.SendEvent(new Event { type = EventType.Layout, mousePosition = at });
            m_View.SendEvent(new Event { type = type, button = 0, mousePosition = at, clickCount = 1 });
        }

        void MoveTo(Vector2 from, Vector2 to, EventType type = EventType.MouseMove)
        {
            for (int k = 1; k <= 5; k++)
            {
                var m = Vector2.Lerp(from, to, k / 5f);
                m_View.SendEvent(new Event { type = EventType.Layout, mousePosition = m });
                m_View.SendEvent(new Event { type = type, button = 0, mousePosition = m, delta = (to - from) / 5f });
            }
        }

        void Click(Vector2 at) { Send(EventType.MouseDown, at); Send(EventType.MouseUp, at); MoveTo(at, at); }

        [Test]
        public void WcsPicksTheCentreOfTheVisibleTowerFace()
        {
            Selection.activeGameObject = m_Tower.gameObject;
            var face = new Vector3(4f, 3f, 2f);
            ToolManager.SetActiveTool<BrushPivotTool>();
            Click(Gui(face));
            Assert.That(Vector3.Distance(m_Tower.transform.position, face), Is.LessThan(0.001f));
            Assert.That(Tools.current, Is.EqualTo(Tool.Move));
        }

        [TestCase(0f, 0f)]
        [TestCase(1f, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(0f, 1f)]
        [TestCase(0f, -1f)]
        public void MoveToUsesFaceCentreAndItsFourGuides(float x, float y)
        {
            var source = new GameObject("Опора").transform;
            source.rotation = Quaternion.Euler(12f, 30f, -7f);
            var previous = source.rotation;
            Selection.activeGameObject = source.gameObject;
            var face = new Vector3(4f, 3f, 2f);
            ObjectPlacementTool.Begin();
            var start = Gui(face);
            var offset = face + new Vector3(x, y, 0f);
            var end = Gui(offset);
            Click(start);
            MoveTo(start, end);
            Click(end);
            Assert.That(ObjectPlacementTool.Active, Is.False);
            Assert.That(Vector3.Distance(source.position, offset), Is.LessThan(0.04f));
            Assert.That(Quaternion.Angle(previous, source.rotation), Is.LessThan(0.001f));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SurfaceMoveSnapsToAVisibleFaceCentreAndCommitsOrCancels(bool commit)
        {
            bool suspended = SessionState.GetBool(BrushShapeHandles.SuspendedKey, false);
            try
            {
                var rod = BrushApi.Create(BrushShape.Cylinder, new Vector3(-4f, 2f, 0f),
                    new Vector3(0.5f, 2f, 0.5f), Quaternion.identity, snap: false);
                BrushApi.ForceUpdate();
                var original = rod.transform.position;
                Selection.activeGameObject = null;
                Tools.current = Tool.Rotate;
                SurfacePlacementMode.Begin();
                Assert.That(Tools.current, Is.EqualTo(Tool.Move));
                Selection.activeGameObject = rod.gameObject;
                var centre = new Vector3(4f, 3f, 2f);
                var mouse = Gui(centre);
                Click(mouse);
                Assert.That(SurfacePlacementMode.Active, Is.True, "First click opens the offset guide");
                Assert.That(Vector3.Dot(rod.transform.up, Vector3.back), Is.GreaterThan(0.999f));
                Assert.That(Vector3.Distance(rod.transform.position, centre + Vector3.back), Is.LessThan(0.002f));
                if (commit) Click(mouse);
                else Press(KeyCode.Escape);
                Assert.That(SurfacePlacementMode.Active, Is.False);
                if (commit) Undo.PerformUndo();
                Assert.That(Vector3.Distance(rod.transform.position, original), Is.LessThan(0.001f));
                Assert.That(Vector3.Dot(rod.transform.up, Vector3.up), Is.GreaterThan(0.999f));
            }
            finally
            {
                SurfacePlacementMode.Stop();
                if (suspended) SessionState.SetBool(BrushShapeHandles.SuspendedKey, true);
                else SessionState.EraseBool(BrushShapeHandles.SuspendedKey);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SphereStaysRoundWhenConfirmedAtTheRadiusRelease(bool samePositionMouseMove)
        {
            var previousOperation = BrushCreateTool.ManualOperation;
            try
            {
                BrushCreateTool.ManualOperation = BrushOperation.Add;
                BrushMenu.ActivateCreation<CreateSphereBrushTool>();
                var start = Gui(Vector3.zero);
                var end = Gui(new Vector3(2f, 0f, 0f));
                Send(EventType.MouseDown, start);
                MoveTo(start, end, EventType.MouseDrag);
                Send(EventType.MouseUp, end);
                if (samePositionMouseMove) Send(EventType.MouseMove, end);
                Click(end);

                var spheres = Brush.Active.Where(brush => brush.shape == BrushShape.Sphere).ToArray();
                Assert.That(spheres, Has.Length.EqualTo(1));
                var sphere = spheres[0];
                Assert.That(sphere.size.y, Is.EqualTo(sphere.size.x));
                Assert.That(sphere.size.z, Is.EqualTo(sphere.size.x));
                Assert.That(sphere.size.x, Is.GreaterThan(1f));
            }
            finally { BrushCreateTool.ManualOperation = previousOperation; }
        }

        [Test]
        public void MovePlaneReadoutIncludesTheNativePickMarginOutsideTheSquare()
        {
            var oldPivot = m_View.pivot;
            var oldRotation = m_View.rotation;
            float oldSize = m_View.size;
            bool wasOrtho = m_View.orthographic;
            int axes = 0;
            void ObserveHover(SceneView view)
            {
                if (view == m_View && Event.current.rawType == EventType.MouseMove)
                    axes = BrushMoveDisplacement.PickAxes(view.camera, Vector3.zero, Quaternion.identity);
            }
            try
            {
                m_View.LookAt(Vector3.zero, Quaternion.identity, 10f, true, true);
                var edge = Gui(() => new Vector3(0.25f, 0.125f, 0) * HandleUtility.GetHandleSize(Vector3.zero));
                var outward = (Gui(Vector3.right) - Gui(Vector3.zero)).normalized;
                SceneView.duringSceneGui += ObserveHover;
                Send(EventType.MouseMove, edge + outward * 2f);
                Assert.That(axes, Is.EqualTo(3), "the XY square also accepts clicks just beyond its edge");
            }
            finally
            {
                SceneView.duringSceneGui -= ObserveHover;
                m_View.LookAt(oldPivot, oldRotation, oldSize, wasOrtho, true);
            }
        }

        void Press(KeyCode key)
        {
            m_View.SendEvent(new Event { type = EventType.KeyDown, keyCode = key });
            m_View.SendEvent(new Event { type = EventType.KeyUp, keyCode = key });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TransformShortcutsWorkAfterClickingInTheScene(bool editBrush)
        {
            var shortcuts = ShortcutManager.instance;
            string previousProfile = shortcuts.activeProfileId;
            bool wasSuspended = SessionState.GetBool(BrushShapeHandles.SuspendedKey, false);
            try
            {
                shortcuts.activeProfileId = ShortcutManager.defaultProfileId;
                m_View.Focus();
                Click(Gui(new Vector3(3f, 4f, 2f)));
                if (editBrush)
                {
                    ActiveEditorTracker.sharedTracker.ForceRebuild();
                    BrushEditContext.Enter();
                    Click(Gui(new Vector3(3f, 4f, 2f)));
                }
                Tools.current = Tool.Move;
                Press(KeyCode.E);
                Assert.That(Tools.current, Is.EqualTo(Tool.Rotate));
                Press(KeyCode.R);
                Assert.That(Tools.current, Is.EqualTo(Tool.Scale));
                Press(KeyCode.W);
                Assert.That(Tools.current, Is.EqualTo(Tool.Move));
                Assert.That(BrushEditContext.IsActive, Is.EqualTo(editBrush));
            }
            finally
            {
                BrushEditContext.Exit();
                shortcuts.activeProfileId = previousProfile;
                if (wasSuspended) SessionState.SetBool(BrushShapeHandles.SuspendedKey, true);
                else SessionState.EraseBool(BrushShapeHandles.SuspendedKey);
            }
        }

        [Test]
        public void ShapeShortcutDiscardsTheBoxPreviewBeforeHeightIsConfirmed()
        {
            var shortcuts = ShortcutManager.instance;
            string previousProfile = shortcuts.activeProfileId;
            var settings = BrushSettings.instance;
            bool previousPreference = settings.shapeHandles;
            bool wasSuspended = SessionState.GetBool(BrushShapeHandles.SuspendedKey, false);
            try
            {
                shortcuts.activeProfileId = ShortcutManager.defaultProfileId;
                settings.shapeHandles = true;
                m_View.Focus();
                int before = Brushes;
                BrushMenu.ActivateCreation<CreateBoxBrushTool>();
                var start = Gui(new Vector3(-6f, 0f, -2f));
                var finish = Gui(new Vector3(-4f, 0f, 0f));
                Send(EventType.MouseDown, start);
                MoveTo(start, finish, EventType.MouseDrag);
                Send(EventType.MouseUp, finish);
                Assert.That(BrushCreateTool.Active.Drawing, Is.True);
                Press(KeyCode.B);
                Assert.That(BrushCreateTool.Active, Is.Null);
                Assert.That(BrushShapeHandles.Enabled, Is.True);
                Assert.That(Brushes, Is.EqualTo(before));
                Assert.That(GUIUtility.hotControl, Is.Zero);
            }
            finally
            {
                settings.shapeHandles = previousPreference;
                shortcuts.activeProfileId = previousProfile;
                if (wasSuspended) SessionState.SetBool(BrushShapeHandles.SuspendedKey, true);
                else SessionState.EraseBool(BrushShapeHandles.SuspendedKey);
            }
        }

        int Brushes => Object.FindObjectsByType<Brush>(FindObjectsInactive.Exclude).Length;

        /// <summary>A box drawn from the floor: its base dragged at (-6, -2) to (-4, 0), its last click at <paramref name="last"/>.</summary>
        void DrawBox(Vector3 last)
        {
            ToolManager.SetActiveTool<CreateBoxBrushTool>();
            m_View.SendEvent(new Event { type = EventType.Repaint });
            Vector2 a = Gui(new Vector3(-6f, 0f, -2f)), b = Gui(new Vector3(-4f, 0f, 0f));
            m_View.SendEvent(new Event { type = EventType.MouseMove, mousePosition = a });
            Send(EventType.MouseDown, a); MoveTo(a, b, EventType.MouseDrag); Send(EventType.MouseUp, b);
            var c = Gui(last);
            MoveTo(b, c);
            Click(c);
        }

        [Test]
        public void ABoxFinishesWhereverItsLastClickLands([Values("sky", "tower", "floor")] string background)
        {
            var last = background == "sky" ? new Vector3(-5f, 9f, -1f) : background == "tower" ? new Vector3(4f, 4f, 2f) : new Vector3(-8f, 0f, -6f);
            int before = Brushes;
            DrawBox(last);
            Assert.AreEqual(before + 1, Brushes, "the box is made");
            Assert.AreNotEqual(m_Tower.gameObject, Selection.activeGameObject, "the brush behind the last click is not selected");
            Assert.AreNotEqual(m_Floor.gameObject, Selection.activeGameObject);
        }

        [Test]
        public void AClickOnTheLevelSelectsTheBrushUnderThePointer()
        {
            Tools.current = Tool.Move;
            Click(Gui(new Vector3(3f, 4f, 2f)));
            Assert.AreEqual(m_Tower.gameObject, Selection.activeGameObject, "the tower");
            Click(Gui(new Vector3(-8f, 0f, -6f)));
            Assert.AreEqual(m_Floor.gameObject, Selection.activeGameObject, "the floor");
        }

        [Test]
        public void TheMoveGizmoKeepsItsDragOverAnotherBrush()
        {
            Tools.current = Tool.Move;
            Tools.pivotMode = UnityEditor.PivotMode.Pivot;
            Tools.pivotRotation = PivotRotation.Global;
            Selection.activeGameObject = m_Tower.gameObject;
            var at = m_Tower.transform.position;
            Vector3 Arrow() => at + Vector3.right * (HandleUtility.GetHandleSize(at) * 0.85f);
            var arrow = Gui(Arrow);
            var to = Gui(() => Arrow() + Vector3.left * 6f); // dragged back over the floor
            m_View.SendEvent(new Event { type = EventType.MouseMove, mousePosition = arrow });
            Send(EventType.MouseDown, arrow);
            int hotControl = GUIUtility.hotControl;
            MoveTo(arrow, to, EventType.MouseDrag);
            Send(EventType.MouseUp, to);
            MoveTo(to, to);
            Assert.AreEqual(m_Tower.gameObject, Selection.activeGameObject, "still the tower");
            Assert.Less(m_Tower.transform.position.x, at.x - 1f,
                $"moved along the arrow ({arrow} to {to}, control {hotControl})");
        }

        [Test]
        public void DrawingAFloorPlanOrASplineOnABrushStartsItThere([Values(false, true)] bool spline)
        {
            if (spline) ToolManager.SetActiveTool<CreateSplineTool>(); else ToolManager.SetActiveTool<CreateFloorPlanTool>();
            m_View.SendEvent(new Event { type = EventType.Repaint });
            var at = Gui(new Vector3(-6f, 0f, -2f)); // on the floor brush
            m_View.SendEvent(new Event { type = EventType.MouseMove, mousePosition = at });
            Click(at);
            var selected = Selection.activeGameObject;
            Assert.IsNotNull(selected, "something is selected");
            Assert.IsTrue(spline ? selected.GetComponent<Spline>() != null : selected.GetComponent<FloorPlan>() != null, "the new " + (spline ? "spline" : "floor plan") + ", not the floor under it (" + selected.name + ")");
        }

        [Test]
        public void AHoldIsLetGoOnlyAfterEveryToolHadTheRelease()
        {
            EditModeMouse.Take(4251);
            var onRelease = ScheduleRelease();
            Assert.AreEqual(4251, GUIUtility.hotControl, "still held while the release goes round the handlers");
            onRelease();
            Assert.AreEqual(0, GUIUtility.hotControl, "let go once nobody did");
            EditModeMouse.Take(4252);
            onRelease = ScheduleRelease();
            EditModeMouse.Take(4253); // a new press before the editor came round: it keeps its hold
            onRelease();
            Assert.AreEqual(4253, GUIUtility.hotControl);
            GUIUtility.hotControl = 0;
        }

        static EditorApplication.CallbackFunction ScheduleRelease()
        {
            var pending = EditorApplication.delayCall?.GetInvocationList() ?? Array.Empty<Delegate>();
            EditModeMouse.ReleaseStaleAfter(new Event { type = EventType.MouseUp });
            // Batch tests can advance the test enumerator without an Inspector update.
            var scheduled = (EditorApplication.CallbackFunction)EditorApplication.delayCall
                .GetInvocationList().Except(pending).Single();
            EditorApplication.delayCall -= scheduled;
            return scheduled;
        }
    }
}
