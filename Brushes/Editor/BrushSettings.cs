using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>What dragging a shape handle does to the other selected brushes.</summary>
    public enum ShapeEditSelection
    {
        /// <summary>Only the brush whose handle is dragged changes.</summary>
        [InspectorName("Один браш")] Single = 0,
        /// <summary>Selected brushes of the same shape change by the same amount.</summary>
        [InspectorName("Относительно")] Offset = 1,
        /// <summary>Selected brushes of the same shape take the dragged brush's new value.</summary>
        [InspectorName("Одинаковое значение")] Match = 2,
    }

    /// <summary>
    /// Project-wide world preset: units per metre, grid and defaults. Geometry is always stored in metres; the preset
    /// only changes how sizes are displayed and snapped.
    /// </summary>
    [FilePath("ProjectSettings/BrushSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class BrushSettings : ScriptableSingleton<BrushSettings>
    {
        public enum Preset { Quake = 0, Source = 1, Unreal = 2, Unity = 3, Custom = 4 } // stored by value: Unity was Metric

        /// <summary>The presets in the order the settings list them.</summary>
        public static readonly Preset[] PresetOrder = { Preset.Unity, Preset.Quake, Preset.Source, Preset.Unreal, Preset.Custom };

        public Preset preset = Preset.Unity;
        public float unitsPerMeter = 1f;
        public string unitLabel = "m";
        public float[] gridSizes = { 0.125f, 0.25f, 0.5f, 1f, 2f, 4f };
        public int gridIndex = 2;
        public Vector3 defaultBoxSize = new Vector3(2f, 2f, 2f);
        public float rotationSnapDegrees = 15f;
        [Header("Новые браши")]
        public BrushOperation newOperation = BrushOperation.Add;
        [Tooltip("Полные имена типов модулей, которые получит каждый новый браш.")] public List<string> newModules = new List<string>();
        [Min(3)] public int newSides = 16;
        [Range(1, 5)] public int newTessellation = 2;
        [Tooltip("Высота ступени в единицах проекта. Ноль соответствует 0,25 м.")] public float newStepHeight = 0f;
        public bool newHollow = false;
        [Tooltip("Значение в единицах проекта. Ноль соответствует шагу сетки.")] public float newInnerRadius = 0f;
        [Tooltip("Толщина ступени в единицах проекта. Ноль соответствует 0,1 м.")] public float newStepThickness = 0f;
        public float newCurveAngle = 90f;
        [Min(1)] public int newStepsPer360 = 16;
        public bool newCounterClockwise = false;
        public bool newSlopedFloor = false;
        public bool newSlopedCeiling = false;
        [Tooltip("Добавляет сплошную опору до пола под прямой или изогнутой лестницей.")] public bool newSupportUnderSteps = true;
        [Tooltip("Привязка нового браша к сетке за крайние вершины формы или за точку опоры. Привязку можно отключить.")] public GridSnap newGridSnap = GridSnap.Shape;
        [Tooltip("Значение в единицах проекта. Ноль соответствует шагу сетки.")] public float newWallThickness = 0f;
        [Range(1f, 180f)] public float newArchAngle = 180f;
        [Tooltip("Значение в единицах проекта. Ноль соответствует 1 м.")] public float newDoorWidth = 0f;
        [Tooltip("Значение в единицах проекта. Ноль соответствует 2,2 м.")] public float newDoorHeight = 0f;
        [Tooltip("Значение в единицах проекта. Ноль соответствует 1,2 м.")] public float newWindowWidth = 0f;
        [Tooltip("Значение в единицах проекта. Ноль соответствует 1,2 м.")] public float newWindowHeight = 0f;
        [Tooltip("Высота над полом в единицах проекта. Ноль соответствует 0,9 м.")] public float newWindowSill = 0f;
        [Header("Выдавливание")]
        [Tooltip("Значение в единицах проекта. Ноль соответствует шагу сетки.")] public float extrudeDistance = 0f;
        public bool extrudeIndividual = false;
        [Tooltip("Флаги статичности новых брашей и планов стен. Сетки и коллайдеры браша наследуют его флаги.")]
        public StaticEditorFlags defaultModelStaticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;
        [Tooltip("Создаёт UV для карт освещения после завершения правки мешей брашей. При отключении развёртку нужно создать вручную перед запеканием.")]
        public bool autoLightmapUVs = true;
        [Tooltip("Материал граней без собственного материала. Если поле пусто, при первом использовании в Assets/CSG Brush создаётся текстура с сеткой в метрах.")]
        public Material defaultMaterial;
        [Header("Редактор")]
        public bool showGenerated = false;
        [Tooltip("Показывает вычитающие браши полупрозрачным красным объёмом. Так их можно выбрать, даже если они полностью вырезали геометрию.")]
        public bool showCuts = false;
        internal const string DimensionsTooltip = "Размеры в метрах в окне сцены. Щёлкните по размеру выделенного браша, введите число и нажмите Enter. Esc отменяет ввод.";
        [Tooltip(DimensionsTooltip)]
        public bool showDimensions = true;
        [Tooltip("Позволяет менять форму выделенного браша перетаскиванием его сторон.")]
        public bool shapeHandles = true;
        [Tooltip("Определяет, как правка формы затронет остальные выделенные браши.")]
        public ShapeEditSelection shapeEditSelection = ShapeEditSelection.Single;
        [Tooltip("Привязывает положение, размеры и поворот брашей к мировой сетке после каждой правки, независимо от родительских объектов.")]
        public bool snapToGrid = true;
        [Tooltip("Максимальное отклонение прямых сегментов стены или сплайна от кривой в метрах. Меньше — плавнее контур и больше сегментов. Минимум 0,01 м.")]
        public float curveTolerance = 0.1f;

        public static event Action Changed;

        /// <summary>The step height new stairs get unless one is set: a comfortable step for a 1.5 to 2 m character, and under Unity's default CharacterController step offset (0.3 m).</summary>
        public const float DefaultStepHeightMeters = 0.25f;

        /// <summary>The tread thickness spiral stairs get unless one is set: thin, well under the default step height.</summary>
        public const float DefaultStepThicknessMeters = 0.1f;

        /// <summary>A door's opening unless set: room for a 0.5 m wide, 1.5 to 2 m tall character.</summary>
        public const float DefaultDoorWidthMeters = 1f, DefaultDoorHeightMeters = 2.2f;
        /// <summary>A window unless set: 1.2 m square, its bottom at 0.9 m (a common sill height).</summary>
        public const float DefaultWindowWidthMeters = 1.2f, DefaultWindowHeightMeters = 1.2f, DefaultWindowSillMeters = 0.9f;
        /// <summary>How far a door or window reaches past each face of a wall, so it cuts cleanly through.</summary>
        public const float OpeningMarginMeters = 0.05f;
        /// <summary>The depth of a door or window not placed on a wall.</summary>
        public const float DefaultOpeningDepthMeters = 0.5f;

        /// <summary>Width and height of the next door or window, in metres.</summary>
        public Vector2 NewOpeningSize(BrushShape shape) => shape == BrushShape.Window
            ? new Vector2(newWindowWidth > 0f ? ToMeters(newWindowWidth) : DefaultWindowWidthMeters, newWindowHeight > 0f ? ToMeters(newWindowHeight) : DefaultWindowHeightMeters)
            : new Vector2(newDoorWidth > 0f ? ToMeters(newDoorWidth) : DefaultDoorWidthMeters, newDoorHeight > 0f ? ToMeters(newDoorHeight) : DefaultDoorHeightMeters);

        /// <summary>Height of the next door's or window's bottom above the floor, in metres.</summary>
        public float NewOpeningSill(BrushShape shape) => shape == BrushShape.Window ? (newWindowSill > 0f ? ToMeters(newWindowSill) : DefaultWindowSillMeters) : 0f;


        /// <summary>The step height of the next stairs, in metres.</summary>
        public float NewStepHeightMeters => Mathf.Max(BrushPolyhedron.MinStepHeight, newStepHeight > 0f ? ToMeters(newStepHeight) : DefaultStepHeightMeters);

        public float GridUnits => gridSizes != null && gridSizes.Length > 0 ? gridSizes[Mathf.Clamp(gridIndex, 0, gridSizes.Length - 1)] : 1f;
        public float GridMeters => ToMeters(GridUnits);

        public float ToUnits(float meters) => meters * unitsPerMeter;
        public float ToMeters(float units) => units / unitsPerMeter;
        public Vector3 ToUnits(Vector3 meters) => meters * unitsPerMeter;
        public Vector3 ToMeters(Vector3 units) => units / unitsPerMeter;

        public string FormatUnits(float meters) => ToUnits(meters).ToString("0.##") + " " + unitLabel;

        public void ApplyPreset(Preset p)
        {
            preset = p;
            switch (p)
            {
                case Preset.Quake:
                    unitsPerMeter = 32f; unitLabel = "u";
                    gridSizes = new float[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 }; gridIndex = 4;
                    defaultBoxSize = new Vector3(64f, 64f, 64f); rotationSnapDegrees = 15f;
                    break;
                case Preset.Source:
                    unitsPerMeter = 39.37f; unitLabel = "u";
                    gridSizes = new float[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 }; gridIndex = 4;
                    defaultBoxSize = new Vector3(64f, 64f, 64f); rotationSnapDegrees = 15f;
                    break;
                case Preset.Unreal:
                    unitsPerMeter = 100f; unitLabel = "cm";
                    gridSizes = new float[] { 1, 5, 10, 25, 50, 100, 200, 500 }; gridIndex = 3;
                    defaultBoxSize = new Vector3(200f, 200f, 200f); rotationSnapDegrees = 15f;
                    break;
                case Preset.Unity:
                    unitsPerMeter = 1f; unitLabel = "m";
                    gridSizes = new float[] { 0.125f, 0.25f, 0.5f, 1f, 2f, 4f }; gridIndex = 2;
                    defaultBoxSize = new Vector3(2f, 2f, 2f); rotationSnapDegrees = 15f;
                    break;
                case Preset.Custom:
                    break;
            }
            NotifyChanged();
        }

        public void SetGridIndex(int index)
        {
            gridIndex = Mathf.Clamp(index, 0, gridSizes.Length - 1);
            NotifyChanged();
        }

        public void NotifyChanged()
        {
            Save(true);
            Changed?.Invoke();
        }
    }

    static class BrushSettingsProvider
    {
        // Keep this order in sync with the visible StaticEditorFlags in Unity 6.6.
        [Flags]
        enum StaticFlagOption
        {
            [InspectorName("Ничего")] None = 0,
            [InspectorName("Участие в глобальном освещении")] ContributeGI = (int)StaticEditorFlags.ContributeGI,
            [InspectorName("Перекрывает другие объекты")] OccluderStatic = (int)StaticEditorFlags.OccluderStatic,
            [InspectorName("Скрывается за преградами")] OccludeeStatic = (int)StaticEditorFlags.OccludeeStatic,
            [InspectorName("Статический батчинг")] BatchingStatic = (int)StaticEditorFlags.BatchingStatic,
            [InspectorName("Участие в запекании отражений")] ReflectionProbeStatic = (int)StaticEditorFlags.ReflectionProbeStatic,
            [InspectorName("Всё")] Everything = -1,
        }

        static StaticEditorFlags DrawStaticFlags(StaticEditorFlags flags)
        {
            const StaticEditorFlags visibleFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;
            var label = new GUIContent("Флаги статичности", "Новые браши, планы стен и сплайны получают эту маску. «Ничего» сбрасывает всю маску. «Всё» включает все биты, в том числе скрытые.");
            EditorGUI.BeginChangeCheck();
            var selected = (StaticFlagOption)EditorGUILayout.EnumFlagsField(label, (StaticFlagOption)flags);
            if (!EditorGUI.EndChangeCheck()) return flags;
            if (selected == StaticFlagOption.None || selected == StaticFlagOption.Everything)
                return (StaticEditorFlags)selected;

            // Partial selections do not modify bits omitted by the current Unity menu.
            return (flags & ~visibleFlags) | ((StaticEditorFlags)selected & visibleFlags);
        }

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            var provider = new SettingsProvider("Project/Brushes", SettingsScope.Project)
            {
                label = "CSG Brush",
                keywords = new HashSet<string>(new[] { "brush", "grid", "units", "preset", "level", "браш", "сетка", "единицы", "пресет", "уровень" }),
                guiHandler = _ =>
                {
                    var s = BrushSettings.instance;
                    EditorGUI.BeginChangeCheck();
                    var presetNames = Array.ConvertAll(BrushSettings.PresetOrder, p => p == BrushSettings.Preset.Custom ? "Пользовательский" : p.ToString());
                    var preset = BrushSettings.PresetOrder[Mathf.Max(0, EditorGUILayout.Popup(new GUIContent("Пресет единиц", "Меняет единицы отображения и параметры сетки. Выберите «Пользовательский», чтобы задать эти значения вручную."), Array.IndexOf(BrushSettings.PresetOrder, s.preset), presetNames))];
                    if (preset != s.preset)
                    {
                        s.ApplyPreset(preset);
                        GUI.changed = false;
                        return;
                    }
                    using (new EditorGUI.DisabledScope(s.preset != BrushSettings.Preset.Custom))
                    {
                        s.unitsPerMeter = EditorGUILayout.FloatField(new GUIContent("Единиц на метр", "Сколько единиц проекта приходится на метр. Например, 100 соответствует сантиметрам. Укажите положительное число."), s.unitsPerMeter);
                        s.unitLabel = EditorGUILayout.TextField(new GUIContent("Обозначение единиц", "Сокращение рядом с размерами и шагами сетки. Например, м или см."), s.unitLabel);
                        var gridText = string.Join(" ", Array.ConvertAll(s.gridSizes, g => g.ToString("0.###")));
                        var newGridText = EditorGUILayout.TextField(new GUIContent("Шаги сетки", "Положительные шаги сетки в единицах проекта. Разделяйте значения пробелами."), gridText);
                        if (newGridText != gridText)
                        {
                            var list = new List<float>();
                            foreach (var part in newGridText.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                                if (float.TryParse(part, out var v) && v > 0f) list.Add(v);
                            if (list.Count > 0) s.gridSizes = list.ToArray();
                        }
                        s.defaultBoxSize = EditorGUILayout.Vector3Field(new GUIContent("Размер коробки", "Размер новой коробки в единицах проекта по осям X, Y и Z."), s.defaultBoxSize);
                        s.rotationSnapDegrees = EditorGUILayout.FloatField(new GUIContent("Шаг поворота (°)", "Шаг привязки вращения в градусах. Чем он больше, тем крупнее угловой шаг. Ноль отключает привязку поворота."), s.rotationSnapDegrees);
                    }
                    var names = Array.ConvertAll(s.gridSizes, g => g.ToString("0.###") + " " + s.unitLabel);
                    s.gridIndex = EditorGUILayout.Popup(new GUIContent("Шаг сетки", "Текущий шаг сетки в единицах проекта. Меньший шаг даёт более точную привязку."), Mathf.Clamp(s.gridIndex, 0, names.Length - 1), names);
                    s.snapToGrid = EditorGUILayout.Toggle(new GUIContent("Привязывать браши к сетке", "После каждой правки положение, размеры и поворот браша привязываются к мировой сетке, независимо от родительского объекта."), s.snapToGrid);
                    s.curveTolerance = Mathf.Max(PointGraph.MinTolerance, EditorGUILayout.FloatField(new GUIContent("Допуск кривых (м)", "Максимальное отклонение стены или сплайна от кривой в метрах. Меньше — плавнее контур и больше сегментов. Минимум 0,01 м. Отдельный объект может переопределить этот допуск."), s.curveTolerance));
                    EditorGUILayout.BeginHorizontal();
                    s.defaultMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent("Основной материал", "Материал граней браша без собственного материала. Если поле пусто, при первом использовании в Assets/CSG Brush создаётся текстура с сеткой в метрах."), s.defaultMaterial, typeof(Material), false);
                    if (GUILayout.Button(new GUIContent("Сетка", "Заново создать материал сетки из списка шагов и использовать его."), GUILayout.Width(56))) { s.defaultMaterial = null; BrushGridMaterial.Regenerate(); BrushGridMaterial.GetOrCreate(); BrushApi.ForceUpdate(); }
                    EditorGUILayout.EndHorizontal();
                    s.showGenerated = EditorGUILayout.Toggle(new GUIContent("Созданные объекты", "Показывает служебные объекты, созданные CSG Brush, в иерархии."), s.showGenerated);
                    s.defaultModelStaticFlags = DrawStaticFlags(s.defaultModelStaticFlags);
                    s.autoLightmapUVs = EditorGUILayout.Toggle(new GUIContent("Автоматические UV карт освещения", "Создаёт UV для карт освещения после завершения правки мешей брашей. При отключении развёртку нужно создать вручную перед запеканием."), s.autoLightmapUVs);
                    EditorGUILayout.HelpBox($"1 м = {s.unitsPerMeter} {s.unitLabel}. Геометрия хранится в метрах. Пресет определяет отображение размеров и привязку к сетке.", MessageType.None);
                    if (EditorGUI.EndChangeCheck())
                        s.NotifyChanged();
                }
            };
            return provider;
        }
    }
}
