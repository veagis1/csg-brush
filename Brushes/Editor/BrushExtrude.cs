using static CsgBrush.Editor.BrushLanguage;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Extrude and Bridge in brush edit mode, drawn in the Brushes overlay. The Extrude toggle shows the settings (distance,
    /// whole selection or individual faces) and previews the result in the Scene view; Apply extrudes. The faces stay
    /// selected, so Apply again extrudes again. Shift-dragging the Move gizmo on faces extrudes too (see BrushEditContext).
    /// </summary>
    [InitializeOnLoad]
    public static class BrushExtrude
    {
        static BrushExtrude()
        {
            SceneView.duringSceneGui -= DrawPreview;
            SceneView.duringSceneGui += DrawPreview;
        }

        /// <summary>The Extrude toggle: its settings are shown and the result previewed.</summary>
        public static bool Active { get => s_Active && BrushEditContext.IsActive; set { s_Active = value; if (value) BrushEditState.Mode = BrushEditMode.Face; s_Previews.Clear(); SceneView.RepaintAll(); } }
        static bool s_Active;

        /// <summary>Why the last extrude or bridge left a brush untouched, or why the previewed extrusion cannot be made.</summary>
        static string lastRefusal;

        static int SelectedFaces() { int n = 0; foreach (var b in BrushEditState.SelectedBrushes()) n += BrushEditState.Sel(b).faces.Count; return n; }

        /// <summary>The extrusion distance in metres: the panel's (one grid step when 0), on the grid when snapping is on, as it will be applied.</summary>
        static float Meters()
        {
            var s = BrushSettings.instance;
            float meters = s.extrudeDistance != 0f ? s.extrudeDistance : s.GridMeters;
            return s.snapToGrid ? BrushSnap.Round(meters, s.GridMeters) : meters;
        }

        /// <summary>The Extrude toggle and Bridge, and under them, while extruding, the settings and Apply.</summary>
        public static void DrawPanel()
        {
            var s = BrushSettings.instance;
            int faces = SelectedFaces();
            EditorGUILayout.BeginHorizontal();
            bool active = GUILayout.Toggle(Active, new GUIContent(Tr("Выдавить"), Tr("Показывает настройки и предварительный результат выдавливания выбранных граней. Кнопка «Применить» создаёт геометрию.")), EditorStyles.miniButton);
            if (active != Active) Active = active;
            using (new EditorGUI.DisabledScope(BrushEditState.Mode != BrushEditMode.Face || BridgeCandidate() == null))
            {
                if (GUILayout.Button(new GUIContent(Tr("Соединить"), Tr("Создаёт блок между двумя выбранными гранями одного браша.")), EditorStyles.miniButton))
                    BridgeSelection();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            if (!Active) { if (!string.IsNullOrEmpty(lastRefusal)) EditorGUILayout.HelpBox(TranslateRefusal(lastRefusal), MessageType.Info); return; }

            float labelWidth = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 96;
            EditorGUI.BeginChangeCheck();
            float d = EditorGUILayout.FloatField(new GUIContent(Tr("Расстояние (м)"), Tr("Расстояние выдавливания в метрах. Отрицательное значение создаёт углубление, ноль соответствует шагу сетки.")), s.extrudeDistance != 0f ? s.extrudeDistance : s.GridMeters);
            int mode = EditorGUILayout.Popup(new GUIContent(Tr("Грани"), Tr("Выдавить всё выделение одним блоком или каждую грань отдельно.")), s.extrudeIndividual ? 1 : 0, new[] { Tr("Всё выделение"), Tr("По отдельности") });
            if (EditorGUI.EndChangeCheck()) { s.extrudeDistance = d; s.extrudeIndividual = mode == 1; SceneView.RepaintAll(); }
            EditorGUIUtility.labelWidth = labelWidth;
            string refusal = PreviewRefusal();
            using (new EditorGUI.DisabledScope(faces == 0 || refusal != null))
            {
                if (GUILayout.Button(new GUIContent(Tr("Применить"), faces > 0 ? Tr("Выдавить граней: ") + faces : Tr("Сначала выберите грани.")), EditorStyles.miniButton))
                    Apply();
            }
            if (faces == 0) EditorGUILayout.HelpBox(Tr("Выберите грани для выдавливания."), MessageType.None);
            else if (refusal != null) EditorGUILayout.HelpBox(TranslateRefusal(refusal), MessageType.Info);
        }

        static string TranslateRefusal(string message)
        {
            switch (message)
            {
                case "The prism could not be built.": return Tr("Не удалось построить призму.");
                case "Select two faces of one brush.": return Tr("Выберите две грани одного браша.");
                case "The brush could not be built.": return Tr("Не удалось построить браш.");
                case "These two faces cannot be bridged.": return Tr("Эти грани нельзя соединить.");
                case "Nothing would be left of the brush.": return Tr("От браша ничего не останется.");
            }
            const string corners = "Both faces need the same number of corners (";
            if (message.StartsWith(corners))
                return Tr("У граней должно быть одинаковое число углов (") + message.Substring(corners.Length).Replace(" and ", Tr(" и "));
            const string result = "The result would be ";
            const string suffix = ". Try another distance or other faces.";
            if (message.StartsWith(result) && message.EndsWith(suffix))
            {
                string reason = message.Substring(result.Length, message.Length - result.Length - suffix.Length);
                return Tr("Некорректная форма: ") + BrushBoolean.DescribeProblem(reason) + Tr(". Измените расстояние или выберите другие грани.");
            }
            return message;
        }

        // ------------------------------------------------------------------ preview

        sealed class Preview { public int key; public BrushPolyhedron result; public string refusal; }
        static readonly Dictionary<Brush, Preview> s_Previews = new Dictionary<Brush, Preview>();

        /// <summary>The extrusion of a brush's selected faces with the current settings, made once per change of shape, selection or settings.</summary>
        static Preview PreviewOf(Brush b)
        {
            var sel = BrushEditState.Sel(b);
            var shape = BrushEditState.ShapeOf(b);
            if (sel.faces.Count == 0 || shape == null || !shape.IsValid) return null;
            float meters = Meters(); bool individual = BrushSettings.instance.extrudeIndividual;
            int key;
            unchecked
            {
                key = shape.ContentHash() * 31 + meters.GetHashCode();
                key = key * 31 + (individual ? 1 : 0);
                var faces = new List<int>(sel.faces); faces.Sort();
                foreach (int f in faces) key = key * 31 + f;
            }
            if (s_Previews.TryGetValue(b, out var p) && p.key == key) return p;
            p = new Preview { key = key };
            if (Mathf.Abs(meters) < 1e-6f) p.refusal = Tr("Расстояние округляется до нуля при текущем шаге сетки.");
            else
            {
                p.result = BrushBoolean.ExtrudeFaces(shape, sel.faces, meters, individual, out _);
                if (p.result == null || !p.result.IsValid) { p.result = null; p.refusal = BrushBoolean.LastRefusal ?? Tr("Эти грани нельзя выдавить на указанное расстояние."); }
            }
            s_Previews[b] = p;
            return p;
        }

        /// <summary>The previewed shape of a brush in its local space (its selected faces extruded with the current settings), or null.</summary>
        public static BrushPolyhedron PreviewShape(Brush b) => b != null ? PreviewOf(b)?.result : null;

        /// <summary>Extrude the selected faces with the current settings, as the preview shows (the Apply button).</summary>
        public static void Apply() => Extrude(Meters(), BrushSettings.instance.extrudeIndividual);

        static string PreviewRefusal()
        {
            foreach (var b in BrushEditState.SelectedBrushes()) { var p = PreviewOf(b); if (p != null && p.refusal != null) return p.refusal; }
            return null;
        }

        static readonly Color PreviewColor = new Color(0.35f, 0.9f, 1f, 1f);

        static void DrawPreview(SceneView view)
        {
            if (Event.current.type != EventType.Repaint || !Active) return;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = PreviewColor;
            foreach (var b in BrushEditState.SelectedBrushes())
            {
                var p = PreviewOf(b);
                if (p == null || p.result == null) continue;
                var m = b.transform.localToWorldMatrix; var poly = p.result;
                foreach (var f in poly.faces)
                {
                    var pts = new Vector3[f.indices.Length + 1];
                    for (int i = 0; i < f.indices.Length; i++) pts[i] = m.MultiplyPoint3x4(poly.vertices[f.indices[i]]);
                    pts[f.indices.Length] = pts[0];
                    Handles.DrawAAPolyLine(2.5f, pts);
                }
            }
        }

        // ------------------------------------------------------------------ actions

        /// <summary>The one brush with exactly two faces selected, or null (the Bridge button's condition).</summary>
        public static Brush BridgeCandidate()
        {
            Brush found = null;
            foreach (var b in BrushEditState.SelectedBrushes())
            {
                int count = BrushEditState.Sel(b).faces.Count;
                if (count == 0) continue;
                if (count != 2 || found != null) return null;
                found = b;
            }
            return found;
        }

        /// <summary>Bridge the two selected faces of the selected brush (the Bridge button in the Brushes overlay); the new walls end up selected.</summary>
        public static void BridgeSelection()
        {
            var b = BridgeCandidate();
            if (b == null) return;
            int group = Undo.GetCurrentGroup();
            lastRefusal = null;
            var sel = BrushEditState.Sel(b);
            var two = new List<int>(sel.faces);
            var remap = BrushApi.BridgeFaces(b, two[0], two[1]);
            if (remap == null) { lastRefusal = BrushBoolean.LastRefusal; SceneView.RepaintAll(); return; }
            var walls = new HashSet<int>();
            for (int f = 0; f < b.polyhedron.faces.Length; f++) if (b.polyhedron.faces[f].source == two[0] && (f >= remap.Length || remap[two[0]] != f)) walls.Add(f);
            sel.faces = walls; sel.vertices.Clear(); sel.edges.Clear();
            Undo.CollapseUndoOperations(group);
            BrushApi.ForceUpdate();
            SceneView.RepaintAll();
        }

        /// <summary>Extrude the selected faces of every selected brush; the extruded faces stay selected.</summary>
        public static void Extrude(float meters, bool individual)
        {
            int group = Undo.GetCurrentGroup();
            lastRefusal = null;
            foreach (var b in BrushEditState.SelectedBrushes())
            {
                var sel = BrushEditState.Sel(b);
                if (sel.faces.Count == 0) continue;
                var remap = BrushApi.ExtrudeFaces(b, new List<int>(sel.faces), meters, individual);
                if (remap == null) { lastRefusal = BrushBoolean.LastRefusal; continue; }
                var kept = new HashSet<int>();
                foreach (var f in sel.faces) if (f < remap.Length && remap[f] >= 0) kept.Add(remap[f]);
                sel.faces = kept; sel.vertices.Clear(); sel.edges.Clear();
            }
            Undo.CollapseUndoOperations(group);
            BrushApi.ForceUpdate();
            SceneView.RepaintAll();
        }
    }
}
