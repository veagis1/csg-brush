using static CsgBrush.Editor.BrushLanguage;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>Pure geometry of drawing a brush: a base rectangle on a plane, then a height. Testable without a Scene view.</summary>
    public static class BrushDraw
    {
        /// <summary>Frame of a drawing plane from its (cardinal) normal: local Y is the normal, X and Z lie in the plane.</summary>
        public static Quaternion PlaneRotation(Vector3 normal)
        {
            normal = Cardinal(normal);
            Vector3 right = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.Cross(normal, Vector3.up).normalized;
            Vector3 forward = Vector3.Cross(right, normal);
            return Quaternion.LookRotation(forward, normal);
        }

        /// <summary>
        /// Frame of a drawing plane on a surface at any angle: local Y is the surface normal itself. A sloped floor or
        /// ceiling keeps X level (along the slope's contour) and Z up and down the slope; a steep wall keeps X level too.
        /// A surface that faces a world axis gets the same frame as <see cref="PlaneRotation"/>.
        /// </summary>
        public static Quaternion SurfaceRotation(Vector3 normal)
        {
            normal = normal.normalized;
            var cardinal = Cardinal(normal);
            if (Vector3.Angle(normal, cardinal) < 0.01f) return PlaneRotation(cardinal);
            Vector3 right = Mathf.Abs(normal.y) > 0.5f ? Vector3.Cross(Vector3.up, normal).normalized : Vector3.Cross(normal, Vector3.up).normalized;
            Vector3 forward = Vector3.Cross(right, normal);
            return Quaternion.LookRotation(forward, normal);
        }

        /// <summary>A snapped point put back onto the surface through <paramref name="onSurface"/>, along the surface normal.</summary>
        public static Vector3 OntoSurface(Vector3 snapped, Vector3 onSurface, Vector3 normal)
        {
            normal = normal.normalized;
            return snapped - normal * Vector3.Dot(snapped - onSurface, normal);
        }

        /// <summary>The nearest world axis to a direction.</summary>
        public static Vector3 Cardinal(Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(n.x), 0f, 0f);
            if (ay >= az) return new Vector3(0f, Mathf.Sign(n.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(n.z));
        }

        /// <summary>A point snapped to the grid in the plane's frame (the origin itself is on the grid).</summary>
        public static Vector3 SnapInPlane(Vector3 point, Vector3 origin, Quaternion planeRotation, float grid)
        {
            var local = Quaternion.Inverse(planeRotation) * (point - origin);
            local = BrushSnap.Round(local, grid);
            local.y = 0f;
            return origin + planeRotation * local;
        }

        /// <summary>A point snapped to the grid within its plane: the coordinate along the (cardinal) normal is kept, so a grid moved off the grid steps still holds it.</summary>
        public static Vector3 SnapOnPlane(Vector3 point, Vector3 normal, float grid)
        {
            var snapped = BrushSnap.Round(point, grid);
            var axis = Cardinal(normal);
            axis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            return snapped + Vector3.Scale(axis, point - snapped);
        }

        /// <summary>
        /// Pose of the brush for a base from <paramref name="origin"/> to <paramref name="opposite"/> (both on the plane)
        /// and a signed height along the plane normal. Sizes are positive; a zero extent becomes one grid step.
        /// </summary>
        public static void Pose(Vector3 origin, Vector3 opposite, float height, Quaternion planeRotation, float grid, out Vector3 centre, out Vector3 size, out Quaternion rotation)
        {
            var local = Quaternion.Inverse(planeRotation) * (opposite - origin);
            float dx = local.x, dz = local.z;
            float step = grid > 0f ? grid : 0.5f;
            if (Mathf.Abs(dx) < 1e-6f) dx = step;
            if (Mathf.Abs(dz) < 1e-6f) dz = step;
            if (Mathf.Abs(height) < 1e-6f) height = step;
            size = new Vector3(Mathf.Abs(dx), Mathf.Abs(height), Mathf.Abs(dz));
            centre = origin + planeRotation * new Vector3(dx * 0.5f, height * 0.5f, dz * 0.5f);
            rotation = planeRotation;
        }

        /// <summary>
        /// Pose of a round brush drawn from its base centre: the drag sets the radius, the height the size along the
        /// normal (a sphere with no height is round). The transform sits at the middle of the height.
        /// </summary>
        public static void CentredPose(Vector3 origin, Vector3 opposite, float height, Quaternion planeRotation, float grid, bool sphere, out Vector3 centre, out Vector3 size, out Quaternion rotation)
        {
            var local = Quaternion.Inverse(planeRotation) * (opposite - origin);
            float step = grid > 0f ? grid : 0.5f;
            float radius = Mathf.Max(step, grid > 0f ? BrushSnap.Round(new Vector2(local.x, local.z).magnitude, grid) : new Vector2(local.x, local.z).magnitude);
            float h = height;
            if (Mathf.Abs(h) < 1e-6f) h = sphere ? radius * 2f : step;
            size = new Vector3(radius * 2f, Mathf.Abs(h), radius * 2f);
            centre = origin + planeRotation * new Vector3(0f, h * 0.5f, 0f);
            rotation = planeRotation;
        }

        /// <summary>
        /// Pose of a brush drawn by its direction, as wedges and linear stairs are: a line from <paramref name="start"/> (the
        /// low end) to <paramref name="end"/> (the high end) gives the direction and the length, both snapped (the length to
        /// the grid, the direction to <paramref name="rotationSnap"/> degrees); <paramref name="side"/> is the signed width to
        /// the right of the line (or about it, <paramref name="centred"/>), <paramref name="height"/> the signed height along
        /// the plane normal. The brush's local +z points along the line, so the shape rises towards the end.
        /// </summary>
        public static void DirectedPose(Vector3 start, Vector3 end, float side, bool centred, float height, Quaternion planeRotation, float grid, float rotationSnap,
            out Vector3 centre, out Vector3 size, out Quaternion rotation)
        {
            var line = Quaternion.Inverse(planeRotation) * (end - start);
            float yaw = Mathf.Atan2(line.x, line.z) * Mathf.Rad2Deg;
            if (rotationSnap > 0f) yaw = BrushSnap.Round(yaw, rotationSnap);
            rotation = planeRotation * Quaternion.Euler(0f, yaw, 0f);
            var along = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float step = grid > 0f ? grid : 0.5f;
            float length = Vector3.Dot(new Vector3(line.x, 0f, line.z), along);
            length = Mathf.Max(step, grid > 0f ? BrushSnap.Round(length, grid) : length);
            float width = grid > 0f ? BrushSnap.Round(side, grid) : side;
            if (Mathf.Abs(width) < 1e-6f) width = step;
            if (Mathf.Abs(height) < 1e-6f) height = step;
            size = new Vector3(Mathf.Abs(width), Mathf.Abs(height), length);
            centre = start + rotation * new Vector3(centred ? 0f : width * 0.5f, height * 0.5f, length * 0.5f);
        }

        /// <summary>
        /// Pose of an arch drawn by its span, as <see cref="DirectedPose"/> with the line from one foot to the other: the
        /// same box, turned so the arch's local +x (its width) runs along the line and its depth to the side.
        /// </summary>
        public static void SpanPose(Vector3 start, Vector3 end, float side, bool centred, float height, Quaternion planeRotation, float grid, float rotationSnap,
            out Vector3 centre, out Vector3 size, out Quaternion rotation)
        {
            DirectedPose(start, end, side, centred, height, planeRotation, grid, rotationSnap, out centre, out size, out rotation);
            rotation *= Quaternion.Euler(0f, -90f, 0f); // local x onto the line, local z across it
            size = new Vector3(size.z, size.y, size.x);
        }

        /// <summary>Signed distance along the plane normal from <paramref name="corner"/> to the point on that line nearest the ray.</summary>
        public static float HeightFromRay(Vector3 corner, Vector3 normal, Ray ray)
        {
            // closest points between the line (corner, normal) and the ray
            Vector3 w = corner - ray.origin;
            float a = Vector3.Dot(normal, normal), b = Vector3.Dot(normal, ray.direction), c = Vector3.Dot(ray.direction, ray.direction);
            float d = Vector3.Dot(normal, w), e = Vector3.Dot(ray.direction, w);
            float denom = a * c - b * b;
            if (Mathf.Abs(denom) < 1e-8f) return 0f; // parallel: no height information
            return (b * e - c * d) / denom;
        }
    }

    /// <summary>
    /// Draw a brush the ProBuilder way: press on a surface (a brush face, else the Scene view grid), drag the base rectangle,
    /// release, move the mouse to set the height, click to create. Wedges, linear stairs and arches are drawn by their direction
    /// instead (see <see cref="IsDirected"/>). Escape cancels the current shape or leaves the tool.
    /// One tool per shape; they share a toolbar button with a shape dropdown.
    /// </summary>
    public abstract class BrushCreateTool : EditorTool
    {
        /// <summary>Operation of the brushes drawn next (Project Settings > Brushes).</summary>
        public static BrushOperation Operation
        {
            get => BrushSettings.instance.newOperation;
            set { BrushSettings.instance.newOperation = value; ManualOperation = value; }
        }

        internal static BrushOperation? ManualOperation
        {
            get
            {
                int value = SessionState.GetInt("CsgBrush.Create.ManualOperation", -1);
                return value < 0 ? null : (BrushOperation)value;
            }
            set => SessionState.SetInt("CsgBrush.Create.ManualOperation", value.HasValue ? (int)value.Value : -1);
        }

        internal static BrushOperation ResolveOperation(BrushOperation? manual, bool target, float inwardAxisDepth) =>
            manual ?? (target && inwardAxisDepth < -0.0001f ? BrushOperation.Subtract : BrushOperation.Add);

        /// <summary>The Create tool that is active, if any.</summary>
        public static BrushCreateTool Active { get; private set; }

        public abstract BrushShape Shape { get; }
        /// <summary>Exactly what the toolbar shows: "Браш «Блок»", "Браш «Изогнутая лестница»".</summary>
        public abstract string Title { get; }

        enum State { Idle, Base, Width, Height }
        State state;
        Vector3 origin, opposite, normal; Quaternion planeRotation; float height;
        float side; bool centred;
        Vector3 hoverPoint, hoverNormal, surfaceNormal;
        bool hoverValid, pickingTarget, hadDrawTarget;
        BrushSurfaceGrid.Frame hoverGrid, drawGrid;
        BrushCutTarget hoverTarget, drawTarget, selectedTarget;
        BrushOperation? drawManual;
        readonly BrushCutPreview cutPreview = new BrushCutPreview();
        int controlId;
        // Not cached: Unity keeps tool instances across domain reloads, and a cached GUIContent would keep an old tooltip.
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get(Shape.ToString()), Title);

        public override void OnActivated() { BrushMenu.PrepareCreation(); ResetGesture(); Active = this; }
        public override void OnWillBeDeactivated() { ResetGesture(); if (Active == this) Active = null; }

        internal bool Drawing => state != State.Idle;
        BrushOperation DrawOperation => ResolveOperation(drawManual, hadDrawTarget,
            height * Vector3.Dot(normal, surfaceNormal));

        internal void ResetGesture()
        {
            if (controlId != 0 && GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
            state = State.Idle;
            pickingTarget = hoverValid = m_OpeningValid = false;
            hoverTarget = drawTarget = default;
            hoverGrid = drawGrid = default;
        }

        internal void DrawOperationPanel()
        {
            using (new EditorGUI.DisabledScope(Drawing))
            using (new EditorGUILayout.HorizontalScope())
            {
                var manual = ManualOperation;
                if (GUILayout.Toggle(manual == BrushOperation.Add, new GUIContent(Tr("+ добавить"),
                        Tr("Обычный объём. Повторный щелчок возвращает определение операции по направлению.")),
                        EditorStyles.miniButtonLeft) != (manual == BrushOperation.Add))
                    ManualOperation = manual == BrushOperation.Add ? null : BrushOperation.Add;
                if (GUILayout.Toggle(manual == BrushOperation.Subtract, new GUIContent(Tr("- вычесть"),
                        Tr("Вырез в указанном объекте. Повторный щелчок возвращает определение операции по направлению.")),
                        EditorStyles.miniButtonRight) != (manual == BrushOperation.Subtract))
                    ManualOperation = manual == BrushOperation.Subtract ? null : BrushOperation.Subtract;
                if (manual != ManualOperation)
                {
                    pickingTarget = false;
                    hoverValid = false;
                    SceneView.RepaintAll();
                }
            }
            if (ManualOperation != BrushOperation.Subtract || Drawing) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                string title = selectedTarget.Valid ? Tr("Цель: ") + selectedTarget.Parent.name : Tr("Выбрать цель");
                if (GUILayout.Button(new GUIContent(title, Tr("Укажите браш или стену. Без выбора цель берётся с грани начала жеста.")),
                        EditorStyles.miniButton))
                {
                    pickingTarget = true;
                    hoverTarget = default;
                    SceneView.RepaintAll();
                }
                if (selectedTarget.Valid && GUILayout.Button(Tr("Сбросить"), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
                    selectedTarget = default;
                    pickingTarget = false;
                    SceneView.RepaintAll();
                }
            }
        }

        void PickTarget(Event e)
        {
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                pickingTarget = false;
                e.Use();
                SceneView.RepaintAll();
                return;
            }
            if (e.type == EventType.Repaint) hoverTarget.Draw();
            if (e.type != EventType.MouseMove && !(e.type == EventType.MouseDown && e.button == 0)) return;
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            hoverTarget = BrushCutTarget.FromBrush(BrushHooks.PickConstructionSurface(ray, out _, out _));
            if (e.type == EventType.MouseDown && HandleUtility.nearestControl == controlId)
            {
                if (hoverTarget.Valid)
                {
                    selectedTarget = hoverTarget;
                    pickingTarget = false;
                }
                e.Use();
            }
            SceneView.RepaintAll();
        }

        void UpdateHover(SceneView view, Vector2 mouse)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var surface = BrushHooks.PickConstructionSurface(ray, out var point, out var faceNormal);
            var hit = point;
            var workingNormal = faceNormal;
            hoverValid = BrushSurfaceGrid.TryDrawingFrame(ray, surface, ref point, ref workingNormal, out hoverGrid) ||
                (!BrushSurfaceGrid.HasHeld && (surface != null || FindGridPlane(view, ray, out point, out workingNormal)));
            bool onSurface = surface != null && Mathf.Abs(Vector3.Dot(point - hit, faceNormal)) < 0.001f &&
                Mathf.Abs(Vector3.Dot(workingNormal, faceNormal)) > 0.999f;
            var selected = ManualOperation == BrushOperation.Subtract && selectedTarget.Valid &&
                StageUtility.GetCurrentStageHandle().Contains(selectedTarget.Parent.gameObject);
            hoverTarget = selected ? selectedTarget : onSurface ? BrushCutTarget.FromBrush(surface) : default;
            if (ManualOperation == BrushOperation.Subtract && !hoverTarget.Valid) hoverValid = false;
            if (!hoverValid) return;
            surfaceNormal = onSurface ? faceNormal : Vector3.zero;
            hoverNormal = hoverGrid.Valid || AlignsToSurface || onSurface ? workingNormal : BrushDraw.Cardinal(workingNormal);
            if (!hoverGrid.Valid) hoverGrid = BrushSurfaceGrid.Frame.OnPlane(point, hoverNormal);
            hoverPoint = BrushDraw.SnapInPlane(point, hoverGrid.Origin, hoverGrid.Rotation, Grid);
        }

        /// <summary>Step sizes and wall thickness for new brushes: the settings, or grid-derived defaults when left at 0.</summary>
        public static void NewBrushParameters(out float stepHeight, out float wallThickness)
        {
            var s = BrushSettings.instance;
            stepHeight = s.NewStepHeightMeters;
            wallThickness = s.newWallThickness > 0f ? s.newWallThickness : s.GridMeters;
        }

        /// <summary>Curved and spiral stairs are placed by their axis: press on the axis, drag the outer radius, then the height.</summary>
        public static bool IsRadial(BrushShape shape) => shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs;

        /// <summary>
        /// Wedges, linear stairs and arches are drawn by their direction, in three steps: press at the low end and drag to
        /// the high end (direction and length; an arch's span, from foot to foot), click for the width (an arch's depth; to
        /// the side of the line the pointer is on, Shift centres it on the line), click for the height.
        /// </summary>
        public static bool IsDirected(BrushShape shape) => shape == BrushShape.Wedge || shape == BrushShape.Stairs || shape == BrushShape.Arch;

        /// <summary>Round brushes are drawn from the centre of their base: press on the centre, drag the radius, then the height.</summary>
        public static bool IsCentred(BrushShape shape) => shape == BrushShape.Cylinder || shape == BrushShape.Cone || shape == BrushShape.Sphere;

        /// <summary>Parameters of a radial shape drawn with an outer radius and a total height: step width from the radius, the number of steps from the height and the step height, the rest from the panel.</summary>
        public static BrushGeometry.ShapeParams ParametersForRadial(BrushShape shape, float outerRadius, float height)
        {
            var s = BrushSettings.instance;
            float grid = s.GridMeters, step = grid > 0f ? grid : 0.01f;
            var p = ParametersFor(shape, new Vector3(outerRadius, height, outerRadius));
            var st = p.stairs;
            st.stepWidth = Mathf.Max(step, BrushSnap.Round(outerRadius - st.innerRadius, grid));
            st.numSteps = BrushPolyhedron.StepCount(height, st.stepHeight);
            p.stairs = st;
            return p;
        }

        /// <summary>
        /// The parameters a brush drawn with the given size gets. Stairs take their number of steps from the drawn height and
        /// the step height; curved and spiral stairs take their step width from the drawn footprint.
        /// </summary>
        public static BrushGeometry.ShapeParams ParametersFor(BrushShape shape, Vector3 size)
        {
            var s = BrushSettings.instance;
            NewBrushParameters(out float stepHeight, out _);
            float grid = s.GridMeters;
            var p = BrushGeometry.ShapeParams.Default(size, s.newSides);
            p.tessellation = s.newTessellation; p.stepHeight = stepHeight;
            var st = p.stairs;
            st.innerRadius = s.newInnerRadius > 0f ? s.newInnerRadius : grid;
            st.stepThickness = s.newStepThickness > 0f ? s.newStepThickness : BrushSettings.DefaultStepThicknessMeters;
            st.curveAngle = Mathf.Max(1f, s.newCurveAngle); st.stepsPer360 = Mathf.Max(1, s.newStepsPer360);
            st.counterClockwise = s.newCounterClockwise; st.slopedFloor = s.newSlopedFloor; st.slopedCeiling = s.newSlopedCeiling;
            st.open = (shape == BrushShape.Stairs || shape == BrushShape.CurvedStairs) && !s.newSupportUnderSteps;
            if (shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs)
            {
                // steps from the drawn height; step width so the footprint's x extent matches the drawn one
                st.stepHeight = stepHeight;
                st.numSteps = BrushPolyhedron.StepCount(size.y, stepHeight);
                st.stepWidth = 1f;
                var trial = shape == BrushShape.CurvedStairs ? BrushPolyhedron.CurvedStairs(st) : BrushPolyhedron.SpiralStairs(st);
                float trialX = trial.Bounds().size.x, ro = st.innerRadius + 1f;
                float targetRo = trialX > 1e-4f ? ro * size.x / trialX : ro;
                st.stepWidth = Mathf.Max(grid > 0f ? grid : 0.01f, BrushSnap.Round(targetRo - st.innerRadius, grid));
            }
            if (shape == BrushShape.Arch) { st.curveAngle = Mathf.Clamp(s.newArchAngle, 1f, 180f); NewBrushParameters(out _, out p.wallThickness); }
            p.stairs = st;
            return p;
        }

        static float Grid => BrushSettings.instance.snapToGrid ? BrushSettings.instance.GridMeters : 0f;

        /// <summary>
        /// A new brush is drawn square to the surface it starts on, slopes included, unless it goes onto the grid: with
        /// snapping on, it stays square to the world, as the grid would turn it back anyway.
        /// </summary>
        static bool AlignsToSurface => !BrushSettings.instance.snapToGrid || BrushSettings.instance.newGridSnap == GridSnap.Off;

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView view) || BrushSurfaceGrid.Picking) return;
            var e = Event.current;
            controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);
            if (e.alt || Tools.viewToolActive) return;
            if (IsOpening(Shape)) { OpeningGUI(e); return; }
            if (pickingTarget) { PickTarget(e); return; }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (state == State.Idle) ToolManager.RestorePreviousPersistentTool();
                ResetGesture(); e.Use(); SceneView.RepaintAll(); return;
            }
            if (state == State.Height && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space))
            {
                Finish(); e.Use(); return;
            }

            switch (state)
            {
                case State.Idle:
                    if (e.type == EventType.MouseMove || e.type == EventType.MouseDown ||
                        (e.type == EventType.Repaint && !hoverValid && EditorWindow.mouseOverWindow == view))
                    {
                        UpdateHover(view, e.mousePosition);
                        if (e.type == EventType.MouseMove) SceneView.RepaintAll();
                    }
                    if (e.type == EventType.MouseDown && e.button == 0 && hoverValid && HandleUtility.nearestControl == controlId)
                    {
                        EditModeMouse.Take(controlId);
                        normal = hoverNormal;
                        drawGrid = hoverGrid;
                        drawTarget = hoverTarget;
                        hadDrawTarget = hoverTarget.Valid;
                        drawManual = ManualOperation;
                        planeRotation = drawGrid.Valid ? drawGrid.Rotation : BrushDraw.SurfaceRotation(normal);
                        origin = hoverPoint; opposite = origin; height = 0f; side = 0f; centred = false; // a new brush starts at the default width, not the last one
                        state = State.Base; e.Use();
                    }
                    if (e.type == EventType.Repaint && hoverValid)
                    {
                        BrushSurfaceGrid.Draw(hoverGrid);
                        if (ManualOperation == BrushOperation.Subtract) hoverTarget.Draw();
                        using (new Handles.DrawingScope(Color.white))
                            Handles.DotHandleCap(-1, hoverPoint, Quaternion.identity, HandleUtility.GetHandleSize(hoverPoint) * 0.04f, EventType.Repaint);
                    }
                    break;

                case State.Base:
                    if (e.type == EventType.MouseDrag && e.button == 0)
                    {
                        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                        var plane = new Plane(normal, origin);
                        if (plane.Raycast(ray, out float t))
                            opposite = BrushDraw.SnapInPlane(ray.GetPoint(t), drawGrid.Valid ? drawGrid.Origin : origin, planeRotation, Grid);
                        e.Use(); SceneView.RepaintAll();
                    }
                    if (e.type == EventType.MouseUp && e.button == 0)
                    {
                        GUIUtility.hotControl = 0; e.Use();
                        var local = Quaternion.Inverse(planeRotation) * (opposite - origin);
                        float min = Grid > 0f ? Grid * 0.5f : 0.05f;
                        bool tooSmall = IsRadial(Shape) || IsCentred(Shape) ? new Vector2(local.x, local.z).magnitude < min : Mathf.Abs(local.x) < min && Mathf.Abs(local.z) < min;
                        if (tooSmall) { ResetGesture(); break; } // a click, not a drag
                        if (IsDirected(Shape)) { state = State.Width; side = 0f; centred = false; break; }
                        state = State.Height; height = 0f;
                    }
                    if (e.type == EventType.Repaint) DrawPreview();
                    break;

                case State.Width:
                    // also on the release: Shift pressed or let go without moving the mouse still counts
                    if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || (e.type == EventType.MouseUp && e.button == 0))
                    {
                        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                        var plane = new Plane(normal, origin);
                        if (plane.Raycast(ray, out float t))
                        {
                            BrushDraw.DirectedPose(origin, opposite, 0f, false, 0f, planeRotation, Grid, BrushSettings.instance.rotationSnapDegrees, out _, out _, out var r);
                            float offset = Vector3.Dot(ray.GetPoint(t) - origin, r * Vector3.right);
                            centred = e.shift;
                            side = centred ? Mathf.Abs(offset) * 2f : offset;
                        }
                        if (e.type != EventType.MouseUp) { e.Use(); SceneView.RepaintAll(); }
                    }
                    if (e.type == EventType.MouseDown && e.button == 0) { EditModeMouse.Take(controlId); e.Use(); }
                    if (e.type == EventType.MouseUp && e.button == 0) { GUIUtility.hotControl = 0; state = State.Height; height = 0f; e.Use(); }
                    if (e.type == EventType.Repaint) DrawPreview();
                    break;

                case State.Height:
                    if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
                    {
                        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                        float h = BrushDraw.HeightFromRay(IsRadial(Shape) || IsCentred(Shape) ? origin : opposite, normal, ray);
                        height = Grid > 0f ? BrushSnap.Round(h, Grid) : h;
                        e.Use(); SceneView.RepaintAll();
                    }
                    if (e.type == EventType.MouseDown && e.button == 0) { EditModeMouse.Take(controlId); e.Use(); }
                    if (e.type == EventType.MouseUp && e.button == 0) { GUIUtility.hotControl = 0; Finish(); e.Use(); }
                    if (e.type == EventType.Repaint) DrawPreview();
                    break;
            }
        }

        /// <summary>Doors and windows are placed with one click, at a size of their own: on a floor plan's wall, into a brush's side, or on the floor.</summary>
        public static bool IsOpening(BrushShape shape) => shape == BrushShape.Door || shape == BrushShape.Window;

        // not kept across a domain reload (Unity keeps tool instances and would restore the flag without the placement)
        [NonSerialized] WallAnchors.Placement m_Opening; [NonSerialized] bool m_OpeningValid;

        void OpeningGUI(Event e)
        {
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { ToolManager.RestorePreviousPersistentTool(); e.Use(); return; }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            {
                m_OpeningValid = WallAnchors.Find(HandleUtility.GUIPointToWorldRay(e.mousePosition), Shape, out m_Opening);
                if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            }
            if (e.type == EventType.MouseDown && e.button == 0 && m_OpeningValid && HandleUtility.nearestControl == controlId)
            {
                var brush = WallAnchors.Create(Shape, m_Opening);
                BrushApi.ForceUpdate();
                Selection.activeGameObject = brush.gameObject;
                e.Use();
            }
            var q = m_Opening.rotation;
            if (e.type == EventType.Repaint && m_OpeningValid && q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > 0.5f)
            {
                BrushSurfaceGrid.Draw(m_Opening.grid);
                if (m_Opening.drawingPoint is Vector3 start)
                    using (new Handles.DrawingScope(Color.white))
                        Handles.DotHandleCap(-1, start, Quaternion.identity, HandleUtility.GetHandleSize(start) * 0.04f, EventType.Repaint);
                var poly = BrushPolyhedron.Box(m_Opening.size);
                var m = Matrix4x4.TRS(m_Opening.position, m_Opening.rotation, Vector3.one);
                Handles.color = m_Opening.plan != null ? new Color(1f, 0.78f, 0.2f, 0.95f) : new Color(1f, 0.4f, 0.2f, 0.9f); // on a plan's wall: the plan's colour
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                foreach (var face in poly.faces)
                    for (int i = 0; i < face.indices.Length; i++)
                        Handles.DrawLine(m.MultiplyPoint3x4(poly.vertices[face.indices[i]]), m.MultiplyPoint3x4(poly.vertices[face.indices[(i + 1) % face.indices.Length]]), 2f);
            }
        }

        /// <summary>The surface under the mouse: a brush face, else the Scene view grid (see <see cref="GridPlane"/>). Nothing past the grid's horizon.</summary>
        internal static bool FindPlane(Vector2 mouse, out Vector3 point, out Vector3 normal)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var surface = BrushHooks.PickConstructionSurface(ray, out point, out normal);
            if (BrushSurfaceGrid.TryDrawingFrame(ray, surface, ref point, ref normal, out _)) return true;
            if (BrushSurfaceGrid.HasHeld) return false;
            if (surface != null) return true;
            return FindGridPlane(SceneView.currentDrawingSceneView ?? SceneView.lastActiveSceneView, ray, out point, out normal);
        }

        static bool FindGridPlane(SceneView view, Ray ray, out Vector3 point, out Vector3 normal)
        {
            if (view == null) { point = Vector3.zero; normal = Vector3.up; return false; }
            var plane = GridPlane(view);
            normal = plane.normal;
            if (plane.Raycast(ray, out float t) && t > 0f) { point = ray.GetPoint(t); return true; }
            point = Vector3.zero; return false;
        }

        /// <summary>
        /// The plane to draw on in empty space, as ProBuilder does: the grid the Scene view shows (its axis and position),
        /// facing the camera; with the grid hidden, a plane through the view pivot, horizontal unless the view looks level.
        /// </summary>
        static Plane GridPlane(SceneView view)
        {
            var camera = view.camera.transform;
            if (view.showGrid || EditorSnapSettings.gridSnapActive)
            {
                GridAxisAndPivot(view, out var normal, out var pivot);
                if (Vector3.Dot(camera.forward, normal) > 0f) normal = -normal;
                var grid = new Plane(normal, pivot);
                if (grid.GetSide(camera.position)) return grid;
            }
            var f = camera.forward;
            var n = Mathf.Abs(f.y) >= 0.02f ? Vector3.up : Mathf.Abs(f.x) > Mathf.Abs(f.z) ? Vector3.right : Vector3.forward;
            if (Vector3.Dot(f, n) > 0f) n = -n;
            return new Plane(n, Grid > 0f ? BrushSnap.Round(view.pivot, Grid) : view.pivot);
        }

        static PropertyInfo s_Grids, s_GridAxis;
        static MethodInfo s_GridPivot;

        /// <summary>The Scene view grid's axis and position. Unity keeps them internal; without them the grid is the floor through the origin.</summary>
        static void GridAxisAndPivot(SceneView view, out Vector3 normal, out Vector3 pivot)
        {
            normal = Vector3.up; pivot = Vector3.zero;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                s_Grids ??= typeof(SceneView).GetProperty("sceneViewGrids", flags);
                var grids = s_Grids?.GetValue(view);
                if (grids == null) return;
                s_GridAxis ??= grids.GetType().GetProperty("gridAxis", flags);
                s_GridPivot ??= grids.GetType().GetMethod("GetPivot", flags);
                if (s_GridAxis == null || s_GridPivot == null) return;
                var axis = s_GridAxis.GetValue(grids);
                int a = System.Convert.ToInt32(axis); // X, Y, Z, All
                normal = a == 0 ? Vector3.right : a == 2 ? Vector3.forward : Vector3.up;
                pivot = (Vector3)s_GridPivot.Invoke(grids, new[] { axis });
            }
            catch (System.Exception) { normal = Vector3.up; pivot = Vector3.zero; }
        }

        void CurrentPose(out Vector3 centre, out Vector3 size, out Quaternion rotation)
        {
            if (IsRadial(Shape))
            {
                // the axis is the press point; the drag sets the outer radius and where the first step starts
                var local = Quaternion.Inverse(planeRotation) * (opposite - origin);
                float radius = new Vector2(local.x, local.z).magnitude;
                float yaw = -Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
                if (radius < 1e-4f) yaw = 0f;
                yaw = BrushSnap.Round(yaw, BrushSettings.instance.rotationSnapDegrees);
                centre = origin + normal * Mathf.Min(0f, height);
                size = new Vector3(Mathf.Max(Grid, BrushSnap.Round(radius, Grid)), Mathf.Abs(height), 0f);
                rotation = planeRotation * Quaternion.Euler(0f, yaw, 0f);
                return;
            }
            if (IsCentred(Shape)) { BrushDraw.CentredPose(origin, opposite, height, planeRotation, Grid, Shape == BrushShape.Sphere, out centre, out size, out rotation); return; }
            if (Shape == BrushShape.Arch) { BrushDraw.SpanPose(origin, opposite, side, centred, height, planeRotation, Grid, BrushSettings.instance.rotationSnapDegrees, out centre, out size, out rotation); return; }
            if (IsDirected(Shape)) { BrushDraw.DirectedPose(origin, opposite, side, centred, height, planeRotation, Grid, BrushSettings.instance.rotationSnapDegrees, out centre, out size, out rotation); return; }
            BrushDraw.Pose(origin, opposite, height, planeRotation, Grid, out centre, out size, out rotation);
        }

        BrushGeometry.ShapeParams CurrentParameters(Vector3 size) => IsRadial(Shape) ? ParametersForRadial(Shape, size.x, size.y) : ParametersFor(Shape, size);

        void DrawPreview()
        {
            BrushSurfaceGrid.Draw(drawGrid);
            CurrentPose(out var centre, out var size, out var rotation);
            var poly = BrushGeometry.ShapePolyhedron(Shape, CurrentParameters(size));
            var m = Matrix4x4.TRS(centre, rotation, Vector3.one);
            bool cut = DrawOperation == BrushOperation.Subtract;
            Handles.color = cut ? new Color(1f, 0.4f, 0.2f, 0.9f) : new Color(0.3f, 0.8f, 1f, 0.9f);
            var previousDepth = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            foreach (var face in poly.faces)
                for (int i = 0; i < face.indices.Length; i++)
                    Handles.DrawLine(m.MultiplyPoint3x4(poly.vertices[face.indices[i]]), m.MultiplyPoint3x4(poly.vertices[face.indices[(i + 1) % face.indices.Length]]), 2f);
            Handles.zTest = previousDepth;
            if (cut && drawTarget.Valid)
            {
                drawTarget.Draw();
                if (Mathf.Abs(height) >= 0.001f)
                {
                    BrushPolyhedron inner = null;
                    var settings = BrushSettings.instance;
                    if (settings.newHollow && (Shape == BrushShape.Box || Shape == BrushShape.Cylinder))
                    {
                        NewBrushParameters(out _, out float wall);
                        if (Shape == BrushShape.Box) inner = BrushPolyhedron.Box(Vector3.Max(size - Vector3.one * (2f * wall), Vector3.one * 0.001f));
                        else inner = BrushPolyhedron.Prism(new Vector3(Mathf.Max(size.x - 2f * wall, 0.001f), size.y * 1.001f, Mathf.Max(size.z - 2f * wall, 0.001f)), CurrentParameters(size).sides, 1f);
                    }
                    cutPreview.Draw(drawTarget, poly, m, inner: inner);
                }
            }
            if (BrushSettings.instance.showDimensions)
                BrushShapeDrawing.DrawDimensions(poly.Bounds(), m, state == State.Height);
        }

        void Finish()
        {
            if (Mathf.Abs(height) < 0.001f) return;
            bool cut = DrawOperation == BrushOperation.Subtract;
            if (cut && (!drawTarget.Valid || (drawTarget.Plan != null && !drawTarget.TryWall(out _))))
            {
                ResetGesture();
                SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(Tr("Укажите цель вычета заново.")));
                return;
            }
            CurrentPose(out var centre, out var size, out var rotation);
            int group = Undo.GetCurrentGroup();
            var parent = cut ? drawTarget.Parent : BrushMenu.CreationParent();
            var brush = CreateDrawnBrush(Shape, origin, centre, size, rotation, parent);
            if (drawGrid.Owner != null) drawGrid.Store(brush);
            if (cut) drawTarget.Assign(brush);
            if (cut && drawTarget.Plan != null) WallAnchors.AttachCutToWall(brush, drawTarget);
            Undo.CollapseUndoOperations(group);
            BrushApi.ForceUpdate();
            Selection.activeGameObject = brush.gameObject;
            ResetGesture();
            SceneView.RepaintAll();
        }

        // The preview has already applied the drawing grid. Accept that pose before the normal move/resize snap pass.
        internal static Brush CreateDrawnBrush(BrushShape shape, Vector3 start, Vector3 centre, Vector3 size, Quaternion rotation, Transform parent = null)
        {
            var s = BrushSettings.instance;
            var brush = BrushApi.Create(shape, centre, size, rotation, parent, snap: false);
            NewBrushParameters(out _, out float wall);
            var p = IsRadial(shape) ? ParametersForRadial(shape, size.x, size.y) : ParametersFor(shape, size);
            Undo.RecordObject(brush, Tr("Создать браш"));
            brush.sides = p.sides; brush.tessellation = p.tessellation;
            brush.stepHeight = shape == BrushShape.Stairs ? p.stepHeight : p.stairs.stepHeight;
            brush.innerRadius = p.stairs.innerRadius; brush.stepWidth = p.stairs.stepWidth; brush.stepThickness = p.stairs.stepThickness;
            brush.curveAngle = p.stairs.curveAngle; brush.numSteps = p.stairs.numSteps; brush.stepsPer360 = p.stairs.stepsPer360;
            brush.counterClockwise = p.stairs.counterClockwise; brush.slopedFloor = p.stairs.slopedFloor; brush.slopedCeiling = p.stairs.slopedCeiling;
            brush.supportUnderSteps = !p.stairs.open;
            if (brush.SupportsHollow && s.newHollow) { brush.hollow = true; brush.wallThickness = wall; }
            brush.gridSnap = s.newGridSnap;
            brush.exactSizeMeters = size;
            BrushApi.SetDrawingPivot(brush, start);
            BrushSnap.Accept(brush);
            brush.transform.hasChanged = false;
            BrushSync.Ensure(brush);
            return brush;
        }
    }

    /// <summary>
    /// The package's icons (Brushes/Editor/Icons, drawn by Tools~/icons/generate_icons.py): the d_ variant on the dark theme.
    /// </summary>
    static class BrushIcons
    {
        public const string Folder = "Packages/digital.dream.csgbrush/Brushes/Editor/Icons/";
        /// <summary>
        /// The icon, or null while it isn't imported yet: after a package update, the scripts can reload before new icons
        /// are imported. Null without an error, so the caller can try again.
        /// </summary>
        public static Texture2D Get(string name)
        {
            string key = (EditorGUIUtility.isProSkin ? "d_" : "") + name;
            if (s_Loaded.TryGetValue(key, out var icon) && icon != null) return icon; // asked for on every repaint
            icon = EditorGUIUtility.isProSkin ? AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "d_" + name + ".png") : null;
            if (icon == null) icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + name + ".png");
            if (icon != null) s_Loaded[key] = icon;
            return icon;
        }

        static readonly System.Collections.Generic.Dictionary<string, Texture2D> s_Loaded = new System.Collections.Generic.Dictionary<string, Texture2D>();
    }

    [EditorTool("Браш «Блок»", variantGroup = typeof(BrushCreateTool), variantPriority = 0)]
    public sealed class CreateBoxBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Box;
        public override string Title => Tr("Браш «Блок»");
        [MenuItem(BrushCommandMenu.CreateRoot + "Блок", false, 1)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Блок", false, 1)] static void Menu() => BrushMenu.ActivateCreation<CreateBoxBrushTool>();
    }

    [EditorTool("Браш «Клин»", variantGroup = typeof(BrushCreateTool), variantPriority = 1)]
    public sealed class CreateWedgeBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Wedge;
        public override string Title => Tr("Браш «Клин»");
        [MenuItem(BrushCommandMenu.CreateRoot + "Клин", false, 2)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Клин", false, 2)] static void Menu() => BrushMenu.ActivateCreation<CreateWedgeBrushTool>();
    }

    [EditorTool("Браш «Цилиндр»", variantGroup = typeof(BrushCreateTool), variantPriority = 2)]
    public sealed class CreateCylinderBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Cylinder;
        public override string Title => Tr("Браш «Цилиндр»");
        [MenuItem(BrushCommandMenu.CreateRoot + "Цилиндр", false, 3)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Цилиндр", false, 3)] static void Menu() => BrushMenu.ActivateCreation<CreateCylinderBrushTool>();
    }

    [EditorTool("Браш «Конус»", variantGroup = typeof(BrushCreateTool), variantPriority = 3)]
    public sealed class CreateConeBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Cone;
        public override string Title => Tr("Браш «Конус»");
        [MenuItem(BrushCommandMenu.CreateRoot + "Конус", false, 4)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Конус", false, 4)] static void Menu() => BrushMenu.ActivateCreation<CreateConeBrushTool>();
    }

    [EditorTool("Браш «Сфера»", variantGroup = typeof(BrushCreateTool), variantPriority = 4)]
    public sealed class CreateSphereBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Sphere;
        public override string Title => Tr("Браш «Сфера»");
        [MenuItem(BrushCommandMenu.CreateRoot + "Сфера", false, 5)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Сфера", false, 5)] static void Menu() => BrushMenu.ActivateCreation<CreateSphereBrushTool>();
    }

    [EditorTool("Браш «Прямая лестница»", variantGroup = typeof(BrushCreateTool), variantPriority = 5)]
    public sealed class CreateStairsBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Stairs;
        public override string Title => Tr("Браш «Прямая лестница»");
        [MenuItem(BrushCommandMenu.CreateRoot + "Прямая лестница", false, 6)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Прямая лестница", false, 6)] static void Menu() => BrushMenu.ActivateCreation<CreateStairsBrushTool>();
    }

    [EditorTool("Браш «Изогнутая лестница»", variantGroup = typeof(BrushCreateTool), variantPriority = 6)]
    public sealed class CreateCurvedStairsBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.CurvedStairs;
        public override string Title => Tr("Браш «Изогнутая лестница»");
        [MenuItem(BrushCommandMenu.CreateRoot + "Изогнутая лестница", false, 7)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Изогнутая лестница", false, 7)] static void Menu() => BrushMenu.ActivateCreation<CreateCurvedStairsBrushTool>();
    }

    [EditorTool("Браш «Винтовая лестница»", variantGroup = typeof(BrushCreateTool), variantPriority = 7)]
    public sealed class CreateSpiralStairsBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.SpiralStairs;
        public override string Title => Tr("Браш «Винтовая лестница»");
        [MenuItem(BrushCommandMenu.CreateRoot + "Винтовая лестница", false, 8)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Винтовая лестница", false, 8)] static void Menu() => BrushMenu.ActivateCreation<CreateSpiralStairsBrushTool>();
    }

    [EditorTool("Браш «Арка»", variantGroup = typeof(BrushCreateTool), variantPriority = 8)]
    public sealed class CreateArchBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Arch;
        public override string Title => Tr("Браш «Арка»");
        [MenuItem(BrushCommandMenu.CreateRoot + "Арка", false, 9)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Арка", false, 9)] static void Menu() => BrushMenu.ActivateCreation<CreateArchBrushTool>();
    }

    [EditorTool("Браш «Дверь»", variantGroup = typeof(BrushCreateTool), variantPriority = 9)]
    public sealed class CreateDoorBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Door;
        public override string Title => Tr("Браш «Дверь»");
        [MenuItem(BrushCommandMenu.CreateRoot + "Дверь", false, 10)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Дверь", false, 10)] static void Menu() => BrushMenu.ActivateCreation<CreateDoorBrushTool>();
    }

    [EditorTool("Браш «Окно»", variantGroup = typeof(BrushCreateTool), variantPriority = 10)]
    public sealed class CreateWindowBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Window;
        public override string Title => Tr("Браш «Окно»");
        [MenuItem(BrushCommandMenu.CreateRoot + "Окно", false, 11)]
        [MenuItem("Tools/" + BrushCommandMenu.CreateRoot + "Окно", false, 11)] static void Menu() => BrushMenu.ActivateCreation<CreateWindowBrushTool>();
    }
}
