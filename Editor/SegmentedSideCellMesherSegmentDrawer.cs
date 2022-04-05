using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MeshBuilder
{
    [CustomPropertyDrawer(typeof(SegmentedSideCellMesher.Segment))]
    public class SegmentedSideCellMesherSegmentDrawer : PropertyDrawer
    {
        private class Props
        {
            public const string SideHzOffset = "SideHzOffset";
            public const string SideVcOffset = "SideVcOffset";
            public const string HeightScale = "HeightScale";
        }

        private static GUIContent GC(string label) => new GUIContent(label);
        private static GUIContent[] Labels = { GC("Offset HZ"), GC("VC"), GC("Height Scale") };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var lineRect = new Rect(position.x, position.y, position.width, lineHeight);

            EditorGUI.indentLevel += 1;
            lineRect = EditorGUI.IndentedRect(lineRect);

            var propHz = property.FindPropertyRelative(Props.SideHzOffset);
            var propVc = property.FindPropertyRelative(Props.SideVcOffset);
            var propHeight = property.FindPropertyRelative(Props.HeightScale);

            var values = new float[3] { propHz.floatValue, propVc.floatValue, propHeight.floatValue };
            EditorGUI.MultiFloatField(lineRect, Labels, values);
            propHz.floatValue = values[0];
            propVc.floatValue = values[1];
            propHeight.floatValue = values[2];

            EditorGUI.indentLevel -= 1;
        }
    }
}