using System;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>Where on its wall an anchored object sits.</summary>
    public enum WallFace
    {
        // the names and the Inspector's order are the floor plan's (FloorPlan.Side); the values are the saved ones
        [Tooltip("По центру толщины стены. Дверные и оконные проёмы прорезают стену насквозь."), InspectorName("По центру")] Centered = 0,
        [Tooltip("На внутренней стороне стены, со стороны комнаты."), InspectorName("Внутри")] Inside = 1,
        [Tooltip("На внешней стороне стены."), InspectorName("Снаружи")] Outside = 2,
    }

    /// <summary>
    /// Keeps an object on a wall of the floor plan above it: which wall (by the ids of its two ends), how far along it,
    /// how high, and its pose relative to the wall's face. The plan places it from these whenever either changes; move it
    /// and it takes the nearest wall. Doors and windows get one by themselves; anything else (a prefab, a brush) is
    /// attached by dropping it on a wall. When its wall is gone the object stays where it was.
    /// </summary>
    [AddComponentMenu("CSG Brush/Wall Anchor")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class WallAnchor : MonoBehaviour
    {
        /// <summary><see cref="startId"/> of an anchor not placed yet: it takes the wall nearest to where the object is.</summary>
        public const int Unplaced = -2;
        /// <summary><see cref="startId"/> of an anchor on no wall: the object stays where it is.</summary>
        public const int Free = -1;

        [HideInInspector] public int startId = Unplaced, endId = Unplaced;
        [InspectorName("Расстояние вдоль стены, м"), Tooltip("Расстояние вдоль стены от её первого угла в метрах. При 0 объект находится у начала стены.")] public float distance;
        [InspectorName("Высота над полом, м"), Tooltip("Высота над полом в метрах. Для проёмов измеряется от нижнего края, для остальных объектов от точки опоры.")] public float height;
        [InspectorName("Плоскость стены"), Tooltip("Выбирает внешнюю, среднюю или внутреннюю плоскость стены.")] public WallFace face = WallFace.Inside;
        [InspectorName("Вынос от стены, м"), Tooltip("Вынос точки опоры от выбранной плоскости стены в метрах. При 0 точка опоры лежит в этой плоскости.")] public float offset;
        // A drawn cut may start just beyond the wall end after rounding its first grid point.
        [SerializeField, HideInInspector] internal float alongOffset;
        /// <summary>The object's rotation relative to the face: identity faces out of the wall (+Z out, up is up).</summary>
        [HideInInspector] public Quaternion rotation = Quaternion.identity;
        /// <summary>The wall's length when last placed: finds the place again from the far end if the first corner is gone.</summary>
        [HideInInspector] public float wallLength;
        /// <summary>
        /// The plan's next point id when its wall was last seen whole: points from this id on were added since, so a run of
        /// them between the wall's two ends is the wall split and bent. -1: not known yet.
        /// </summary>
        [HideInInspector] public int newPointsFrom = -1;

        /// <summary>Set when the plan last placed it: false while its wall is not in the plan.</summary>
        [NonSerialized] public bool onWall;

        /// <summary>Set by the editor layer: the anchor's values changed (Inspector, undo, added).</summary>
        public static Action<WallAnchor> Changed;

        public FloorPlan Plan => transform.parent != null ? transform.parent.GetComponent<FloorPlan>() : null;

        /// <summary>The brushes it carries are placed by their wall, not snapped to the grid.</summary>
        public void MarkBrushes(bool anchored)
        {
            foreach (var b in GetComponentsInChildren<Brush>(true)) b.anchored = anchored;
        }

        void OnEnable() { MarkBrushes(true); Changed?.Invoke(this); }
        void OnDisable() => MarkBrushes(false);

        void OnValidate()
        {
            if (height < 0f) height = 0f;
            Changed?.Invoke(this);
        }
    }
}
