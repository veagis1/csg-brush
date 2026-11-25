using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Editor
{
    [InitializeOnLoad]
    public static class BrushMenu
    {
        const string kMenu = "GameObject/CSG Brush/";

        static BrushMenu() => ToolManager.activeToolChanged += OnToolChanged;

        internal static int ToolVersion { get; private set; }

        static void OnToolChanged()
        {
            ToolVersion++;
            var type = ToolManager.activeToolType;
            if (type == typeof(CreateFloorPlanTool) || type == typeof(CreateSplineTool) ||
                type != null && typeof(BrushCreateTool).IsAssignableFrom(type))
                BrushShapeHandles.SuspendForCreation();
        }

        // Unity 6000.6 exposes setters for the Hierarchy default parent but keeps its stage-aware getter internal.
        static readonly Func<Transform> GetDefaultParent = (Func<Transform>)typeof(SceneView)
            .GetMethod("GetDefaultParentObjectIfSet", BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
            ?.CreateDelegate(typeof(Func<Transform>));

        internal static Transform CreationParent(bool useSelection = false)
        {
            var parent = GetDefaultParent?.Invoke();
            if (parent != null) return parent;

            var selected = Selection.activeTransform;
            if (useSelection && selected != null && !selected.TryGetComponent<Brush>(out _) &&
                StageUtility.GetCurrentStageHandle().Contains(selected.gameObject))
                return selected;

            return PrefabStageUtility.GetCurrentPrefabStage()?.prefabContentsRoot.transform;
        }

        static Vector3 Snap(Vector3 p, float g)
        {
            if (g <= 0f) return p;
            return new Vector3(Mathf.Round(p.x / g) * g, Mathf.Round(p.y / g) * g, Mathf.Round(p.z / g) * g);
        }

        internal static void ActivateCreation<T>() where T : EditorTool
        {
            if (ToolManager.activeToolType != typeof(T)) ToolManager.SetActiveTool<T>();
            else
            {
                ToolVersion++;
                PrepareCreation();
                BrushShapeHandles.SuspendForCreation();
                BrushCreateTool.Active?.ResetGesture();
            }
        }

        internal static void PrepareCreation()
        {
            BrushShapeHandles.CancelGesture();
            BrushSurfaceGrid.CancelPicking();
            if (ToolManager.activeContextType != typeof(GameObjectToolContext))
                ToolManager.SetActiveContext<GameObjectToolContext>();
        }

        internal static void PrepareObjectCreation()
        {
            PrepareCreation();
            if (Tools.current == Tool.Custom) ToolManager.RestorePreviousPersistentTool();
            ToolVersion++;
            BrushShapeHandles.SuspendForCreation();
        }

        static Brush CreateAtPivot(BrushShape shape)
        {
            PrepareObjectCreation();
            var s = BrushSettings.instance;
            var size = s.defaultBoxSize;
            Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            pivot = Snap(pivot, s.GridMeters);
            var brush = BrushApi.Create(shape, pivot, size, Quaternion.identity, CreationParent(useSelection: true));
            Selection.activeGameObject = brush.gameObject;
            return brush;
        }

        [MenuItem(kMenu + "Блок", false, 10)] static void Box() => CreateAtPivot(BrushShape.Box);
        [MenuItem(kMenu + "Клин (пандус)", false, 11)] static void Wedge() => CreateAtPivot(BrushShape.Wedge);
        [MenuItem(kMenu + "Цилиндр", false, 12)] static void Cylinder() => CreateAtPivot(BrushShape.Cylinder);
        [MenuItem(kMenu + "Конус", false, 13)] static void Cone() => CreateAtPivot(BrushShape.Cone);
        [MenuItem(kMenu + "Сфера", false, 14)] static void Sphere() => CreateAtPivot(BrushShape.Sphere);
        [MenuItem(kMenu + "Прямая лестница", false, 15)] static void Stairs() => CreateAtPivot(BrushShape.Stairs);
        [MenuItem(kMenu + "Изогнутая лестница", false, 16)] static void CurvedStairs() => CreateAtPivot(BrushShape.CurvedStairs);
        [MenuItem(kMenu + "Винтовая лестница", false, 17)] static void SpiralStairs() => CreateAtPivot(BrushShape.SpiralStairs);
        [MenuItem(kMenu + "Арка", false, 18)] static void Arch() => CreateAtPivot(BrushShape.Arch);
        [MenuItem(kMenu + "Дверь", false, 19)] static void Door() => CreateOpening(BrushShape.Door);
        [MenuItem(kMenu + "Окно", false, 20)] static void Window() => CreateOpening(BrushShape.Window);

        /// <summary>A door or window: on the middle of the first wall of a selected floor plan, else standing at the view pivot.</summary>
        static void CreateOpening(BrushShape shape)
        {
            PrepareObjectCreation();
            var plan = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<FloorPlan>() : null;
            var walls = new System.Collections.Generic.List<FloorPlan.Wall>();
            if (plan != null) plan.Walls(walls);
            Brush brush;
            if (walls.Count > 0) brush = WallAnchors.Place(plan, shape, 0, walls[0].Length * 0.5f);
            else
            {
                var s = BrushSettings.instance;
                var opening = s.NewOpeningSize(shape);
                Vector3 pivot = Snap(SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero, s.GridMeters);
                brush = BrushApi.Create(shape, pivot + Vector3.up * (s.NewOpeningSill(shape) + opening.y * 0.5f), new Vector3(opening.x, opening.y, BrushSettings.DefaultOpeningDepthMeters), Quaternion.identity, CreationParent());
            }
            Selection.activeGameObject = brush.gameObject;
        }

        [MenuItem(kMenu + "Полая комната", false, 30)]
        static void Room()
        {
            var brush = CreateAtPivot(BrushShape.Box);
            var s = BrushSettings.instance;
            BrushApi.SetSize(brush, new Vector3(s.defaultBoxSize.x * 4f, s.defaultBoxSize.y * 2f, s.defaultBoxSize.z * 4f));
            BrushApi.SetHollow(brush, true, s.GridMeters);
            brush.name = "Room";
        }

        // order: right-click a brush in the Hierarchy (GameObject menu) or in the Scene view / Inspector (component context)
        static Brush[] SelectedBrushes() => Selection.GetFiltered<Brush>(SelectionMode.Editable | SelectionMode.ExcludePrefab);

        [MenuItem(kMenu + "В начало", false, 50)] static void ToFirst() { foreach (var b in SelectedBrushes()) BrushApi.ToFirst(b); }
        [MenuItem(kMenu + "В начало", true)] static bool CanToFirst() => SelectedBrushes().Length > 0;
        [MenuItem(kMenu + "В конец", false, 51)] static void ToLast() { foreach (var b in SelectedBrushes()) BrushApi.ToLast(b); }
        [MenuItem(kMenu + "В конец", true)] static bool CanToLast() => SelectedBrushes().Length > 0;
        [MenuItem(kMenu + "Переместить выше", false, 52)] static void Up() => StepSelected(-1);
        [MenuItem(kMenu + "Переместить выше", true)] static bool CanUp() => SelectedBrushes().Length > 0;
        [MenuItem(kMenu + "Переместить ниже", false, 53)] static void Down() => StepSelected(1);
        [MenuItem(kMenu + "Переместить ниже", true)] static bool CanDown() => SelectedBrushes().Length > 0;

        /// <summary>Step every selected brush, the one furthest in the direction first; one that cannot move holds back those behind it, so the selection keeps its order.</summary>
        static void StepSelected(int direction)
        {
            var brushes = new System.Collections.Generic.List<Brush>(SelectedBrushes());
            brushes.Sort((a, b) => direction * (b.transform.GetSiblingIndex() - a.transform.GetSiblingIndex()));
            var blocked = new System.Collections.Generic.HashSet<Transform>();
            foreach (var b in brushes)
            {
                var neighbour = BrushApi.VisibleNeighbour(b, direction);
                if (neighbour == null || blocked.Contains(neighbour)) { blocked.Add(b.transform); continue; }
                BrushApi.Step(b, direction);
            }
        }

        [MenuItem("CONTEXT/Brush/В начало")] static void ContextToFirst(MenuCommand c) => BrushApi.ToFirst((Brush)c.context);
        [MenuItem("CONTEXT/Brush/В конец")] static void ContextToLast(MenuCommand c) => BrushApi.ToLast((Brush)c.context);
        [MenuItem("CONTEXT/Brush/Переместить выше")] static void ContextUp(MenuCommand c) => BrushApi.Step((Brush)c.context, -1);
        [MenuItem("CONTEXT/Brush/Переместить ниже")] static void ContextDown(MenuCommand c) => BrushApi.Step((Brush)c.context, 1);

        [MenuItem("CSG Brush/Перестроить сейчас")]
        static void Rebuild() => BrushApi.ForceUpdate();
    }
}
