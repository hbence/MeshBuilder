using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MeshBuilder
{
    [CustomEditor(typeof(SplineComponent))]
    public class SplineComponentEditor : Editor
    {
        private const string PrefIsEditing = "SCE_isEditingControlPoints";

        private static readonly Quaternion Identity = Quaternion.identity;
        private static readonly Vector3 Zero = Vector3.zero;
        private static readonly Vector3 LabelOffset = Vector3.up * -1.75f;
        private static readonly Color ControlPointColor = Color.blue;

        private const float PointRemoveDistance = 0.5f;
        private const float PointAddDistance = 5f;

        private SplineComponent spline;
        private Vector3[] pointBuffer;
        
        private int selectedPointId;
        private bool isEditingControlPoints = false;

        private void OnEnable()
        {
            spline = (SplineComponent) target;
            isEditingControlPoints = EditorPrefs.GetBool(PrefIsEditing, false);
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

            DrawGUI();

            Input();

            Handles.matrix = spline.transform.localToWorldMatrix;

            DrawSpline();
            if (isEditingControlPoints)
            {
                DrawControlPoints();
            }
        }

        private void DrawGUI()
        {
            float buttonWidth = 100;
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(5, 5, buttonWidth, 70));
            GUILayout.BeginVertical();

            string editLabel = isEditingControlPoints ? "Hide Point Edit" : "Show Point Edit";
            if (GUILayout.Button(editLabel))
            {
                isEditingControlPoints = !isEditingControlPoints;

                EditorPrefs.SetBool(PrefIsEditing, isEditingControlPoints);
            }

            if (isEditingControlPoints)
            {
                if (GUILayout.Button("Insert Point"))
                {
                    if (ControlPointCount == 0)
                    {
                        Undo.RecordObject(spline, "Insert point");
                        spline.AddControlPoint(0, spline.transform.position + Vector3.forward);
                    }
                    else
                    {
                        if (selectedPointId >= 0 && selectedPointId < ControlPointCount - 1)
                        {
                            Undo.RecordObject(spline, "Insert point");
                            spline.AddControlPoint(selectedPointId + 1, CurveHalfPoint(selectedPointId, selectedPointId + 1));
                        }
                        else
                        {
                            Vector3 lastPoint = CP(ControlPointCount - 1);
                            Vector3 pos;
                            if (ControlPointCount == 1)
                            {
                                pos = lastPoint + Vector3.right * PointAddDistance;
                            }
                            else
                            {
                                if (spline.Spline.IsClosed)
                                {
                                    pos = CurveHalfPoint(ControlPointCount - 1, 0);
                                }
                                else
                                {
                                    Vector3 delta = lastPoint - CP(ControlPointCount - 2);
                                    if (delta.sqrMagnitude > 0)
                                    {
                                        pos = lastPoint + delta.normalized * PointAddDistance;
                                    }
                                    else
                                    {
                                        pos = lastPoint + Vector3.right * PointAddDistance;
                                    }
                                }
                            }

                            Undo.RecordObject(spline, "Insert point");
                            spline.AddControlPoint(ControlPointCount, pos);
                        }
                    }
                }

                if (selectedPointId >= 0)
                {
                    if (GUILayout.Button("Delete Point"))
                    {
                        if (selectedPointId >= 0 && selectedPointId < ControlPointCount)
                        {
                            Undo.RecordObject(spline, "Remove point");
                            spline.RemoveControlPoint(selectedPointId);
                            selectedPointId = -1;
                        }
                    }
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        void DrawControlPoints()
        {
            var points = spline.ControlPoints;

            if (points == null)
            {
                return;
            }    

            Handles.CapFunction cap = Handles.SphereHandleCap;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 pos = points[i];
                var newPos = pos;
                int id = GUIUtility.GetControlID(FocusType.Passive);
                float size = 0.35f * HandleUtility.GetHandleSize(pos);

                Handles.Label(pos + LabelOffset, i.ToString());
                
                if (i == selectedPointId)
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
                    selectedPointId = i;
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
            int pointCount = spline.CalculateSplinePoints(pointBuffer);

            Handles.DrawAAPolyLine(5f, pointCount, pointBuffer);
        }

        void Input()
        {
            var currentEvent = Event.current;
            var eventType = currentEvent.type;
            Vector2 mousePosition = Event.current.mousePosition;
            mousePosition = HandleUtility.GUIPointToWorldRay(mousePosition).origin;
            if (eventType == EventType.MouseDown)
            {
                if (currentEvent.control)
                {
                    for (int i = 0; i < spline.ControlPointCount; ++i)
                    {
                        if (Vector2.Distance(mousePosition, spline.ControlPoints[i]) <= PointRemoveDistance)
                        {
                            Undo.RecordObject(spline, "Remove point");
                            spline.RemoveControlPoint(i);
                            Event.current.Use();
                            break;
                        }
                    }
                }
            }

            if (eventType == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Delete)
                {
                    if (selectedPointId >= 0 && selectedPointId < spline.ControlPointCount)
                    {
                        spline.RemoveControlPoint(selectedPointId);
                        selectedPointId = -1;
                        Event.current.Use();
                    }
                }
            }
        }
        
        private Vector3 CP(int index) => spline.ControlPoints[index];
        private int ControlPointCount => spline == null ? 0 :
                                        spline.ControlPoints == null ? 0 : spline.ControlPointCount;

        private Vector3 CurveHalfPoint(int a, int b)
            => spline.ArcCalculator.Calculate(Utils.ToFloat3Array(spline.ControlPoints), a, b, 0.5f, spline.Spline.IsClosed);
        

        private void InitBuffer()
        {
            if (pointBuffer == null || pointBuffer.Length != CalcPointCount(spline))
            {
                pointBuffer = new Vector3[CalcPointCount(spline)];
            }
        }

        static private int CalcPointCount(SplineComponent spline)
            => Mathf.Max(2, spline.SegmentCount * spline.SegmentLookupCount);
    }
}
