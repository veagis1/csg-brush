using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// Marks a container of brushes made by code (a level generator, a test, a tool). Rebuilding it replaces only what is
    /// inside; brushes beside it are kept. It stays first among its siblings, so a subtract brush you add after it still
    /// cuts what was generated, and your additions are never cut by generated cuts.
    /// </summary>
    [AddComponentMenu("CSG Brush/Generated Brushes")]
    [DisallowMultipleComponent]
    public sealed class GeneratedBrushes : MonoBehaviour
    {
        [Tooltip("Название генератора. При повторном запуске он заменяет все браши внутри этого объекта.")]
        [InspectorName("Генератор")]
        public string generator;
    }
}
