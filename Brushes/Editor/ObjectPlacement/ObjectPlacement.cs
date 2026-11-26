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
            Undo.RecordObject(source, Tr(ObjectPlacementTool.Label));
            source.position = destination;
            foreach (var brush in source.GetComponentsInChildren<Brush>(true))
            {
                BrushSnap.Accept(brush);
                BrushSync.NotifyTransformChanged(brush);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(source);
            return true;
        }

        internal static bool ApplySurfacePreview(Transform source, Pose pose)
        {
            // The original pose must still be restorable if the selection or editability changes during the drag.
            if (source == null || !ValidSurfacePose(pose)) return false;
            source.SetPositionAndRotation(pose.position, pose.rotation);
            RefreshSurfaceSource(source);
            return true;
        }

        internal static bool ApplyPositionPreview(Transform source, Vector3 destination)
        {
            if (source == null || !float.IsFinite(destination.sqrMagnitude)) return false;
            source.position = destination;
            RefreshSurfaceSource(source);
            return true;
        }

        internal static Quaternion SurfaceLocalRotation(Transform parent, Quaternion worldRotation)
        {
            if (parent == null) return worldRotation;
            var rotation = Quaternion.Inverse(parent.rotation) * worldRotation;
            var scaleSigns = Vector3.one;
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.parent)
            {
                var scale = ancestor.localScale;
                scaleSigns.x *= Mathf.Sign(scale.x);
                scaleSigns.y *= Mathf.Sign(scale.y);
                scaleSigns.z *= Mathf.Sign(scale.z);
            }
            // Unity converts rotations through each reflected ancestor. Conjugating by the
            // accumulated reflection changes the signs of the quaternion's vector components.
            return new Quaternion(rotation.x * scaleSigns.y * scaleSigns.z,
                rotation.y * scaleSigns.x * scaleSigns.z, rotation.z * scaleSigns.x * scaleSigns.y, rotation.w);
        }

        internal static void RestoreSurfacePreview(Transform source, Vector3 localPosition, Quaternion localRotation)
        {
            if (source == null) return;
            source.SetLocalPositionAndRotation(localPosition, localRotation);
            RefreshSurfaceSource(source);
        }

        static void RefreshSurfaceSource(Transform source)
        {
            BrushSnap.InvalidateParents();
            foreach (var brush in source.GetComponentsInChildren<Brush>(true))
            {
                BrushSnap.Accept(brush);
                BrushSync.NotifyTransformChanged(brush);
                brush.transform.hasChanged = false;
            }
        }

        internal static bool CommitSurface(Transform source, Pose pose)
        {
            if (!CanMove(source) || IsWallWindow(source) || !ValidSurfacePose(pose)) return false;
            if (source.position.Equals(pose.position) && Quaternion.Angle(source.rotation, pose.rotation) < 0.001f) return false;
            Undo.RegisterCompleteObjectUndo(source, Tr(ObjectPlacementTool.Label));
            ApplySurfacePreview(source, pose);
            PrefabUtility.RecordPrefabInstancePropertyModifications(source);
            return true;
        }

        static bool ValidSurfacePose(Pose pose)
        {
            float rotationLengthSquared = Quaternion.Dot(pose.rotation, pose.rotation);
            return float.IsFinite(pose.position.sqrMagnitude) &&
                float.IsFinite(rotationLengthSquared) && rotationLengthSquared > 1e-8f;
        }
    }
}
