using static CsgBrush.Editor.BrushLanguage;
using UnityEditor;
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
        const float PreferredWidth = 335f, MinimumWidth = 290f;

        public OverlayToolbar CreateHorizontalToolbarContent() => CreateOperationToolbar();
        public OverlayToolbar CreateVerticalToolbarContent() => CreateOperationToolbar();

        static OverlayToolbar CreateOperationToolbar()
        {
            var toolbar = new OverlayToolbar();
            toolbar.Add(new IMGUIContainer(DrawCreateMenu));
            toolbar.Add(new IMGUIContainer(ObjectPlacementTool.DrawButton));
            toolbar.Add(new IMGUIContainer(SurfacePlacementMode.DrawButton));
            toolbar.Add(new IMGUIContainer(() => DrawGridAndVisibility(BrushSettings.instance)));
            toolbar.Add(new IMGUIContainer(DrawSnapMenus));
            toolbar.Add(new IMGUIContainer(DrawTransformTools));
            toolbar.Add(new IMGUIContainer(DrawEditToggles));
            foreach (var operation in new[] { BrushOperation.Add, BrushOperation.Subtract })
            {
                var button = new EditorToolbarButton(() =>
                {
                    BrushCreateTool.ManualOperation = BrushCreateTool.ManualOperation == operation ? null : operation;
                    SceneView.RepaintAll();
                });
                string caption = operation == BrushOperation.Add ? "+ добавить" : "- вычесть";
                BrushLanguage.Bind(button, () => button.text = Tr(caption));
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
            var label = new GUIContent(Tr("Создать"), Tr("Выберите форму, затем укажите её положение и размер в сцене."));
            var style = EditorStyles.toolbarDropDown;
            var rect = GUILayoutUtility.GetRect(label, style, GUILayout.ExpandWidth(false));
            if (EditorGUI.DropdownButton(rect, label, FocusType.Passive, style))
                BrushCommandMenu.BuildCreateMenu().DropDown(rect);
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

        static void DrawTransformTools()
        {
            using var row = new EditorGUILayout.HorizontalScope();
            DrawTransformTool(Tool.Move, "MoveTool", "W", "Переместить выделение");
            DrawTransformTool(Tool.Rotate, "RotateTool", "E", "Повернуть выделение");
            DrawTransformTool(Tool.Scale, "ScaleTool", "R", "Масштабировать выделение");
        }

        static void DrawTransformTool(Tool tool, string icon, string fallback, string tooltip)
        {
            var image = EditorGUIUtility.IconContent(icon).image;
            var content = new GUIContent(image == null ? fallback : "", image, Tr(tooltip));
            bool active = Tools.current == tool;
            if (GUILayout.Toggle(active, content, EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(24)) && !active)
                BrushMenu.ActivateTransform(tool);
        }

        static void DrawEditToggles()
        {
            using var row = new EditorGUILayout.HorizontalScope();
            var icon = BrushIcons.Get("ShapeEdit");
            var content = new GUIContent(icon != null ? Tr(" Правка формы") : Tr("Правка формы"), icon, Tr("Тяните грань для изменения формы (B). Ctrl перемещает её шагами текущей сетки от начала жеста. Shift перемещает обе стороны симметрично."));
            bool shapeEdit = BrushShapeHandles.Enabled;
            bool shapes = GUILayout.Toggle(shapeEdit, content, EditorStyles.miniButton, GUILayout.ExpandWidth(false), GUILayout.Height(18));
            if (shapes != shapeEdit) BrushShapeHandles.Enabled = shapes;
            bool editable = false;
            foreach (var go in Selection.gameObjects)
                if (go.TryGetComponent<Brush>(out var b) && (b.shape == BrushShape.Custom || BrushApi.CanConvertToCustom(b.shape))) { editable = true; break; }
            using var disabled = new EditorGUI.DisabledScope(!editable);
            bool editing = BrushEditContext.IsActive;
            var edit = new GUIContent(Tr("Правка браша"), editable ? Tr("Перемещение, поворот и масштаб действуют на выделенные вершины, рёбра или грани. Режим выбора меняется на панели инструментов или клавишами 1, 2 и 3.") : Tr("Выберите браш."));
            if (GUILayout.Toggle(editing, edit, EditorStyles.miniButton, GUILayout.ExpandWidth(false), GUILayout.Height(18)) != editing)
                BrushEditContext.Toggle();
        }

        static void DrawShapeEditSelection(BrushSettings s)
        {
            var options = new GUIContent[SelectionIcons.Length];
            for (int i = 0; i < options.Length; i++) options[i] = IconOrName(SelectionIcons[i], Tr(SelectionNames[i]), Tr(SelectionTips[i]));
            int current = Mathf.Clamp((int)s.shapeEditSelection, 0, options.Length - 1);
            int chosen = GUILayout.Toolbar(current, options, EditorStyles.miniButton, GUILayout.Width(options.Length * 24), GUILayout.Height(18));
            if (chosen != current) { s.shapeEditSelection = (ShapeEditSelection)chosen; s.NotifyChanged(); }
        }

        static void DrawGridAndVisibility(BrushSettings s)
        {
            using var row = new EditorGUILayout.HorizontalScope();
            const string snapTip = "Включить привязку к сетке при правке положения, размеров, поворота и вершин. Шаг указан справа в метрах. Клавиши [ и ] уменьшают и увеличивают шаг.";
            var icon = EditorGUIUtility.IconContent("SceneViewSnap")?.image;
            var snapContent = icon != null ? new GUIContent(icon, Tr(snapTip)) : new GUIContent(Tr("Сетка"), Tr(snapTip));
            bool snap = GUILayout.Toggle(s.snapToGrid, snapContent, EditorStyles.miniButton, GUILayout.Width(icon != null ? 26 : 42), GUILayout.Height(18));
            if (snap != s.snapToGrid) { s.snapToGrid = snap; s.NotifyChanged(); }
            var names = s.GridStepNames();
            int idx = EditorGUILayout.Popup(Mathf.Clamp(s.gridIndex, 0, names.Length - 1), names, GUILayout.Width(74));
            if (idx != s.gridIndex) s.SetGridIndex(idx);
            bool cuts = GUILayout.Toggle(s.showCuts, new GUIContent(Tr("Вычеты"), Tr("Показывать вычитающие браши полупрозрачным красным объёмом. Так их можно выбрать, даже если они полностью убрали геометрию.")), EditorStyles.miniButton, GUILayout.Width(BrushLanguage.English ? 42 : 54), GUILayout.Height(18));
            if (cuts != s.showCuts) { s.showCuts = cuts; s.NotifyChanged(); SceneView.RepaintAll(); }
            bool sizes = GUILayout.Toggle(s.showDimensions, new GUIContent(Tr("Размеры"), Tr(BrushSettings.DimensionsTooltip)), EditorStyles.miniButton, GUILayout.Width(BrushLanguage.English ? 76 : 62), GUILayout.Height(18));
            if (sizes != s.showDimensions) { s.showDimensions = sizes; s.NotifyChanged(); SceneView.RepaintAll(); }
            var offset = new GUIContent("Δ", Tr("Относительное смещение") + "\n" + Tr(BrushSettings.RelativeDisplacementTooltip));
            bool showOffset = GUILayout.Toggle(s.showRelativeDisplacement, offset, EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(20));
            if (showOffset != s.showRelativeDisplacement) { s.showRelativeDisplacement = showOffset; s.NotifyChanged(); SceneView.RepaintAll(); }
        }

        static void DrawSnapMenus()
        {
            using var row = new EditorGUILayout.HorizontalScope();
            BrushSurfaceGrid.DrawMenu();
            BrushPivotTool.DrawMenu();
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
                SurfacePlacementMode.DrawButton();
            }

            DrawGridAndVisibility(s);
            DrawSnapMenus();
            DrawTransformTools();
            DrawEditToggles();
            if (showShapeSelection) DrawShapeEditSelection(s);
            if (BrushCreateTool.Active != null) DrawNewBrush(s, BrushCreateTool.Active);

            var selected = Selection.activeGameObject;
            if (selected != null && selected.TryGetComponent<FloorPlan>(out _))
            {
                bool planEditing = FloorPlanEditContext.IsActive;
                if (GUILayout.Toggle(planEditing, new GUIContent(Tr("Правка плана стен"), Tr("Перемещение, поворот и масштаб действуют на выделенные точки или стены. Клавиши 1 и 2 меняют режим выбора.")), EditorStyles.miniButton) != planEditing)
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
                EditorGUILayout.LabelField(BrushLanguage.Format("Поворот / масштаб у родителей: {0}", parents.Count), EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent(Tr("Выбрать"), Tr("Выделить родительские объекты с поворотом или масштабом.")))) Selection.objects = parents.ConvertAll(t => (Object)t.gameObject).ToArray();
                if (GUILayout.Button(new GUIContent(Tr("Сбросить"), Tr("Сбросить поворот и масштаб этих родительских объектов.")))) { BrushSnap.ResetTransformedParents(); BrushApi.ForceUpdate(); }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>Linear and curved stairs: solid under the steps, or slabs of a thickness.</summary>
        static void SupportUnderSteps(BrushSettings s)
        {
            s.newSupportUnderSteps = EditorGUILayout.Toggle(new GUIContent(Tr("Опора под ступенями"), Tr("Включено — под ступенями сплошная опора до пола. Выключено — отдельные плиты.")), s.newSupportUnderSteps);
            if (!s.newSupportUnderSteps) s.newStepThickness = MetersField(Tr("Толщина ступени"), Tr("Толщина плиты в метрах. При нуле используется 0,1 м."), s.newStepThickness, BrushSettings.DefaultStepThicknessMeters);
        }

        static float MetersField(string label, string tooltip, float meters, float placeholderMeters)
        {
            EditorGUI.BeginChangeCheck();
            float shown = meters > 0f ? meters : placeholderMeters;
            float v = EditorGUILayout.FloatField(new GUIContent(label + Tr(" (м)"), tooltip), shown);
            return EditorGUI.EndChangeCheck() ? Mathf.Max(0f, v) : meters;
        }

        /// <summary>
        /// The modules every new brush gets, as one dropdown however many there are: it shows Nothing, the one module, or how
        /// many, and opens a checklist of all of them, as Unity's layer mask fields do.
        /// </summary>
        static string ModuleName(System.Type type) => type == typeof(BrushTrigger) ? Tr("Триггер браша") : ObjectNames.NicifyVariableName(type.Name);

        static void ModulesDropdown(BrushSettings s, System.Collections.Generic.List<System.Type> moduleTypes)
        {
            var chosen = moduleTypes.FindAll(t => s.newModules.Contains(t.FullName));
            string summary = chosen.Count == 0 ? Tr("Без модулей") : chosen.Count == 1 ? ModuleName(chosen[0]) : BrushLanguage.Format("Модулей: {0}", chosen.Count);
            var rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent(Tr("Модули"), Tr("Компоненты с игровыми данными, которые будут добавлены к каждому новому брашу.")));
            if (!EditorGUI.DropdownButton(rect, new GUIContent(summary, string.Join(", ", chosen.ConvertAll(ModuleName))), FocusType.Keyboard)) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(Tr("Без модулей")), chosen.Count == 0, () => { s.newModules.Clear(); s.NotifyChanged(); });
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
                s.newGridSnap = BrushEditor.EnumPopup(new GUIContent(Tr("Привязка к сетке"), Tr("К сетке привязываются крайние вершины формы или начало рабочей системы координат объекта. Привязку можно отключить.")), s.newGridSnap);
            }
            var moduleTypes = BrushApi.ModuleTypes();
            if (moduleTypes.Count > 0) ModulesDropdown(s, moduleTypes);
            BrushCreateTool.NewBrushParameters(out float stepHeight, out float wall);
            switch (shape)
            {
                case BrushShape.Cylinder:
                case BrushShape.Cone:
                    s.newSides = Mathf.Max(3, EditorGUILayout.IntField(new GUIContent(Tr("Стороны"), Tr("Число сторон по окружности, от 3. Чем их больше, тем плавнее контур.")), s.newSides));
                    break;
                case BrushShape.Sphere:
                    s.newTessellation = EditorGUILayout.IntSlider(new GUIContent(Tr("Детализация"), Tr("Уровень разбиения сферы от 1 до 5. Чем больше значение, тем больше граней и плавнее поверхность.")), s.newTessellation, 1, 5);
                    break;
                case BrushShape.Stairs:
                    s.newStepHeight = MetersField(Tr("Высота ступени"), Tr("Высота ступени в метрах. Чем больше значение, тем меньше ступеней на заданной высоте. При нуле используется 0,25 м."), s.newStepHeight, stepHeight);
                    SupportUnderSteps(s);
                    break;
                case BrushShape.CurvedStairs:
                    s.newInnerRadius = MetersField(Tr("Внутренний радиус"), Tr("Радиус в метрах. Ноль соответствует шагу сетки."), s.newInnerRadius, s.GridMeters);
                    s.newStepHeight = MetersField(Tr("Высота ступени"), Tr("Высота ступени в метрах. Чем больше значение, тем меньше ступеней на заданной высоте. При нуле используется 0,25 м."), s.newStepHeight, stepHeight);
                    s.newCurveAngle = EditorGUILayout.FloatField(new GUIContent(Tr("Угол поворота"), Tr("Угол, который проходит лестница, в градусах. Чем больше угол, тем дальше идут ступени вокруг опоры.")), s.newCurveAngle);
                    s.newCounterClockwise = EditorGUILayout.Toggle(new GUIContent(Tr("Против часовой"), Tr("Включено — подъём против часовой стрелки. Выключено — по часовой стрелке. Направление оценивается сверху.")), s.newCounterClockwise);
                    SupportUnderSteps(s);
                    break;
                case BrushShape.SpiralStairs:
                    s.newInnerRadius = MetersField(Tr("Внутренний радиус"), Tr("Радиус в метрах. Ноль соответствует шагу сетки."), s.newInnerRadius, s.GridMeters);
                    s.newStepThickness = MetersField(Tr("Толщина ступени"), Tr("Толщина плиты в метрах. При нуле используется 0,1 м."), s.newStepThickness, BrushSettings.DefaultStepThicknessMeters);
                    s.newStepHeight = MetersField(Tr("Высота ступени"), Tr("Высота ступени в метрах. Чем больше значение, тем меньше ступеней на заданной высоте. При нуле используется 0,25 м."), s.newStepHeight, stepHeight);
                    s.newStepsPer360 = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent(Tr("Ступеней на оборот"), Tr("Число ступеней на 360°, минимум одна. Чем больше ступеней, тем меньше поворот между соседними.")), s.newStepsPer360));
                    s.newSlopedCeiling = EditorGUILayout.Toggle(new GUIContent(Tr("Наклонный потолок"), Tr("Заменяет ступенчатую нижнюю поверхность лестницы наклонной.")), s.newSlopedCeiling);
                    s.newSlopedFloor = EditorGUILayout.Toggle(new GUIContent(Tr("Наклонный пол"), Tr("Заменяет ступенчатую верхнюю поверхность лестницы наклонной.")), s.newSlopedFloor);
                    s.newCounterClockwise = EditorGUILayout.Toggle(new GUIContent(Tr("Против часовой"), Tr("Включено — подъём против часовой стрелки. Выключено — по часовой стрелке. Направление оценивается сверху.")), s.newCounterClockwise);
                    break;
                case BrushShape.Arch:
                    s.newWallThickness = MetersField(Tr("Толщина"), Tr("Толщина арки в метрах. Ноль соответствует шагу сетки."), s.newWallThickness, wall);
                    s.newArchAngle = EditorGUILayout.Slider(new GUIContent(Tr("Угол"), Tr("Угол арки в градусах. 180° даёт полную арку. Чем меньше угол, тем меньшая часть вершины остаётся.")), s.newArchAngle, 1f, 180f);
                    s.newSides = Mathf.Max(3, EditorGUILayout.IntField(new GUIContent(Tr("Сегменты"), Tr("Число сегментов арки, от 3. Чем их больше, тем плавнее дуга.")), s.newSides));
                    break;
                case BrushShape.Door:
                    s.newDoorWidth = MetersField(Tr("Ширина"), Tr("Ширина дверного проёма в метрах. При нуле используется 1 м."), s.newDoorWidth, BrushSettings.DefaultDoorWidthMeters);
                    s.newDoorHeight = MetersField(Tr("Высота"), Tr("Высота дверного проёма в метрах. При нуле используется 2,2 м."), s.newDoorHeight, BrushSettings.DefaultDoorHeightMeters);
                    break;
                case BrushShape.Window:
                    s.newWindowWidth = MetersField(Tr("Ширина"), Tr("Ширина оконного проёма в метрах. При нуле используется 1,2 м."), s.newWindowWidth, BrushSettings.DefaultWindowWidthMeters);
                    s.newWindowHeight = MetersField(Tr("Высота"), Tr("Высота оконного проёма в метрах. При нуле используется 1,2 м."), s.newWindowHeight, BrushSettings.DefaultWindowHeightMeters);
                    s.newWindowSill = MetersField(Tr("Подоконник"), Tr("Высота нижней кромки окна над полом в метрах. При нуле используется 0,9 м."), s.newWindowSill, BrushSettings.DefaultWindowSillMeters);
                    break;
            }
            if (shape == BrushShape.Box || shape == BrushShape.Cylinder)
            {
                s.newHollow = EditorGUILayout.Toggle(new GUIContent(Tr("Полый браш"), Tr("Включено — создаются только стены заданной толщины. Выключено — создаётся сплошной браш.")), s.newHollow);
                using (new EditorGUI.DisabledScope(!s.newHollow))
                    s.newWallThickness = MetersField(Tr("Толщина стены"), Tr("Толщина стены в метрах. Ноль соответствует шагу сетки."), s.newWallThickness, wall);
            }
            if (EditorGUI.EndChangeCheck()) { s.NotifyChanged(); SceneView.RepaintAll(); }
            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUILayout.Space(2);
        }

        [Shortcut("Brushes/Grid smaller", typeof(SceneView), KeyCode.LeftBracket, displayName = "CSG Brush/Decrease Grid Step")]
        static void GridSmaller() { BrushSettings.instance.SetGridIndex(BrushSettings.instance.gridIndex - 1); }

        [Shortcut("Brushes/Grid larger", typeof(SceneView), KeyCode.RightBracket, displayName = "CSG Brush/Increase Grid Step")]
        static void GridLarger() { BrushSettings.instance.SetGridIndex(BrushSettings.instance.gridIndex + 1); }
    }
}
