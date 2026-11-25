using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// A brush group: the brushes below it (up to the next group) are combined into one mesh per layer and one set of
    /// convex colliders, which the group holds as its generated children. A brush is built by the nearest group above
    /// it; brushes with none are built by their scene's automatic group (hidden, one per scene). A group inside a
    /// prefab builds into the prefab, which then carries its own mesh and colliders; a prefab without a group is a
    /// stamp that carves the level it is placed in (in the editor). The group's transform moves everything it builds.
    /// </summary>
    [AddComponentMenu("CSG Brush/Группа брашей")]
    [Icon("Packages/digital.dream.csgbrush/Brushes/Editor/Icons/BrushGroup.png")]
    [DisallowMultipleComponent]
    public sealed class BrushGroup : MonoBehaviour
    {
        public const string DefaultName = "<[default model]>";
        public const string MeshChildName = "<[mesh]>";
        /// <summary>Brushes on the Default layer render as <see cref="MeshChildName"/>; every other layer gets its own child, named after the layer.</summary>
        public static string MeshChildNameFor(int layer) => layer == 0 ? MeshChildName : "<[mesh " + (string.IsNullOrEmpty(LayerMask.LayerToName(layer)) ? layer.ToString() : LayerMask.LayerToName(layer)) + "]>";
        public static bool IsMeshChildName(string name) => name == MeshChildName || (name.StartsWith("<[mesh ") && name.EndsWith("]>"));

        /// <summary>The scene's automatic group, for the brushes of that scene that are under no group of their own.</summary>
        [HideInInspector] public bool isDefault;

        /// <summary>Key of the content the generated children were last built from; a prefab instance whose brushes still match its prefab's key keeps the prefab's built meshes.</summary>
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("bakedKey")] public int builtKey;

        /// <summary>
        /// Built once in the editor and moved as a whole at runtime (a lift, a drawbridge): its brushes are not static, and
        /// moving the group's transform moves everything it builds. It is never carved again at runtime.
        /// </summary>
        [Tooltip("Геометрия строится один раз. В игре группа перемещается целиком, например как лифт. Её браши становятся подвижными.")]
        [InspectorName("Подвижная группа")]
        public bool moving;

        /// <summary>What every render mesh of the group gets on its renderer (the materials come from the brushes).</summary>
        public RenderingSettings rendering = new RenderingSettings();
    }

    /// <summary>A brush group's renderer settings, as on a Mesh Renderer; the defaults are Unity's.</summary>
    [System.Serializable]
    public sealed class RenderingSettings
    {
        [InspectorName("Отбрасывать тени"), Tooltip("Режим отбрасывания теней мешами группы.")] public UnityEngine.Rendering.ShadowCastingMode castShadows = UnityEngine.Rendering.ShadowCastingMode.On;
        [InspectorName("Кэшировать статические тени"), Tooltip("Записывать тени в кэшированные карты теней один раз. Для статического освещения.")] public bool staticShadowCaster;
        [InspectorName("Принимать тени"), Tooltip("Отображать на мешах группы тени других объектов.")] public bool receiveShadows = true;
        [InspectorName("Глобальное освещение"), Tooltip("Источник глобального освещения статических мешей. Выберите карты освещения или световые пробы.")] public ReceiveGI receiveGlobalIllumination = ReceiveGI.Lightmaps;
        [InspectorName("Плотность карты освещения"), Tooltip("Относительная плотность текселей карты освещения. При 2 плотность вдвое выше, при 0,5 вдвое ниже. При 0 меш участвует в расчёте освещения, но не занимает место в карте.")] [Min(0f)] public float scaleInLightmap = 1f;
        [InspectorName("Сглаживать швы"), Tooltip("Сглаживать швы карты освещения на стыках граней.")] public bool stitchLightmapSeams = true;
        [InspectorName("Параметры карт освещения"), Tooltip("Ресурс параметров запекания. Пустое поле использует настройки сцены.")] public Object lightmapParameters;
        [InspectorName("Жёсткий угол"), Tooltip("Порог в градусах. Более резкие рёбра разделяют острова UV. Уменьшение создаёт больше разрезов. Диапазон от 1° до 180°.")] [Range(1f, 180f)] public float lightmapHardAngle = 88f;
        [InspectorName("Отклонение углов"), Tooltip("Допустимое искажение углов на развёртке в процентах. Меньше — точнее углы, больше — свободнее раскладка. Диапазон от 1% до 75%.")] [Range(1f, 75f)] public float lightmapAngleError = 8f;
        [InspectorName("Отклонение площади"), Tooltip("Допустимое искажение площади граней на развёртке в процентах. Уменьшение лучше сохраняет пропорции. Диапазон от 1% до 75%.")] [Range(1f, 75f)] public float lightmapAreaError = 15f;
        [InspectorName("Зазор между островами"), Tooltip("Расстояние между островами UV в пикселях для карты 1024 × 1024. Больше — меньше утечек света, но меньше места для граней. Диапазон от 1 до 64.")] [Range(1f, 64f)] public float lightmapPackMargin = 4f;
        [InspectorName("Световые пробы"), Tooltip("Как меши получают освещение от световых проб при отсутствии карты освещения.")] public UnityEngine.Rendering.LightProbeUsage lightProbes = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        [InspectorName("Пробы отражений"), Tooltip("Как меши используют пробы отражений.")] public UnityEngine.Rendering.ReflectionProbeUsage reflectionProbes = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
        [InspectorName("Точка выборки проб"), Tooltip("Объект, задающий точку выборки проб. Пустое поле использует границы каждого меша.")] public Transform anchorOverride;
        [InspectorName("Векторы движения"), Tooltip("Режим расчёта векторов движения для размытия и временных эффектов.")] public MotionVectorGenerationMode motionVectors = MotionVectorGenerationMode.Object;
        [InspectorName("Отсечение за преградами"), Tooltip("Скрывать меши за преградами, в том числе подвижные.")] public bool dynamicOcclusion = true;
        [InspectorName("Маска слоёв рендеринга"), Tooltip("Слои рендеринга мешей для фильтрации света и декалей.")] public uint renderingLayerMask = 1;
    }
}
