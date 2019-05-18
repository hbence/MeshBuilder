using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace MeshBuilder
{
    [CustomPropertyDrawer(typeof(MeshBuilderDrawer.RenderInfo))]
    public class RenderInfoEditor : PropertyDrawer
    {
        private const bool ExpandChildren = true;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.isExpanded)
            {
                float lineHeight = EditorGUIUtility.singleLineHeight + 2;
                float height = lineHeight * 5;

                var subMeshInfo = property.FindPropertyRelative("subMeshInfo");
                if (subMeshInfo != null)
                {
                    height += subMeshInfo.arraySize * 4 * lineHeight;
                }

                return height;
            }
            else
            {
                return base.GetPropertyHeight(property, label);
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight + 2;
            float start = position.y;
            var lineRect = new Rect(position.x, position.y, position.width, lineHeight);

            lineRect = EditorGUI.IndentedRect(lineRect);

            property.isExpanded = EditorGUI.BeginFoldoutHeaderGroup(lineRect, property.isExpanded, property.displayName);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;

                lineRect.y += lineHeight;
                EditorGUI.PropertyField(lineRect, property.FindPropertyRelative("hide"));

                var subMeshInfo = property.FindPropertyRelative("subMeshInfo");

                lineRect.y += lineHeight;
                GUI.Label(lineRect, "Submeshes: " + subMeshInfo.arraySize);

                DisplayArray(subMeshInfo, ref lineRect);

                lineRect.y += lineHeight;
                if (GUI.Button(lineRect, "Add Submesh"))
                {
                    ++subMeshInfo.arraySize;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndFoldoutHeaderGroup();
            EditorGUI.EndProperty();
        }

        private void DisplayArray(SerializedProperty arrayProp, ref Rect lineRect)
        {
            for (int i = 0; i < arrayProp.arraySize; ++i)
            {
                bool remove = false;
                var child = arrayProp.GetArrayElementAtIndex(i);
                DisplaySubmesh(child, i, out remove, ref lineRect);

                if (remove)
                {
                    arrayProp.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
        }

        private void DisplaySubmesh(SerializedProperty prop, int count, out bool remove, ref Rect lineRect)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight + 2;

            remove = false;

            prop.NextVisible(ExpandChildren);
            var material = prop.Copy();
            prop.NextVisible(ExpandChildren);
            var recieveShadow = prop.Copy();
            prop.NextVisible(ExpandChildren);
            var castShadow = prop;

            var bgRect = lineRect;
            bgRect.y += lineHeight - 4;
            bgRect.height += lineHeight * 3.25f;
            EditorGUI.DrawRect(bgRect, new Color(1, 1, 1, 0.2f));

            // name and remove button
            lineRect.y += lineHeight;
            var halfRect = lineRect;
            halfRect.width = lineRect.width * 0.75f;
            EditorGUI.LabelField(lineRect, "Submesh " + count);

            halfRect.x += halfRect.width;
            halfRect.y -= 2;
            halfRect.width = lineRect.width - halfRect.width;
            if (GUI.Button(halfRect, "Remove"))
            {
                remove = true;
            }

            lineRect.y += lineHeight;
            EditorGUI.PropertyField(lineRect, material);
            lineRect.y += lineHeight;
            EditorGUI.PropertyField(lineRect, recieveShadow);
            lineRect.y += lineHeight;
            EditorGUI.PropertyField(lineRect, castShadow);

            lineRect.y += lineHeight * 0.5f;
        }
    }
}
