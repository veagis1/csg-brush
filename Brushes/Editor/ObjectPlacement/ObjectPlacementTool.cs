using static CsgBrush.Editor.BrushLanguage;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    [EditorTool("Move onto object")]
    public sealed class ObjectPlacementTool : EditorTool
    {
        internal const string Label = "Переместить на объект";
        internal const string Tip = "Совместить начало WCS с точкой на другом объекте. Перетаскивание с Shift показывает положение примитива. Ctrl — шаг сетки в метрах, Esc — отмена.";
        const string PerpendicularTip = "Перпендикулярно грани. Направляет локальную ось +Y по нормали и ставит основание на видимую грань. У окна на стене ориентацию задаёт стена.";
        const string MagnetTip = "Магнит. Притягивает к видимым вершинам, серединам рёбер и центрам граней даже при выключенном показе точек. Выключите магнит для свободного размещения. Ctrl применяет шаг сетки в обоих случаях.";
        const string PointsTip = "Опорные точки. Показывает видимые вершины, середины рёбер и центры граней объекта под курсором. Наведите на точку, чтобы увидеть её тип.";
        const string MenuPath = "CSG Brush/" + Label;

        internal static bool Active => SurfacePlacementMode.Active;
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("Mode_Vertex"), Tr(Tip));
        public override bool IsAvailable() => Available();

        // Picking the global EditorTool from Unity's toolbar opens the same native Move gesture as the overlay.
        public override void OnActivated() => EditorApplication.delayCall += OpenFromToolbar;
        public override void OnWillBeDeactivated() => EditorApplication.delayCall -= OpenFromToolbar;
        static void OpenFromToolbar()
        {
            if (ToolManager.activeToolType == typeof(ObjectPlacementTool) && !Active) Begin();
        }

        [MenuItem(MenuPath, true)]
        static bool Available() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem(MenuPath, false, 32)]
        internal static void Begin() => SurfacePlacementMode.Begin();

        internal static void DrawButton()
        {
            SurfacePlacementMode.HandleEscape(EditorWindow.focusedWindow as SceneView);
            using var row = new EditorGUILayout.HorizontalScope();
            using var disabled = new EditorGUI.DisabledScope(!Available());
            if (GUILayout.Toggle(Active, new GUIContent(Tr(Label), Tr(Tip)), EditorStyles.miniButton,
                GUILayout.ExpandWidth(false)) != Active) Begin();
            SurfacePlacementMode.Perpendicular = DrawOption(Option.Perpendicular, SurfacePlacementMode.Perpendicular, PerpendicularTip);
            SurfacePlacementMode.Magnet = DrawOption(Option.Magnet, SurfacePlacementMode.Magnet, MagnetTip);
            SurfacePlacementMode.ShowPoints = DrawOption(Option.Points, SurfacePlacementMode.ShowPoints, PointsTip);
        }

        enum Option { Perpendicular, Magnet, Points }

        static bool DrawOption(Option option, bool enabled, string tip)
        {
            var rect = GUILayoutUtility.GetRect(22f, 22f, GUILayout.Width(22f), GUILayout.Height(22f));
            bool value = GUI.Toggle(rect, enabled, new GUIContent("", Tr(tip)), EditorStyles.miniButton);
            if (Event.current.type != EventType.Repaint) return value;
            var color = GUI.contentColor * (EditorGUIUtility.isProSkin ? Color.white : new Color(0.18f, 0.18f, 0.18f));
            if (!GUI.enabled) color.a *= 0.45f;
            void Stroke(float x, float y, float width, float height) =>
                EditorGUI.DrawRect(new Rect(rect.center.x + x, rect.center.y + y, width, height), color);
            switch (option)
            {
                case Option.Perpendicular:
                    Stroke(-1, -7, 2, 12);
                    Stroke(-7, 4, 14, 2);
                    break;
                case Option.Magnet:
                    Stroke(-6, -6, 3, 3);
                    Stroke(3, -6, 3, 3);
                    Stroke(-6, -2, 3, 6);
                    Stroke(3, -2, 3, 6);
                    Stroke(-5, 3, 3, 2);
                    Stroke(2, 3, 3, 2);
                    Stroke(-3, 4, 6, 2);
                    break;
                default:
                    Stroke(-5, -5, 10, 1);
                    Stroke(-5, 4, 10, 1);
                    Stroke(-5, -5, 1, 10);
                    Stroke(4, -5, 1, 10);
                    Stroke(-7, -7, 4, 4);
                    Stroke(3, -7, 4, 4);
                    Stroke(-7, 3, 4, 4);
                    Stroke(3, 3, 4, 4);
                    Stroke(-1, -1, 2, 2);
                    break;
            }
            return value;
        }
    }
}
