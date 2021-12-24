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
        private const string PrefRoadVisualization = "SCE_showRoadVisualization";

        private static readonly Quaternion Identity = Quaternion.identity;
        private static readonly Vector3 Zero = Vector3.zero;
        private static readonly Vector3 LabelOffset = Vector3.up * -1.75f;
        private static readonly Color ControlPointColor = Color.blue;
        private static readonly Color RotationPointColor = Color.magenta;

        private const float PointRemoveDistance = 0.5f;
        private const float PointAddDistance = 5f;

        private SplineComponent spline;
        private Vector3[] pointBuffer;
        private int pointCount;
        
        private int selectedPointId;
        private bool isEditingControlPoints = false;
        private bool showRoadVisualization = false;

        private void OnEnable()
        {
            spline = (SplineComponent) target;
            isEditingControlPoints = EditorPrefs.GetBool(PrefIsEditing, false);
            showRoadVisualization = EditorPrefs.GetBool(PrefRoadVisualization, false);
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

            InitBuffer();
            pointCount = spline.CalculateSplinePoints(pointBuffer, 20);

            DrawSpline();
            if (showRoadVisualization)
            {
                DrawRoadLines();
            }
            if (isEditingControlPoints)
            {
                DrawControlPoints();
            }
        }

        private void DrawGUI()
        {
            float buttonWidth = 100;
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(5, 5, buttonWidth, 90));
            GUILayout.BeginVertical();

            if (GUILayout.Button(isEditingControlPoints ? "Hide Point Edit" : "Show Point Edit"))
            {
                isEditingControlPoints = !isEditingControlPoints;

                EditorPrefs.SetBool(PrefIsEditing, isEditingControlPoints);
            }

            if (GUILayout.Button(showRoadVisualization ? "Hide Road Viz" : "Show Road Viz"))
            {
                showRoadVisualization = !showRoadVisualization;

                EditorPrefs.SetBool(PrefRoadVisualization, showRoadVisualization);
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

            if (spline.UseCustomRotation && spline.RotationValues != null && spline.RotationValues.Length > 0)
            {
                if (pointBuffer != null && pointCount > 0)
                {
                    Handles.color = RotationPointColor;

                    for (int rotIndex = 0; rotIndex < spline.RotationValues.Length; ++rotIndex)
                    {
                        DrawRotationControl(spline.RotationValues[rotIndex].Distance);
                    }
                }
            }

            Repaint();
        }

        private void DrawRotationControl(float distance)
        {
            Handles.CapFunction cap = Handles.SphereHandleCap;

            float prevDist = 0;
            float curDist = 0;
            for (int i = 0; i < pointCount - 1; ++i)
            {
                curDist += Vector3.Distance(pointBuffer[i], pointBuffer[i + 1]);
                if (curDist >= distance)
                {
                    float t = (distance - prevDist) / (curDist - prevDist);
                    Vector3 position = Vector3.Lerp(pointBuffer[i], pointBuffer[i + 1], t);
                    Handles.FreeMoveHandle(position, Identity, 0.75f, Zero, cap);
                    break;
                }
                prevDist = curDist;
            }
        }
        
        private void DrawRoadLines()
        {
            if (pointBuffer != null && pointCount > 0)
            {
                float scale = 1f;
                float rotation = 0f;

                float distance = 0;

                var points = new List<Vector3>();
                for (int i = 0; i < pointCount - 1; ++i)
                {
                    scale = GetScale(distance);
                    rotation = GetRotation(distance);

                    var cur = pointBuffer[i];
                    var next = pointBuffer[i + 1];
                    AddSidePoints(cur, next, points, scale, rotation);

                    distance += Vector3.Distance(cur, next);
                }

                var beforeLast = pointBuffer[pointCount - 2];
                var last = pointBuffer[pointCount - 1];

                scale = GetScale(distance);
                rotation = GetRotation(distance);
                AddSidePoints(last, last + (last - beforeLast), points, scale, rotation);

                for (int i = 0; i < pointCount - 1; ++i)
                {
                    int cur = i * 2;
                    int next = (i+1) * 2;
                    
                    points.Add(points[cur]);
                    points.Add(points[next]);

                    points.Add(points[cur + 1]);
                    points.Add(points[next + 1]);
                }

                Handles.DrawLines(points.ToArray());
            }
        }

        private float GetScale(float distance)
            => spline.UseCustomScaling && spline.ScaleValues != null && spline.ScaleValues.Length > 0 ? 
                SplineModifier.GetValueAtDistance(distance, spline.ScaleValues).x : 
                1;

        private float GetRotation(float distance)
            => spline.UseCustomRotation && spline.RotationValues != null && spline.RotationValues.Length > 0 ?
                SplineModifier.GetValueAtDistance(distance, spline.RotationValues) : 
                1;

        static private void AddSidePoints(Vector3 cur, Vector3 next, List<Vector3> points, float scale = 1, float rotation = 0)
        {
            var forward = next - cur;
            forward = forward.sqrMagnitude > 0 ? forward : Vector3.forward;
            forward.Normalize();

            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized * scale;
            right = Quaternion.AngleAxis(rotation, forward) * right;

            points.Add(cur - right);
            points.Add(cur + right);
        }

        private void DrawSpline()
        {
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
