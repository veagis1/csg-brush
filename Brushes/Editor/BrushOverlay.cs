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
        const float PreferredWidth = 315f, MinimumWidth = 250f;

        public OverlayToolbar CreateHorizontalToolbarContent() => CreateOperationToolbar();
        public OverlayToolbar CreateVerticalToolbarContent() => CreateOperationToolbar();

        static OverlayToolbar CreateOperationToolbar()
        {
            var toolbar = new OverlayToolbar();
            toolbar.Add(new IMGUIContainer(DrawCreateMenu));
            toolbar.Add(new IMGUIContainer(ObjectPlacementTool.DrawButton));
            toolbar.Add(new IMGUIContainer(() => DrawDimensionsAndSnapping(BrushSettings.instance)));
            toolbar.Add(new IMGUIContainer(DrawEditToggles));
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
            float panelWidth = PreferredWidth;
            container = new IMGUIContainer(() =>
            {
                float width = container.contentRect.width;
                if (Event.current.type == EventType.Layout)
                    panelWidth = (float.IsNaN(width) || width <= 0f ? PreferredWidth : Mathf.Max(MinimumWidth, width)) - 8f;
                float labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Mathf.Clamp(panelWidth * 0.45f, 145f, 190f); // from the panel, not the whole Scene view
                try { Draw(); }
                finally { EditorGUIUtility.labelWidth = labelWidth; }
            });
            container.style.minWidth = MinimumWidth;
            return container;
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

        static void DrawEditToggles()
        {
            using var row = new EditorGUILayout.HorizontalScope();
            var icon = BrushIcons.Get("ShapeEdit");
            var content = new GUIContent(icon != null ? " Правка формы" : "Правка формы", icon, "Тяните грань для изменения формы (B). Ctrl перемещает её шагами текущей сетки от начала жеста. Shift перемещает обе стороны симметрично.");
            bool shapeEdit = BrushShapeHandles.Enabled;
            bool shapes = GUILayout.Toggle(shapeEdit, content, EditorStyles.miniButton, GUILayout.ExpandWidth(false), GUILayout.Height(18));
            if (shapes != shapeEdit) BrushShapeHandles.Enabled = shapes;
            bool editable = false;
            foreach (var go in Selection.gameObjects)
                if (go.TryGetComponent<Brush>(out var b) && (b.shape == BrushShape.Custom || BrushApi.CanConvertToCustom(b.shape))) { editable = true; break; }
            using var disabled = new EditorGUI.DisabledScope(!editable);
            bool editing = BrushEditContext.IsActive;
            var edit = new GUIContent("Правка браша", editable ? "Перемещение, поворот и масштаб действуют на выделенные вершины, рёбра или грани. Режим выбора меняется на панели инструментов или клавишами 1, 2 и 3." : "Выберите браш.");
            if (GUILayout.Toggle(editing, edit, EditorStyles.miniButton, GUILayout.ExpandWidth(false), GUILayout.Height(18)) != editing)
                BrushEditContext.Toggle();
        }

        static void DrawShapeEditSelection(BrushSettings s)
        {
            var options = new GUIContent[SelectionIcons.Length];
            for (int i = 0; i < options.Length; i++) options[i] = IconOrName(SelectionIcons[i], SelectionNames[i], SelectionTips[i]);
            int current = Mathf.Clamp((int)s.shapeEditSelection, 0, options.Length - 1);
            int chosen = GUILayout.Toolbar(current, options, EditorStyles.miniButton, GUILayout.Width(options.Length * 24), GUILayout.Height(18));
            if (chosen != current) { s.shapeEditSelection = (ShapeEditSelection)chosen; s.NotifyChanged(); }
        }

        static void DrawDimensionsAndSnapping(BrushSettings s)
        {
            using var row = new EditorGUILayout.HorizontalScope();
            bool sizes = GUILayout.Toggle(s.showDimensions, new GUIContent("Размеры", BrushSettings.DimensionsTooltip), EditorStyles.miniButton, GUILayout.Width(62), GUILayout.Height(18));
            if (sizes != s.showDimensions) { s.showDimensions = sizes; s.NotifyChanged(); SceneView.RepaintAll(); }
            BrushSurfaceGrid.DrawMenu();
            var snapIcon = EditorGUIUtility.IconContent("SceneViewSnap");
            var snapContent = snapIcon != null && snapIcon.image != null ? new GUIContent(snapIcon.image, "Привязка к сетке. При правке привязывает положение, размеры, поворот и вершины.") : new GUIContent("Привязка");
            bool snap = GUILayout.Toggle(s.snapToGrid, snapContent, EditorStyles.miniButton, GUILayout.Width(snapIcon != null && snapIcon.image != null ? 26 : 70), GUILayout.Height(18));
            if (snap != s.snapToGrid) { s.snapToGrid = snap; s.NotifyChanged(); }
        }

        static void Draw()
        {
            var s = BrushSettings.instance;
            bool showShapeSelection = BrushShapeHandles.Enabled;
            EditorGUILayout.BeginVertical();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCreateMenu();
                ObjectPlacementTool.DrawButton();
            }

            // Independent row keeps each pair together even at the minimum width.
            EditorGUILayout.BeginHorizontal();
            var gridIcon = EditorGUIUtility.IconContent("GridAxisY");
            GUILayout.Label(gridIcon != null && gridIcon.image != null ? new GUIContent(gridIcon.image, "Шаг сетки. Клавиши [ и ] уменьшают и увеличивают шаг.") : new GUIContent("Сетка"), GUILayout.Width(gridIcon != null && gridIcon.image != null ? 20 : 42), GUILayout.Height(18));
            var names = s.GridStepNames();
            int idx = EditorGUILayout.Popup(Mathf.Clamp(s.gridIndex, 0, names.Length - 1), names, GUILayout.Width(74));
            if (idx != s.gridIndex) s.SetGridIndex(idx);
            bool cuts = GUILayout.Toggle(s.showCuts, new GUIContent("Вычеты", "Показывать вычитающие браши полупрозрачным красным объёмом. Так их можно выбрать, даже если они полностью убрали геометрию."), EditorStyles.miniButton, GUILayout.Width(54), GUILayout.Height(18));
            if (cuts != s.showCuts) { s.showCuts = cuts; s.NotifyChanged(); SceneView.RepaintAll(); }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            DrawDimensionsAndSnapping(s);
            DrawEditToggles();
            if (showShapeSelection) DrawShapeEditSelection(s);
            if (BrushCreateTool.Active != null) DrawNewBrush(s, BrushCreateTool.Active);

            var selected = Selection.activeGameObject;
            if (selected != null && selected.TryGetComponent<FloorPlan>(out _))
            {
                bool planEditing = FloorPlanEditContext.IsActive;
                if (GUILayout.Toggle(planEditing, new GUIContent("Правка плана стен", "Перемещение, поворот и масштаб действуют на выделенные точки или стены. Клавиши 1 и 2 меняют режим выбора."), EditorStyles.miniButton) != planEditing)
                {
                    if (planEditing) FloorPlanEditContext.Exit(); else FloorPlanEditContext.Enter();
                }
            }
            if (BrushEditContext.IsActive) BrushExtrude.DrawPanel();

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
