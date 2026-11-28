using UnityEngine;

namespace WorldSim
{
    // Shows a field under a readable name in the Inspector (drawn by Editor/InspectorLabelDrawer.cs); [Tooltip] still works.
    // ReadOnly greys the field out, for values the game writes and you only watch.
    public class InspectorLabelAttribute : PropertyAttribute
    {
        public readonly string Text;
        public bool ReadOnly { get; set; }

        public InspectorLabelAttribute(string text) { Text = text; }
    }
}
