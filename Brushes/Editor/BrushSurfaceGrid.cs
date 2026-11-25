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

            internal bool Valid => worldPlane || (Owner != null && Owner.gameObject.activeInHierarchy);
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
        static SceneView pickingView;
        internal static bool Picking => pickingView != null;
        internal static bool HasHeld => held.Valid && !SceneVisibilityManager.instance.IsHidden(held.Owner.gameObject);

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
                if (!held.Plane.Raycast(ray, out float distance)) return false;
                frame = held;
                point = ray.GetPoint(distance);
                normal = frame.Normal;
                return true;
            }
            if (surface == null) return false;
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
            !SceneVisibilityManager.instance.IsHidden(owner.gameObject);

        internal static void DrawMenu()
        {
            if (!GUILayout.Button(new GUIContent("Привязки ▾", "Выберите мировую сетку или сетку поверхности."),
                    EditorStyles.miniButton, GUILayout.ExpandWidth(false), GUILayout.Height(18))) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Мировая сетка"), !Local, () =>
            {
                Local = false;
                held = default;
                SceneView.RepaintAll();
            });
            menu.AddItem(new GUIContent("Локальная сетка поверхности"), Local, () =>
            {
                Local = true;
                SceneView.RepaintAll();
            });
            menu.AddItem(new GUIContent("Закрепить сетку по выбранной грани"), false, () =>
            {
                pickingView = SceneView.lastActiveSceneView;
                candidate = default;
                SceneView.RepaintAll();
            });
            if (HasHeld)
                menu.AddItem(new GUIContent("Освободить рабочую сетку"), false, () =>
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
            Draw(candidate);
            return true;
        }

        internal static void Draw(Frame frame)
        {
            if (!frame.Valid || Event.current.type != EventType.Repaint) return;
            var step = BrushSettings.instance.GridMeters;
            if (!(step > 0f) || float.IsInfinity(step)) return;
            const int radius = 12;
            var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            var point = frame.Plane.Raycast(ray, out float t) ? ray.GetPoint(t) : frame.Origin;
            // Keep visible lines on real grid nodes as the camera pulls away.
            float minSpacing = HandleUtility.GetHandleSize(point) * 0.1f;
            while (step < minSpacing) step *= 2f;
            var center = BrushDraw.SnapInPlane(point, frame.Origin, frame.Rotation, step);
            var local = Quaternion.Inverse(frame.Rotation) * (center - frame.Origin);
            int column = Mathf.RoundToInt(local.x / step), row = Mathf.RoundToInt(local.z / step);
            var minor = new Color(0.38f, 0.88f, 0.83f, 0.34f);
            var major = new Color(0.55f, 0.96f, 0.91f, 0.70f);
            var previous = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.LessEqual;
                using (new Handles.DrawingScope(minor,
                    Matrix4x4.TRS(center + frame.Normal * 0.001f, frame.Rotation, Vector3.one)))
                {
                    float edge = radius * step;
                    for (int i = -radius; i <= radius; i++)
                    {
                        float offset = i * step;
                        Handles.color = (column + i) % 4 == 0 ? major : minor;
                        Handles.DrawLine(new Vector3(offset, 0f, -edge), new Vector3(offset, 0f, edge));
                        Handles.color = (row + i) % 4 == 0 ? major : minor;
                        Handles.DrawLine(new Vector3(-edge, 0f, offset), new Vector3(edge, 0f, offset));
                    }
                }
            }
            finally { Handles.zTest = previous; }
        }
    }
}
