using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace CsgBrush.Editor
{
    /// <summary>Scene view overlay: grid size and snap, the new brush's parameters while a Create tool is active, the edit mode toggle, lint.</summary>
    [Icon(BrushIcons.Folder + "Brush.png")]
    [Overlay(typeof(SceneView), "Brushes", "CSG Brush", "Brushes", true)]
    public sealed class BrushOverlay : Overlay, ICreateHorizontalToolbar, ICreateVerticalToolbar
    {
        const float PreferredWidth = 315f, Gap = 4f;

        /// <summary>The panel's width: rows of buttons wrap to fit it.</summary>
        static float s_Width = PreferredWidth;

        public OverlayToolbar CreateHorizontalToolbarContent() => CreateOperationToolbar();
        public OverlayToolbar CreateVerticalToolbarContent() => CreateOperationToolbar();

        static OverlayToolbar CreateOperationToolbar()
        {
            var toolbar = new OverlayToolbar();
            toolbar.Add(new IMGUIContainer(DrawCreateMenu));
            foreach (var operation in new[] { BrushOperation.Add, BrushOperation.Subtract })
            {
                var button = new EditorToolbarButton(operation == BrushOperation.Add ? "+ добавить" : "- вычесть", () =>
                {
                    BrushCreateTool.ManualOperation = BrushCreateTool.ManualOperation == operation ? null : operation;
                    SceneView.RepaintAll();
                });
                toolbar.Add(button);
                button.schedule.Execute(() =>
                {
                    var tool = BrushCreateTool.Active;
                    bool visible = tool != null && !BrushCreateTool.IsOpening(tool.Shape);
                    button.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                    button.SetEnabled(visible && !tool.Drawing);
                    bool chosen = BrushCreateTool.ManualOperation == operation;
                    button.style.borderBottomWidth = chosen ? 2f : StyleKeyword.Null;
                    button.style.borderBottomColor = chosen ? new Color(0.20f, 0.75f, 0.82f) : StyleKeyword.Null;
                }).Every(150);
            }
            toolbar.SetupChildrenAsButtonStrip();
            return toolbar;
        }

        static void DrawCreateMenu()
        {
            var label = new GUIContent("Создать", "Выберите форму, затем укажите её положение и размер в сцене.");
            if (EditorGUILayout.DropdownButton(label, FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.ExpandWidth(false)))
                EditorUtility.DisplayPopupMenu(GUILayoutUtility.GetLastRect(), "CSG Brush/Создать/", null);
        }

        public override VisualElement CreatePanelContent()
        {
            IMGUIContainer container = null;
            container = new IMGUIContainer(() =>
            {
                float width = container.contentRect.width;
                s_Width = float.IsNaN(width) || width < PreferredWidth ? PreferredWidth : width;
                s_Width -= 8f; // the panel's padding
                float labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Mathf.Clamp(s_Width * 0.45f, 145f, 190f); // from the panel, not the whole Scene view
                try { Draw(); }
                finally { EditorGUIUtility.labelWidth = labelWidth; }
            });
            container.style.minWidth = PreferredWidth;
            return container;
        }

        /// <summary>A row of buttons that wraps: before a button of this width, start a new row if it would not fit.</summary>
        static void Wrap(ref float x, float width)
        {
            if (x > 0f && x + width > s_Width)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                x = 0f;
            }
            x += width + Gap;
        }

        /// <summary>A fixed width for the next button of a wrapping row, counted along the row.</summary>
        static GUILayoutOption Width(ref float x, float width)
        {
            x += width + Gap;
            return GUILayout.Width(width);
        }

        static readonly string[] SelectionIcons = { "ShapeEdit_Single", "ShapeEdit_Offset", "ShapeEdit_Match" };
        static readonly string[] SelectionNames = { "Один", "Сдвиг", "Равно" };
        static readonly string[] SelectionTips =
        {
            "Один браш. Меняется только браш, манипулятор которого вы тянете.",
            "Относительная правка. Выбранные браши той же формы меняются на одинаковую величину.",
            "Выбранные браши той же формы получают новое значение изменённого параметра.",
        };

        /// <summary>An icon, or its name while the icon isn't imported yet.</summary>
        static GUIContent IconOrName(string icon, string name, string tooltip)
        {
            var image = BrushIcons.Get(icon);
            return image != null ? new GUIContent(image, tooltip) : new GUIContent(name, tooltip);
        }

        /// <summary>The Shape Edit toggle and, while it is on, what a drag does to the other selected brushes (Single / Offset / Match): part of the grid row.</summary>
        static void DrawShapeEdit(BrushSettings s, ref float x)
        {
            var icon = BrushIcons.Get("ShapeEdit");
            var content = new GUIContent(icon != null ? " Правка формы" : "Правка формы", icon, "Тяните грань для изменения формы (B). Ctrl перемещает её шагами текущей сетки от начала жеста. Shift перемещает обе стороны симметрично.");
            float width = EditorStyles.miniButton.CalcSize(content).x + (s.shapeHandles ? Gap + SelectionIcons.Length * 24f : 0f);
            Wrap(ref x, width); // the toggle and its choices stay together
            bool shapes = GUILayout.Toggle(s.shapeHandles, content, EditorStyles.miniButton, GUILayout.Height(18));
            if (shapes != s.shapeHandles) { s.shapeHandles = shapes; s.NotifyChanged(); SceneView.RepaintAll(); }
            if (s.shapeHandles)
            {
                var options = new GUIContent[SelectionIcons.Length];
                for (int i = 0; i < options.Length; i++) options[i] = IconOrName(SelectionIcons[i], SelectionNames[i], SelectionTips[i]);
                int current = Mathf.Clamp((int)s.shapeEditSelection, 0, options.Length - 1);
                int chosen = GUILayout.Toolbar(current, options, EditorStyles.miniButton, GUILayout.Width(options.Length * 24), GUILayout.Height(18));
                if (chosen != current) { s.shapeEditSelection = (ShapeEditSelection)chosen; s.NotifyChanged(); }
            }
        }

        static void Draw()
        {
            var s = BrushSettings.instance;
            EditorGUILayout.BeginVertical();

            // grid row: icon, size dropdown, snap toggle, view toggles, Shape Edit
            EditorGUILayout.BeginHorizontal();
            float x = 0f;
            var gridIcon = EditorGUIUtility.IconContent("GridAxisY");
            GUILayout.Label(gridIcon != null && gridIcon.image != null ? new GUIContent(gridIcon.image, "Шаг сетки. Клавиши [ и ] уменьшают и увеличивают шаг.") : new GUIContent("Сетка"), Width(ref x, gridIcon != null && gridIcon.image != null ? 20 : 42), GUILayout.Height(18));
            var names = s.GridStepNames();
            int idx = EditorGUILayout.Popup(Mathf.Clamp(s.gridIndex, 0, names.Length - 1), names, Width(ref x, 74));
            if (idx != s.gridIndex) s.SetGridIndex(idx);
            var snapIcon = EditorGUIUtility.IconContent("SceneViewSnap");
            var snapContent = snapIcon != null && snapIcon.image != null ? new GUIContent(snapIcon.image, "Привязка к сетке. При правке привязывает положение, размеры, поворот и вершины.") : new GUIContent("Привязка");
            bool snap = GUILayout.Toggle(s.snapToGrid, snapContent, EditorStyles.miniButton, Width(ref x, snapIcon != null && snapIcon.image != null ? 26 : 70), GUILayout.Height(18));
            if (snap != s.snapToGrid) { s.snapToGrid = snap; s.NotifyChanged(); }
            bool cuts = GUILayout.Toggle(s.showCuts, new GUIContent("Вычеты", "Показывать вычитающие браши полупрозрачным красным объёмом. Так их можно выбрать, даже если они полностью убрали геометрию."), EditorStyles.miniButton, Width(ref x, 54), GUILayout.Height(18));
            if (cuts != s.showCuts) { s.showCuts = cuts; s.NotifyChanged(); SceneView.RepaintAll(); }
            bool sizes = GUILayout.Toggle(s.showDimensions, new GUIContent("Размеры", BrushSettings.DimensionsTooltip), EditorStyles.miniButton, Width(ref x, 62), GUILayout.Height(18));
            if (sizes != s.showDimensions) { s.showDimensions = sizes; s.NotifyChanged(); SceneView.RepaintAll(); }
            DrawShapeEdit(s, ref x);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCreateMenu();
                BrushSurfaceGrid.DrawMenu();
            }
            if (BrushCreateTool.Active != null) DrawNewBrush(s, BrushCreateTool.Active);

            // edit mode: one row, Edit brush toggle then Vertex / Edge / Face
            EditorGUILayout.BeginHorizontal();
            bool editing = BrushEditContext.IsActive;
            bool editable = false;
            foreach (var go in Selection.gameObjects)
                if (go.TryGetComponent<Brush>(out var b) && (b.shape == BrushShape.Custom || BrushApi.CanConvertToCustom(b.shape))) { editable = true; break; }
            if (editing && !editable) BrushEditContext.Exit();
            bool plan = !editable && Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<FloorPlan>() != null;
            if (plan)
            {
                bool planEditing = FloorPlanEditContext.IsActive;
                if (GUILayout.Toggle(planEditing, new GUIContent("Правка плана стен", "Перемещение, поворот и масштаб действуют на выделенные точки или стены. Клавиши 1 и 2 меняют режим выбора."), EditorStyles.miniButton) != planEditing)
                {
                    if (planEditing) FloorPlanEditContext.Exit(); else FloorPlanEditContext.Enter();
                }
            }
            else using (new EditorGUI.DisabledScope(!editable))
            {
                bool wantEditing = GUILayout.Toggle(editing, new GUIContent("Правка браша", editable ? "Перемещение, поворот и масштаб действуют на выделенные вершины, рёбра или грани. Режим выбора меняется на панели инструментов или клавишами 1, 2 и 3." : "Выберите браш."), EditorStyles.miniButton);
                if (wantEditing != editing) { if (wantEditing) BrushEditContext.Enter(); else BrushEditContext.Exit(); }
            }
            EditorGUILayout.EndHorizontal();
            if (editing) BrushExtrude.DrawPanel();

            // lint: parents that rotate or scale brushes off the grid
            var parents = BrushSnap.TransformedParents();
            if (parents.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Поворот / масштаб у родителей: {parents.Count}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Выбрать", "Выделить родительские объекты с поворотом или масштабом."))) Selection.objects = parents.ConvertAll(t => (Object)t.gameObject).ToArray();
                if (GUILayout.Button(new GUIContent("Сбросить", "Сбросить поворот и масштаб этих родительских объектов."))) { BrushSnap.ResetTransformedParents(); BrushApi.ForceUpdate(); }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>Linear and curved stairs: solid under the steps, or slabs of a thickness.</summary>
        static void SupportUnderSteps(BrushSettings s)
        {
            s.newSupportUnderSteps = EditorGUILayout.Toggle(new GUIContent("Опора под ступенями", "Включено — под ступенями сплошная опора до пола. Выключено — отдельные плиты."), s.newSupportUnderSteps);
            if (!s.newSupportUnderSteps) s.newStepThickness = MetersField("Толщина ступени", "Толщина плиты в метрах. При нуле используется 0,1 м.", s.newStepThickness, BrushSettings.DefaultStepThicknessMeters);
        }

        static float MetersField(string label, string tooltip, float meters, float placeholderMeters)
        {
            EditorGUI.BeginChangeCheck();
            float shown = meters > 0f ? meters : placeholderMeters;
            float v = EditorGUILayout.FloatField(new GUIContent(label + " (м)", tooltip), shown);
            return EditorGUI.EndChangeCheck() ? Mathf.Max(0f, v) : meters;
        }

        /// <summary>
        /// The modules every new brush gets, as one dropdown however many there are: it shows Nothing, the one module, or how
        /// many, and opens a checklist of all of them, as Unity's layer mask fields do.
        /// </summary>
        static string ModuleName(System.Type type) => type == typeof(BrushTrigger) ? "Триггер браша" : ObjectNames.NicifyVariableName(type.Name);

        static void ModulesDropdown(BrushSettings s, System.Collections.Generic.List<System.Type> moduleTypes)
        {
            var chosen = moduleTypes.FindAll(t => s.newModules.Contains(t.FullName));
            string summary = chosen.Count == 0 ? "Без модулей" : chosen.Count == 1 ? ModuleName(chosen[0]) : $"Модулей: {chosen.Count}";
            var rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent("Модули", "Компоненты с игровыми данными, которые будут добавлены к каждому новому брашу."));
            if (!EditorGUI.DropdownButton(rect, new GUIContent(summary, string.Join(", ", chosen.ConvertAll(ModuleName))), FocusType.Keyboard)) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Без модулей"), chosen.Count == 0, () => { s.newModules.Clear(); s.NotifyChanged(); });
            menu.AddSeparator("");
            foreach (var t in moduleTypes)
            {
                string name = t.FullName;
                bool on = s.newModules.Contains(name);
                menu.AddItem(new GUIContent(ModuleName(t)), on, () =>
                {
                    if (on) s.newModules.Remove(name); else s.newModules.Add(name);
                    s.NotifyChanged();
                });
            }
            menu.DropDown(rect);
        }

        /// <summary>What the next brush of the active Create tool gets: operation, surface and the shape's parameters (Project Settings > Brushes holds them).</summary>
        static void DrawNewBrush(BrushSettings s, BrushCreateTool tool)
        {
            var shape = tool.Shape;
            float labelWidth = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 155;
            EditorGUILayout.LabelField(tool.Title, EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            if (!BrushCreateTool.IsOpening(shape)) // a door or window is always a cut
            {
                tool.DrawOperationPanel();
                s.newGridSnap = (GridSnap)EditorGUILayout.EnumPopup(new GUIContent("Привязка к сетке", "К сетке привязываются крайние вершины формы либо точка опоры. Привязку можно отключить."), s.newGridSnap);
            }
            var moduleTypes = BrushApi.ModuleTypes();
            if (moduleTypes.Count > 0) ModulesDropdown(s, moduleTypes);
            BrushCreateTool.NewBrushParameters(out float stepHeight, out float wall);
            switch (shape)
            {
                case BrushShape.Cylinder:
                case BrushShape.Cone:
                    s.newSides = Mathf.Max(3, EditorGUILayout.IntField(new GUIContent("Стороны", "Число сторон по окружности, от 3. Чем их больше, тем плавнее контур."), s.newSides));
                    break;
                case BrushShape.Sphere:
                    s.newTessellation = EditorGUILayout.IntSlider(new GUIContent("Детализация", "Уровень разбиения сферы от 1 до 5. Чем больше значение, тем больше граней и плавнее поверхность."), s.newTessellation, 1, 5);
                    break;
                case BrushShape.Stairs:
                    s.newStepHeight = MetersField("Высота ступени", "Высота ступени в метрах. Чем больше значение, тем меньше ступеней на заданной высоте. При нуле используется 0,25 м.", s.newStepHeight, stepHeight);
                    SupportUnderSteps(s);
                    break;
                case BrushShape.CurvedStairs:
                    s.newInnerRadius = MetersField("Внутренний радиус", "Радиус в метрах. Ноль соответствует шагу сетки.", s.newInnerRadius, s.GridMeters);
                    s.newStepHeight = MetersField("Высота ступени", "Высота ступени в метрах. Чем больше значение, тем меньше ступеней на заданной высоте. При нуле используется 0,25 м.", s.newStepHeight, stepHeight);
                    s.newCurveAngle = EditorGUILayout.FloatField(new GUIContent("Угол поворота", "Угол, который проходит лестница, в градусах. Чем больше угол, тем дальше идут ступени вокруг опоры."), s.newCurveAngle);
                    s.newCounterClockwise = EditorGUILayout.Toggle(new GUIContent("Против часовой", "Включено — подъём против часовой стрелки. Выключено — по часовой стрелке. Направление оценивается сверху."), s.newCounterClockwise);
                    SupportUnderSteps(s);
                    break;
                case BrushShape.SpiralStairs:
                    s.newInnerRadius = MetersField("Внутренний радиус", "Радиус в метрах. Ноль соответствует шагу сетки.", s.newInnerRadius, s.GridMeters);
                    s.newStepThickness = MetersField("Толщина ступени", "Толщина плиты в метрах. При нуле используется 0,1 м.", s.newStepThickness, BrushSettings.DefaultStepThicknessMeters);
                    s.newStepHeight = MetersField("Высота ступени", "Высота ступени в метрах. Чем больше значение, тем меньше ступеней на заданной высоте. При нуле используется 0,25 м.", s.newStepHeight, stepHeight);
                    s.newStepsPer360 = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Ступеней на оборот", "Число ступеней на 360°, минимум одна. Чем больше ступеней, тем меньше поворот между соседними."), s.newStepsPer360));
                    s.newSlopedCeiling = EditorGUILayout.Toggle(new GUIContent("Наклонный потолок", "Заменяет ступенчатую нижнюю поверхность лестницы наклонной."), s.newSlopedCeiling);
                    s.newSlopedFloor = EditorGUILayout.Toggle(new GUIContent("Наклонный пол", "Заменяет ступенчатую верхнюю поверхность лестницы наклонной."), s.newSlopedFloor);
                    s.newCounterClockwise = EditorGUILayout.Toggle(new GUIContent("Против часовой", "Включено — подъём против часовой стрелки. Выключено — по часовой стрелке. Направление оценивается сверху."), s.newCounterClockwise);
                    break;
                case BrushShape.Arch:
                    s.newWallThickness = MetersField("Толщина", "Толщина арки в метрах. Ноль соответствует шагу сетки.", s.newWallThickness, wall);
                    s.newArchAngle = EditorGUILayout.Slider(new GUIContent("Угол", "Угол арки в градусах. 180° даёт полную арку. Чем меньше угол, тем меньшая часть вершины остаётся."), s.newArchAngle, 1f, 180f);
                    s.newSides = Mathf.Max(3, EditorGUILayout.IntField(new GUIContent("Сегменты", "Число сегментов арки, от 3. Чем их больше, тем плавнее дуга."), s.newSides));
                    break;
                case BrushShape.Door:
                    s.newDoorWidth = MetersField("Ширина", "Ширина дверного проёма в метрах. При нуле используется 1 м.", s.newDoorWidth, BrushSettings.DefaultDoorWidthMeters);
                    s.newDoorHeight = MetersField("Высота", "Высота дверного проёма в метрах. При нуле используется 2,2 м.", s.newDoorHeight, BrushSettings.DefaultDoorHeightMeters);
                    break;
                case BrushShape.Window:
                    s.newWindowWidth = MetersField("Ширина", "Ширина оконного проёма в метрах. При нуле используется 1,2 м.", s.newWindowWidth, BrushSettings.DefaultWindowWidthMeters);
                    s.newWindowHeight = MetersField("Высота", "Высота оконного проёма в метрах. При нуле используется 1,2 м.", s.newWindowHeight, BrushSettings.DefaultWindowHeightMeters);
                    s.newWindowSill = MetersField("Подоконник", "Высота нижней кромки окна над полом в метрах. При нуле используется 0,9 м.", s.newWindowSill, BrushSettings.DefaultWindowSillMeters);
                    break;
            }
            if (shape == BrushShape.Box || shape == BrushShape.Cylinder)
            {
                s.newHollow = EditorGUILayout.Toggle(new GUIContent("Полый браш", "Включено — создаются только стены заданной толщины. Выключено — создаётся сплошной браш."), s.newHollow);
                using (new EditorGUI.DisabledScope(!s.newHollow))
                    s.newWallThickness = MetersField("Толщина стены", "Толщина стены в метрах. Ноль соответствует шагу сетки.", s.newWallThickness, wall);
            }
            if (EditorGUI.EndChangeCheck()) { s.NotifyChanged(); SceneView.RepaintAll(); }
            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUILayout.Space(2);
        }

        [Shortcut("Brushes/Grid smaller", typeof(SceneView), KeyCode.LeftBracket, displayName = "CSG Brush/Уменьшить шаг сетки")]
        static void GridSmaller() { BrushSettings.instance.SetGridIndex(BrushSettings.instance.gridIndex - 1); }

        [Shortcut("Brushes/Grid larger", typeof(SceneView), KeyCode.RightBracket, displayName = "CSG Brush/Увеличить шаг сетки")]
        static void GridLarger() { BrushSettings.instance.SetGridIndex(BrushSettings.instance.gridIndex + 1); }
    }
}
