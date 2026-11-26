using UnityEngine;

namespace CsgBrush.Editor
{
    internal sealed partial class PlacementScene
    {
        internal Pose SurfacePose(Vector3 contact, Vector3 normal, Quaternion initialRotation)
        {
            var up = normal.normalized;
            var forward = Vector3.ProjectOnPlane(initialRotation * Vector3.forward, up);
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.Cross(initialRotation * Vector3.right, up);
            var rotation = Quaternion.LookRotation(forward, up);
            var basis = SurfaceMatrix(new Pose(Vector3.zero, rotation));

            float lowest = float.PositiveInfinity;
            foreach (var part in moving)
            {
                if (part.Owner == null || !part.Owner.transform.IsChildOf(source)) continue;
                var geometry = Read(part);
                if (geometry == null) continue;
                var matrix = basis * PartToSource(part.Owner.transform);
                foreach (var vertex in geometry.Vertices)
                    lowest = Mathf.Min(lowest, Vector3.Dot(up, matrix.MultiplyPoint3x4(vertex)));
            }
            return new Pose(float.IsFinite(lowest) ? contact - up * lowest : contact, rotation);
        }

        internal void DrawSurfacePreview(Pose pose, Color color) => DrawMovingWire(SurfaceMatrix(pose), color);

        Matrix4x4 SurfaceMatrix(Pose pose)
        {
            var parent = source.parent;
            var localRotation = ObjectPlacement.SurfaceLocalRotation(parent, pose.rotation);
            var matrix = Matrix4x4.TRS(Vector3.zero, localRotation, source.localScale);
            if (parent != null) matrix = parent.localToWorldMatrix * matrix;
            matrix.SetColumn(3, new Vector4(pose.position.x, pose.position.y, pose.position.z, 1f));
            return matrix;
        }
    }
}
