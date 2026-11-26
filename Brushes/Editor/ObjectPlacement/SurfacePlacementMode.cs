using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static CsgBrush.Editor.BrushLanguage;

namespace CsgBrush.Editor
{
    // A mode of Unity's Move tool. The native handles keep ordinary drags, Shift takes the surface gesture.
    internal static class SurfacePlacementMode
    {
        internal const string Label = "Переместить на";
        const string MenuPath = "CSG Brush/" + Label;
        const string Tip = "Включить Move. Выберите объект и наведите курсор на видимую грань. Shift + ЛКМ перетаскивает объект по поверхности. Локальная +Y направлена наружу. Щелчок по маркеру открывает отступ в метрах, Esc отменяет.";
        const string PickSource = "Выберите объект для размещения на грани";
        const string PickFace = "Укажите грань или тяните с Shift   •   Esc — отмена";
        const string PickOffset = "Выберите отступ по направляющей   •   Ctrl — шаг сетки";
        static readonly Color PreviewColor = new Color(0.2f, 0.91f, 0.97f, 0.9f);

        internal static bool Active { get; private set; }
        static Transform source;
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

        [MenuItem(MenuPath, true)]
        static bool Available() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem(MenuPath, false, 33)]
        internal static void Begin()
        {
            if (Active) { Stop(); return; }
            if (!Available()) return;
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
            ObjectChangeEvents.changesPublished += GeometryChanged;
            SceneView.lastActiveSceneView?.Focus();
            SceneView.RepaintAll();
        }

        internal static void DrawButton()
        {
            using var disabled = new EditorGUI.DisabledScope(!Available());
            if (GUILayout.Toggle(Active, new GUIContent(Tr(Label), Tr(Tip)), EditorStyles.miniButton,
                GUILayout.ExpandWidth(false)) != Active) Begin();
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
            ObjectChangeEvents.changesPublished -= GeometryChanged;
            CancelGesture();
            source = null;
            scene = null;
            SceneView.RepaintAll();
        }

        static void SelectSource()
        {
            var selected = Selection.activeTransform;
            var next = Selection.count == 1 && ObjectPlacement.CanMove(selected) &&
                !ObjectPlacement.IsWallWindow(selected) ? selected : null;
            if (source == next) return;
            CancelGesture();
            source = next;
            Invalidate();
        }

        static void Invalidate()
        {
            scene = null;
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
                // Transform matrices are read live, including the source pose during preview.
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
        }

        static void Remember(SceneView view)
        {
            owner = view;
            initialRotation = source.rotation;
            initialLocalPosition = source.localPosition;
            initialLocalRotation = source.localRotation;
        }

        static void Preview(Pose pose)
        {
            if (previewing && lastPreview.position.Equals(pose.position) && lastPreview.rotation.Equals(pose.rotation)) return;
            lastPreview = pose;
            previewing = ObjectPlacement.ApplySurfacePreview(source, pose);
        }

        static void Finish(Pose pose)
        {
            Restore();
            ObjectPlacement.CommitSurface(source, pose);
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
            // Unity can deliver the next mouse event before its selectionChanged callback.
            SelectSource();
            if (source == null)
            {
                if (owner != null || previewing) CancelGesture();
                if (e.type == EventType.Repaint) Hint(view, PickSource);
                return false; // Unity and BrushHooks handle the source selection normally.
            }
            if (owner != null && owner != view) return true;
            if (held != 0 && (e.rawType == EventType.Ignore || e.alt || Tools.viewToolActive || GUIUtility.hotControl != held))
                CancelGesture();
            if (e.alt || Tools.viewToolActive) return true;

            scene ??= new PlacementScene(source);
            if (guide != null && !scene.Available(view.camera, anchor.Part)) CancelGesture();
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (guide != null)
            {
                bool over = guide.Slider(view, out bool apply);
                if (over || apply)
                {
                    var onGuide = GuidePose();
                    if (GuideVisible(view))
                    {
                        if (apply) { Finish(onGuide); return true; }
                        if (e.type != EventType.Layout) Preview(onGuide);
                    }
                    if (e.isMouse) { e.Use(); view.Repaint(); }
                    if (e.type != EventType.Repaint) return true;
                }
            }
            if (e.type == EventType.Layout)
            {
                if (guide != null || e.shift) HandleUtility.AddControl(id, 0f);
                else HandleUtility.AddDefaultControl(id);
                return true;
            }
            if (GUIUtility.hotControl != 0 && GUIUtility.hotControl != held) return true;
            if (e.type != EventType.Repaint && !e.isMouse && !e.isKey) return true;

            if (guide != null)
            {
                if (e.isMouse || e.isKey) guide.Update(view.camera, e.mousePosition, e.control ? BrushSettings.instance.GridMeters : 0f);
                var pose = GuidePose();
                bool visible = GuideVisible(view);
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    EditModeMouse.Take(id);
                    held = id;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && e.button == 0 && held == id)
                {
                    e.Use();
                    Release();
                    if (visible) Finish(pose);
                    return true;
                }
                if (visible) Preview(pose);
                if (e.type == EventType.Repaint)
                {
                    Hint(view, visible ? PickOffset : "Укажите видимую точку на выбранной грани");
                    guide.DrawEdges();
                    Marker(anchor, view);
                    Handles.Label(guide.Position, "   " + BrushSettings.FormatMeters(Vector3.Distance(guide.Origin, guide.Position)));
                }
                else view.Repaint();
                return true;
            }

            bool hit = scene.TryPickSurface(view.camera, e.mousePosition, out var surface);
            if (e.type == EventType.MouseDown && e.button == 0 &&
                (e.shift || hit && (HandleUtility.nearestControl == id || BrushHooks.UnitySelectionTakes(view))))
            {
                Remember(view);
                EditModeMouse.Take(id);
                held = id;
                e.Use();
            }
            if (hit)
            {
                var feature = surface.Feature;
                var contact = feature.Part == null ? surface.Position : feature.Position;
                var pose = scene.SurfacePose(contact, surface.Normal, held != 0 ? initialRotation : source.rotation);
                if (held != 0) Preview(pose);
                if (e.type == EventType.MouseUp && e.button == 0 && held == id)
                {
                    e.Use();
                    Release();
                    if (feature.Part == null) { Finish(pose); return true; }
                    anchor = feature;
                    guide = new PlacementGuide(feature, view.camera, e.mousePosition);
                }
                if (e.type == EventType.Repaint)
                {
                    scene.DrawSurfacePreview(pose, PreviewColor);
                    if (feature.Part != null) Marker(feature, view);
                    else using (new Handles.DrawingScope(PreviewColor))
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
            return true;
        }

        static Pose GuidePose()
        {
            var normal = anchor.Part.Owner.transform.worldToLocalMatrix.transpose.MultiplyVector(anchor.Face.Normal).normalized;
            return scene.SurfacePose(guide.Position, normal, initialRotation);
        }

        static bool GuideVisible(SceneView view) =>
            scene.IsVisible(view.camera, anchor.Part, anchor.Face, guide.Position);

        static void Marker(PlacementScene.Feature point, SceneView view)
        {
            using (new Handles.DrawingScope(Color.yellow))
                PlacementMarker.Draw(point.Position, view.camera.transform.rotation, PlacementMarker.Kind(point), 0.065f);
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
