using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MeshBuilder
{
    [CustomEditor(typeof(SplineComponent))]
    public class SplineComponentEditor : Editor
    {
        private static readonly Quaternion Identity = Quaternion.identity;
        private static readonly Vector3 Zero = Vector3.zero;
        private static readonly Vector3 LabelOffset = Vector3.up * 0.75f;
        private static readonly Color ControlPointColor = Color.blue;

        private SplineComponent spline;
        private Vector3[] pointBuffer;
        
        private int selectedControlPointId;

        private void OnEnable()
        {
            spline = (SplineComponent) target;    
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
        }

        public void OnSceneGUI()
        {
            if (spline == null)
            {
                return;
            }

            DrawSpline();
            DrawControlPoints();
        }

        void DrawControlPoints()
        {
            Handles.CapFunction cap = Handles.SphereHandleCap;
            var points = spline.ControlPoints;
            for (int i = 0; i < points.Length; i++)
            {
                var pos = points[i];
                var newPos = pos;
                int id = GUIUtility.GetControlID(FocusType.Passive);
                float size = 0.35f * HandleUtility.GetHandleSize(pos);

                Handles.Label(pos + LabelOffset, i.ToString());
                
                if (i == selectedControlPointId)
                {
                    newPos = Handles.PositionHandle(pos, Identity);
                }
                else
                {
                    Handles.color = ControlPointColor;
                    Handles.FreeMoveHandle(id, pos, Identity, size, Zero, cap);
                }

                if (id == EditorGUIUtility.hotControl)
                {
                    selectedControlPointId = i;
                }

                if (pos != newPos)
                {
                    Undo.RecordObject(spline, "Move control point position");
                    spline.UpdateControlPoint(i, newPos);
                }
            }
            Repaint();
        }
        
        private void DrawSpline()
        {
            InitBuffer();
            int pointCount = spline.CalculateDebugSplinePoints(pointBuffer);
            Handles.DrawAAPolyLine(5f, pointCount, pointBuffer);
        }

        private void InitBuffer()
        {
            if (pointBuffer == null || pointBuffer.Length != CalcPointCount(spline))
            {
                pointBuffer = new Vector3[CalcPointCount(spline)];
            }
        }

        static private int CalcPointCount(SplineComponent spline)
            => spline.SegmentCount * spline.SegmentLookupCount;
    }
}
