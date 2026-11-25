using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Every brush operation as an undoable step. The Inspector, the menus, the Scene view tool and the
    /// tests all go through here, so undo behaves the same everywhere.
    /// </summary>
    public static partial class BrushApi
    {
        /// <summary>
        /// A new brush. With <paramref name="snap"/> it lands on the grid, as one drawn in the Scene view does; without, it
        /// stays exactly where it is put (a level built from measured numbers). Under a moving group it is not static.
        /// </summary>
        public static Brush Create(BrushShape shape, Vector3 position, Vector3 sizeMeters, Quaternion rotation, Transform parent = null, string name = null, bool snap = true)
        {
            var go = new GameObject(name ?? shape.ToString());
            var group = parent != null ? parent.GetComponentInParent<BrushGroup>(true) : null;
            // static unless made otherwise: its meshes and colliders follow it; a moving group's brushes move with it
            GameObjectUtility.SetStaticEditorFlags(go, group != null && group.moving ? 0 : BrushSettings.instance.defaultModelStaticFlags);
            Undo.RegisterCreatedObjectUndo(go, "Создать браш");
            if (parent != null) Undo.SetTransformParent(go.transform, parent, "Создать браш");
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = BrushSnap.CounterScale(go.transform); // world scale one, whatever the parent
            var brush = Undo.AddComponent<Brush>(go);
            brush.shape = shape;
            brush.size = sizeMeters;
            brush.gridSnap = snap ? BrushSettings.instance.newGridSnap : GridSnap.Off;
            if (snap) BrushSnap.SnapOnce(brush); // a new brush lands on the grid; a loaded one is accepted where it is
            if (shape == BrushShape.Stairs || shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs)
            {
                var d = BrushGeometry.ShapeParams.Default(sizeMeters);
                brush.stepHeight = d.stepHeight;
                brush.innerRadius = d.stairs.innerRadius; brush.stepWidth = d.stairs.stepWidth; brush.stepThickness = d.stairs.stepThickness;
                brush.curveAngle = d.stairs.curveAngle; brush.numSteps = d.stairs.numSteps; brush.stepsPer360 = d.stairs.stepsPer360;
            }
            if (brush.IsOpening) brush.operation = BrushOperation.Subtract; // a door or window is a cut
            if (shape == BrushShape.Arch) { var p = BrushCreateTool.ParametersFor(shape, sizeMeters); brush.curveAngle = p.stairs.curveAngle; brush.wallThickness = p.wallThickness; brush.sides = p.sides; }
            foreach (var t in ModuleTypes()) if (BrushSettings.instance.newModules.Contains(t.FullName)) Undo.AddComponent(go, t); // the project's modules for new brushes
            BrushSync.Ensure(brush);
            BrushSync.RequestFullUpdate(brush);
            return brush;
        }

        public static void Delete(Brush brush)
        {
            if (brush == null) return;
            Undo.DestroyObjectImmediate(brush.gameObject);
        }

        public static void SetSize(Brush brush, Vector3 sizeMeters)
        {
            if (BrushSettings.instance.snapToGrid && !brush.IsOpening) sizeMeters = BrushSnap.SnapSize(sizeMeters, BrushSettings.instance.GridMeters);
            if (brush.size == sizeMeters) return; // no-op edits must not create undo entries
            Undo.RecordObject(brush, "Изменить размер браша");
            brush.size = sizeMeters;
            BrushSync.Ensure(brush);
        }

        public static void SetShape(Brush brush, BrushShape shape)
        {
            if (brush.shape == shape) return;
            if (shape == BrushShape.Custom) { ConvertToCustom(brush); return; }
            Undo.RecordObject(brush, "Изменить форму браша");
            brush.shapeOriginOffsetMeters = Vector3.zero;
            brush.shape = shape;
            PrefabUtility.RecordPrefabInstancePropertyModifications(brush);
            BrushSync.Ensure(brush);
        }

        public static void SetOperation(Brush brush, BrushOperation operation)
        {
            if (brush.operation == operation) return;
            Undo.RecordObject(brush, "Изменить операцию браша");
            BrushSync.RequestFullUpdate(brush);
            brush.operation = operation;
            BrushSync.Ensure(brush);
        }

        /// <summary>A brush's static flags (lightmaps, occlusion, batching), which its render triangles and colliders take; undoable.</summary>
        public static void SetStaticFlags(Brush brush, StaticEditorFlags flags)
        {
            if (GameObjectUtility.GetStaticEditorFlags(brush.gameObject) == flags) return;
            Undo.RecordObject(brush.gameObject, "Изменить флаги статичности");
            GameObjectUtility.SetStaticEditorFlags(brush.gameObject, flags);
            BrushCache.Forget(brush);
            BrushCsg.MarkDirty(brush);
        }

        public static void SetCollision(Brush brush, Colliders.ColliderKind kind)
        {
            if (brush.collision == kind) return;
            Undo.RecordObject(brush, "Изменить столкновения браша");
            brush.collision = kind;
            BrushSync.Ensure(brush);
        }

        /// <summary>Add a module (a game's data) to a brush, or return the one it has; undoable.</summary>
        public static BrushModule AddModule(Brush brush, System.Type type)
        {
            if (brush == null || type == null || !typeof(BrushModule).IsAssignableFrom(type) || type.IsAbstract) return null;
            if (brush.GetComponent(type) is BrushModule existing) return existing;
            var module = Undo.AddComponent(brush.gameObject, type) as BrushModule;
            BrushSync.Ensure(brush);
            return module;
        }

        public static T AddModule<T>(Brush brush) where T : BrushModule => (T)AddModule(brush, typeof(T));

        /// <summary>The module types the project offers (every concrete BrushModule in the loaded assemblies).</summary>
        public static List<System.Type> ModuleTypes()
        {
            var list = new List<System.Type>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<BrushModule>()) if (!t.IsAbstract) list.Add(t);
            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return list;
        }

        public static void SetHollow(Brush brush, bool hollow, float wallThicknessMeters)
        {
            if (brush.hollow == hollow && Mathf.Approximately(brush.wallThickness, wallThicknessMeters)) return;
            Undo.RecordObject(brush, "Изменить полость браша");
            brush.hollow = hollow;
            brush.wallThickness = wallThicknessMeters;
            BrushSync.Ensure(brush);
        }

        public static void SetMaterial(Brush brush, Material material)
        {
            if (brush.material == material) return;
            Undo.RecordObject(brush, "Изменить материал браша");
            brush.material = material;
            BrushSync.Ensure(brush);
        }

        /// <summary>Subtract brushes: which material the faces they cut take.</summary>
        public static void SetCutFaces(Brush brush, CutFaces cutFaces)
        {
            if (brush.cutFaces == cutFaces) return;
            Undo.RecordObject(brush, "Изменить материал граней среза");
            brush.cutFaces = cutFaces;
            BrushSync.Ensure(brush);
        }

        public static void SetStairs(Brush brush, float stepHeightMeters)
        {
            Undo.RecordObject(brush, "Изменить лестницу");
            brush.stepHeight = Mathf.Max(BrushPolyhedron.MinStepHeight, stepHeightMeters);
            BrushSync.Ensure(brush);
        }

        public static void Move(Brush brush, Vector3 position)
        {
            if (brush.transform.position == position) return;
            Undo.RecordObject(brush.transform, "Переместить браш");
            brush.transform.position = position;
            BrushSync.NotifyTransformChanged(brush);
        }

        public static void Rotate(Brush brush, Quaternion rotation)
        {
            if (brush.transform.rotation == rotation) return;
            Undo.RecordObject(brush.transform, "Повернуть браш");
            brush.transform.rotation = rotation;
            BrushSync.NotifyTransformChanged(brush);
        }

        public static void ToFirst(Brush brush)
        {
            if (brush.transform.GetSiblingIndex() == 0) return;
            Undo.SetSiblingIndex(brush.transform, 0, "Переместить браш в начало");
            BrushSync.RequestFullUpdate(brush);
        }

        public static void ToLast(Brush brush)
        {
            var t = brush.transform;
            int last = t.parent != null ? t.parent.childCount - 1 : t.gameObject.scene.rootCount - 1;
            if (t.GetSiblingIndex() == last) return;
            Undo.SetSiblingIndex(t, last, "Переместить браш в конец");
            BrushSync.RequestFullUpdate(brush);
        }

        /// <summary>The next sibling up (-1) or down (1) that the Hierarchy shows, or null at the end; hidden generated objects are skipped.</summary>
        public static Transform VisibleNeighbour(Brush brush, int direction)
        {
            var t = brush.transform;
            var roots = t.parent == null ? t.gameObject.scene.GetRootGameObjects() : null;
            int count = t.parent != null ? t.parent.childCount : roots.Length;
            for (int i = t.GetSiblingIndex() + direction; i >= 0 && i < count; i += direction)
            {
                var sibling = t.parent != null ? t.parent.GetChild(i) : roots[i].transform;
                if ((sibling.gameObject.hideFlags & HideFlags.HideInHierarchy) == 0) return sibling;
            }
            return null;
        }

        /// <summary>One step up (-1) or down (1), past the next sibling the Hierarchy shows.</summary>
        public static void Step(Brush brush, int direction)
        {
            var neighbour = VisibleNeighbour(brush, direction);
            if (neighbour == null) return;
            Undo.SetSiblingIndex(brush.transform, neighbour.GetSiblingIndex(), direction < 0 ? "Переместить браш выше" : "Переместить браш ниже");
            BrushSync.RequestFullUpdate(brush);
        }

        // ------------------------------------------------------------------ custom shapes

        /// <summary>The polyhedron of a parametric shape.</summary>
        public static BrushPolyhedron PolyhedronFor(BrushShape shape, Vector3 size, int sides)
        {
            return BrushGeometry.ShapePolyhedron(shape, BrushGeometry.ShapeParams.Default(size, sides));
        }

        /// <summary>Every parametric shape has a polyhedron now, so every shape can be edited by hand.</summary>
        public static bool CanConvertToCustom(BrushShape shape) => shape != BrushShape.Custom;

        /// <summary>Turn a parametric brush into an editable Custom shape (the first face or vertex edit does this implicitly).</summary>
        public static bool ConvertToCustom(Brush brush)
        {
            if (brush.shape == BrushShape.Custom) return true;
            if (!CanConvertToCustom(brush.shape)) return false;
            Undo.RecordObject(brush, "Изменить форму браша");
            brush.customFrom = brush.shape;
            brush.polyhedron = BrushGeometry.Polyhedron(brush).Clone();
            brush.hollow = false;
            brush.shape = BrushShape.Custom;
            PrefabUtility.RecordPrefabInstancePropertyModifications(brush);
            BrushSync.Ensure(brush);
            return true;
        }

        /// <summary>Back to the parametric shape the Custom brush came from; the hand edits are discarded (undoable).</summary>
        public static void ResetShape(Brush brush)
        {
            if (brush.shape != BrushShape.Custom) return;
            Undo.RecordObject(brush, "Сбросить форму браша");
            var custom = PivotOf(brush);
            brush.size = brush.polyhedron != null && brush.polyhedron.IsValid ? brush.polyhedron.Bounds().size : brush.size;
            if (custom.HasValue) brush.pivot = custom.Value; // the shape stays where it was
            brush.shape = brush.customFrom;
            brush.polyhedron = new BrushPolyhedron();
            PrefabUtility.RecordPrefabInstancePropertyModifications(brush);
            BrushSync.Ensure(brush);
        }

        /// <summary>
        /// Where the transform sits in the brush's box, in its <see cref="Brush.pivotMode"/> (a fraction of the size, or
        /// metres from the left, bottom, back corner); a Custom shape's comes from its vertices. Curved and spiral stairs,
        /// doors and windows have no numeric pivot field and use the vertex picker.
        /// </summary>
        public static Vector3? PivotOf(Brush brush)
        {
            if (!PivotBox(brush, out var size, out var distance)) return null;
            return brush.pivotMode == PivotMode.Normalized ? new Vector3(Fraction(distance.x, size.x), Fraction(distance.y, size.y), Fraction(distance.z, size.z)) : distance;
        }

        /// <summary>The brush's box size and its pivot in metres from the box's left, bottom, back corner.</summary>
        static bool PivotBox(Brush brush, out Vector3 size, out Vector3 distance)
        {
            if (brush.HasPivot) { size = brush.ClampedSize; distance = brush.PivotDistance(size); return true; }
            size = distance = default;
            if (brush.shape != BrushShape.Custom || brush.polyhedron == null || !brush.polyhedron.IsValid) return false;
            var b = brush.polyhedron.Bounds();
            size = b.size; distance = -b.min;
            return true;
        }

        static float Fraction(float distance, float size) => size > 1e-6f ? Mathf.Clamp01(distance / size) : 0.5f;

        internal static bool CanPickPivot(Brush brush) => brush != null && !brush.IsGenerated &&
            !brush.anchored && (brush.UsesShapeOriginOffset || PivotBox(brush, out _, out _));

        /// <summary>Place the transform on an authored vertex without moving the solid or its children.</summary>
        public static bool SetPivotToVertex(Brush brush, int vertexIndex)
        {
            if (!CanPickPivot(brush)) return false;
            var vertices = BrushGeometry.Polyhedron(brush).vertices;
            if (vertexIndex < 0 || vertexIndex >= vertices.Length) return false;
            SetPivotAtLocalPoint(brush, vertices[vertexIndex]);
            if (brush.gridSnap == GridSnap.Shape)
            {
                Undo.RecordObject(brush, "Привязать гизмо к вершине");
                brush.gridSnap = GridSnap.Pivot;
                PrefabUtility.RecordPrefabInstancePropertyModifications(brush);
            }
            return true;
        }

        /// <summary>Place the pivot at the centre of the authored geometry's local bounds.</summary>
        public static void ResetPivot(Brush brush)
        {
            if (CanPickPivot(brush)) SetPivotAtLocalPoint(brush, BrushGeometry.Polyhedron(brush).Bounds().center);
        }

        static void SetPivotAtLocalPoint(Brush brush, Vector3 localPoint)
        {
            if (brush.UsesShapeOriginOffset)
            {
                if (localPoint.sqrMagnitude >= 1e-12f) MovePivot(brush, brush.pivot, localPoint);
                return;
            }
            PivotBox(brush, out var size, out var current);
            var d = current + localPoint;
            SetPivot(brush, brush.pivotMode == PivotMode.Absolute ? d :
                new Vector3(Fraction(d.x, size.x), Fraction(d.y, size.y), Fraction(d.z, size.z)));
        }

        /// <summary>
        /// Move the brush's pivot (in its <see cref="Brush.pivotMode"/>) and its transform with it, so the shape stays where
        /// it is; on a Custom shape the vertices move instead. Children stay where they are too. Undoable.
        /// </summary>
        public static void SetPivot(Brush brush, Vector3 pivot)
        {
            if (!PivotBox(brush, out var size, out var current)) return;
            bool relative = brush.pivotMode == PivotMode.Normalized;
            if (relative) pivot = new Vector3(Mathf.Clamp01(pivot.x), Mathf.Clamp01(pivot.y), Mathf.Clamp01(pivot.z));
            else pivot = new Vector3(Mathf.Clamp(pivot.x, 0f, size.x), Mathf.Clamp(pivot.y, 0f, size.y), Mathf.Clamp(pivot.z, 0f, size.z));
            var target = relative ? Vector3.Scale(pivot, size) : pivot;
            var move = target - current; // the new pivot in the old local space, from the old one
            if (move.sqrMagnitude < 1e-12f && (!brush.HasPivot || (brush.pivot - pivot).sqrMagnitude < 1e-12f)) return; // no-op edits must not create undo entries
            MovePivot(brush, pivot, move);
        }

        static void MovePivot(Brush brush, Vector3 pivot, Vector3 move)
        {
            var t = brush.transform;
            Undo.RecordObject(brush, "Задать точку опоры браша");
            Undo.RecordObject(t, "Задать точку опоры браша");
            var children = new List<(Transform child, Vector3 position, Quaternion rotation)>();
            foreach (Transform c in t)
                if (!Brush.IsGeneratedChildName(c.name)) { Undo.RecordObject(c, "Задать точку опоры браша"); children.Add((c, c.position, c.rotation)); }
            var world = t.TransformPoint(move);
            if (brush.HasPivot) brush.pivot = pivot;
            else if (brush.UsesShapeOriginOffset) brush.shapeOriginOffsetMeters -= move;
            else
            {
                var poly = brush.polyhedron.Clone();
                for (int i = 0; i < poly.vertices.Length; i++) poly.vertices[i] -= move;
                brush.polyhedron = poly;
                if (Brush.UsesShapeOriginOffsetFor(brush.customFrom)) brush.shapeOriginOffsetMeters -= move;
            }
            t.position = world;
            BrushSnap.Accept(brush); // the transform moved, the shape did not: nothing to snap
            foreach (var (c, position, rotation) in children)
            {
                c.SetPositionAndRotation(position, rotation);
                PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(brush);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            BrushSync.NotifyTransformChanged(brush);
            BrushSync.Ensure(brush);
        }

        /// <summary>Measure the pivot as a fraction of the size or as a distance; the pivot itself stays where it is. Undoable.</summary>
        public static void SetPivotMode(Brush brush, PivotMode mode)
        {
            if (brush.pivotMode == mode) return;
            Undo.RecordObject(brush, "Задать режим точки опоры браша");
            bool has = PivotBox(brush, out var size, out var distance) && brush.HasPivot;
            brush.pivotMode = mode;
            if (has) brush.pivot = mode == PivotMode.Normalized ? new Vector3(Fraction(distance.x, size.x), Fraction(distance.y, size.y), Fraction(distance.z, size.z)) : distance;
            BrushSync.Ensure(brush);
        }

        /// <summary>Replace the whole editable shape (tools commit their result through this).</summary>
        public static void SetPolyhedron(Brush brush, BrushPolyhedron polyhedron)
        {
            if (polyhedron == null || !polyhedron.IsValid) return;
            Undo.RecordObject(brush, "Изменить форму браша");
            if (brush.shape != BrushShape.Custom) { brush.customFrom = brush.shape; brush.hollow = false; brush.shape = BrushShape.Custom; }
            brush.polyhedron = polyhedron;
            BrushSync.Ensure(brush);
        }

        /// <summary>Push (positive) or pull a face along its normal; the distance is rounded to the grid when snapping is on.</summary>
        public static void PushFace(Brush brush, int face, float distanceMeters)
        {
            if (!ConvertToCustom(brush)) return;
            if (BrushSettings.instance.snapToGrid) distanceMeters = BrushSnap.Round(distanceMeters, BrushSettings.instance.GridMeters);
            if (Mathf.Abs(distanceMeters) < 1e-6f) return;
            Undo.RecordObject(brush, "Сдвинуть грань");
            brush.polyhedron.PushFace(face, distanceMeters);
            BrushSync.Ensure(brush);
        }

        /// <summary>
        /// Extrude the selected faces by a distance (rounded to the grid when snapping is on), each on its own or the
        /// whole selection as one. Runs as a boolean, so it works whatever the block passes through. Returns per
        /// original face index its index afterwards (-1 when gone), or null when nothing was done.
        /// </summary>
        public static int[] ExtrudeFaces(Brush brush, IEnumerable<int> faces, float distanceMeters, bool individual)
        {
            if (!ConvertToCustom(brush)) return null;
            if (BrushSettings.instance.snapToGrid) distanceMeters = BrushSnap.Round(distanceMeters, BrushSettings.instance.GridMeters);
            if (Mathf.Abs(distanceMeters) < 1e-6f) return null;
            var result = BrushBoolean.ExtrudeFaces(brush.polyhedron, faces, distanceMeters, individual, out var remap);
            if (result == null || !result.IsValid) return null;
            Undo.RecordObject(brush, "Вытянуть грани");
            brush.polyhedron = result;
            BrushSync.Ensure(brush);
            return remap;
        }

        /// <summary>
        /// Bridge two faces of a brush: the volume between them is added, the faces vanish into the join and the
        /// walls become new faces. Returns per original face index its index afterwards (-1 when gone), or null
        /// when nothing was done (<see cref="BrushBoolean.LastRefusal"/> says why).
        /// </summary>
        public static int[] BridgeFaces(Brush brush, int faceA, int faceB)
        {
            if (!ConvertToCustom(brush)) return null;
            var result = BrushBoolean.BridgeFaces(brush.polyhedron, faceA, faceB, out var remap);
            if (result == null || !result.IsValid) return null;
            Undo.RecordObject(brush, "Соединить грани");
            brush.polyhedron = result;
            BrushSync.Ensure(brush);
            return remap;
        }

        /// <summary>Move a vertex to a world position (snapped to the world grid when snapping is on); bent faces split into triangles.</summary>
        public static void MoveVertex(Brush brush, int vertex, Vector3 worldPosition)
        {
            if (!ConvertToCustom(brush)) return;
            if (BrushSettings.instance.snapToGrid) worldPosition = BrushSnap.Round(worldPosition, BrushSettings.instance.GridMeters);
            var local = brush.transform.InverseTransformPoint(worldPosition);
            if ((brush.polyhedron.vertices[vertex] - local).sqrMagnitude < 1e-10f) return;
            Undo.RecordObject(brush, "Переместить вершину");
            brush.polyhedron.MoveVertex(vertex, local);
            BrushSync.Ensure(brush);
        }

        /// <summary>Apply a non-uniform transform scale to the size and reset the scale to one.</summary>
        public static void ApplyScale(Brush brush)
        {
            var t = brush.transform;
            Undo.RecordObject(brush, "Применить масштаб к размеру");
            Undo.RecordObject(t, "Применить масштаб к размеру");
            var s = t.localScale;
            brush.size = new Vector3(brush.size.x * Mathf.Abs(s.x), brush.size.y * Mathf.Abs(s.y), brush.size.z * Mathf.Abs(s.z));
            t.localScale = Vector3.one;
            BrushSync.NotifyTransformChanged(brush);
            BrushSync.Ensure(brush);
        }

        /// <summary>
        /// Rebuild every model and its convex colliders now, instead of waiting for the editor loop. Used by tests, menus
        /// and scripts; returns what was built.
        /// </summary>
        public static RebuildReport ForceUpdate()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BrushSnap.ProcessChanged(); // what the editor loop does every frame
            BrushHooks.Process();
            BrushCsg.RebuildAllNow();
            Colliders.Editor.ConvexColliderBuilder.FlushDeferred();
            BrushSync.ApplyVisibility();
            return RebuildReport.OfLoadedScenes(sw.Elapsed.TotalMilliseconds);
        }
    }
}
