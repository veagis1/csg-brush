using static CsgBrush.Editor.BrushLanguage;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.Rendering;

namespace CsgBrush.Editor
{
    internal static class BrushSurfaceGrid
    {
        internal readonly struct Frame
        {
            internal readonly Transform Owner;
            readonly Vector3 localOrigin;
            readonly Quaternion localRotation;
            readonly bool worldPlane;

            internal Frame(Transform owner, Vector3 localOrigin, Quaternion localRotation, bool worldPlane = false)
            {
                Owner = owner;
                this.localOrigin = localOrigin;
                this.localRotation = localRotation;
                this.worldPlane = worldPlane;
            }

            internal static Frame OnPlane(Vector3 point, Vector3 normal)
            {
                if (normal.sqrMagnitude < 1e-8f) return default;
                return new Frame(null, BrushDraw.OntoSurface(Vector3.zero, point, normal),
                    BrushDraw.SurfaceRotation(normal), true);
            }

            internal bool Valid => worldPlane || (Owner != null && Owner.gameObject.activeInHierarchy && BrushSceneVisibility.CanPick(Owner));
            internal Vector3 Origin => worldPlane ? localOrigin : Owner.TransformPoint(localOrigin);
            internal Quaternion Rotation => worldPlane ? localRotation : Owner.rotation * localRotation;
            internal Vector3 Normal => Rotation * Vector3.up;
            internal Plane Plane => new Plane(Normal, Origin);

            internal void Store(Brush brush)
            {
                brush.workGridOwner = Owner;
                brush.workGridOrigin = localOrigin;
                brush.workGridRotation = localRotation;
            }
        }

        static Frame held, candidate;
        static Vector3 candidatePoint;
        static SceneView pickingView;
        internal static bool Picking => pickingView != null;
        internal static bool HasHeld => held.Valid;

        internal static bool Local
        {
            get => SessionState.GetBool("CsgBrush.SurfaceGrid.Local", true);
            set => SessionState.SetBool("CsgBrush.SurfaceGrid.Local", value);
        }

        internal static Frame ForSurface(Brush surface, Vector3 point, Vector3 normal)
        {
            var owner = surface.generatedBy != null ? surface.generatedBy.transform : surface.transform;
            var referenceOrigin = owner.position;
            var axis = owner.right;
            if (BrushCutTarget.FromBrush(surface).TryWall(out var wall))
            {
                var local = owner.InverseTransformPoint(point);
                wall.Project(new Vector2(local.x, local.z), out float along, out _);
                wall.At(along, out var position, out var direction, out _);
                axis = owner.TransformVector(new Vector3(direction.x, 0f, direction.y));
                referenceOrigin = owner.TransformPoint(new Vector3(position.x, 0f, position.y)) - axis * along;
            }
            var tangent = Vector3.ProjectOnPlane(axis, normal);
            if (tangent.sqrMagnitude < 0.25f)
                tangent = Vector3.ProjectOnPlane(owner.forward, normal);
            var rotation = tangent.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(Vector3.Cross(tangent.normalized, normal), normal)
                : BrushDraw.SurfaceRotation(normal);
            var origin = BrushDraw.OntoSurface(referenceOrigin, point, normal);
            return new Frame(owner, owner.InverseTransformPoint(origin), Quaternion.Inverse(owner.rotation) * rotation);
        }

        internal static bool TryDrawingFrame(Ray ray, Brush surface, ref Vector3 point, ref Vector3 normal, out Frame frame)
        {
            frame = default;
            if (!Local) return false;
            if (HasHeld)
            {
                if (!BrushDraw.TryGridPoint(ray, held.Plane, out point)) return false;
                frame = held;
                normal = frame.Normal;
                return true;
            }
            if (surface == null || !BrushSceneVisibility.CanPick(surface.transform)) return false;
            frame = ForSurface(surface, point, normal);
            return frame.Valid;
        }

        internal static bool TryMoveFrame(Brush brush, out Frame frame)
        {
            frame = default;
            if (!Local) return false;
            if (HasHeld && UsableFor(held.Owner, brush))
            {
                frame = held;
                return true;
            }
            if (!UsableFor(brush.workGridOwner, brush)) return false;
            frame = new Frame(brush.workGridOwner, brush.workGridOrigin, brush.workGridRotation);
            return frame.Valid;
        }

        static bool UsableFor(Transform owner, Brush brush) => owner != null && owner != brush.transform &&
            owner.gameObject.scene == brush.gameObject.scene && !owner.IsChildOf(brush.transform) &&
            owner.gameObject.activeInHierarchy && BrushSceneVisibility.CanPick(owner);

        internal static void DrawMenu()
        {
            if (!GUILayout.Button(new GUIContent(Tr("Привязки ▾"), Tr("Выберите мировую сетку или сетку поверхности.")),
                    EditorStyles.miniButton, GUILayout.ExpandWidth(false), GUILayout.Height(18))) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(Tr("Мировая сетка")), !Local, () =>
            {
                Local = false;
                held = default;
                SceneView.RepaintAll();
            });
            menu.AddItem(new GUIContent(Tr("Локальная сетка поверхности")), Local, () =>
            {
                Local = true;
                SceneView.RepaintAll();
            });
            menu.AddItem(new GUIContent(Tr("Закрепить сетку по выбранной грани")), false, () =>
            {
                pickingView = SceneView.lastActiveSceneView;
                candidate = default;
                SceneView.RepaintAll();
            });
            if (HasHeld)
                menu.AddItem(new GUIContent(Tr("Освободить рабочую сетку")), false, () =>
                {
                    held = default;
                    SceneView.RepaintAll();
                });
            menu.ShowAsContext();
        }

        internal static void CancelPicking()
        {
            if (pickingView == null) return;
            pickingView = null;
            candidate = default;
            SceneView.RepaintAll();
        }

        internal static bool OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            if (e.alt || Tools.viewToolActive) return false;
            if (pickingView != view)
            {
                // Each creation tool draws the plane it actually uses, including wall openings and floor plans.
                if (BrushCreateTool.Active != null || ToolManager.activeToolType == typeof(CreateFloorPlanTool) ||
                    ToolManager.activeToolType == typeof(CreateSplineTool)) return false;
                if (HasHeld) Draw(held);
                else if (Tools.current == Tool.Move && GUIUtility.hotControl != 0 &&
                    Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent<Brush>(out var brush) &&
                    TryMoveFrame(brush, out var frame)) Draw(frame);
                return false;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                pickingView = null;
                candidate = default;
                e.Use();
                view.Repaint();
                return true;
            }
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (e.type == EventType.MouseMove || (e.type == EventType.MouseDown && e.button == 0))
            {
                var surface = BrushHooks.PickConstructionSurface(HandleUtility.GUIPointToWorldRay(e.mousePosition), out var point, out var normal);
                candidate = surface != null ? ForSurface(surface, point, normal) : default;
                candidatePoint = point;
                if (e.type == EventType.MouseDown)
                {
                    if (candidate.Valid)
                    {
                        held = candidate;
                        Local = true;
                        pickingView = null;
                    }
                    e.Use();
                }
                view.Repaint();
            }
            if (candidate.Valid && e.type == EventType.Repaint)
            {
                Draw(candidate, candidatePoint);
                DrawCursorPoint(candidatePoint);
            }
            return true;
        }

        const int RadiusInSteps = 12;
        static Material gridMaterial;

        static BrushSurfaceGrid()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseGridMaterial;
            EditorApplication.quitting += ReleaseGridMaterial;
        }

        static void ReleaseGridMaterial()
        {
            if (gridMaterial != null) Object.DestroyImmediate(gridMaterial);
        }

        internal static void DrawCursorPoint(Vector3 point)
        {
            var view = SceneView.currentDrawingSceneView;
            if (view == null || view.camera == null) return;
            using (new Handles.DrawingScope(Color.white))
                Handles.DrawSolidDisc(point, view.camera.transform.forward, HandleUtility.GetHandleSize(point) * 0.035f);
        }

        internal static void Draw(Frame frame, Vector3? focus = null)
        {
            if (!frame.Valid || Event.current.type != EventType.Repaint) return;
            var step = BrushSettings.instance.GridMeters;
            if (!(step > 0f) || float.IsInfinity(step)) return;
            var point = focus ?? frame.Origin;
            if (!focus.HasValue)
            {
                var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
                if (frame.Plane.Raycast(ray, out float t)) point = ray.GetPoint(t);
            }
            // Keep visible lines on real grid nodes as the camera pulls away.
            float minSpacing = HandleUtility.GetHandleSize(point) * 0.1f;
            while (step < minSpacing) step *= 2f;
            var cells = Quaternion.Inverse(frame.Rotation) * (point - frame.Origin) / step;
            int column = Mathf.RoundToInt(cells.x), row = Mathf.RoundToInt(cells.z);
            var minor = new Color(0.38f, 0.88f, 0.83f, 0.34f);
            var major = new Color(0.55f, 0.96f, 0.91f, 0.70f);
            // Hardware lines use the same depth as the surface grid used for snapping.
            // Camera-facing AA strips can disappear into a coplanar face in orthographic views.
            if (!UseOverlayMaterial()) return;
            GL.PushMatrix();
            try
            {
                GL.MultMatrix(Matrix4x4.TRS(point + frame.Normal * 0.001f, frame.Rotation, Vector3.one));
                float radius = RadiusInSteps * step;
                for (int i = -RadiusInSteps; i <= RadiusInSteps; i++)
                {
                    DrawChord((column - cells.x + i) * step, radius, Vector3.right, Vector3.forward, (column + i) % 4 == 0 ? major : minor);
                    DrawChord((row - cells.z + i) * step, radius, Vector3.forward, Vector3.right, (row + i) % 4 == 0 ? major : minor);
                }
            }
            finally { GL.PopMatrix(); }
        }

        internal static bool UseOverlayMaterial()
        {
            if (gridMaterial == null)
            {
                gridMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                gridMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                gridMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                gridMaterial.SetInt("_ZWrite", 0);
                gridMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
                gridMaterial.SetInt("_Cull", (int)CullMode.Off);
            }
            return gridMaterial.SetPass(0);
        }

        static void DrawChord(float offset, float radius, Vector3 across, Vector3 along, Color color)
        {
            float side = offset / radius;
            if (Mathf.Abs(side) >= 1f) return;
            float halfChord = Mathf.Sqrt(1f - side * side);
            const int segments = 16;
            GL.Begin(GL.LINE_STRIP);
            for (int i = 0; i <= segments; i++)
            {
                float position = Mathf.Lerp(-halfChord, halfChord, (float)i / segments);
                float distance = Mathf.Sqrt(side * side + position * position);
                float fade = Mathf.InverseLerp(0.45f, 1f, distance);
                GL.Color(new Color(color.r, color.g, color.b, color.a * Mathf.SmoothStep(1f, 0f, fade)));
                GL.Vertex(across * offset + along * (position * radius));
            }
            GL.End();
        }
    }
}
