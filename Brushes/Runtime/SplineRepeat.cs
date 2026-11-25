using System;
using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// Places a copy of an object every few metres along a spline: sleepers on a rail, posts along a fence, brackets on a
    /// pipe, lamps along a road. Each run of the path (from an end or a junction to the next) gets its own row. The copies
    /// are generated: hidden, made again whenever the spline or these settings change. Edit the source to change them.
    /// </summary>
    [AddComponentMenu("CSG Brush/Повтор вдоль сплайна")]
    [RequireComponent(typeof(Spline))]
    [ExecuteAlways]
    public sealed class SplineRepeat : MonoBehaviour
    {
        [InspectorName("Объект для копирования"), Tooltip("Объект или префаб для размещения вдоль сплайна. При пустом поле копии не строятся.")] public GameObject source;
        [InspectorName("Шаг копий"), Tooltip("Расстояние между копиями в метрах. Меньший шаг увеличивает число копий. Минимум 0,05 м.")] public float spacing = 2f;
        [InspectorName("Начальный отступ"), Tooltip("Отступ до первой копии в метрах на каждом участке. Ноль — начало участка. Если отступ дальше конца, копий на участке не будет.")] public float start = 0f;
        [InspectorName("Угол вокруг сечения"), Tooltip("Угол в градусах вокруг сечения. Ноль — сверху, 90° — справа, 180° — снизу.")] public float angle;
        [InspectorName("Отступ от поверхности"), Tooltip("Расстояние в метрах от поверхности до опорной точки копии. Ноль — на поверхности, положительное значение сдвигает наружу.")] public float offset;
        [InspectorName("Вертикально относительно сечения"), Tooltip("Ориентирует верх копии по верху сечения. При отключении верх направлен наружу вдоль нормали поверхности.")] public bool upright;
        [InspectorName("Дополнительный поворот"), Tooltip("Поворот копии по осям в градусах. Нули сохраняют направление вдоль сплайна и выбранную ориентацию верха.")] public Vector3 rotation;

        /// <summary>The hidden object holding the copies. Derived: made again from the settings.</summary>
        [HideInInspector] public Transform container;
        /// <summary>A hash of what the copies were made from.</summary>
        [HideInInspector] public int builtKey;

        public const string ContainerName = "<[repeated objects]>";

        /// <summary>Set by the editor layer: the settings changed (Inspector, undo, added).</summary>
        public static Action<SplineRepeat> Changed;

        void OnEnable() => Changed?.Invoke(this);
        void OnValidate()
        {
            spacing = Mathf.Max(0.05f, spacing);
            start = Mathf.Max(0f, start);
            Changed?.Invoke(this);
        }
    }
}
