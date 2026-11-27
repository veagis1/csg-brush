using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static CsgBrush.Editor.BrushLanguage;

namespace CsgBrush.Editor
{
    // Native Move keeps its handles. A click on a target or Shift-drag uses the placement options.
    internal static class SurfacePlacementMode
    {
        const string PickSource = "Выберите объект для размещения на грани";
        const string PickFace = "Укажите грань или тяните с Shift   •   Ctrl — шаг сетки   •   Esc — отмена";
        const string PickOffset = "Выберите отступ по направляющей   •   Ctrl — шаг сетки";
        static readonly Color PreviewColor = new Color(0.2f, 0.91f, 0.97f, 0.9f);
        static readonly PlacementPointDisplay pointDisplay = new PlacementPointDisplay();

        internal static bool Active { get; private set; }
        internal static bool Perpendicular
        {
            get => SessionState.GetBool("CSGBrush.Placement.Perpendicular", false);
            set => SetOption("CSGBrush.Placement.Perpendicular", Perpendicular, value, true);
        }
        internal static bool Magnet
        {
            get => SessionState.GetBool("CSGBrush.Placement.Magnet", true);
            set => SetOption("CSGBrush.Placement.Magnet", Magnet, value, true);
        }
        internal static bool ShowPoints
        {
            get => SessionState.GetBool("CSGBrush.Placement.Points", false);
            set => SetOption("CSGBrush.Placement.Points", ShowPoints, value, false);
        }

        static Transform source;
        static bool window;
        static bool AlignToFace => Perpendicular && !window;
        static PlacementScene scene;
        static SceneView owner;
        static PlacementScene.Feature anchor;
        static PlacementGuide guide;
        static Quaternion initialRotation;
        static Vector3 initialLocalPosition;
        static Quaternion initialLocalRotation;
        static bool previewing;
        static Pose lastPreview;
        static int held;

        static void SetOption(string key, bool previous, bool value, bool restartGesture)
        {
            if (previous == value) return;
            if (restartGesture && Active) CancelGesture();
            SessionState.SetBool(key, value);
            SceneView.RepaintAll();
        }

        internal static void Begin()
        {
            if (Active) { Stop(); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            BrushMenu.PrepareCreation();
            BrushMenu.ActivateTransform(Tool.Move);
            Active = true;
            SelectSource();
            Selection.selectionChanged += SelectSource;
            ToolManager.activeToolChanged += Stop;
            ToolManager.activeContextChanged += Stop;
            Undo.undoRedoPerformed += Stop;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            EditorSceneManager.sceneSaving += SceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.hierarchyChanged += Invalidate;
            EditorApplication.projectChanged += Invalidate;
            SceneVisibilityManager.visibilityChanged += HierarchyAccessChanged;
            SceneVisibilityManager.pickingChanged += HierarchyAccessChanged;
            BrushCsg.MeshesWritten += MeshesWritten;
            ObjectChangeEvents.changesPublished += GeometryChanged;
            SceneView.lastActiveSceneView?.Focus();
            SceneView.RepaintAll();
        }

        static void PlayStateChanged(PlayModeStateChange state) => Stop();
        static void SceneSaving(Scene saving, string path)
        {
            if (source != null && source.gameObject.scene == saving) Stop();
        }

        internal static void Stop()
        {
            if (!Active) return;
            Active = false;
            Selection.selectionChanged -= SelectSource;
            ToolManager.activeToolChanged -= Stop;
            ToolManager.activeContextChanged -= Stop;
            Undo.undoRedoPerformed -= Stop;
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            EditorSceneManager.sceneSaving -= SceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.hierarchyChanged -= Invalidate;
            EditorApplication.projectChanged -= Invalidate;
            SceneVisibilityManager.visibilityChanged -= HierarchyAccessChanged;
            SceneVisibilityManager.pickingChanged -= HierarchyAccessChanged;
            BrushCsg.MeshesWritten -= MeshesWritten;
            ObjectChangeEvents.changesPublished -= GeometryChanged;
            CancelGesture();
            source = null;
            scene = null;
            pointDisplay.Invalidate();
            SceneView.RepaintAll();
        }

        static void SelectSource()
        {
            var selected = Selection.activeTransform;
            var next = Selection.count == 1 && ObjectPlacement.CanMove(selected) ? selected : null;
            if (source == next) return;
            CancelGesture();
            source = next;
            window = ObjectPlacement.IsWallWindow(next);
            Invalidate();
        }

        static void MeshesWritten(BrushGroup group) { if (owner == null) Invalidate(); }

        static void HierarchyAccessChanged()
        {
            CancelGesture();
            Invalidate();
        }

        static void Invalidate()
        {
            // Live CSG rebuilding can remove the very contact face we are dragging over.
            // Keep the target catalogue from before the gesture until release or cancellation.
            if (owner == null) scene = null;
            pointDisplay.Invalidate();
            SceneView.RepaintAll();
        }

        static void GeometryChanged(ref ObjectChangeEventStream changes)
        {
            for (int i = 0; i < changes.length; i++)
            {
                Object item;
                switch (changes.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        changes.GetChangeGameObjectOrComponentPropertiesEvent(i, out var component);
                        item = EditorUtility.EntityIdToObject(component.entityId);
                        break;
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        changes.GetChangeAssetObjectPropertiesEvent(i, out var asset);
                        item = EditorUtility.EntityIdToObject(asset.entityId);
                        break;
                    default: continue;
                }
                if (item is Transform transform)
                {
                    if (source == null || !transform.IsChildOf(source)) pointDisplay.Invalidate();
                    continue;
                }
                if (!(item is Brush || item is Mesh || item is Renderer || item is MeshFilter || item is Collider || item is PointGraph))
                    continue;
                if (guide != null && (item == anchor.Part.Owner || item == anchor.Part.Mesh ||
                    anchor.Part.Owner is Brush brush && brush.generatedBy == item)) CancelGesture();
                Invalidate();
            }
        }

        static void Restore()
        {
            if (previewing && source != null)
                ObjectPlacement.RestoreSurfacePreview(source, initialLocalPosition, initialLocalRotation);
            previewing = false;
        }

        static void Release()
        {
            if (held != 0 && GUIUtility.hotControl == held) GUIUtility.hotControl = 0;
            held = 0;
        }

        static void CancelGesture()
        {
            Release();
            Restore();
            owner = null;
            guide = null;
            anchor = default;
            scene = null;
        }

        static void Remember(SceneView view)
        {
            owner = view;
            initialRotation = source.rotation;
            initialLocalPosition = source.localPosition;
            initialLocalRotation = source.localRotation;
        }

        static Pose PoseAt(Vector3 contact, Vector3 normal)
        {
            var rotation = owner != null ? initialRotation : source.rotation;
            return AlignToFace ? scene.SurfacePose(contact, normal, rotation) : new Pose(contact, rotation);
        }

        static void Preview(Pose pose)
        {
            // The wall owns a window's transform, sill, depth and parent. Draw its candidate layout
            // until commit rather than moving the live cut back and forth between floor plans.
            if (window || previewing && lastPreview.position.Equals(pose.position) && lastPreview.rotation.Equals(pose.rotation)) return;
            lastPreview = pose;
            previewing = AlignToFace ? ObjectPlacement.ApplySurfacePreview(source, pose) :
                ObjectPlacement.ApplyPositionPreview(source, pose.position);
        }

        static void DrawPreview(PlacementScene.Part target, Pose pose)
        {
            if (AlignToFace) scene.DrawSurfacePreview(pose, PreviewColor);
            else scene.DrawPreview(target.Owner, pose.position, PreviewColor, out _);
        }

        static void Finish(PlacementScene.Part target, Pose pose)
        {
            Restore();
            if (AlignToFace) ObjectPlacement.CommitSurface(source, pose);
            else ObjectPlacement.Commit(source, pose.position, target.Owner);
            Stop();
        }

        internal static bool OnSceneGUI(SceneView view)
        {
            if (!Active || view.camera == null) return false;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                e.Use();
                Stop();
                return true;
            }
            SelectSource();
            if (source == null)
            {
                if (owner != null || previewing) CancelGesture();
                if (e.type == EventType.Repaint) Hint(view, PickSource);
                return false;
            }
            if (owner != null && owner != view) return true;
            if (held != 0 && (e.rawType == EventType.Ignore || e.alt || Tools.viewToolActive || GUIUtility.hotControl != held))
                CancelGesture();
            if (e.alt || Tools.viewToolActive) return true;

            scene ??= new PlacementScene(source);
            if (guide != null && !scene.Available(view.camera, anchor.Part))
            {
                CancelGesture();
                view.Repaint();
                return true;
            }
            int id = GUIUtility.GetControlID(FocusType.Passive);
            bool overPanel = false;
            if (guide != null)
            {
                overPanel = guide.Slider(view, out bool apply);
                if (apply && GuideVisible(view))
                {
                    Finish(anchor.Part, GuidePose());
                    return true;
                }
            }
            if (e.type == EventType.Layout)
            {
                if (guide != null || e.shift) HandleUtility.AddControl(id, 0f);
                else HandleUtility.AddDefaultControl(id);
                return true;
            }
            if (GUIUtility.hotControl != 0 && GUIUtility.hotControl != held && !overPanel) return true;
            if (e.type != EventType.Repaint && !e.isMouse && !e.isKey && !overPanel) return true;

            if (guide != null) GuideInput(view, id, overPanel);
            else TargetInput(view, id);
            return true;
        }

        static void GuideInput(SceneView view, int id, bool overPanel)
        {
            var e = Event.current;
            if (!overPanel && (e.isMouse || e.isKey))
                guide.Update(view.camera, e.mousePosition, e.control ? BrushSettings.instance.GridMeters : 0f);
            var pose = GuidePose();
            bool allowed = GuideVisible(view);
            if (allowed) Preview(pose);
            else Restore();
            if (!overPanel && e.type == EventType.MouseDown && e.button == 0)
            {
                EditModeMouse.Take(id);
                held = id;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && held == id)
            {
                e.Use();
                Release();
                if (allowed && !overPanel) Finish(anchor.Part, pose);
                return;
            }
            if (e.type == EventType.Repaint)
            {
                Hint(view, allowed ? PickOffset : "Укажите видимую точку на выбранной грани");
                if (allowed) DrawPreview(anchor.Part, pose);
                pointDisplay.Draw(scene, view.camera, anchor.Part, anchor, ShowPoints);
                if (scene.IsVisible(view.camera, anchor))
                {
                    guide.DrawEdges();
                    Handles.Label(guide.Position, "   " + BrushSettings.FormatMeters(Vector3.Distance(guide.Origin, guide.Position)));
                }
            }
            else view.Repaint();
        }

        static Pose GuidePose()
        {
            var normal = AlignToFace ? anchor.Part.Owner.transform.worldToLocalMatrix.transpose.MultiplyVector(anchor.Face.Normal).normalized : Vector3.zero;
            return PoseAt(guide.Position, normal);
        }

        static bool GuideVisible(SceneView view) =>
            !Perpendicular || scene.IsVisible(view.camera, anchor.Part, anchor.Face, guide.Position);

        static void TargetInput(SceneView view, int id)
        {
            var e = Event.current;
            bool wantFaceSnap = Perpendicular && Magnet;
            bool surfaceHit = scene.TryPickSurface(view.camera, e.mousePosition, out var surface, wantFaceSnap);
            var feature = Magnet && !Perpendicular ? scene.Pick(view.camera, e.mousePosition) : surface.Feature;
            var target = feature.Part ?? surface.Part;
            if (e.type == EventType.MouseDown && e.button == 0 &&
                (e.shift || target != null && (HandleUtility.nearestControl == id || BrushHooks.UnitySelectionTakes(view))))
            {
                Remember(view);
                EditModeMouse.Take(id);
                held = id;
                e.Use();
            }
            if (target != null)
            {
                var contact = feature.Part != null ? feature.Position : surface.Position;
                if (feature.Part == null && e.control)
                    contact = GridContact(view.camera, surface, BrushSettings.instance.GridMeters);
                var pose = PoseAt(contact, surface.Normal);
                if (held != 0) Preview(pose);
                if (e.type == EventType.MouseUp && e.button == 0 && held == id)
                {
                    e.Use();
                    Release();
                    if (feature.Part == null) { Finish(target, pose); return; }
                    anchor = feature;
                    guide = new PlacementGuide(feature, view.camera, e.mousePosition);
                }
                if (e.type == EventType.Repaint)
                {
                    DrawPreview(target, pose);
                    pointDisplay.Draw(scene, view.camera, surfaceHit ? surface.Part : target, feature, ShowPoints);
                    if (feature.Part == null)
                        using (new Handles.DrawingScope(PreviewColor))
                            Handles.DrawWireDisc(contact, surface.Normal, HandleUtility.GetHandleSize(contact) * 0.06f);
                }
            }
            else if (held != 0)
            {
                Restore();
                if (e.type == EventType.MouseUp && e.button == 0) { e.Use(); CancelGesture(); }
            }
            if (held != 0 && e.type == EventType.MouseDrag) e.Use();
            if (e.type == EventType.Repaint) Hint(view, PickFace);
            else view.Repaint();
        }

        static Vector3 GridContact(Camera camera, PlacementScene.SurfaceHit hit, float step)
        {
            if (step <= 0f) return hit.Position;
            var frame = BrushDraw.SurfaceRotation(hit.Normal);
            var origin = Vector3.Project(hit.Position, hit.Normal);
            var node = BrushDraw.SnapInPlane(hit.Position, origin, frame, step);
            bool OnFace(Vector3 p) => hit.Face.Contains(hit.Part.Owner.transform.InverseTransformPoint(p)) &&
                scene.IsVisible(camera, hit.Part, hit.Face, p);
            if (OnFace(node)) return node;
            float nearest = float.PositiveInfinity;
            var result = hit.Position;
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    var next = node + frame * new Vector3(x * step, 0f, z * step);
                    float distance = (next - hit.Position).sqrMagnitude;
                    if (distance >= nearest || !OnFace(next)) continue;
                    nearest = distance;
                    result = next;
                }
            return result;
        }

        static void Hint(SceneView view, string message)
        {
            Handles.BeginGUI();
            GUI.Label(new Rect(8f, view.position.height - 65f, view.position.width - 16f, 22f),
                Tr(message), EditorStyles.helpBox);
            Handles.EndGUI();
        }
    }
}
