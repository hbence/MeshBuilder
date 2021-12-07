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

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            float height = lineHeight * 5;

            var typeProp = property.FindPropertyRelative(PropName.Type);
            Type type = (Type)typeProp.enumValueIndex;

            if (type == Type.TaperedSideOnly || type == Type.TaperedNoBottomFullCell)
            {
                height += 2 * lineHeight;
            }
            else if (type == Type.SegmentedSideOnly || type == Type.SegmentedNoBottomFullCell)
            {
                height += lineHeight;
                var offsetProp = property.FindPropertyRelative(PropName.SegmentedOffsets);
                if (offsetProp.isExpanded)
                {
                    for (int i = 0; i < offsetProp.arraySize; ++i)
                    {
                        if (offsetProp.GetArrayElementAtIndex(i).isExpanded)
                        {
                            height += lineHeight * 2;
                        }
                    }

                    height += lineHeight * (offsetProp.arraySize + 2);
                }
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var lineRect = new Rect(position.x, position.y, position.width, lineHeight);

            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel += 1;

            lineRect = EditorGUI.IndentedRect(lineRect);
            lineRect.y += lineHeight;

            var typeProp = property.FindPropertyRelative(PropName.Type);
            Type type = (Type)typeProp.enumValueIndex;

            EditorGUI.PropertyField(lineRect, typeProp);
            lineRect.y += lineHeight;

            DrawRelativeProperty(ref lineRect, property, PropName.CellSize, lineHeight);
            DrawRelativeProperty(ref lineRect, property, PropName.Height, lineHeight);
            DrawRelativeProperty(ref lineRect, property, PropName.LerpToExactEdge, lineHeight);

            if (type == Type.TaperedSideOnly || type == Type.TaperedNoBottomFullCell)
            {
                DrawRelativeProperty(ref lineRect, property, PropName.TaperedTopOffset, lineHeight);
                DrawRelativeProperty(ref lineRect, property, PropName.TaperedBottomOffset, lineHeight);
            }
            else if (type == Type.SegmentedSideOnly || type == Type.SegmentedNoBottomFullCell)
            {
                DrawRelativeProperty(ref lineRect, property, PropName.SegmentedOffsets, lineHeight);
            }

            EditorGUI.indentLevel = indent;

            EditorGUI.EndProperty();
        }

        static private void DrawRelativeProperty(ref Rect rect, SerializedProperty property, string name, float lineHeight)
        {
            EditorGUI.PropertyField(rect, property.FindPropertyRelative(name), true);
            rect.y += lineHeight;
        }
    }
}