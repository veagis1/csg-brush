using UnityEngine;

namespace CsgBrush.Colliders
{
    /// <summary>What a brush's volume is to physics: solid geometry, a trigger volume, or no collider at all.</summary>
    public enum ColliderKind
    {
        [InspectorName("Твёрдое тело")] Solid = 0,
        [InspectorName("Триггер")] Trigger = 1,
        [InspectorName("Нет")] None = 2,
    }
}
