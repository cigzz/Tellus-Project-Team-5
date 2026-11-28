using UnityEditor;
using UnityEngine;
using WorldSim;

// Draws a field marked [InspectorLabel] with that text instead of its code name, keeping its tooltip.
[CustomPropertyDrawer(typeof(InspectorLabelAttribute))]
public class InspectorLabelDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var a = (InspectorLabelAttribute)attribute;
        using (new EditorGUI.DisabledScope(a.ReadOnly))
            EditorGUI.PropertyField(position, property, new GUIContent(a.Text, label.tooltip), true);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
        EditorGUI.GetPropertyHeight(property, label, true);
}
