using UnityEngine;
using UnityEditor;

namespace MeshBuilder
{
    [CustomPropertyDrawer(typeof(MarchingSquaresComponent.InitializationInfo))]
    public class MarchingSquaresInitializationInfoDrawer : PropertyDrawer
    {
        private class Props
        {
            public const string cellSize = "cellSize";
            public const string generateUVs = "generateUVs";
            public const string generateNormals = "generateNormals";
            public const string submeshes = "submeshes";
        }
        
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            float height = lineHeight;
            if (property.isExpanded)
            {
                height = lineHeight * 5;

                var submeshes = property.FindPropertyRelative(Props.submeshes);
                if (submeshes.isExpanded)
                {
                    height += lineHeight;
                    for (int i = 0; i < submeshes.arraySize; ++i)
                    {
                        var elem = submeshes.GetArrayElementAtIndex(i);
                        height += elem.isExpanded ? EditorGUI.GetPropertyHeight(elem) : lineHeight;
                    }
                }
            }
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var lineRect = new Rect(position.x, position.y, position.width, lineHeight);

            property.isExpanded = EditorGUI.Foldout(lineRect, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                var indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel += 1;

                lineRect = EditorGUI.IndentedRect(lineRect);
                lineRect.y += lineHeight;

                Draw(Props.cellSize);
                Draw(Props.generateUVs);
                Draw(Props.generateNormals);
                Draw(Props.submeshes);

                EditorGUI.indentLevel = indent;

                void Draw(string propName)
                {
                    EditorGUI.PropertyField(lineRect, property.FindPropertyRelative(propName), true);
                    lineRect.y += lineHeight;
                }
            }
            EditorGUI.EndProperty();
        }
    }
}