using UnityEditor;
using UnityEngine;

namespace SkyMavis.AxieMixer3D.Editor
{
    [CustomPropertyDrawer(typeof(LayerFieldAttribute))]
    public class LayerFieldAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.Integer)
            {
                using (new EditorGUI.PropertyScope(position, label, property))
                {
                    property.intValue = EditorGUI.LayerField(position, label, property.intValue);
                }
            }
            else
            {
                EditorGUI.PropertyField(position, property, label);
            }
        }
    }
}
