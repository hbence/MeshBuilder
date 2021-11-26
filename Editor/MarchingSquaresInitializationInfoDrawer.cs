using UnityEngine;
using UnityEditor;

using static MeshBuilder.MarchingSquaresComponent.InitializationInfo;

namespace MeshBuilder
{
    [CustomPropertyDrawer(typeof(MarchingSquaresComponent.InitializationInfo))]
    public class MarchingSquaresInitializationInfoDrawer : PropertyDrawer
    {
        private class PropName
        {
            public const string Type = "type";
            public const string CellSize = "cellSize";
            public const string Height = "height";
            public const string LerpToExactEdge = "lerpToExactEdge";
            public const string TaperedTopOffset = "taperedTopOffset";
            public const string TaperedBottomOffset = "taperedBottomOffset";
            public const string SegmentedOffsets = "segmentedOffsets";
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel += 1;

            var typeProp = property.FindPropertyRelative(PropName.Type);
            Type type = (Type)typeProp.enumValueIndex;

            EditorGUILayout.PropertyField(typeProp);
            EditorGUILayout.PropertyField(property.FindPropertyRelative(PropName.CellSize));
            EditorGUILayout.PropertyField(property.FindPropertyRelative(PropName.Height));
            EditorGUILayout.PropertyField(property.FindPropertyRelative(PropName.LerpToExactEdge));

            if (type == Type.TaperedSideOnly || type == Type.TaperedNoBottomFullCell)
            {
                EditorGUILayout.PropertyField(property.FindPropertyRelative(PropName.TaperedTopOffset));
                EditorGUILayout.PropertyField(property.FindPropertyRelative(PropName.TaperedBottomOffset));
            }
            else if (type == Type.SegmentedSideOnly || type == Type.SegmentedNoBottomFullCell)
            {
                EditorGUILayout.PropertyField(property.FindPropertyRelative(PropName.SegmentedOffsets));
            }

            EditorGUI.indentLevel = indent;

            EditorGUI.EndProperty();
        }
    }
}