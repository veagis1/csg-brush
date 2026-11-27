using static CsgBrush.Editor.BrushLanguage;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

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

    /// <summary>Persist the original project field names for scene tools. Unity's JSON overwrite also reads these names.</summary>
    public abstract class BrushToolDefaults : ScriptableSingleton<BrushSettings>
    {
        [SerializeField, FormerlySerializedAs("defaultGridIndex")] int gridIndex = 2;
        internal int defaultGridIndex { get => gridIndex; set => gridIndex = value; }

        [Tooltip("Расстояние в метрах. Ноль соответствует шагу сетки.")]
        [SerializeField, FormerlySerializedAs("defaultExtrudeDistance")] float extrudeDistance = 0f;
        internal float defaultExtrudeDistance { get => extrudeDistance; set => extrudeDistance = value; }

        [SerializeField, FormerlySerializedAs("defaultExtrudeIndividual")] bool extrudeIndividual = false;
        internal bool defaultExtrudeIndividual { get => extrudeIndividual; set => extrudeIndividual = value; }

        [Tooltip("Показывает вычитающие браши полупрозрачным красным объёмом. Так их можно выбрать, даже если они полностью вырезали геометрию.")]
        [SerializeField, FormerlySerializedAs("defaultShowCuts")] bool showCuts = false;
        internal bool defaultShowCuts { get => showCuts; set => showCuts = value; }

        [Tooltip(BrushSettings.DimensionsTooltip)]
        [SerializeField, FormerlySerializedAs("defaultShowDimensions")] bool showDimensions = true;
        internal bool defaultShowDimensions { get => showDimensions; set => showDimensions = value; }

        [Tooltip("Позволяет менять форму выделенного браша перетаскиванием его сторон.")]
        [SerializeField, FormerlySerializedAs("defaultShapeHandles")] bool shapeHandles = true;
        internal bool defaultShapeHandles { get => shapeHandles; set => shapeHandles = value; }

        [Tooltip("Определяет, как правка формы затронет остальные выделенные браши.")]
        [SerializeField, FormerlySerializedAs("defaultShapeEditSelection")] ShapeEditSelection shapeEditSelection = ShapeEditSelection.Single;
        internal ShapeEditSelection defaultShapeEditSelection { get => shapeEditSelection; set => shapeEditSelection = value; }

        [Tooltip("Привязывает положение, размеры и поворот брашей к мировой сетке после каждой правки, независимо от родительских объектов.")]
        [SerializeField, FormerlySerializedAs("defaultSnapToGrid")] bool snapToGrid = true;
        internal bool defaultSnapToGrid { get => snapToGrid; set => snapToGrid = value; }
    }

    /// <summary>Project-wide grid and brush defaults. One Unity world unit is one metre.</summary>
    [FilePath("ProjectSettings/BrushSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class BrushSettings : BrushToolDefaults
    {
        // Keep the old serialized names so existing settings can be converted in place.
        const int LegacyUnityPreset = 3;
        [SerializeField, HideInInspector] int preset = LegacyUnityPreset;
        [SerializeField, HideInInspector] float unitsPerMeter = 1f;
        [SerializeField, HideInInspector] string unitLabel = "м";

        public new static BrushSettings instance
        {
            get
            {
                var settings = ScriptableSingleton<BrushSettings>.instance;
                if (settings.MigrateLegacyUnits()) settings.Save(true);
                return settings;
            }
        }
        public float[] gridSizes = { 0.125f, 0.25f, 0.5f, 1f, 2f, 4f };
        public Vector3 defaultBoxSize = new Vector3(2f, 2f, 2f);
        public float rotationSnapDegrees = 15f;
        [Header("Новые браши")]
        public BrushOperation newOperation = BrushOperation.Add;
        [Tooltip("Полные имена типов модулей, которые получит каждый новый браш.")] public List<string> newModules = new List<string>();
        [Min(3)] public int newSides = 16;
        [Range(1, 5)] public int newTessellation = 2;
        [Tooltip("Высота ступени в метрах. Ноль соответствует 0,25 м.")] public float newStepHeight = 0f;
        public bool newHollow = false;
        [Tooltip("Радиус в метрах. Ноль соответствует шагу сетки.")] public float newInnerRadius = 0f;
        [Tooltip("Толщина ступени в метрах. Ноль соответствует 0,1 м.")] public float newStepThickness = 0f;
        public float newCurveAngle = 90f;
        [Min(1)] public int newStepsPer360 = 16;
        public bool newCounterClockwise = false;
        public bool newSlopedFloor = false;
        public bool newSlopedCeiling = false;
        [Tooltip("Добавляет сплошную опору до пола под прямой или изогнутой лестницей.")] public bool newSupportUnderSteps = true;
        [Tooltip("Привязка нового браша к сетке за крайние вершины формы или за начало рабочей системы координат объекта. Привязку можно отключить.")] public GridSnap newGridSnap = GridSnap.Shape;
        [Tooltip("Толщина стены в метрах. Ноль соответствует шагу сетки.")] public float newWallThickness = 0f;
        [Range(1f, 180f)] public float newArchAngle = 180f;
        [Tooltip("Ширина двери в метрах. Ноль соответствует 1 м.")] public float newDoorWidth = 0f;
        [Tooltip("Высота двери в метрах. Ноль соответствует 2,2 м.")] public float newDoorHeight = 0f;
        [Tooltip("Ширина окна в метрах. Ноль соответствует 1,2 м.")] public float newWindowWidth = 0f;
        [Tooltip("Высота окна в метрах. Ноль соответствует 1,2 м.")] public float newWindowHeight = 0f;
        [Tooltip("Высота подоконника над полом в метрах. Ноль соответствует 0,9 м.")] public float newWindowSill = 0f;
        [Tooltip("Флаги статичности новых брашей и планов стен. Сетки и коллайдеры браша наследуют его флаги.")]
        public StaticEditorFlags defaultModelStaticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;
        [Tooltip("Создаёт UV для карт освещения после завершения правки мешей брашей. При отключении развёртку нужно создать вручную перед запеканием.")]
        public bool autoLightmapUVs = true;
        [Tooltip("Материал граней без собственного материала. Если поле пусто, при первом использовании в Assets/CSG Brush создаётся текстура с сеткой в метрах.")]
        public Material defaultMaterial;
        [Header("Редактор")]
        public bool showGenerated = false;
        internal const string DimensionsTooltip = "Размеры в метрах в окне сцены. Щёлкните по размеру выделенного браша, введите число и нажмите Enter. Esc отменяет ввод.";
        internal const string RelativeDisplacementTooltip = "При перемещении показывает смещение по мировой оси в метрах. В плоскости видны две оси, с Shift — все три. При вращении показывает угол в градусах у кольца выбранной оси. Знак указывает направление, ноль соответствует началу перетаскивания.";
        [Tooltip(RelativeDisplacementTooltip)]
        public bool showRelativeDisplacement = true;
        [Tooltip("Максимальное отклонение прямых сегментов стены или сплайна от кривой в метрах. Меньше — плавнее контур и больше сегментов. Минимум 0,01 м.")]
        public float curveTolerance = 0.1f;

        // The scene tools use session overrides. Unity restores them after assembly reload and clears them on exit.
        internal const string SessionKey = "CsgBrush.Tools.";
        public int gridIndex
        {
            get => SessionState.GetInt(SessionKey + nameof(gridIndex), defaultGridIndex);
            set => SessionState.SetInt(SessionKey + nameof(gridIndex), value);
        }

        public bool snapToGrid
        {
            get => SessionState.GetBool(SessionKey + nameof(snapToGrid), defaultSnapToGrid);
            set => SessionState.SetBool(SessionKey + nameof(snapToGrid), value);
        }

        public bool showCuts
        {
            get => SessionState.GetBool(SessionKey + nameof(showCuts), defaultShowCuts);
            set => SessionState.SetBool(SessionKey + nameof(showCuts), value);
        }

        public bool showDimensions
        {
            get => SessionState.GetBool(SessionKey + nameof(showDimensions), defaultShowDimensions);
            set => SessionState.SetBool(SessionKey + nameof(showDimensions), value);
        }

        public bool shapeHandles
        {
            get => SessionState.GetBool(SessionKey + nameof(shapeHandles), defaultShapeHandles);
            set => SessionState.SetBool(SessionKey + nameof(shapeHandles), value);
        }

        public ShapeEditSelection shapeEditSelection
        {
            get => (ShapeEditSelection)SessionState.GetInt(SessionKey + nameof(shapeEditSelection), (int)defaultShapeEditSelection);
            set => SessionState.SetInt(SessionKey + nameof(shapeEditSelection), (int)value);
        }

        public float extrudeDistance
        {
            get => SessionState.GetFloat(SessionKey + nameof(extrudeDistance), defaultExtrudeDistance);
            set => SessionState.SetFloat(SessionKey + nameof(extrudeDistance), value);
        }

        public bool extrudeIndividual
        {
            get => SessionState.GetBool(SessionKey + nameof(extrudeIndividual), defaultExtrudeIndividual);
            set => SessionState.SetBool(SessionKey + nameof(extrudeIndividual), value);
        }

        public string blenderPath = "";
        public bool blenderPackTextures = false;

        // Preserve the flat serialized project defaults, including values written by earlier releases.
        [NonSerialized] NewBrushOptions workingOptions;
        [NonSerialized] bool hasWorkingOptions;
        internal NewBrushOptions CreationOptions => hasWorkingOptions ? workingOptions : new NewBrushOptions(this);

        internal ref NewBrushOptions EditCreationOptions()
        {
            if (!hasWorkingOptions)
            {
                workingOptions = new NewBrushOptions(this);
                workingOptions.newModules = new List<string>(workingOptions.newModules);
                hasWorkingOptions = true;
            }
            return ref workingOptions;
        }

        internal void ResetCreationOptions() => hasWorkingOptions = false;

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
        public Vector2 NewOpeningSize(BrushShape shape)
        {
            var options = CreationOptions;
            return shape == BrushShape.Window
                ? new Vector2(options.newWindowWidth > 0f ? options.newWindowWidth : DefaultWindowWidthMeters,
                    options.newWindowHeight > 0f ? options.newWindowHeight : DefaultWindowHeightMeters)
                : new Vector2(options.newDoorWidth > 0f ? options.newDoorWidth : DefaultDoorWidthMeters,
                    options.newDoorHeight > 0f ? options.newDoorHeight : DefaultDoorHeightMeters);
        }

        /// <summary>Height of the next opening's bottom above the floor, in metres.</summary>
        public float NewOpeningSill(BrushShape shape)
        {
            float sill = CreationOptions.newWindowSill;
            return shape == BrushShape.Window ? (sill > 0f ? sill : DefaultWindowSillMeters) : 0f;
        }

        public float NewStepHeightMeters
        {
            get
            {
                float height = CreationOptions.newStepHeight;
                return Mathf.Max(BrushPolyhedron.MinStepHeight, height > 0f ? height : DefaultStepHeightMeters);
            }
        }

        public float GridMeters => gridSizes != null && gridSizes.Length > 0 ? gridSizes[Mathf.Clamp(gridIndex, 0, gridSizes.Length - 1)] : 1f;

        [Obsolete("GridMeters returns the grid step in metres.")]
        public float GridUnits => GridMeters;

        [Obsolete("Use the length in metres directly.")]
        public float ToUnits(float meters) => meters;

        [Obsolete("Use the length in metres directly.")]
        public float ToMeters(float units) => units;

        [Obsolete("Use the vector in metres directly.")]
        public Vector3 ToUnits(Vector3 meters) => meters;

        [Obsolete("Use the vector in metres directly.")]
        public Vector3 ToMeters(Vector3 units) => units;

        [Obsolete("Use FormatMeters.")]
        public string FormatUnits(float meters) => FormatMeters(meters);

        public static string FormatMeters(float meters) => meters.ToString("0.#####", BrushLanguage.Culture) + Tr(" м");
        internal static string FormatGridSteps(float[] steps) => steps == null ? "" : string.Join(" ", Array.ConvertAll(steps, step => step.ToString("R", BrushLanguage.Culture)));

        internal string[] GridStepNames() => gridSizes == null || gridSizes.Length == 0
            ? new[] { FormatMeters(GridMeters) }
            : Array.ConvertAll(gridSizes, step => step.ToString("G6", BrushLanguage.Culture) + Tr(" м"));

        internal static bool TryParseGridSteps(string text, out float[] steps)
        {
            var parts = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            steps = new float[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Replace(',', '.');
                if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                    value <= 0f || !float.IsFinite(value)) return false;
                steps[i] = value;
            }
            return steps.Length > 0;
        }

        internal bool MigrateLegacyUnits()
        {
            if (unitsPerMeter == 1f && preset == LegacyUnityPreset && unitLabel == "м") return false;

            if (unitsPerMeter > 0f && unitsPerMeter != 1f && float.IsFinite(unitsPerMeter))
            {
                if (gridSizes == null || gridSizes.Length == 0) gridSizes = new[] { 1f }; // the old fallback was one project unit
                for (var i = 0; i < gridSizes.Length; i++) gridSizes[i] /= unitsPerMeter;
                defaultBoxSize = new Vector3(defaultBoxSize.x / unitsPerMeter, defaultBoxSize.y / unitsPerMeter, defaultBoxSize.z / unitsPerMeter);
                newStepHeight /= unitsPerMeter;
                newInnerRadius /= unitsPerMeter;
                newStepThickness /= unitsPerMeter;
                newWallThickness /= unitsPerMeter;
                newDoorWidth /= unitsPerMeter;
                newDoorHeight /= unitsPerMeter;
                newWindowWidth /= unitsPerMeter;
                newWindowHeight /= unitsPerMeter;
                newWindowSill /= unitsPerMeter;
                defaultExtrudeDistance /= unitsPerMeter;
                SessionState.EraseFloat(SessionKey + nameof(extrudeDistance));
                SessionState.EraseInt(SessionKey + nameof(gridIndex));
            }

            preset = LegacyUnityPreset;
            unitsPerMeter = 1f;
            unitLabel = "м";
            return true;
        }

        public void SetGridIndex(int index)
        {
            gridIndex = Mathf.Clamp(index, 0, Mathf.Max(0, (gridSizes?.Length ?? 0) - 1));
            NotifyToolChanged();
        }

        internal static void NotifyToolChanged() => Changed?.Invoke();

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

        [Flags]
        enum EnglishStaticFlags
        {
            Nothing = 0,
            ContributeGI = (int)StaticFlagOption.ContributeGI,
            OccluderStatic = (int)StaticFlagOption.OccluderStatic,
            OccludeeStatic = (int)StaticFlagOption.OccludeeStatic,
            BatchingStatic = (int)StaticFlagOption.BatchingStatic,
            ReflectionProbeStatic = (int)StaticFlagOption.ReflectionProbeStatic,
            Everything = -1
        }

        static StaticEditorFlags DrawStaticFlags(StaticEditorFlags flags)
        {
            const StaticEditorFlags visibleFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;
            var label = new GUIContent(Tr("Флаги статичности"), Tr("Новые браши, планы стен и сплайны получают эту маску. «Ничего» сбрасывает всю маску. «Всё» включает все биты, в том числе скрытые."));
            EditorGUI.BeginChangeCheck();
            var selected = BrushLanguage.English
                ? (StaticFlagOption)(int)(EnglishStaticFlags)EditorGUILayout.EnumFlagsField(label, (EnglishStaticFlags)(int)flags)
                : (StaticFlagOption)EditorGUILayout.EnumFlagsField(label, (StaticFlagOption)flags);
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
                keywords = new HashSet<string>(new[] { "brush", "grid", "meters", "level", "language", "язык", "браш", "сетка", "метры", "уровень" }),
                guiHandler = _ =>
                {
                    float previousWidth = EditorGUIUtility.labelWidth;
                    try
                    {
                        // Allow for the bilingual language selector and the longer Russian labels.
                        EditorGUIUtility.labelWidth = Mathf.Min(240f, EditorGUIUtility.currentViewWidth * 0.45f);
                        DrawFields();
                    }
                    finally { EditorGUIUtility.labelWidth = previousWidth; }
                }
            };
            return provider;
        }

        static bool showCreationDefaults, showToolDefaults;
        static readonly (string field, string label)[] CreationDefaults =
        {
            (nameof(BrushSettings.newGridSnap), "Привязка к сетке"),
            (nameof(BrushSettings.newModules), "Модули"),
            (nameof(BrushSettings.newHollow), "Полый браш"),
            (nameof(BrushSettings.newWallThickness), "Толщина стены (м)"),
            (nameof(BrushSettings.newSides), "Стороны"),
            (nameof(BrushSettings.newTessellation), "Детализация"),
            (nameof(BrushSettings.newArchAngle), "Угол арки (°)"),
            (nameof(BrushSettings.newStepHeight), "Высота ступени (м)"),
            (nameof(BrushSettings.newInnerRadius), "Внутренний радиус (м)"),
            (nameof(BrushSettings.newStepThickness), "Толщина ступени (м)"),
            (nameof(BrushSettings.newCurveAngle), "Угол лестницы (°)"),
            (nameof(BrushSettings.newStepsPer360), "Ступеней на оборот"),
            (nameof(BrushSettings.newCounterClockwise), "Против часовой"),
            (nameof(BrushSettings.newSlopedFloor), "Наклонный пол"),
            (nameof(BrushSettings.newSlopedCeiling), "Наклонный потолок"),
            (nameof(BrushSettings.newSupportUnderSteps), "Опора под ступенями"),
            (nameof(BrushSettings.newDoorWidth), "Ширина двери (м)"),
            (nameof(BrushSettings.newDoorHeight), "Высота двери (м)"),
            (nameof(BrushSettings.newWindowWidth), "Ширина окна (м)"),
            (nameof(BrushSettings.newWindowHeight), "Высота окна (м)"),
            (nameof(BrushSettings.newWindowSill), "Высота подоконника (м)"),
        };

        static void DrawCreationDefaults(BrushSettings settings)
        {
            showCreationDefaults = EditorGUILayout.Foldout(showCreationDefaults, Tr("Параметры новых форм по умолчанию"), true);
            if (!showCreationDefaults) return;
            using var indent = new EditorGUI.IndentLevelScope();
            using var serialized = new SerializedObject(settings);
            foreach (var (field, label) in CreationDefaults)
            {
                var property = serialized.FindProperty(field);
                BrushEditor.DrawProperty(property, new GUIContent(Tr(label), Tr(property.tooltip)), true);
            }
            if (serialized.ApplyModifiedPropertiesWithoutUndo()) settings.ResetCreationOptions();
        }

        static void DrawToolDefaults(BrushSettings s)
        {
            showToolDefaults = EditorGUILayout.Foldout(showToolDefaults, Tr("Параметры инструментов по умолчанию"), true);
            if (!showToolDefaults) return;
            using var indent = new EditorGUI.IndentLevelScope();
            bool cuts = EditorGUILayout.Toggle(Tr("Вычеты"), s.defaultShowCuts);
            if (cuts != s.defaultShowCuts) s.defaultShowCuts = s.showCuts = cuts;
            bool sizes = EditorGUILayout.Toggle(new GUIContent(Tr("Размеры"), Tr(BrushSettings.DimensionsTooltip)), s.defaultShowDimensions);
            if (sizes != s.defaultShowDimensions) s.defaultShowDimensions = s.showDimensions = sizes;
            bool handles = EditorGUILayout.Toggle(Tr("Правка формы"), s.defaultShapeHandles);
            if (handles != s.defaultShapeHandles) s.defaultShapeHandles = s.shapeHandles = handles;
            var selection = BrushEditor.EnumPopup(new GUIContent(Tr("Правка нескольких брашей")), s.defaultShapeEditSelection);
            if (selection != s.defaultShapeEditSelection) s.defaultShapeEditSelection = s.shapeEditSelection = selection;
            float distance = EditorGUILayout.FloatField(new GUIContent(Tr("Расстояние (м)"), Tr("Расстояние выдавливания в метрах. Отрицательное значение создаёт углубление, ноль соответствует шагу сетки.")), s.defaultExtrudeDistance);
            if (distance != s.defaultExtrudeDistance) s.defaultExtrudeDistance = s.extrudeDistance = distance;
            bool individual = EditorGUILayout.Toggle(Tr("Выдавливать грани по отдельности"), s.defaultExtrudeIndividual);
            if (individual != s.defaultExtrudeIndividual) s.defaultExtrudeIndividual = s.extrudeIndividual = individual;
        }

        static void DrawBlenderSettings(BrushSettings s)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var path = EditorGUILayout.TextField(new GUIContent("Blender", Tr("Путь к Blender 3.6 или новее. Пустое поле включает автоматический поиск.")), s.blenderPath);
                if (GUILayout.Button(Tr("Обзор…"), GUILayout.ExpandWidth(false)))
                {
                    var picked = BlenderExport.ChooseExecutable();
                    if (!string.IsNullOrEmpty(picked)) path = picked;
                }
                if (path != s.blenderPath)
                {
                    s.blenderPath = path;
                    BlenderExport.Forget();
                    GUI.changed = true;
                }
            }
            s.blenderPackTextures = EditorGUILayout.Toggle(new GUIContent(Tr("Упаковывать текстуры"), Tr("Сохраняет изображения внутри файла .blend. Без упаковки Blender использует пути к исходным изображениям.")), s.blenderPackTextures);
        }

        static void DrawFields()
        {
            BrushLanguage.DrawPreference();
            var s = BrushSettings.instance;
            EditorGUI.BeginChangeCheck();
            var gridText = BrushSettings.FormatGridSteps(s.gridSizes);
            var newGridText = EditorGUILayout.DelayedTextField(new GUIContent(Tr("Шаги сетки (м)"), Tr("Положительные шаги сетки в метрах. Разделяйте значения пробелами. В дробях можно использовать запятую или точку.")), gridText);
            if (newGridText != gridText && BrushSettings.TryParseGridSteps(newGridText, out var steps))
                s.gridSizes = steps;
            s.defaultBoxSize = EditorGUILayout.Vector3Field(new GUIContent(Tr("Размер коробки (м)"), Tr("Размер новой коробки в метрах по осям X, Y и Z.")), s.defaultBoxSize);
            s.rotationSnapDegrees = EditorGUILayout.FloatField(new GUIContent(Tr("Шаг поворота (°)"), Tr("Шаг привязки вращения в градусах. Чем он больше, тем крупнее угловой шаг. Ноль отключает привязку поворота.")), s.rotationSnapDegrees);
            var names = s.GridStepNames();
            int grid = EditorGUILayout.Popup(new GUIContent(Tr("Шаг сетки"), Tr("Текущий шаг сетки в метрах. Меньший шаг даёт более точную привязку.")), Mathf.Clamp(s.defaultGridIndex, 0, names.Length - 1), names);
            if (grid != s.defaultGridIndex) s.defaultGridIndex = s.gridIndex = grid;
            bool snap = EditorGUILayout.Toggle(new GUIContent(Tr("Привязывать браши к сетке"), Tr("После каждой правки положение, размеры и поворот браша привязываются к мировой сетке, независимо от родительского объекта.")), s.defaultSnapToGrid);
            if (snap != s.defaultSnapToGrid) s.defaultSnapToGrid = s.snapToGrid = snap;
            s.curveTolerance = Mathf.Max(PointGraph.MinTolerance, EditorGUILayout.FloatField(new GUIContent(Tr("Допуск кривых (м)"), Tr("Максимальное отклонение стены или сплайна от кривой в метрах. Меньше — плавнее контур и больше сегментов. Минимум 0,01 м. Отдельный объект может переопределить этот допуск.")), s.curveTolerance));
            EditorGUILayout.BeginHorizontal();
            s.defaultMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent(Tr("Основной материал"), Tr("Материал граней браша без собственного материала. Если поле пусто, при первом использовании в Assets/CSG Brush создаётся текстура с сеткой в метрах.")), s.defaultMaterial, typeof(Material), false);
            if (GUILayout.Button(new GUIContent(Tr("Сетка"), Tr("Заново создать материал сетки из списка шагов и использовать его.")), GUILayout.Width(56))) { s.defaultMaterial = null; BrushGridMaterial.Regenerate(); BrushGridMaterial.GetOrCreate(); BrushApi.ForceUpdate(); }
            EditorGUILayout.EndHorizontal();
            s.showGenerated = EditorGUILayout.Toggle(new GUIContent(Tr("Созданные объекты"), Tr("Показывает служебные объекты, созданные CSG Brush, в иерархии.")), s.showGenerated);
            s.defaultModelStaticFlags = DrawStaticFlags(s.defaultModelStaticFlags);
            s.autoLightmapUVs = EditorGUILayout.Toggle(new GUIContent(Tr("Автоматические UV карт освещения"), Tr("Создаёт UV для карт освещения после завершения правки мешей брашей. При отключении развёртку нужно создать вручную перед запеканием.")), s.autoLightmapUVs);
            DrawToolDefaults(s);
            DrawCreationDefaults(s);
            DrawBlenderSettings(s);
            if (EditorGUI.EndChangeCheck())
                s.NotifyChanged();
        }
    }
}
