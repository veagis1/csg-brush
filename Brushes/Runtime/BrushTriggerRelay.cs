using UnityEngine;

namespace CsgBrush
{
    /// <summary>Forwards events from a generated collider piece to its owning brush.</summary>
    [AddComponentMenu("")]
    public sealed class BrushTriggerRelay : MonoBehaviour
    {
        public Brush brush;
        void OnTriggerEnter(Collider other) { if (brush != null) brush.PieceTriggerEnter(other); }
        void OnTriggerExit(Collider other) { if (brush != null) brush.PieceTriggerExit(other); }
        void OnTriggerStay(Collider other) { if (brush != null) brush.PieceTriggerStay(other); }
    }
}
