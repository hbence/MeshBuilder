using UnityEngine;
using UnityEditor;

using static MeshBuilder.MarchingSquaresComponent;
using static MeshBuilder.MarchingSquaresMesher;

namespace MeshBuilder
{
    [CustomPropertyDrawer(typeof(InitializationInfo.MesherInitInfo))]
    public class MarchingSquaresMesherInitInfoDrawer : PropertyDrawer
    {
        private class Props
        {
            public const string type = "type";
            public const string lerpToExactEdge = "lerpToExactEdge";
            public const string useCullingData = "useCullingData";
            public const string useHeightData = "useHeightData";
            public const string offsetY = "offsetY";
            public const string heightScale = "heightScale";
            public const string uScale = "uScale";
            public const string vScale = "vScale";
            public const string normalizeUV = "normalizeUV";
            public const string isFlipped = "isFlipped";
            public const string scaledOffset = "scaledOffset";
            public const string optimizationMode = "optimizationMode";
            public const string sideHeight = "sideHeight";
            public const string bottomHeightScale = "bottomHeightScale";
            public const string bottomScaledOffset = "bottomScaledOffset";
            public const string segments = "segments";
        }
        private static GUIContent GC(string label) => new GUIContent(label);
        private static GUIContent[] Labels = { GC("U"), GC("V")};

        private static int GetPropCount(MesherType type, SerializedProperty property)
        {
            int lineCount = 0;
            switch (type)
            {
                case MesherType.TopCell: lineCount = 11; break;
                case MesherType.ScaledTopCell: lineCount = 12; break;
                case MesherType.OptimizedTopCell: lineCount = 13; break;
                case MesherType.SideCell: lineCount = 15; break;
                case MesherType.SegmentedSideCell:
                    {
                        lineCount = 12;
                        var segmentsProp = property.FindPropertyRelative(Props.segments);
                        if (segmentsProp.isExpanded)
                        {
                            int segmentsCount = segmentsProp.arraySize;
                            lineCount += segmentsCount + 1;
                        }
                        break;
                    }
                case MesherType.FullCell: lineCount = 15; break;
            }

            if (lineCount > 0)
            {
                // uv scale is combined
                lineCount -= 1;
            }

            return lineCount;
        }
        
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            float height = lineHeight;
            if (property.isExpanded)
            {
                var typeProp = property.FindPropertyRelative(Props.type);
                MesherType type = (MesherType)typeProp.enumValueIndex;

                height = lineHeight * GetPropCount(type, property);
            }
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var lineRect = new Rect(position.x, position.y, position.width, lineHeight);

            EditorGUI.BeginProperty(position, label, property);

            property.isExpanded = EditorGUI.Foldout(lineRect, property.isExpanded, label, true);

            if (property.isExpanded)
            {
                lineRect.x += 30;
                lineRect.y += lineHeight;

                var typeProp = property.FindPropertyRelative(Props.type);
                MesherType type = (MesherType)typeProp.enumValueIndex;

                Draw(Props.type);

                Draw(Props.lerpToExactEdge);
                Draw(Props.useCullingData);
                Draw(Props.useHeightData);
                Draw(Props.offsetY);

                var u = property.FindPropertyRelative(Props.uScale);
                var v = property.FindPropertyRelative(Props.vScale);
                var uv = new float[2] { u.floatValue, v.floatValue };
                EditorGUI.MultiFloatField(lineRect, GC("UV Scale"), Labels, uv);
                u.floatValue = uv[0];
                v.floatValue = uv[1];
                lineRect.y += lineHeight;

                Draw(Props.normalizeUV);
                Draw(Props.isFlipped);
                Draw(Props.heightScale);

                switch (type)
                {
                    case MesherType.ScaledTopCell:
                        {
                            Draw(Props.scaledOffset);
                            break;
                        }
                    case MesherType.OptimizedTopCell:
                        {
                            Draw(Props.scaledOffset);
                            Draw(Props.optimizationMode);
                            break;
                        }
                    case MesherType.SideCell: /*fall through*/
                    case MesherType.FullCell:
                        {
                            Draw(Props.scaledOffset);
                            Draw(Props.sideHeight);
                            Draw(Props.bottomHeightScale);
                            Draw(Props.bottomScaledOffset);
                            break;
                        }

                    case MesherType.SegmentedSideCell:
                        {
                            Draw(Props.segments);

                            break;
                        }
                }

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