using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Mathematics;

using static MeshBuilder.Utils;

namespace MeshBuilder
{
    [CustomEditor(typeof(SplineComponent))]
    public class SplineComponentEditor : Editor
    {
        private const string PrefIsEditing = "SCE_isEditingControlPoints";
        private const string PrefIsEditingRotation = "SCE_isEditingRotation";
        private const string PrefIsEditingScaling = "SCE_isEditingScaling";
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

        private int selectedControlPoint;
        private int selectedRotationPoint;
        private int selectedScalingPoint;

        private BoolEditorPref isEditingControlPoints;
        private BoolEditorPref isEditingRotation;
        private BoolEditorPref isEditingScaling;
        private BoolEditorPref showRoadVisualization;

        private void OnEnable()
        {
            spline = (SplineComponent)target;
            isEditingControlPoints = CreatePref(PrefIsEditing, false);
            isEditingRotation = CreatePref(PrefIsEditingRotation, false);
            isEditingScaling = CreatePref(PrefIsEditingScaling, false);
            showRoadVisualization = CreatePref(PrefRoadVisualization, false);

            selectedControlPoint = -1;
            selectedRotationPoint = -1;
            selectedScalingPoint = -1;
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

            if (isEditingRotation)
            {
                DrawRotationPoints();
            }

            if (isEditingScaling)
            {
                DrawScalingPoints();
            }

            if (isEditingControlPoints || isEditingRotation || isEditingScaling)
            {
                Repaint();
            }
        }

        private void DrawGUI()
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(5, 5, 160, 220));
            GUILayout.BeginVertical();

            showRoadVisualization.DrawToggle("Show Road Viz");

            if (isEditingControlPoints.DrawToggle("Show Control Points"))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                GUILayout.BeginVertical();

                HandleControlPointEditButtons(spline, ref selectedControlPoint);

                GUILayout.EndVertical();
                GUILayout.Space(20);
                GUILayout.EndHorizontal();
            }

            if (isEditingRotation.DrawToggle("Show Rotation Points"))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                GUILayout.BeginVertical();

                spline.RotationValues = HandleDistanceValueEditButtons(spline.RotationValues, ref selectedRotationPoint, spline, "Insert Point", "Remove Point");

                GUILayout.EndVertical();
                GUILayout.Space(20);
                GUILayout.EndHorizontal();
            }

            if (isEditingScaling.DrawToggle("Show Scaling Points"))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                GUILayout.BeginVertical();

                spline.ScaleValues = HandleDistanceValueEditButtons(spline.ScaleValues, ref selectedScalingPoint, spline, "Insert Point", "Remove Point");

                GUILayout.EndVertical();
                GUILayout.Space(20);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private delegate T InsertPoint<T>(T values, ref int selectedIndex);
        private delegate T RemovePoint<T>(T values, ref int selectedIndex);

        static private T HandleEditButtons<T>(T values, ref int selected, SplineComponent spline, string insertLabel, InsertPoint<T> insert, string removeLabel, RemovePoint<T> remove)
        {
            if (GUILayout.Button(insertLabel))
            {
                Undo.RecordObject(spline, insertLabel);
                values = insert(values, ref selected);
            }

            if (values != null && selected >= 0)
            {
                if (GUILayout.Button(removeLabel))
                {
                    Undo.RecordObject(spline, removeLabel);
                    values = remove(values, ref selected);
                }
            }

            return values;
        }

        static private void HandleControlPointEditButtons(SplineComponent spline, ref int selected)
            => HandleEditButtons(spline, ref selected, spline, 
                "Insert Point",
                (SplineComponent s, ref int index) =>
                {
                    if (s != null)
                    {
                        int count = s.ControlPointCount;
                        if (count == 0)
                        {
                            index = 0;
                            s.AddControlPoint(0, s.transform.position + Vector3.forward);
                        }
                        else
                        {
                            if (index >= 0 && index < count - 1)
                            {
                                s.AddControlPoint(index + 1, CurveHalfPoint(index, index + 1));
                                index = index + 1;
                            }
                            else
                            {
                                Vector3 lastPoint = CP(count - 1);
                                Vector3 pos;
                                if (count == 1)
                                {
                                    pos = lastPoint + Vector3.right * PointAddDistance;
                                }
                                else
                                {
                                    if (spline.Spline.IsClosed)
                                    {
                                        pos = CurveHalfPoint(count - 1, 0);
                                    }
                                    else
                                    {
                                        Vector3 delta = lastPoint - CP(count - 2);
                                        pos = lastPoint + ((delta.sqrMagnitude > 0) ? delta.normalized : Vector3.right) * PointAddDistance; 
                                    }
                                }

                                s.AddControlPoint(count, pos);
                                index = s.ControlPointCount - 1;
                            }
                        }
                    }

                    Vector3 CurveHalfPoint(int a, int b) => s.ArcCalculator.Calculate(ToFloat3Array(spline.ControlPoints), a, b, 0.5f, s.Spline.IsClosed);
                    Vector3 CP(int i) => s.ControlPoints[i];

                    return s;
                },
                "Remove Point",
                (SplineComponent s, ref int index) =>
                {
                    if (index >= 0 && index < s.ControlPointCount)
                    {
                        s.RemoveControlPoint(index);
                        index = -1;
                    }
                    return s;
                }
        );

        static private SplineModifier.ValueAtDistance<T>[] HandleDistanceValueEditButtons<T>(SplineModifier.ValueAtDistance<T>[] distValues, ref int selectedIndex, SplineComponent spline, string insertLabel, string removeLabel) where T : struct
            => HandleEditButtons(distValues, ref selectedIndex, spline,
                insertLabel,
                (SplineModifier.ValueAtDistance<T>[] values, ref int index) =>
                {
                    var newValues = new List<SplineModifier.ValueAtDistance<T>>();
                    if (values != null && values.Length > 0)
                    {
                        newValues.AddRange(values);
                    }

                    float distance = 0;
                    T value = default;
                    if (index < newValues.Count)
                    {
                        distance = (index < newValues.Count - 1) ? Mathf.Lerp(values[index].Distance, values[index + 1].Distance, 0.5f) : newValues[index].Distance + 1f;
                    }

                    if (index >= 0 && index < newValues.Count)
                    {
                        newValues.Insert(index + 1, new SplineModifier.ValueAtDistance<T>(distance, value));
                    }
                    else
                    {
                        newValues.Add(new SplineModifier.ValueAtDistance<T>(distance, value));
                    }
                    return newValues.ToArray();
                },
                removeLabel,
                (SplineModifier.ValueAtDistance<T>[] values, ref int index) => 
                {
                    if (index < values.Length)
                    {
                        if (values.Length == 1)
                        {
                            index = -1;
                            return null;
                        }

                        var newValues = new List<SplineModifier.ValueAtDistance<T>>(values);
                        newValues.RemoveAt(index);
                        values = newValues.ToArray();
                        index = Mathf.Min(index, newValues.Count - 1);
                    }
                    return values;
                }
        );

        void DrawControlPoints()
            => DrawPointHandlers(true, spline.ControlPoints, ref selectedControlPoint, spline, "Control Point Moved", 1,
                (int[] ids, ref Vector3 pos, bool selected) => 
                {
                    if (selected)
                    {
                        var newPos = Handles.PositionHandle(pos, Identity);
                        if (pos != newPos)
                        {
                            pos = newPos;
                            return true;
                        }
                    }
                    else
                    {
                        Handles.color = ControlPointColor;
                        float size = 0.35f * HandleUtility.GetHandleSize(pos);
                        Handles.FreeMoveHandle(ids[0], pos, Identity, size, Zero, Handles.SphereHandleCap);
                    }

                    return false;
                });

        void DrawRotationPoints()
            => DrawPointHandlers(spline.UseCustomRotation, spline.RotationValues, ref selectedRotationPoint, spline, "Spline Rotation Changed", 2,
                (int[] ids, ref SplineModifier.ValueAtDistance<float> rotation, bool selected) => 
                {
                    bool changed = false;

                    int moveId = ids[0];
                    int rotId = ids[1];

                    float angle = rotation.Value;

                    int aIndex, bIndex;
                    Vector3 position = CalcPositionAtDistance(rotation.Distance, out aIndex, out bIndex);
                    float size = 0.35f * HandleUtility.GetHandleSize(position);

                    var normal = (pointBuffer[bIndex] - pointBuffer[aIndex]).normalized;
                    var rot = Quaternion.AngleAxis(angle, normal);
                    var up = rot * Vector3.up;
                    var right = Vector3.Cross(up, normal);

                    if (selected)
                    {
                        var newRot = Handles.FreeRotateHandle(rotId, rot, position, size * 2);
                        float deltaAngle = Quaternion.Angle(rot, newRot);

                        if (isMouseDown)
                        {
                            var cam = Camera.current;
                            float dot = Vector3.Dot(normal, cam.transform.forward);
                            if (dot > 0)
                            {
                                if (mouseDelta.x < 0)
                                {
                                    deltaAngle *= -1;
                                }
                            }
                            else
                            {
                                if (mouseDelta.x > 0)
                                {
                                    deltaAngle *= -1;
                                }
                            }
                        }

                        float newAngle = angle + deltaAngle;
                        
                        if (angle != newAngle)
                        {
                            rotation.Value = newAngle;
                            changed = true;
                        }

                        Handles.color = new Color(1, 1, 1, 1);
                        Handles.DrawWireArc(position, normal, Vector3.up, angle, size * 2);
                        Handles.DrawLine(position, position + Vector3.up * size * 2);
                        Handles.color = new Color(1, 0, 0, 1);
                        Handles.DrawLine(position, position + up * size * 2);
                        
                        var newPos = Handles.Slider(moveId, position, normal, size, Handles.ArrowHandleCap, 0);
                        if (newPos != position)
                        {
                            rotation.Distance = ChangeDistance(position, newPos, rotation.Distance, normal, spline.RotationValues);
                            changed = true;
                        }
                    }
                    else
                    {
                        Handles.color = new Color(1, 1, 1, 0.6f);
                        Handles.FreeRotateHandle(moveId, rot, position, 1f);
                        Handles.color = Color.red;
                        Handles.DrawLine(position, position + right * 2f);
                        Handles.DrawLine(position, position + right * -2f);
                        
                        Handles.DrawSolidArc(position, normal, Vector3.up, angle, 1.5f);
                    }

                    return changed;
                }
            );

        private static float ChangeDistance<T>(Vector3 currentPos, Vector3 newPos, float currentDistance, Vector3 currentNormal, SplineModifier.ValueAtDistance<T>[] values) where T : struct
        {
            float moveDelta = Vector3.Distance(newPos, currentPos);

            float minDistance = 0;
            float maxDistance = float.MaxValue;

            int rotIndex = System.Array.FindIndex(values, (SplineModifier.ValueAtDistance<T> v) => v.Distance == currentDistance);

            if (rotIndex >= 0)
            {
                if (rotIndex > 0)
                {
                    minDistance = Mathf.Max(minDistance, values[rotIndex - 1].Distance + 0.1f);
                }
                if (rotIndex < values.Length - 1)
                {
                    maxDistance = Mathf.Min(maxDistance, values[rotIndex + 1].Distance - 0.1f);
                }
            }

            float newDistance = currentDistance + moveDelta * Vector3.Dot(currentNormal, (newPos - currentPos));
            newDistance = Mathf.Clamp(newDistance, minDistance, maxDistance);
            return newDistance;
        }

        private delegate bool PointHandleDrawer<T>(int[] ids, ref T value, bool selected) where T : struct;

        private static int[] IDBuffer = new int[4];

        void DrawPointHandlers<T>(bool isUsed, T[] values, ref int selectedIndex, SplineComponent spline, string changeMessage, int idCountForDrawer, PointHandleDrawer<T> draw) where T : struct
        {
            if (IDBuffer.Length < idCountForDrawer)
            {
                IDBuffer = new int[idCountForDrawer];
            }

            if (isUsed && values != null && values.Length > 0)
            {
                for (int i = 0; i < values.Length; ++i)
                {
                    for (int j = 0; j < IDBuffer.Length; ++j)
                    {
                        IDBuffer[j] = (j < idCountForDrawer) ? GUIUtility.GetControlID(FocusType.Passive) : -1;
                    }

                    var value = values[i];
                    bool changed = draw(IDBuffer, ref value, i == selectedIndex);

                    if (changed)
                    {
                        Undo.RecordObject(spline, changeMessage);
                        values[i] = value;
                    }

                    foreach (var id in IDBuffer)
                    {
                        if (id == EditorGUIUtility.hotControl)
                        {
                            selectedIndex = i;
                        }
                        else if (id == -1)
                        {
                            break;
                        }
                    }
                }
            }
        }

        void DrawScalingPoints()
            => DrawPointHandlers(true, spline.ScaleValues, ref selectedScalingPoint, spline, "Spline Scale Changed", 2,
                (int[] ids, ref SplineModifier.ValueAtDistance<float2> scale, bool selected) =>
                {
                    bool changed = false;

                    int moveId = ids[0];
                    int scaleId = ids[1];

                    int aIndex, bIndex;
                    Vector3 position = CalcPositionAtDistance(scale.Distance, out aIndex, out bIndex);
                    float size = 0.35f * HandleUtility.GetHandleSize(position);

                    var normal = (pointBuffer[bIndex] - pointBuffer[aIndex]).normalized;
                    var up = Vector3.up;
                    var right = Vector3.Cross(up, normal);
                    var rot = Quaternion.LookRotation(normal, up);

                    if (selected)
                    {
                        Handles.color = Color.red;
                        float x = Handles.ScaleSlider(scale.Value.x, position, right, rot, 1, 0);
                        Handles.color = Color.green;
                        float y = Handles.ScaleSlider(scale.Value.y, position, up, rot, 1, 0);

                        if (scale.Value.x != x || scale.Value.y != y)
                        {
                            scale.Value = new float2(x, y);
                            changed = true;
                        }

                        Handles.color = Color.red;
                        var newPos = Handles.Slider(moveId, position, normal, size, Handles.ArrowHandleCap, 0);
                        if (newPos != position)
                        {
                            scale.Distance = ChangeDistance(position, newPos, scale.Distance, normal, spline.RotationValues);
                            changed = true;
                        }
                    }
                    else
                    {
                        Handles.color = new Color(1, 1, 1, 1f);
                        Handles.FreeMoveHandle(moveId, position, rot, 1f, Vector3.zero, Handles.CubeHandleCap);
                        Handles.DrawDottedLine(position, position + right * 2f, 2f);
                        Handles.DrawSolidDisc(position + right * 2f, Vector3.up, 0.13f);
                        Handles.DrawDottedLine(position, position + right * -2f, 2f);
                        Handles.DrawSolidDisc(position + right * -2f, Vector3.up, 0.13f);

                        Handles.color = Color.red;
                        Handles.DrawLine(position, position + right * 2f * scale.Value.x);
                        Handles.DrawLine(position, position + right * -2f * scale.Value.x);
                    }

                    return changed;
                });

        private Vector3 CalcPositionAtDistance(float distance, out int aIndex, out int bIndex)
        {
            float prevDist = 0;
            float curDist = 0;
            for (int i = 0; i < pointCount - 1; ++i)
            {
                curDist += Vector3.Distance(pointBuffer[i], pointBuffer[i + 1]);
                if (curDist >= distance)
                {
                    aIndex = i;
                    bIndex = i + 1;
                    float t = (distance - prevDist) / (curDist - prevDist);
                    return Vector3.Lerp(pointBuffer[i], pointBuffer[i + 1], t);
                }
                prevDist = curDist;
            }

            aIndex = pointCount - 2;
            bIndex = pointCount - 1;

            return pointBuffer[aIndex];
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

        private bool isMouseDown;
        private Vector2 prevMousePosition;
        private Vector2 mouseDelta;

        void Input()
        {
            var currentEvent = Event.current;
            var eventType = currentEvent.type;
            Vector2 mousePosition = Event.current.mousePosition;
            mousePosition = HandleUtility.GUIPointToWorldRay(mousePosition).origin;
            if (eventType == EventType.MouseDown)
            {
                isMouseDown = true;
                mousePosition = HandleUtility.GUIPointToWorldRay(mousePosition).origin;

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
            else if (eventType == EventType.MouseUp)
            {
                isMouseDown = false;
            }

            mouseDelta = mousePosition - prevMousePosition;
            prevMousePosition = mousePosition;

            if (eventType == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Delete)
                {
                    if (selectedControlPoint >= 0 && selectedControlPoint < spline.ControlPointCount)
                    {
                        spline.RemoveControlPoint(selectedControlPoint);
                        selectedControlPoint = -1;
                        Event.current.Use();
                    }
                }
            }
        }
        
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
