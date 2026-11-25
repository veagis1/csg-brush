using static CsgBrush.Editor.BrushLanguage;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    [EditorTool(ObjectPlacementTool.Label)]
    public sealed class ObjectPlacementTool : EditorTool
    {
        internal const string Label = "Переместить к";
        const string Menu = "CSG Brush/" + Label;
        const string Tip = "Переместить выбранный объект началом его рабочей системы координат к вершине или середине ребра другого объекта. Второй щелчок фиксирует положение. Ctrl — шаг сетки в метрах, Esc — отмена.";
        static readonly Color PreviewColor = new Color(0.2f, 0.91f, 0.97f, 0.9f);
        Transform source;
        PlacementScene scene;
        PlacementScene.Feature anchor, hover;
        PlacementGuide guide;
        SceneView owner;
        Vector2 mouse;
        bool control, confirmOnRelease;
        int held;

        internal static bool Active => ToolManager.activeToolType == typeof(ObjectPlacementTool);
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("Mode_Vertex"), Tr(Tip));
        public override bool IsAvailable() => Available();

        [MenuItem(Menu, true)]
        static bool Available() => Selection.gameObjects.Length == 1 && ObjectPlacement.CanMove(Selection.activeTransform);

        [MenuItem(Menu, false, 32)]
        internal static void Begin()
        {
            if (!Available()) return;
            if (Active) ToolManager.RestorePreviousPersistentTool();
            ToolManager.SetActiveTool<ObjectPlacementTool>();
            SceneView.lastActiveSceneView?.Focus();
        }

        internal static void DrawButton()
        {
            using var disabled = new EditorGUI.DisabledScope(!Available());
            if (GUILayout.Button(new GUIContent(Tr(Label), Tr(Tip)), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                Begin();
        }

        public override void OnActivated()
        {
            BrushShapeHandles.CancelGesture();
            BrushSurfaceGrid.CancelPicking();
            if (ToolManager.activeContextType != typeof(GameObjectToolContext))
                ToolManager.SetActiveContext<GameObjectToolContext>();
            source = Selection.activeTransform;
            Selection.selectionChanged += SelectionChanged;
            Undo.undoRedoPerformed += Stop;
            EditorApplication.hierarchyChanged += Refresh;
            EditorApplication.projectChanged += Refresh;
            ObjectChangeEvents.changesPublished += GeometryChanged;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Refresh();
        }

        public override void OnWillBeDeactivated()
        {
            Selection.selectionChanged -= SelectionChanged;
            Undo.undoRedoPerformed -= Stop;
            EditorApplication.hierarchyChanged -= Refresh;
            EditorApplication.projectChanged -= Refresh;
            ObjectChangeEvents.changesPublished -= GeometryChanged;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Release();
            scene = null;
            guide = null;
            anchor = hover = default;
            source = null;
            owner = null;
            SceneView.RepaintAll();
        }

        void Refresh()
        {
            scene = null;
            SceneView.RepaintAll();
        }

        void GeometryChanged(ref ObjectChangeEventStream changes)
        {
            for (int i = 0; i < changes.length; i++)
            {
                Object changed;
                switch (changes.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        changes.GetChangeGameObjectOrComponentPropertiesEvent(i, out var component);
                        changed = EditorUtility.EntityIdToObject(component.entityId);
                        break;
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        changes.GetChangeAssetObjectPropertiesEvent(i, out var asset);
                        changed = EditorUtility.EntityIdToObject(asset.entityId);
                        break;
                    default: continue;
                }
                if (!(changed is Mesh || changed is Brush || changed is MeshFilter ||
                    changed is Renderer || changed is Collider)) continue;
                if (guide != null && (changed == anchor.Part.Owner || changed == anchor.Part.Mesh))
                {
                    Release();
                    guide = null;
                    anchor = default;
                    owner = null;
                }
                Refresh();
            }
        }

        void SelectionChanged() { if (source != Selection.activeTransform || !Available()) Stop(); }
        void OnPlayMode(PlayModeStateChange change) { if (change == PlayModeStateChange.ExitingEditMode) Stop(); }
        void Stop() { if (Active) ToolManager.RestorePreviousPersistentTool(); }
        void Release()
        {
            if (held != 0 && GUIUtility.hotControl == held) GUIUtility.hotControl = 0;
            held = 0;
            confirmOnRelease = false;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView view) || view.camera == null) return;
            if (source == null || !ObjectPlacement.CanMove(source)) { Stop(); return; }
            var e = Event.current;
            if (held != 0 && (GUIUtility.hotControl != held || e.alt || Tools.viewToolActive ||
                (owner != null && owner != view && e.rawType == EventType.MouseUp))) Release();
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); Stop(); return; }
            if (owner != null && owner != view || e.alt || Tools.viewToolActive) return;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) { HandleUtility.AddDefaultControl(id); return; }
            if (e.isMouse || e.type == EventType.MouseMove) mouse = e.mousePosition;
            if (e.isMouse || e.isKey) control = e.control;
            if (e.type != EventType.Repaint && !e.isMouse && !e.isKey) return;
            if (scene == null) scene = new PlacementScene(source);
            if (guide != null && !scene.Available(view.camera, anchor.Part))
            {
                Release();
                anchor = default;
                guide = null;
                owner = null;
            }
            if (guide == null) hover = scene.Pick(view.camera, mouse);
            else guide.Update(view.camera, mouse, control ? BrushSettings.instance.GridMeters : 0f);

            if (e.type == EventType.MouseDown && e.button == 0 && HandleUtility.nearestControl == id)
            {
                EditModeMouse.Take(id);
                held = id;
                confirmOnRelease = guide != null;
                if (guide == null && hover.Part != null)
                {
                    anchor = hover;
                    owner = view;
                    guide = new PlacementGuide(anchor, view.camera, mouse);
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && held == id)
            {
                bool finish = confirmOnRelease && guide != null;
                Release();
                e.Use();
                if (finish) { ObjectPlacement.Commit(source, guide.Position); Stop(); return; }
            }
            if (e.type == EventType.Repaint) Draw(view);
            else view.Repaint();
        }

        void Draw(SceneView view)
        {
            var feature = guide != null ? anchor : hover;
            var hint = guide == null ? Tr("Укажите вершину или середину ребра") : Tr("Укажите положение на ребре   •   Ctrl — шаг сетки");
            Handles.BeginGUI();
            GUI.Label(new Rect(8f, view.position.height - 65f, view.position.width - 16f, 22f),
                hint + Tr("   •   Esc — отмена"), EditorStyles.helpBox);
            Handles.EndGUI();
            if (feature.Part == null) return;
            var origin = feature.Position;
            var destination = guide?.Position ?? origin;
            scene.DrawPreview(destination, PreviewColor);
            guide?.DrawEdges();
            using (new Handles.DrawingScope(Color.yellow))
            {
                float size = HandleUtility.GetHandleSize(origin) * 0.065f;
                if (feature.Midpoint) Handles.RectangleHandleCap(0, origin, view.camera.transform.rotation, size, EventType.Repaint);
                else Handles.DotHandleCap(0, origin, Quaternion.identity, size, EventType.Repaint);
            }
            if (guide == null) return;
            using (new Handles.DrawingScope(PreviewColor))
                Handles.DotHandleCap(0, destination, Quaternion.identity, HandleUtility.GetHandleSize(destination) * 0.055f, EventType.Repaint);
            Handles.Label(destination, "   " + BrushSettings.FormatMeters(Vector3.Distance(origin, destination)), EditorStyles.whiteBoldLabel);
        }
    }
}
