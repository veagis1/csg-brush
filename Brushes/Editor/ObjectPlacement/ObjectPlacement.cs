using static CsgBrush.Editor.BrushLanguage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Editor
{
    internal static class ObjectPlacement
    {
        internal static bool CanMove(Transform source)
        {
            if (!IsSelectableGeometry(source) || EditorApplication.isPlayingOrWillChangePlaymode ||
                (source.hideFlags & HideFlags.NotEditable) != 0 ||
                (source.gameObject.hideFlags & HideFlags.NotEditable) != 0) return false;
            if (source.TryGetComponent<SplineAnchor>(out _)) return false;
            if (source.TryGetComponent<WallAnchor>(out _)) return IsWallWindow(source);
            if (source.TryGetComponent<Brush>(out var brush) && (brush.IsGenerated || brush.anchored)) return false;
            return true;
        }

        internal static bool IsWallWindow(Transform source) => source != null &&
            source.TryGetComponent<Brush>(out var brush) && WallAnchors.CanEditWindowPivot(brush);

        internal static bool CanTarget(Transform source, Transform candidate)
        {
            if (source == null || !IsSelectableGeometry(candidate) || candidate.IsChildOf(source)) return false;
            if (!IsWallWindow(source)) return true;
            if (!candidate.TryGetComponent<Brush>(out var brush) || !brush.isActiveAndEnabled) return false;
            var target = BrushCutTarget.FromBrush(brush);
            var plan = target.Plan;
            if (plan == null || !target.Valid || !IsSelectableGeometry(plan.transform) || plan.gameObject.scene != source.gameObject.scene) return false;
            if (source.parent == plan.transform) return true;
            if ((plan.hideFlags & HideFlags.NotEditable) != 0 ||
                (plan.gameObject.hideFlags & HideFlags.NotEditable) != 0) return false;
            return !PrefabUtility.IsPartOfPrefabInstance(source) || PrefabUtility.IsAddedGameObjectOverride(source.gameObject);
        }

        internal static bool TryWindowPlacement(Transform source, Component candidate, Vector3 destination, out WallAnchors.Placement placement)
        {
            placement = default;
            return IsWallWindow(source) && candidate is Brush wallBrush && CanTarget(source, wallBrush.transform) &&
                WallAnchors.TryWindowPlacement(source.GetComponent<Brush>(), BrushCutTarget.FromBrush(wallBrush), destination, out placement);
        }

        internal static bool IsSelectableGeometry(Transform item)
        {
            if (item == null) return false;
            var go = item.gameObject;
            if (!go.activeInHierarchy || !go.scene.IsValid() || !StageUtility.GetCurrentStageHandle().Contains(go)) return false;
            var visibility = SceneVisibilityManager.instance;
            if (visibility.IsHidden(go) || visibility.IsPickingDisabled(go)) return false;
            for (var parent = item; parent != null; parent = parent.parent)
                if (BrushSync.IsGenerated(parent) || Brush.IsGeneratedChildName(parent.name)) return false;
            return true;
        }

        // Grid intervals start at the selected feature. Keep the last whole interval inside either endpoint.
        internal static Vector3 PointOnEdge(Vector3 a, Vector3 b, float originFraction, float cursorFraction, float stepMeters)
        {
            float origin = Mathf.Clamp01(originFraction), at = Mathf.Clamp01(cursorFraction);
            float length = Vector3.Distance(a, b);
            if (length <= 1e-7f || stepMeters <= 0f) return Vector3.Lerp(a, b, at);
            float steps = length / stepMeters;
            float count = Mathf.Clamp(Mathf.Round((at - origin) * steps),
                -Mathf.Floor(origin * steps + 1e-5f), Mathf.Floor((1f - origin) * steps + 1e-5f));
            return Vector3.Lerp(a, b, origin + count / steps);
        }

        internal static bool Commit(Transform source, Vector3 destination, Component candidate = null)
        {
            if (!CanMove(source) || !float.IsFinite(destination.sqrMagnitude)) return false;
            if (IsWallWindow(source))
                return TryWindowPlacement(source, candidate, destination, out var placement) &&
                    WallAnchors.MoveWindow(source.GetComponent<Brush>(), placement);
            if (source.position == destination) return false;
            Undo.RecordObject(source, Tr("Переместить объект к вершине или ребру"));
            source.position = destination;
            foreach (var brush in source.GetComponentsInChildren<Brush>(true))
            {
                BrushSnap.Accept(brush);
                BrushSync.NotifyTransformChanged(brush);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(source);
            return true;
        }
    }
}
