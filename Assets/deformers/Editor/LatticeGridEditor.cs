using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MeshBuilder
{
    [CustomEditor(typeof(LatticeGrid))]
    public class LatticeGridEditor : Editor
    {
        private static readonly Color NormalLineColor = new Color(0, 0, 0, 1.0f);
        private static readonly Color OverLineColor = new Color(0.5f, 1, 1, 1.0f);
        private static readonly Color SelectedLineColor = new Color(1, 0.3f, 0.3f, 1.0f);
        private static readonly Color NormalPointColor = new Color(1, 1, 1, 0.8f);
        private static readonly Color OverPointeColor = new Color(0.5f, 1, 1, 1);
        private static readonly Color SelectedPointColor = new Color(1, 0.3f, 0.3f, 1);

        private const float PointRadius = 0.1f;
        private const float PointSelectionDistance = 20f; // in pixels
        private const float LineSelectionDistance = 0.1f;

        private SelectedInfo selectedInfo;
        private bool needsRepaint = false;

        // I have to check if the gizmo is used when handling the mouse events so selected nodes don't get cleared
        // before being moved
        // unfortunately I didn't find a proper way of doing that, what I do here is a bit hacky but mostly works
        // TODO: revisit this later
        private bool clearSelection = false;

        private void OnEnable()
        {
            selectedInfo = new SelectedInfo();
            clearSelection = false;
        }

        private void OnSceneGUI()
        {
            var lattice = target as LatticeGrid;

            CheckForPropertyChange(lattice);

            if (selectedInfo == null)
            {
                selectedInfo = new SelectedInfo();
            }
            selectedInfo.RecordState();

            Event guiEvent = Event.current;
            if (guiEvent.type == EventType.Repaint)
            {
                Draw(lattice);
            }
            else
            {
                HandleInput(guiEvent, lattice);

                if (needsRepaint || !selectedInfo.DoesMatchRecordedState)
                {
                    needsRepaint = false;
                    HandleUtility.Repaint();
                }
            }

            if (GUIUtility.hotControl != 0)
            {
                clearSelection = false;
            }

            HandleSelection(lattice, lattice.transform);

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        }

        private void CheckForPropertyChange(LatticeGrid lattice)
        {
            if (lattice.XLength != lattice.Grid.XLength ||
                lattice.YLength != lattice.Grid.YLength ||
                lattice.ZLength != lattice.Grid.ZLength)
            {
                lattice.ResizeGrid(lattice.XLength, lattice.YLength, lattice.ZLength);
            }
            if (lattice.CellSize != lattice.Grid.CellSize)
            {
                // this looks strange, but the Vector3 value can be set from the editor
                // which won't call the setter function, so force it if the cellSize changed
                lattice.CellSize = lattice.CellSize;
            }
        }

        private void HandleSelection(LatticeGrid lattice, Transform transform)
        {
            if (selectedInfo.HasSelection)
            {
                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    var handlePos = CalcSelectionCenter(lattice);
                    handlePos = transform.TransformPoint(handlePos);
                    var newV = Handles.PositionHandle(handlePos, transform.rotation);

                    if (cc.changed)
                    {
                        Undo.RecordObject(lattice, "Lattice Vertex Change");
                        Vector3 delta = newV - handlePos;
                        delta = transform.worldToLocalMatrix * (delta);

                        var indices = selectedInfo.SelectedPointsIndices;
                        foreach (int i in indices)
                        {
                            lattice.Grid.Vertices[i] += delta;
                        }
                    }
                }
            }
        }

        private const int LeftClick = 0;

        void HandleInput(Event guiEvent, LatticeGrid lattice)
        {
            if (guiEvent.button == LeftClick)
            {
                if (guiEvent.type == EventType.MouseDown)
                {
                    bool additiveSelect = (guiEvent.modifiers == EventModifiers.Control);
                    HandleLeftMouseDown(additiveSelect);
                }
                else if (guiEvent.type == EventType.MouseUp)
                {
                    if (clearSelection)
                    {
                        clearSelection = false;
                        selectedInfo.ClearSelection();
                        needsRepaint = true;
                    }
                }
            }

            UpdateMouseOverInfo(guiEvent.mousePosition, lattice);
        }

        void UpdateMouseOverInfo(Vector3 mousePosition, LatticeGrid lattice)
        {
            selectedInfo.NotOverAnything();

            var tNormal = lattice.transform.up;

            Camera cam = SceneView.lastActiveSceneView.camera;
            Vector3 camPos = cam.transform.position;
            var candidates = new List<KeyValuePair<int, float>>();

            var grid = lattice.Grid;
            var verts = lattice.Grid.Vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                var p = lattice.transform.TransformPoint(verts[i]);
                var screenPos = HandleUtility.WorldToGUIPoint(p);

                if (Vector2.Distance(mousePosition, screenPos) < PointSelectionDistance)
                {
                    candidates.Add(new KeyValuePair<int, float>(i, Vector3.Distance(p, camPos)));
                }
            }

            if (candidates.Count > 0)
            {
                float closeDist = candidates[0].Value;
                int closeInd = candidates[0].Key;
                for (int i = 1; i < candidates.Count; ++i)
                {
                    if (candidates[i].Value < closeDist)
                    {
                        closeDist = candidates[i].Value;
                        closeInd = candidates[i].Key;
                    }
                }
                selectedInfo.MouseOverPoint(closeInd);
            }
        }

        private const bool AdditiveSelect = true;
        private const bool ExclusiveSelect = false;

        void HandleLeftMouseDown(bool additiveSelect)
        {
            if (selectedInfo.IsOverAnything)
            {
                int overPoint = selectedInfo.OverPointIndex;
                if (selectedInfo.IsSelected(overPoint))
                {
                    selectedInfo.UnselectPoint(overPoint);
                }
                else
                {
                    if (!additiveSelect)
                    {
                        selectedInfo.ClearSelection();
                    }
                    selectedInfo.SelectPoint(overPoint);
                }
            }
            else
            {
                clearSelection = true;
            }

            needsRepaint = true;
        }

        void Draw(LatticeGrid lattice)
        {
            Camera cam = SceneView.lastActiveSceneView.camera;
            var verts = lattice.Grid.Vertices;

            Vector3 locCam = lattice.transform.InverseTransformPoint(cam.transform.position);
            float closeDist = float.MaxValue;
            float farDist = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                float dist = Vector3.Distance(verts[i], locCam);
                closeDist = Mathf.Min(closeDist, dist);
                farDist = Mathf.Max(farDist, dist);
            }

            using (var scope = new Handles.DrawingScope(lattice.transform.localToWorldMatrix))
            {
                var handleNormal = cam == null ? Vector3.up : cam.transform.forward;
                handleNormal = lattice.transform.InverseTransformDirection(handleNormal);

                var grid = lattice.Grid;
                for (int i = 0; i < verts.Length; i++)
                {
                    int forward = grid.StepIndex(i, 0, 0, 1);
                    int right = grid.StepIndex(i, 0, 1, 0);
                    int up = grid.StepIndex(i, 1, 0, 0);

                    float alpha = CalcAlpha(verts[i]);

                    if (forward >= 0)
                    {
                        DrawLine(i, forward, verts, alpha);
                    }
                    if (right >= 0)
                    {
                        DrawLine(i, right, verts, alpha);
                    }
                    if (up >= 0)
                    {
                        DrawLine(i, up, verts, alpha);
                    }

                    DrawPoint(i, verts[i], handleNormal, alpha);
                }
                needsRepaint = false;
            }

            float CalcAlpha(Vector3 p)
            {
                float dist = Vector3.Distance(p, locCam);

                var a = 1.0f - ((dist - closeDist) / (farDist - closeDist));
                return a * a * a;
            }
        }

        private void DrawLine(int a, int b, Vector3[] verts, float alpha)
        {
            Color color = NormalLineColor;
            if (selectedInfo.IsOverAnything && (selectedInfo.IsOver(a) || selectedInfo.IsOver(b)))
            {
                color = OverLineColor;
            }
            color.a *= alpha;
            if (selectedInfo.HasSelection && (selectedInfo.IsSelected(a) || selectedInfo.IsSelected(b)))
            {
                color = SelectedLineColor;
            }
            Handles.color = color;
            Handles.DrawLine(verts[a], verts[b]);
        }

        private void DrawPoint(int i, Vector3 p, Vector3 normal, float alpha)
        {
            Color color = NormalPointColor;
            color.a *= alpha;
            if (selectedInfo.IsOver(i))
            {
                color = OverPointeColor;
            }
            if (selectedInfo.IsSelected(i))
            {
                color = SelectedPointColor;
            }
            Handles.color = color;
            Handles.DrawSolidDisc(p, normal, PointRadius);
        }

        private Vector3 CalcSelectionCenter(LatticeGrid lattice)
        {
            var verts = lattice.Grid.Vertices;
            var selected = selectedInfo.SelectedPointsIndices;
            if (selected.Count == 1)
            {
                return verts[selected[0]];
            }
            else if (selected.Count > 1)
            {
                Vector3 center = Vector3.zero;
                foreach (var i in selected)
                {
                    center += verts[i];
                }
                center /= selected.Count;
                return center;
            }

            return Vector3.zero;
        }

        private class SelectedInfo
        {
            public enum SelectionState
            {
                Nothing,
                MouseOverPoint
            }

            public SelectionState State { get; private set; }

            public List<int> SelectedPointsIndices { get; }
            public int OverPointIndex { get; private set; }

            private SelectionState recordedState;
            private int recordedPoint;

            public SelectedInfo()
            {
                SelectedPointsIndices = new List<int>();
            }

            public void RecordState()
            {
                recordedState = State;
                recordedPoint = OverPointIndex;
            }

            public bool DoesMatchRecordedState { get { return State == recordedState && recordedPoint == OverPointIndex; } }

            public bool IsSelected(int index)
            {
                return SelectedPointsIndices.Contains(index);
            }

            public bool IsOver(int index)
            {
                return State == SelectionState.MouseOverPoint && OverPointIndex == index;
            }

            public bool IsOverAnything { get { return State == SelectionState.MouseOverPoint; } }
            public void NotOverAnything() { State = SelectionState.Nothing; }

            public void MouseOverPoint(int index)
            {
                OverPointIndex = index;
                State = SelectionState.MouseOverPoint;
            }

            public void SelectPoint(int index)
            {
                if (!SelectedPointsIndices.Contains(index))
                {
                    SelectedPointsIndices.Add(index);
                }
            }

            public void UnselectPoint(int index)
            {
                SelectedPointsIndices.Remove(index);
            }

            public void ClearSelection()
            {
                SelectedPointsIndices.Clear();
            }

            public bool HasSelection
            {
                get { return SelectedPointsIndices.Count > 0; }
            }
        }
    }
}