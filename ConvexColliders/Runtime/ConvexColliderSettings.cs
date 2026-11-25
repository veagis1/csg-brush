using UnityEngine;

namespace CsgBrush.Colliders
{
    /// <summary>
    /// Settings of a model's convex colliders, added to every model automatically. The builder makes one
    /// convex collider per brush piece, because character controllers and PhysX both handle convex shapes far better than concave mesh
    /// colliders (start-in-solid detection, sweeps, contact generation). Rendering uses the model's CSG mesh.
    ///
    /// Pieces live under a child named by <see cref="ContainerName"/> and are rebuilt with the model's mesh (editor only).
    /// </summary>
    [AddComponentMenu("CSG Brush/Convex Colliders")]
    [DisallowMultipleComponent]
    public sealed class ConvexColliderSettings : MonoBehaviour
    {
        public const string ContainerName = "<[convex colliders]>";

        [Tooltip("Перестраивать коллайдеры после каждого изменения модели в редакторе. При отключении они не обновляются автоматически.")]
        [InspectorName("Автоматически перестраивать")]
        public bool autoRebuild = true;
        [Tooltip("Части с меньшим объёмом отбрасываются. Порог в кубических метрах. При увеличении отсеивается больше мелких частей. При 0 порог не отсекает части положительного объёма.")]
        [InspectorName("Минимальный объём части, м³")]
        public float minPieceVolume = 1e-6f;
        [Tooltip("Показывать сгенерированные части коллайдера в иерархии. Скрытие не меняет столкновения.")]
        [InspectorName("Показывать части в иерархии")]
        public bool showInHierarchy = true;

        [Header("Последнее перестроение")]
        [HideInInspector] public int lastGeometryHash;
        [InspectorName("Число брашей")]
        public int brushCount;
        [InspectorName("Число частей")]
        public int pieceCount;
        [InspectorName("Прямоугольные коллайдеры")]
        public int boxColliders;
        [InspectorName("Выпуклые коллайдеры-меши")]
        public int meshColliders;
        [InspectorName("Триггерные объёмы")]
        public int triggerVolumes;
        [InspectorName("Время построения, мс")]
        public float buildMilliseconds;
    }
}
