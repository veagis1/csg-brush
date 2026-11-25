using static CsgBrush.Editor.BrushLanguage;
using CsgBrush.Colliders;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    [CustomEditor(typeof(Brush))]
    [CanEditMultipleObjects]
    public sealed class BrushEditor : UnityEditor.Editor
    {
        SerializedProperty shapeProp, operationProp, collisionProp, physicsMaterialProp, provideContactsProp, sizeProp, hollowProp, wallProp, sidesProp, tessProp, stepHeightProp, materialProp;
        SerializedProperty innerRadiusProp, stepWidthProp, stepThicknessProp, supportUnderStepsProp, curveAngleProp, numStepsProp, stepsPer360Prop, addToFirstStepProp, ccwProp, slopedFloorProp, slopedCeilingProp;

        void OnEnable()
        {
            shapeProp = serializedObject.FindProperty(nameof(Brush.shape));
            operationProp = serializedObject.FindProperty(nameof(Brush.operation));
            collisionProp = serializedObject.FindProperty(nameof(Brush.collision));
            physicsMaterialProp = serializedObject.FindProperty(nameof(Brush.physicsMaterial));
            provideContactsProp = serializedObject.FindProperty(nameof(Brush.provideContacts));
            sizeProp = serializedObject.FindProperty(nameof(Brush.size));
            hollowProp = serializedObject.FindProperty(nameof(Brush.hollow));
            wallProp = serializedObject.FindProperty(nameof(Brush.wallThickness));
            sidesProp = serializedObject.FindProperty(nameof(Brush.sides));
            tessProp = serializedObject.FindProperty(nameof(Brush.tessellation));
            stepHeightProp = serializedObject.FindProperty(nameof(Brush.stepHeight));
            materialProp = serializedObject.FindProperty(nameof(Brush.material));
            innerRadiusProp = serializedObject.FindProperty(nameof(Brush.innerRadius));
            stepWidthProp = serializedObject.FindProperty(nameof(Brush.stepWidth));
            stepThicknessProp = serializedObject.FindProperty(nameof(Brush.stepThickness));
            supportUnderStepsProp = serializedObject.FindProperty(nameof(Brush.supportUnderSteps));
            curveAngleProp = serializedObject.FindProperty(nameof(Brush.curveAngle));
            numStepsProp = serializedObject.FindProperty(nameof(Brush.numSteps));
            stepsPer360Prop = serializedObject.FindProperty(nameof(Brush.stepsPer360));
            addToFirstStepProp = serializedObject.FindProperty(nameof(Brush.addToFirstStep));
            ccwProp = serializedObject.FindProperty(nameof(Brush.counterClockwise));
            slopedFloorProp = serializedObject.FindProperty(nameof(Brush.slopedFloor));
            slopedCeilingProp = serializedObject.FindProperty(nameof(Brush.slopedCeiling));
        }

        static bool s_CollisionOpen = true, s_RenderingOpen = true;

        // the Shape popup: Custom first, a separator, then the parametric shapes by name (null marks the separator)
        static BrushShape?[] s_ShapeOrder;
        static GUIContent[] s_ShapeNames;
        static bool shapeNamesEnglish;

        static void BuildShapeOrder()
        {
            if (s_ShapeOrder != null && shapeNamesEnglish == BrushLanguage.English) return;
            shapeNamesEnglish = BrushLanguage.English;
            var shapes = new System.Collections.Generic.List<BrushShape>();
            foreach (BrushShape shape in System.Enum.GetValues(typeof(BrushShape))) if (shape != BrushShape.Custom) shapes.Add(shape);
            shapes.Sort((a, b) => string.CompareOrdinal(ShapeName(a), ShapeName(b)));
            var order = new System.Collections.Generic.List<BrushShape?> { BrushShape.Custom, null };
            foreach (var shape in shapes) order.Add(shape);
            s_ShapeOrder = order.ToArray();
            s_ShapeNames = System.Array.ConvertAll(s_ShapeOrder, shape => shape.HasValue ? EnumOption(shape.Value) : GUIContent.none);
        }

        internal static string ShapeName(BrushShape shape) => EnumOption(shape).text;

        internal static GUIContent EnumOption<T>(T option) where T : struct, System.Enum
        {
            var name = option.ToString();
            var field = typeof(T).GetField(name);
            var label = field == null ? null : (InspectorNameAttribute)System.Attribute.GetCustomAttribute(field, typeof(InspectorNameAttribute));
            var tooltip = field == null ? null : (TooltipAttribute)System.Attribute.GetCustomAttribute(field, typeof(TooltipAttribute));
            return new GUIContent(Tr(label?.displayName ?? ObjectNames.NicifyVariableName(name)), Tr(tooltip?.tooltip));
        }

        internal static T EnumPopup<T>(GUIContent label, T value) where T : struct, System.Enum
        {
            var values = (T[])System.Enum.GetValues(typeof(T));
            int selected = EditorGUILayout.Popup(label, System.Array.IndexOf(values, value), System.Array.ConvertAll(values, EnumOption));
            return selected >= 0 ? values[selected] : value;
        }

        internal static void DrawProperty(SerializedProperty property, GUIContent label, bool children = false)
        {
            if (!BrushLanguage.English || property.propertyType != SerializedPropertyType.Enum)
            {
                EditorGUILayout.PropertyField(property, label, children);
                return;
            }

            var rect = EditorGUILayout.GetControlRect();
            using var scope = new EditorGUI.PropertyScope(rect, label, property);
            EditorGUI.BeginChangeCheck();
            var choices = System.Array.ConvertAll(property.enumDisplayNames, name => new GUIContent(Tr(name)));
            int selected = EditorGUI.Popup(rect, scope.content, property.enumValueIndex, choices);
            if (EditorGUI.EndChangeCheck() && selected >= 0) property.enumValueIndex = selected;
        }

        internal static GUIContent PropertyLabel(System.Type type, SerializedProperty property)
        {
            var field = type.GetField(property.name);
            var label = field == null ? null : (InspectorNameAttribute)System.Attribute.GetCustomAttribute(field, typeof(InspectorNameAttribute));
            return new GUIContent(Tr(label?.displayName ?? property.displayName), Tr(property.tooltip));
        }

        internal static void DrawNamedProperties(SerializedObject data)
        {
            data.UpdateIfRequiredOrScript();
            var type = data.targetObject.GetType();
            var property = data.GetIterator();
            for (bool first = true; property.NextVisible(first); first = false)
            {
                var field = type.GetField(property.name);
                var category = field == null ? null : (System.ComponentModel.CategoryAttribute)
                    System.Attribute.GetCustomAttribute(field, typeof(System.ComponentModel.CategoryAttribute));
                if (category != null)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(Tr(category.Category), EditorStyles.boldLabel);
                }
                bool script = property.propertyPath == "m_Script";
                using (new EditorGUI.DisabledScope(script))
                    DrawProperty(property, script ? new GUIContent(Tr("Скрипт")) : PropertyLabel(type, property), true);
            }
            data.ApplyModifiedProperties();
        }

        static string BuildSourceLabel(string source)
        {
            if (source == "none (stamp)") return Tr("Нет группы (штамп префаба)");
            if (source == "scene (unsaved)") return Tr("Несохранённая сцена");
            const string scenePrefix = "scene ";
            return source.StartsWith(scenePrefix, System.StringComparison.Ordinal) ? Tr("Сцена ") + source.Substring(scenePrefix.Length) : source;
        }

        void ShapeField()
        {
            BuildShapeOrder();
            var rect = EditorGUILayout.GetControlRect();
            var label = EditorGUI.BeginProperty(rect, new GUIContent(Tr("Форма"), Tr("Определяет геометрию браша. Произвольная форма редактируется через вершины, рёбра и грани.")), shapeProp);
            EditorGUI.showMixedValue = shapeProp.hasMultipleDifferentValues;
            int current = System.Array.IndexOf(s_ShapeOrder, (BrushShape?)(BrushShape)shapeProp.intValue);
            int picked = EditorGUI.Popup(rect, label, current, s_ShapeNames);
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();
            if (picked == current || picked < 0 || !s_ShapeOrder[picked].HasValue) return;
            foreach (var item in targets) BrushApi.SetShape((Brush)item, s_ShapeOrder[picked].Value);
            serializedObject.Update();
        }

        /// <summary>The pivot and how it is measured; setting it moves the transform, so the shape stays put.</summary>
        void DrawPivot()
        {
            Vector3? first = null; bool mixed = false, mixedMode = false; var mode = ((Brush)target).pivotMode;
            foreach (var t in targets)
            {
                var b = (Brush)t;
                var p = BrushApi.PivotOf(b);
                if (!p.HasValue) return; // a shape that keeps its own
                if (b.pivotMode != mode) mixedMode = true;
                if (first == null) first = p; else if ((first.Value - p.Value).sqrMagnitude > 1e-10f) mixed = true;
            }
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = mixedMode;
            var pickedMode = EnumPopup(new GUIContent(Tr("Режим WCS"), Tr("Положение начала рабочей системы координат объекта задаётся в долях габарита или метрах от левого нижнего заднего угла. За границами габарита доля может выходить за диапазон от 0 до 1.")), mode);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck()) { foreach (var t in targets) BrushApi.SetPivotMode((Brush)t, pickedMode); return; }
            if (mixedMode) return;
            bool relative = mode == PivotMode.Normalized;
            var shown = first.Value;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent(relative ? Tr("Начало WCS") : Tr("Начало WCS (м)"), Tr("Положение начала рабочей системы координат объекта относительно левого нижнего заднего угла. При Y = 0 начало лежит на нижней грани, при Z = 0 на задней.")));
            float w = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 14f;
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = mixed;
            var picked = new Vector3(EditorGUILayout.FloatField("X", shown.x), EditorGUILayout.FloatField("Y", shown.y), EditorGUILayout.FloatField("Z", shown.z));
            EditorGUI.showMixedValue = false;
            EditorGUIUtility.labelWidth = w;
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
                foreach (var t in targets)
                {
                    var b = (Brush)t; var own = BrushApi.PivotOf(b).Value;
                    BrushApi.SetPivot(b, Changed(own, shown, picked));
                }
        }

        /// <summary>Only the axes the field changed, so a mixed selection keeps its other axes.</summary>
        static Vector3 Changed(Vector3 own, Vector3 shown, Vector3 picked) => new Vector3(picked.x != shown.x ? picked.x : own.x, picked.y != shown.y ? picked.y : own.y, picked.z != shown.z ? picked.z : own.z);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            ShapeField();
            DrawProperty(operationProp, new GUIContent(Tr("Операция"), Tr("«Добавить» заполняет объём. «Вычесть» вырезает его из брашей выше в иерархии.")));
            if (targets.Length == 1 && target is Brush owner)
            {
                string builtBy = BrushCsg.BuiltBy(owner, out var reference);
                var label = new GUIContent(Tr("Группа брашей"), Tr("Родительская группа, которая строит меш и коллайдеры."));
                if (reference != null) using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(label, reference, reference.GetType(), true);
                else EditorGUILayout.LabelField(label, new GUIContent(BuildSourceLabel(builtBy)), EditorStyles.wordWrappedMiniLabel);
            }

            foreach (var t in targets) if (t is Brush pb && !string.IsNullOrEmpty(pb.problem)) { EditorGUILayout.HelpBox(pb.problem + Tr(" Отмените последнее изменение или восстановите исходную форму."), MessageType.Error); break; }
            EditorGUILayout.Space(4);
            var shape = (BrushShape)shapeProp.enumValueIndex;
            if (shape == BrushShape.Custom)
            {
                var brush = (Brush)target;
                var poly = brush.polyhedron;
                int parts = 0;
                if (poly != null && poly.IsValid) { var pieces = new System.Collections.Generic.List<ConvexPolytope>(); ConvexDecomposition.Decompose(poly, pieces); parts = pieces.Count; }
                string info = poly != null && poly.IsValid
                    ? BrushLanguage.Format("Вершин {0} · граней {1} · {2}", poly.vertices.Length, poly.faces.Length,
                        parts == 1 ? Tr("выпуклая форма") : BrushLanguage.Format("выпуклых частей {0}", parts))
                    : Tr("Форма отсутствует");
                EditorGUILayout.LabelField(Tr("Произвольная форма"), info);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(BrushEditContext.IsActive ? Tr("Завершить правку") : Tr("Править форму"))) BrushEditContext.Toggle();
                if (GUILayout.Button(new GUIContent(Tr("Восстановить форму"), Tr("Вернуть исходную форму — ") + ShapeName(brush.customFrom)))) foreach (var t in targets) BrushApi.ResetShape((Brush)t);
                EditorGUILayout.EndHorizontal();
            }
            else if (shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs)
            {
                var sz = sizeProp.vector3Value;
                EditorGUILayout.LabelField(Tr("Размер"), BrushLanguage.Format("{0} × {1} × {2} (по параметрам)", BrushSettings.FormatMeters(sz.x), BrushSettings.FormatMeters(sz.y), BrushSettings.FormatMeters(sz.z)));
            }
            else DrawSize(shape);
            DrawPivot();
            if (targets.Length == 1 && target is Brush single && BrushApi.CanPickPivot(single) && !EditorUtility.IsPersistent(single))
                BrushPivotTool.DrawButtons(single);
            bool snaps = true; foreach (var t in targets) if (((Brush)t).IsOpening || ((Brush)t).IsGenerated) snaps = false; // placed by their wall or plan
            if (snaps) DrawProperty(serializedObject.FindProperty(nameof(Brush.gridSnap)), new GUIContent(Tr("Привязка к сетке"), Tr("При перемещении и изменении размеров привязывает к сетке крайние вершины или начало рабочей системы координат объекта. Поворот привязку не вызывает.")));

            if (shape != BrushShape.Custom && BrushApi.CanConvertToCustom(shape))
            {
                if (GUILayout.Button(new GUIContent(BrushEditContext.IsActive ? Tr("Завершить правку") : Tr("Править форму"), Tr("Перемещение, поворот и масштабирование действуют на вершины, рёбра или грани в окне сцены. При первой правке браш получает произвольную форму."))))
                    BrushEditContext.Toggle();
            }
            if (shape == BrushShape.Box || shape == BrushShape.Cylinder)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(hollowProp, new GUIContent(Tr("Полый"), Tr("Оставить только стенки. При отключении весь объём становится сплошным.")), GUILayout.Width(EditorGUIUtility.labelWidth + 20f));
                using (new EditorGUI.DisabledScope(!hollowProp.boolValue))
                    DrawMetersField(wallProp, Tr("Толщина стенок"));
                EditorGUILayout.EndHorizontal();
            }
            switch (shape)
            {
                case BrushShape.Cylinder:
                case BrushShape.Cone:
                    EditorGUILayout.PropertyField(sidesProp, new GUIContent(Tr("Грани"), Tr(sidesProp.tooltip)));
                    break;
                case BrushShape.Sphere:
                    EditorGUILayout.PropertyField(tessProp, new GUIContent(Tr("Детализация"), Tr(tessProp.tooltip)));
                    break;
                case BrushShape.Arch:
                    DrawMetersField(wallProp, Tr("Толщина"));
                    EditorGUILayout.Slider(curveAngleProp, 1f, 180f, new GUIContent(Tr("Угол"), Tr("Размах арки в градусах. 180° даёт полную арку. Меньшее значение оставляет верхний сегмент.")));
                    EditorGUILayout.PropertyField(sidesProp, new GUIContent(Tr("Сегменты"), Tr(sidesProp.tooltip)));
                    break;
                case BrushShape.CurvedStairs:
                    DrawMetersField(innerRadiusProp, Tr("Внутренний радиус"));
                    DrawMetersField(stepWidthProp, Tr("Ширина ступени"));
                    DrawStepsByHeight();
                    EditorGUILayout.PropertyField(curveAngleProp, new GUIContent(Tr("Угол поворота"), Tr(curveAngleProp.tooltip)));
                    DrawMetersField(addToFirstStepProp, Tr("Поправка первой ступени"), true);
                    EditorGUILayout.PropertyField(ccwProp, new GUIContent(Tr("Против часовой стрелки"), Tr(ccwProp.tooltip)));
                    DrawSupportUnderSteps();
                    break;
                case BrushShape.SpiralStairs:
                    DrawMetersField(innerRadiusProp, Tr("Внутренний радиус"));
                    DrawMetersField(stepWidthProp, Tr("Ширина ступени"));
                    DrawStepsByHeight();
                    DrawMetersField(stepThicknessProp, Tr("Толщина ступени"));
                    EditorGUILayout.PropertyField(stepsPer360Prop, new GUIContent(Tr("Ступеней на 360°"), Tr(stepsPer360Prop.tooltip)));
                    DrawMetersField(addToFirstStepProp, Tr("Поправка первой ступени"), true);
                    EditorGUILayout.PropertyField(slopedCeilingProp, new GUIContent(Tr("Наклонный низ"), Tr(slopedCeilingProp.tooltip)));
                    EditorGUILayout.PropertyField(slopedFloorProp, new GUIContent(Tr("Наклонный верх"), Tr(slopedFloorProp.tooltip)));
                    EditorGUILayout.PropertyField(ccwProp, new GUIContent(Tr("Против часовой стрелки"), Tr(ccwProp.tooltip)));
                    break;
                case BrushShape.Stairs:
                    DrawMetersField(stepHeightProp, Tr("Высота ступени"));
                    var sz = sizeProp.vector3Value;
                    int steps = Mathf.Max(1, Mathf.RoundToInt(sz.y / Mathf.Max(BrushPolyhedron.MinStepHeight, stepHeightProp.floatValue)));
                    EditorGUILayout.LabelField(" ", BrushLanguage.Format("Ступеней {0} · длина {1} ({2} на ступень)", steps, BrushSettings.FormatMeters(sz.z), BrushSettings.FormatMeters(sz.z / steps)), EditorStyles.miniLabel);
                    DrawSupportUnderSteps();
                    break;
            }

            EditorGUILayout.Space(4);
            s_CollisionOpen = EditorGUILayout.BeginFoldoutHeaderGroup(s_CollisionOpen, Tr("Столкновения"));
            if (s_CollisionOpen)
            {
                EditorGUI.indentLevel++;
                DrawProperty(collisionProp, new GUIContent(Tr("Столкновения"), Tr("Режим коллайдера. Твёрдое тело останавливает движение, триггер сообщает о пересечении, режим «Нет» отключает коллайдеры.")));
                if (target is Brush cb && cb.EffectiveCollision() != cb.collision)
                    EditorGUILayout.LabelField(" ", BrushLanguage.Format("Режим задан модулем ({0})", EnumOption(cb.EffectiveCollision()).text), EditorStyles.miniLabel);
                if (target is Brush pb2 && pb2.EffectiveCollision() != ColliderKind.None)
                {
                    EditorGUILayout.PropertyField(physicsMaterialProp, new GUIContent(Tr("Физический материал"), Tr("Материал всех частей коллайдера, включая триггеры. Задаёт трение и упругость. Может использоваться для выбора звука.")));
                    EditorGUILayout.PropertyField(provideContactsProp, new GUIContent(Tr("Передавать контакты"), Tr("Части браша передают данные о контактах в обработчики OnCollision.")));
                    EditorGUILayout.LabelField(" ", Tr("Части наследуют тег, слой и флаги статичности объекта."), EditorStyles.miniLabel);
                }
                if (target is Brush mb)
                {
                    var modules = mb.Modules();
                    if (modules.Length > 0)
                    {
                        var names = new System.Text.StringBuilder();
                        foreach (var m in modules) { if (names.Length > 0) names.Append(", "); names.Append(m is BrushTrigger ? Tr("Триггер браша") : ObjectNames.NicifyVariableName(m.GetType().Name)); if (m.gameObject != mb.gameObject) names.Append(Tr(" (источник: ") + m.gameObject.name + ")"); }
                        EditorGUILayout.LabelField(Tr("Модули"), names.ToString(), EditorStyles.miniLabel);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            s_RenderingOpen = EditorGUILayout.BeginFoldoutHeaderGroup(s_RenderingOpen, Tr("Рендеринг"));
            if (s_RenderingOpen)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(materialProp, new GUIContent(Tr("Материал"), Tr("Материал всех граней. Его также можно перетащить на браш в окне сцены.")));
                if (!operationProp.hasMultipleDifferentValues && operationProp.enumValueIndex == (int)BrushOperation.Subtract)
                    DrawProperty(serializedObject.FindProperty(nameof(Brush.cutFaces)), new GUIContent(Tr("Материал срезов"), Tr("Срезы получают материал этого браша или вырезаемого объёма.")));
                EditorGUILayout.LabelField(" ", Tr("Грани используют слой и флаги статичности этого объекта. Остальные настройки задаёт группа брашей."), EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed)
                foreach (var t in targets) BrushSync.Ensure((Brush)t);

            foreach (var t in targets)
            {
                var brush = (Brush)t;
                var s = brush.transform.localScale;
                if (s != Vector3.one)
                {
                    EditorGUILayout.HelpBox(Tr("Масштаб объекта: ") + s + Tr(". При включённой привязке масштаб применяется к габаритам после отпускания инструмента. В остальных случаях нажмите кнопку ниже."), MessageType.Info);
                    if (GUILayout.Button(Tr("Применить масштаб к размеру"))) BrushApi.ApplyScale(brush);
                    break;
                }
            }
        }

        void DrawSize(BrushShape shape)
        {
            var size = sizeProp.vector3Value;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent(Tr("Размер (м)"), shape == BrushShape.Wedge ? Tr("X — ширина, Y — высота, Z — длина. Пандус поднимается по оси Z.") : shape == BrushShape.Stairs ? Tr("X — ширина, Y — общая высота, Z — длина. Лестница поднимается по оси Z.") : Tr("X — ширина, Y — высота, Z — глубина.")));
            float w = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 14f;
            EditorGUI.BeginChangeCheck();
            float x = EditorGUILayout.FloatField("X", size.x);
            float y = EditorGUILayout.FloatField("Y", size.y);
            float z = EditorGUILayout.FloatField("Z", size.z);
            if (EditorGUI.EndChangeCheck())
                sizeProp.vector3Value = new Vector3(Mathf.Max(0f, x), Mathf.Max(0f, y), Mathf.Max(0f, z));
            EditorGUIUtility.labelWidth = w;
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Curved and spiral stairs: the step height and the height they climb. The number of steps follows from the two;
        /// a new step height keeps the height, a new height keeps the step height.
        /// </summary>
        void DrawSupportUnderSteps()
        {
            EditorGUILayout.PropertyField(supportUnderStepsProp, new GUIContent(Tr("Опора под ступенями"), Tr("Заполнить объём под ступенями до пола. При отключении остаются отдельные плиты ступеней.")));
            if (!supportUnderStepsProp.boolValue) DrawMetersField(stepThicknessProp, Tr("Толщина ступени"));
        }

        void DrawStepsByHeight()
        {
            float stepHeight = Mathf.Max(BrushPolyhedron.MinStepHeight, stepHeightProp.floatValue);
            float height = numStepsProp.intValue * stepHeight;
            DrawMetersField(stepHeightProp, Tr("Высота ступени"));
            float newStep = Mathf.Max(BrushPolyhedron.MinStepHeight, stepHeightProp.floatValue);
            if (!Mathf.Approximately(newStep, stepHeight)) numStepsProp.intValue = BrushPolyhedron.StepCount(height, newStep);
            EditorGUI.showMixedValue = numStepsProp.hasMultipleDifferentValues || stepHeightProp.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            float shown = EditorGUILayout.FloatField(new GUIContent(Tr("Высота (м)"), Tr("Высота лестницы в метрах. Число ступеней округляется по отношению общей высоты к высоте ступени.")), numStepsProp.intValue * newStep);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck()) numStepsProp.intValue = BrushPolyhedron.StepCount(shown, newStep);
        }

        static void DrawMetersField(SerializedProperty prop, string label, bool allowNegative = false)
        {
            EditorGUI.BeginChangeCheck();
            float v = EditorGUILayout.FloatField(new GUIContent(label + Tr(" (м)"), Tr(prop.tooltip)), prop.floatValue);
            if (EditorGUI.EndChangeCheck())
                prop.floatValue = allowNegative ? v : Mathf.Max(0f, v);
        }
    }
}
