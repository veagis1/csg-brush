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
            if (source.TryGetComponent<Brush>(out var brush) && (brush.IsGenerated || brush.anchored)) return false;
            return !source.TryGetComponent<WallAnchor>(out _) && !source.TryGetComponent<SplineAnchor>(out _);
        }

        internal static bool CanTarget(Transform source, Transform candidate) =>
            source != null && IsSelectableGeometry(candidate) && !candidate.IsChildOf(source);

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

        internal static bool Commit(Transform source, Vector3 destination)
        {
            if (!CanMove(source) || !float.IsFinite(destination.sqrMagnitude) ||
                source.position == destination) return false;
            Undo.RecordObject(source, "Привязать объект");
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
