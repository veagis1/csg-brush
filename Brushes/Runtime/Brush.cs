using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using CsgBrush.Colliders;

namespace CsgBrush
{
    /// <summary>What of a brush goes onto the grid when it is moved or resized (a rotation never moves it).</summary>
    public enum GridSnap
    {
        [Tooltip("Крайние вершины ложатся на линии сетки, чтобы соседние браши стыковались без зазоров."), InspectorName("По форме")] Shape,
        [Tooltip("К сетке привязывается точка опоры, а форма перемещается вместе с ней."), InspectorName("По точке опоры")] Pivot,
        [Tooltip("Положение и размеры сохраняют введённые значения без округления по сетке."), InspectorName("Отключено")] Off,
    }

    /// <summary>How a brush's pivot is measured.</summary>
    public enum PivotMode
    {
        [Tooltip("Координата от 0 до 1 по каждой оси. Точка опоры смещается вместе с изменением размера."), InspectorName("Доля размера")] Normalized,
        [Tooltip("Расстояние от левого нижнего заднего угла. При изменении размера сохраняется в метрах."), InspectorName("Расстояние")] Absolute,
    }

    /// <summary>Which material the faces a subtractive brush cuts take.</summary>
    public enum CutFaces
    {
        [Tooltip("Материал вычитающего браша. Например, проём можно облицевать материалом дверной рамы."), InspectorName("Материал вычитающего браша")] CutterMaterial,
        [Tooltip("Материал вырезаемого объёма. Например, внутренние поверхности проёма получают материал стены."), InspectorName("Материал вырезаемого объёма")] CutSolidMaterial,
    }

    public enum BrushShape
    {
        [InspectorName("Блок")] Box = 0,
        /// <summary>Ramp: rises along the local Z axis.</summary>
        [InspectorName("Клин (пандус)")] Wedge = 1,
        [InspectorName("Цилиндр")] Cylinder = 2,
        [InspectorName("Конус")] Cone = 3,
        [InspectorName("Сфера")] Sphere = 4,
        /// <summary>Linear stairs climbing along the local Z axis.</summary>
        [InspectorName("Прямая лестница")] Stairs = 5,
        /// <summary>Edited by hand: the shape is the polyhedron on the brush, decomposed into convex pieces.</summary>
        [InspectorName("Произвольная форма")] Custom = 6,
        /// <summary>Steps wrapping around an inner column over an angle; each step a solid block from the floor.</summary>
        [InspectorName("Изогнутая лестница")] CurvedStairs = 7,
        /// <summary>Separate step slabs wrapping around an inner column, possibly several turns.</summary>
        [InspectorName("Винтовая лестница")] SpiralStairs = 8,
        /// <summary>An arch filling its box: a ring of segments between an outer and an inner ellipse, standing on the floor.</summary>
        [InspectorName("Арка")] Arch = 9,
        /// <summary>A doorway to cut into a wall: a box of the opening's size, subtract by new. On a floor plan it rides on a wall (see <see cref="WallAnchor"/>).</summary>
        [InspectorName("Дверь")] Door = 10,
        /// <summary>A window to cut into a wall: as <see cref="Door"/>, standing on a sill.</summary>
        [InspectorName("Окно")] Window = 11,
    }

    public enum BrushOperation
    {
        [InspectorName("Добавить")] Add = 0,
        [InspectorName("Вычесть")] Subtract = 1,
    }

    /// <summary>
    /// A convex level-building volume. Place and rotate it with the normal Unity tools; its size lives on
    /// this component and is centred on the transform, like a BoxCollider. Rendering and collision are
    /// generated automatically (CSG through the Manifold library, one convex collider per piece). Sizes are stored in
    /// metres, the same as in the Inspector.
    /// </summary>
    [AddComponentMenu("Браш")]
    [Icon("Packages/digital.dream.csgbrush/Brushes/Editor/Icons/Brush.png")]
    [DisallowMultipleComponent]
    [SelectionBase]
    [ExecuteAlways]
    public sealed class Brush : MonoBehaviour
    {
        public const string ShapeChildName = "<[shape]>";
        public const string HollowChildName = "<[hollow]>";
        /// <summary>Custom shapes are decomposed into convex pieces, one hidden child each: "<[piece 0]>", "<[piece 1]>", ...</summary>
        public const string PieceChildPrefix = "<[piece ";

        public static bool IsGeneratedChildName(string name) => name == ShapeChildName || name == HollowChildName || name.StartsWith(PieceChildPrefix);

        /// <summary>The editable shape when <see cref="shape"/> is Custom. May be concave; convex pieces are derived from it.</summary>
        [HideInInspector] public BrushPolyhedron polyhedron = new BrushPolyhedron();
        /// <summary>The parametric shape a Custom brush was converted from, for "Reset to ...".</summary>
        [HideInInspector] public BrushShape customFrom = BrushShape.Box;
        /// <summary>Set by the editor layer when the shape (or one of its convex parts) cannot be built; shown in the Inspector and the Scene view, never in the console.</summary>
        [NonSerialized] public string problem;

        public BrushShape shape = BrushShape.Box;
        public BrushOperation operation = BrushOperation.Add;
        [Tooltip("Твёрдое тело блокирует движение. Триггер посылает события при пересечении. В режиме «Нет» коллайдеры не создаются. Модуль браша может переопределить режим.")]
        public ColliderKind collision = ColliderKind.Solid;
        [Tooltip("Физический материал всех частей браша, включая триггеры. Задаёт трение и упругость, а также может использоваться для выбора звука.")]
        public PhysicsMaterial physicsMaterial;
        [Tooltip("Передавать данные о контактах частей браша в обработчики OnCollision.")]
        public bool provideContacts;
        // the surface kind and fall-damage flag of earlier versions, migrated into modules on the next sync (see LegacySurfaceMigration)
        [SerializeField, HideInInspector, FormerlySerializedAs("surface")] int legacySurface;
        [SerializeField, HideInInspector, FormerlySerializedAs("noFallDamage")] bool legacyNoFallDamage;

        [Tooltip("Габариты по осям X, Y и Z, в метрах.")]
        public Vector3 size = new Vector3(2f, 2f, 2f);

        [SerializeField, HideInInspector] internal Vector3 exactSizeMeters;
        [SerializeField, HideInInspector] internal Transform workGridOwner;
        [SerializeField, HideInInspector] internal Vector3 workGridOrigin;
        [SerializeField, HideInInspector] internal Quaternion workGridRotation = Quaternion.identity;
        [SerializeField, HideInInspector] internal bool compoundPart, addressedCut;
        [SerializeField, HideInInspector] internal Brush cutTarget;
        [SerializeField, HideInInspector] internal FloorPlan cutPlan;
        [SerializeField, HideInInspector] internal int cutWallStartId, cutWallEndId, generatedWallStartId, generatedWallEndId;

        /// <summary>
        /// Where the transform sits in the shape's box, per axis, measured from its left, bottom, back corner (the back is
        /// the side away from where the brush faces): a fraction of the size, or metres (see <see cref="pivotMode"/>).
        /// Curved and spiral stairs, doors and windows keep theirs; a Custom shape's comes from its vertices.
        /// </summary>
        public Vector3 pivot = new Vector3(0.5f, 0.5f, 0.5f);
        /// <summary>How <see cref="pivot"/> is measured.</summary>
        public PivotMode pivotMode = PivotMode.Normalized;

        // Local metres from the transform to the construction axis of a round stair or the centre of an opening.
        [SerializeField, HideInInspector] internal Vector3 shapeOriginOffsetMeters;
        [Tooltip("Привязывает к сетке крайние вершины или точку опоры при перемещении и масштабировании. Поворот не вызывает привязку.")]
        public GridSnap gridSnap = GridSnap.Shape;

        /// <summary>Box-like shapes whose geometry follows <see cref="pivot"/>.</summary>
        public bool HasPivot => shape != BrushShape.Custom && !HasParametricSize && !IsOpening;

        internal bool UsesShapeOriginOffset => UsesShapeOriginOffsetFor(shape);

        internal static bool UsesShapeOriginOffsetFor(BrushShape shape) =>
            shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs || shape == BrushShape.Door || shape == BrushShape.Window;

        /// <summary>The pivot in metres from the box's left, bottom, back corner for a box of <paramref name="boxSize"/>, kept inside the box.</summary>
        public Vector3 PivotDistance(Vector3 boxSize)
        {
            var d = pivotMode == PivotMode.Normalized ? Vector3.Scale(pivot, boxSize) : pivot;
            return new Vector3(Mathf.Clamp(d.x, 0f, boxSize.x), Mathf.Clamp(d.y, 0f, boxSize.y), Mathf.Clamp(d.z, 0f, boxSize.z));
        }

        /// <summary>How far a shape of <paramref name="boxSize"/> sits from the transform in local space: none at the centre.</summary>
        public Vector3 PivotShiftFor(Vector3 boxSize) => HasPivot ? boxSize * 0.5f - PivotDistance(boxSize) :
            UsesShapeOriginOffset ? shapeOriginOffsetMeters : Vector3.zero;

        /// <summary>How far the shape sits from the transform in local space: none at the centre.</summary>
        public Vector3 PivotShift => PivotShiftFor(ClampedSize);

        [Tooltip("У блока или цилиндра остаются только стенки. При отключении весь объём сплошной.")]
        public bool hollow;
        [Tooltip("Толщина стенок полого браша или пояса арки в метрах. Чем больше значение, тем меньше внутренний проём.")]
        public float wallThickness = 0.5f;

        [Tooltip("Число граней цилиндра или конуса либо сегментов арки. Больше граней дают более гладкий контур. Минимум 3.")]
        [Min(3)] public int sides = 16;
        [Tooltip("Детализация сферы. При 1 граней меньше, при 5 поверхность наиболее гладкая.")]
        [Range(1, 5)] public int tessellation = 2;
        [Tooltip("Высота ступени в метрах, минимум 0,1 м. При фиксированной высоте лестницы увеличение уменьшает число ступеней.")]
        public float stepHeight = 0.25f;
        [Tooltip("Радиус внутренней колонны изогнутой или винтовой лестницы в метрах. При увеличении ступени располагаются дальше от оси.")]
        public float innerRadius = 0.5f;
        [Tooltip("Ширина ступени от внутренней колонны до внешнего края, в метрах. При увеличении лестница становится шире.")]
        public float stepWidth = 1.5f;
        [Tooltip("Толщина каждой плиты ступени в метрах. Применяется к винтовой лестнице и лестнице без опоры под ступенями.")]
        public float stepThickness = 0.1f;
        [Tooltip("У прямой или изогнутой лестницы пространство под ступенями заполнено до пола. При отключении остаются отдельные плиты ступеней.")]
        public bool supportUnderSteps = true;
        [Tooltip("Размах изогнутой лестницы или арки в градусах. Чем больше угол, тем длиннее дуга. Для арки максимум 180°.")]
        public float curveAngle = 90f;
        [Tooltip("Число ступеней изогнутой или винтовой лестницы. Минимум 1.")]
        [Min(1)] public int numSteps = 8;
        [Tooltip("Число ступеней на полный оборот 360° винтовой лестницы. При увеличении угол между ступенями уменьшается. Минимум 1.")]
        [Min(1)] public int stepsPer360 = 16;
        [Tooltip("Поправка высоты первой ступени изогнутой или винтовой лестницы в метрах. Отрицательное число опускает её, 0 оставляет исходную высоту.")]
        public float addToFirstStep = 0f;
        [Tooltip("При взгляде сверху ступени идут против часовой стрелки. При отключении лестница закручивается по часовой стрелке.")]
        public bool counterClockwise;
        [Tooltip("Верх винтовой лестницы образует наклонный пандус. При отключении верх остаётся ступенчатым.")]
        public bool slopedFloor;
        [Tooltip("Нижняя поверхность винтовой лестницы образует непрерывный уклон. При отключении низ остаётся ступенчатым.")]
        public bool slopedCeiling;

        [Tooltip("Материал всех граней браша. Его также можно перетащить на браш в окне сцены.")]
        public Material material;
        [Tooltip("Для операции «Вычесть». Выбирает, получают срезы материал вычитающего браша или вырезаемого объёма.")]
        public CutFaces cutFaces = CutFaces.CutterMaterial;

        /// <summary>Set by the editor layer; called when the component needs to push its values into the generated structure.</summary>
        public static Action<Brush> SyncRequested;

        /// <summary>The generator that made this brush (a floor plan's wall, say), or null for a brush you authored.</summary>
        [HideInInspector] public BrushGenerator generatedBy;

        /// <summary>Made by a <see cref="BrushGenerator"/>: never snapped, picked or edited on its own.</summary>
        public bool IsGenerated => generatedBy != null;

        /// <summary>A door or window: a cut of a fixed size, not snapped to the grid, always clickable.</summary>
        public bool IsOpening => shape == BrushShape.Door || shape == BrushShape.Window;

        /// <summary>Set by a <see cref="WallAnchor"/> on the brush or above it: the brush rides on a wall.</summary>
        [NonSerialized] public bool anchored;

        /// <summary>Placed by something else (its generator, its wall): never snapped to the grid.</summary>
        public bool IsPlaced => generatedBy != null || IsOpening || anchored || gridSnap == GridSnap.Off;

        // ------------------------------------------------------------------ registry

        static readonly List<Brush> s_Active = new List<Brush>();
        [NonSerialized] int m_ActiveSlot; // index in s_Active plus one; 0 when not registered
        [NonSerialized] Transform m_Transform;

        /// <summary>Editor-side data derived from the brush (pick shape, build key); dropped whenever the brush changes. Lives and dies with the brush, so it needs no dictionary and no cleanup.</summary>
        [NonSerialized] internal object editorCache;

        /// <summary>The brush's transform without a call into Unity: for loops over every brush each frame.</summary>
        public Transform CachedTransform => m_Transform != null ? m_Transform : (m_Transform = transform);

        /// <summary>
        /// Every enabled brush on an active GameObject: in all loaded scenes, in Prefab Mode and in prefab contents
        /// loaded for building, never in prefab assets. Kept by OnEnable and OnDisable, so a domain reload rebuilds it and
        /// scene unloads, deletes and deactivation empty it. Unordered. Iterate by index: a foreach allocates.
        /// </summary>
        public static IReadOnlyList<Brush> Active => s_Active;

        /// <summary>Incremented whenever a brush joins or leaves <see cref="Active"/>; lets callers cache what they derive from it.</summary>
        public static int ActiveVersion { get; private set; }

        void Register()
        {
            if (m_ActiveSlot != 0) return;
            m_Transform = transform;
            s_Active.Add(this);
            m_ActiveSlot = s_Active.Count;
            ActiveVersion++;
        }

        void Unregister()
        {
            int i = m_ActiveSlot - 1;
            if (i < 0) return;
            m_ActiveSlot = 0;
            if (i >= s_Active.Count || !ReferenceEquals(s_Active[i], this)) { s_Active.Remove(this); ActiveVersion++; return; } // defensive: never out of step
            int last = s_Active.Count - 1;
            if (i != last) { var moved = s_Active[last]; s_Active[i] = moved; moved.m_ActiveSlot = i + 1; } // swap-remove: O(1)
            s_Active.RemoveAt(last);
            ActiveVersion++;
        }

        public Vector3 ClampedSize
        {
            get
            {
                const float min = 0.001f;
                return new Vector3(Mathf.Max(size.x, min), Mathf.Max(size.y, min), Mathf.Max(size.z, min));
            }
        }

        public bool SupportsHollow => shape == BrushShape.Box || shape == BrushShape.Cylinder;

        /// <summary>Shapes whose size follows from their parameters (like a Custom shape follows its vertices).</summary>
        public bool HasParametricSize => shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs;

        /// <summary>The stair parameters as the polyhedron generators take them.</summary>
        public StairParams Stairs => new StairParams
        {
            innerRadius = innerRadius, stepWidth = stepWidth, stepHeight = stepHeight, stepThickness = stepThickness, curveAngle = curveAngle,
            numSteps = numSteps, stepsPer360 = stepsPer360, addToFirstStep = addToFirstStep, counterClockwise = counterClockwise, slopedFloor = slopedFloor, slopedCeiling = slopedCeiling, open = !supportUnderSteps,
        };

        /// <summary>True when a hollow (subtractive) child should exist for this brush.</summary>
        public bool IsHollow => hollow && SupportsHollow;

        // ---- modules

        /// <summary>The modules on this brush's object and its parents (own first): the game's data on the brush.</summary>
        public BrushModule[] Modules() => GetComponentsInParent<BrushModule>(false);

        /// <summary>What the volume is to physics once the modules have had their say.</summary>
        public ColliderKind EffectiveCollision()
        {
            foreach (var m in Modules()) if (m != null && m.enabled && m.OverrideCollision(out var o)) return o;
            return collision;
        }

        /// <summary>Everything the modules put on the pieces, hashed: part of each piece's identity.</summary>
        public int ModuleFingerprint()
        {
            unchecked
            {
                int h = (int)EffectiveCollision();
                h = h * 31 + (physicsMaterial != null ? physicsMaterial.name.GetHashCode() : 0) + (provideContacts ? 7 : 0);
                h = h * 31 + gameObject.tag.GetHashCode();
                foreach (var m in Modules())
                {
                    if (m == null || !m.enabled) continue;
                    h = h * 31 + m.GetType().FullName.GetHashCode();
                    h = h * 31 + m.Fingerprint();
                }
                return h;
            }
        }

        // ---- legacy surface data (versions before modules)

        /// <summary>
        /// Registered by a game's module package: given the old surface kind (0 solid, 1 slick, 2 water, 3 trigger,
        /// 4 no collision) and fall-damage flag, add the matching module and return true. Trigger and no-collision
        /// are handled by the brush itself.
        /// </summary>
        public static Func<Brush, int, bool, bool> LegacySurfaceMigration;
        public bool HasLegacySurface => legacySurface != 0 || legacyNoFallDamage;
        public (int surface, bool noFallDamage) LegacySurface => (legacySurface, legacyNoFallDamage);
        public void ClearLegacySurface() { legacySurface = 0; legacyNoFallDamage = false; }

        // ---- trigger events, one per brush however many pieces it is made of

        /// <summary>A collider entered the brush's volume (raised once, when it enters the first piece).</summary>
        public event Action<Collider> TriggerEntered;
        /// <summary>A collider left the brush's volume (raised once, when it leaves the last piece).</summary>
        public event Action<Collider> TriggerExited;
        readonly Dictionary<Collider, int> insidePieces = new Dictionary<Collider, int>();

        /// <summary>Called by the relay on a trigger piece.</summary>
        public void PieceTriggerEnter(Collider other)
        {
            insidePieces.TryGetValue(other, out int n);
            insidePieces[other] = n + 1;
            if (n > 0) return;
            TriggerEntered?.Invoke(other);
            foreach (var l in GetComponents<IBrushTriggerListener>()) l.OnBrushTriggerEnter(this, other);
            gameObject.SendMessage("OnTriggerEnter", other, SendMessageOptions.DontRequireReceiver); // scripts on the brush, as on any trigger
        }

        readonly Dictionary<Collider, (int frame, float step)> lastStay = new Dictionary<Collider, (int, float)>();

        /// <summary>Called by the relay on a trigger piece: OnTriggerStay on the brush object, once per physics step whatever the pieces.</summary>
        public void PieceTriggerStay(Collider other)
        {
            var now = (Time.frameCount, Time.fixedTime);
            if (lastStay.TryGetValue(other, out var last) && last == now) return;
            lastStay[other] = now;
            gameObject.SendMessage("OnTriggerStay", other, SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>Called by the relay on a trigger piece.</summary>
        public void PieceTriggerExit(Collider other)
        {
            if (!insidePieces.TryGetValue(other, out int n)) return;
            if (n > 1) { insidePieces[other] = n - 1; return; }
            insidePieces.Remove(other);
            lastStay.Remove(other);
            TriggerExited?.Invoke(other);
            foreach (var l in GetComponents<IBrushTriggerListener>()) l.OnBrushTriggerExit(this, other);
            gameObject.SendMessage("OnTriggerExit", other, SendMessageOptions.DontRequireReceiver);
        }

        void OnValidate()
        {
            if (size.x < 0f) size.x = 0f;
            if (size.y < 0f) size.y = 0f;
            if (size.z < 0f) size.z = 0f;
            if (wallThickness < 0f) wallThickness = 0f;
            if (stepHeight < BrushPolyhedron.MinStepHeight) stepHeight = BrushPolyhedron.MinStepHeight;
            if (innerRadius < 0f) innerRadius = 0f;
            if (stepWidth < 0.001f) stepWidth = 0.001f;
            if (stepThickness < 0.001f) stepThickness = 0.001f;
            if (curveAngle < 1f) curveAngle = 1f;
            if (numSteps < 1) numSteps = 1;
            if (stepsPer360 < 1) stepsPer360 = 1;
            SyncRequested?.Invoke(this);
        }

        void OnEnable()
        {
            Register();
            SyncRequested?.Invoke(this);
        }

        void OnDisable() => Unregister();

        void OnDrawGizmosSelected()
        {
            // The wire box follows the hand during a drag, including the Scale tool: the transform scale is what
            // the size will become once the drag is released and the scale is applied to the size.
            var scale = transform.lossyScale;
            var shown = Vector3.Scale(ClampedSize, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = operation == BrushOperation.Subtract ? new Color(1f, 0.4f, 0.2f, 0.9f) : new Color(0.3f, 0.8f, 1f, 0.9f);
            if (shape == BrushShape.Custom && polyhedron != null && polyhedron.IsValid)
            {
                var sc = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                foreach (var face in polyhedron.faces)
                    for (int i = 0; i < face.indices.Length; i++)
                        Gizmos.DrawLine(Vector3.Scale(polyhedron.vertices[face.indices[i]], sc), Vector3.Scale(polyhedron.vertices[face.indices[(i + 1) % face.indices.Length]], sc));
                return;
            }
            if (HasParametricSize)
            {
                // Draw the steps and their construction axis at the actual geometry pose.
                Gizmos.matrix = transform.localToWorldMatrix;
                var poly = shape == BrushShape.CurvedStairs ? BrushPolyhedron.CurvedStairs(Stairs) : BrushPolyhedron.SpiralStairs(Stairs);
                var shift = PivotShift;
                foreach (var face in poly.faces)
                    for (int i = 0; i < face.indices.Length; i++)
                        Gizmos.DrawLine(poly.vertices[face.indices[i]] + shift, poly.vertices[face.indices[(i + 1) % face.indices.Length]] + shift);
                var b = poly.Bounds();
                Gizmos.DrawLine(shift + new Vector3(0f, b.min.y, 0f), shift + new Vector3(0f, b.max.y, 0f));
                return;
            }
            Gizmos.DrawWireCube(Vector3.Scale(PivotShift, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))), shown);
        }
    }
}
