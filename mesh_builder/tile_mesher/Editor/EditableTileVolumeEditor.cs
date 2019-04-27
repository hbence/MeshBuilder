using System.Collections.Generic;

using UnityEngine;
using UnityEditor;

namespace MeshBuilder
{
    using BrushMode = EditableTileVolume.BrushInfo.BrushMode;

    [CustomEditor(typeof(EditableTileVolume))]
    public class EditableTileVolumeEditor : Editor
    {
        List<Vector3Int> hitList;
        Vector3 rayStart;
        Vector3 rayEnd;

        private const bool IncludeChildren = true;

        private SerializedProperty dataResizeProp;
        private SerializedProperty dataOffsetProp;

        private SerializedProperty lockYLevelProp;
        private SerializedProperty lockedLevelProp;

        private SerializedProperty brushMode;
        private SerializedProperty brushSize;
        private SerializedProperty brushSelectedIndex;
        private SerializedProperty brushRemoveSelected;
        private SerializedProperty brushReplaceIndex;
        private SerializedProperty brushSkipIndices;

        private void OnEnable()
        {
            hitList = new List<Vector3Int>();

            dataResizeProp = serializedObject.FindProperty("dataResize.size");
            dataOffsetProp = serializedObject.FindProperty("dataOffset");

            lockYLevelProp = serializedObject.FindProperty("lockYLevel");
            lockedLevelProp = serializedObject.FindProperty("lockedLevel");

            brushMode = serializedObject.FindProperty("brush.mode");
            brushSize = serializedObject.FindProperty("brush.brushSize");
            brushSelectedIndex = serializedObject.FindProperty("brush.selectedIndex");
            brushRemoveSelected = serializedObject.FindProperty("brush.removeOnlySelected");
            brushReplaceIndex = serializedObject.FindProperty("brush.replaceIndex");
            brushSkipIndices = serializedObject.FindProperty("brush.skipIndices");

            var volume = target as EditableTileVolume;
            if (volume != null && volume.Data == null)
            {
                volume.Init();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var volume = target as EditableTileVolume;

            if (volume == null)
            {
                return;
            }
            else if (volume.Data == null || volume.Data.IsNull)
            {
                volume.Init();
            }

            EditorGUILayout.LabelField("Volume Size: [" + volume.DataXSize + ", " + volume.DataYSize + ", " + volume.DataZSize + "]");
            
            DisplayDataChangeProp();
            DisplayBrushProp();
            
            serializedObject.ApplyModifiedProperties();
        }

        private bool isDataChangeToggleOpen;
        private void DisplayDataChangeProp()
        {
            var volume = target as EditableTileVolume;

            isDataChangeToggleOpen = EditorGUILayout.BeginToggleGroup("Data Volume Change", isDataChangeToggleOpen);

            if (isDataChangeToggleOpen)
            {
                EditorGUILayout.PropertyField(dataResizeProp);
                if (GUILayout.Button("Resize"))
                {
                    volume.ApplyDataResize();
                    needsRepaint = true;
                }
                EditorGUILayout.PropertyField(dataOffsetProp);
                if (GUILayout.Button("Move"))
                {
                    volume.ApplyDataOffset();
                    needsRepaint = true;
                }
            }

            EditorGUILayout.EndToggleGroup();
            EditorGUILayout.Separator();
        }

        private bool useBrush = false;
        private void DisplayBrushProp()
        {
            useBrush = EditorGUILayout.BeginToggleGroup("Brush", useBrush);

            if (useBrush)
            {
                EditorGUILayout.PropertyField(brushMode);

                var mode = (BrushMode)brushMode.intValue;
                if (mode == BrushMode.Add)
                {
                    EditorGUILayout.PropertyField(brushSize);
                    EditorGUILayout.PropertyField(brushSelectedIndex);
                    EditorGUILayout.PropertyField(brushSkipIndices);
                }
                else if (mode == BrushMode.Remove)
                {
                    EditorGUILayout.PropertyField(brushSize);
                    EditorGUILayout.PropertyField(brushRemoveSelected);
                    if (brushRemoveSelected.boolValue)
                    {
                        EditorGUILayout.PropertyField(brushSelectedIndex);
                    }
                }
                else if (mode == BrushMode.Replace)
                {
                    EditorGUILayout.PropertyField(brushSize);
                    EditorGUILayout.PropertyField(brushSelectedIndex);
                    EditorGUILayout.PropertyField(brushReplaceIndex);
                }

                EditorGUILayout.Separator();
                EditorGUILayout.LabelField("Level Lock");
                EditorGUILayout.PropertyField(lockYLevelProp);
                bool lockLevel = lockYLevelProp.boolValue;
                if (lockLevel)
                {
                    EditorGUILayout.PropertyField(lockedLevelProp);
                }
            }
            EditorGUILayout.EndToggleGroup();
            EditorGUILayout.Separator();
        }

        private Vector3 lastCamPos;
        private Vector3 lastCamLook;

        private bool DidCameraChange
        {
            get
            {
                Camera cam = SceneView.lastActiveSceneView.camera;
                return lastCamPos != cam.transform.position || lastCamLook != cam.transform.forward;
            }
        }

        private bool needsRepaint = false;
        private void OnSceneGUI()
        {
            var tileVolume = target as EditableTileVolume;

            if (DidCameraChange)
            {
                Camera cam = SceneView.lastActiveSceneView.camera;
                lastCamPos = cam.transform.position;
                lastCamLook = cam.transform.forward;
                needsRepaint = true;
            }

            Event guiEvent = Event.current;

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (guiEvent.type == EventType.Repaint)
            {
                Draw(tileVolume);
            }
            else if (guiEvent.type == EventType.MouseDown)
            {
                if (guiEvent.button == 0)
                {
                    if (editToggle)
                    {
                        Camera cam = SceneView.lastActiveSceneView.camera;
                        Ray ray = HandleUtility.GUIPointToWorldRay(guiEvent.mousePosition);
                        CalcHitList(ray, tileVolume);
                        needsRepaint = true;
                    }
                }
            }
            else
            {
                if (needsRepaint)
                {
                    needsRepaint = false;
                    HandleUtility.Repaint();
                }
            }

            DrawScreenGUI(tileVolume);

            Tools.hidden = editToggle;

            if (editToggle)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }
        }

        static private Vector3 Zero = Vector3.zero;
        static private Vector3 Forward  = Vector3.forward;
        static private Vector3 Backward = Vector3.back;
        static private Vector3 Right = Vector3.right;
        static private Vector3 Left  = Vector3.left;
        static private Vector3 Up    = Vector3.up;
        static private Vector3 Down  = Vector3.down;

        private void Draw(EditableTileVolume volume)
        {
            Camera cam = SceneView.lastActiveSceneView.camera;
            Transform trans = volume.transform;

            using (var scope = new Handles.DrawingScope(trans.localToWorldMatrix))
            {
                var camPos = trans.InverseTransformPoint(cam.transform.position);
                var camLook = trans.InverseTransformDirection(cam.transform.forward);
                DrawBgGrid(volume, camPos, camLook, Vector3.one);

                DrawHitList(hitList, volume);
            }

            needsRepaint = false;
        }

        private void DrawBgGrid(EditableTileVolume volume, Vector3 camPos, Vector3 camLook, Vector3 cellSize)
        {
            var halfSize = new Vector3(volume.DataXSize, volume.DataYSize, volume.DataZSize);
            halfSize.x *= cellSize.x;
            halfSize.y *= cellSize.y;
            halfSize.z *= cellSize.z;
            halfSize *= 0.5f;

            var pos = (camLook.y < 0) ? Down * halfSize.y : Up * halfSize.y;
            DrawGrid(pos, Forward, Right, volume.DataZSize, volume.DataXSize, cellSize.z, cellSize.x);

            pos = (camLook.x > 0) ? Right * halfSize.x : Left * halfSize.x;
            DrawGrid(pos, Up, Forward, volume.DataYSize, volume.DataZSize, cellSize.y, cellSize.z);
            
            pos = (camLook.z > 0) ? Forward * halfSize.z : Backward * halfSize.z;
            DrawGrid(pos, Up, Right, volume.DataYSize, volume.DataXSize, cellSize.y, cellSize.x);
        }

        private void DrawGrid(Vector3 center, Vector3 forward, Vector3 right, int forwardCellCount, int rightCellCount, float forwardSize, float rightSize)
        {
            Vector3 halfForwardSize = forward * (forwardSize * forwardCellCount * 0.5f);
            Vector3 halfRightSize = right * (rightSize * rightCellCount * 0.5f);
            Vector3 start = center - halfRightSize - halfForwardSize;
            Vector3 end = center - halfRightSize + halfForwardSize;
            for (int i = 0; i < rightCellCount + 1; ++i)
            {
                Handles.DrawLine(start, end);
                start += right * rightSize;
                end += right * rightSize;
            }

            start = center - halfForwardSize - halfRightSize;
            end = center - halfForwardSize + halfRightSize;
            for (int i = 0; i < forwardCellCount + 1; ++i)
            {
                Handles.DrawLine(start, end);
                start += forward * forwardSize;
                end += forward * forwardSize;
            }
        }

        private void DrawHitList(List<Vector3Int> coords, EditableTileVolume volume)
        {
            Vector3 cellSize = Vector3.one;

            Vector3 offset = new Vector3(volume.DataXSize * -0.5f * cellSize.x + 0.5f * cellSize.x, 
                                        volume.DataYSize * -0.5f + 0.5f * cellSize.y, 
                                        volume.DataZSize * -0.5f + 0.5f * cellSize.z);
            Vector3 prev = Vector3.zero;
            foreach (var c in coords)
            {
                var position = new Vector3(c.x + offset.x, c.y + offset.y, c.z + offset.z);
                Handles.color = Color.red;
                Handles.DrawWireCube(position, Vector3.one);
                Handles.color = Color.magenta;
                Handles.CubeHandleCap(0, position, Quaternion.identity, 0.5f, EventType.Used);

                Handles.color = Color.white;

                if (prev != Vector3.zero)
                {
                    Handles.DrawLine(prev, position);
                }
                prev = position;
            }

            Handles.color = Color.yellow;
            Handles.DrawDottedLine(rayStart, rayEnd, 0.2f);
        }
        
        private void CalcHitList(Ray ray, EditableTileVolume volume)
        {
            hitList.Clear();
            
            var volTrans = volume.transform;
            ray.origin = volTrans.InverseTransformPoint(ray.origin);
            var dir = volTrans.InverseTransformDirection(ray.direction).normalized;
            ray.direction = dir;

            rayStart = ray.origin;
            rayEnd = ray.origin + ray.direction * 100f;

            Vector3 cellSize = Vector3.one;
            Vector3 halfSize = new Vector3(volume.DataXSize * cellSize.x * 0.5f, volume.DataYSize * cellSize.y * 0.5f, volume.DataZSize * cellSize.z * 0.5f);

            GridRayIntersection intersect = new GridRayIntersection(halfSize, new Vector3Int(volume.DataXSize, volume.DataYSize, volume.DataZSize), cellSize, ray);

            if (intersect.DidHit)
            {
                hitList.Add(intersect.CurrentTraverseCell);
                for (int i = 0; i < 10; ++i)
                {
                    hitList.Add(intersect.NextCell());
                }
            }
        }

        // helper utility class for traversing the grid along a ray
        private class GridRayIntersection
        {
            private Vector3 GridOffset { get; set; }
            private Vector3Int GridLength { get; set; }
            private Vector3 CellSize { get; set; }

            private Ray Ray { get; set; }

            public Vector3 HitPoint { get; private set; }
            public bool DidHit { get; private set; } = false;

            public GridRayIntersection(Vector3 gridOffset, Vector3Int gridLength, Vector3 cellSize, Ray ray)
            {
                GridOffset = gridOffset;
                GridLength = gridLength;
                CellSize = cellSize;

                Ray = ray;

                CalcHit();
            }

            private void CalcHit()
            {
                Vector3 halfSize = new Vector3(GridLength.x * CellSize.x, GridLength.y * CellSize.y, GridLength.z * CellSize.z);
                halfSize *= 0.5f;

                int vectorIndex = 0;
                Vector3 point = CalcHitPoint(Ray, halfSize, vectorIndex);
                while (!IsInBounds(point, halfSize) && vectorIndex < 2)
                {
                    ++vectorIndex;
                    point = CalcHitPoint(Ray, halfSize, vectorIndex);
                }

                DidHit = IsInBounds(point, halfSize);
                if (DidHit)
                {
                    HitPoint = point;
                    traverse = new Traverse(HitPoint, Ray.direction, this);
                }
                else
                {
                    traverse = null;
                }
            }

            private Vector3 CalcHitPoint(Ray ray, Vector3 halfSize, int vectorIndex)
            {
                float bound = (ray.direction[vectorIndex] > 0) ? -halfSize[vectorIndex] : halfSize[vectorIndex];
                float factor = CalcCellHitFactor(ray.origin[vectorIndex], ray.direction[vectorIndex], bound);
                return ray.origin + ray.direction * factor;
            }

            private float CalcCellHitFactor(float origin, float dir, float bound)
            {
                return dir == 0 ? 0 : (bound - origin) / dir;
            }

            private bool IsInBounds(Vector3 point, Vector3 halfSize)
            {
                return point.x >= -halfSize.x && point.x <= halfSize.x &&
                        point.y >= -halfSize.y && point.y <= halfSize.y &&
                        point.z >= -halfSize.z && point.z <= halfSize.z;
            }

            private Vector3Int ToCoord(Vector3 pos)
            {
                pos += GridOffset;
                return new Vector3Int(Mathf.FloorToInt(pos.x / CellSize.x), Mathf.FloorToInt(pos.y / CellSize.y), Mathf.FloorToInt(pos.z / CellSize.z));
            }
            
            private Traverse traverse = null;

            public Vector3Int NextCell()
            {
                return traverse != null ? traverse.NextCell() : Vector3Int.zero;
            }

            public Vector3Int CurrentTraverseCell { get { return traverse != null ? traverse.CurrentCell : Vector3Int.zero; } }

            private class Traverse
            {
                private GridRayIntersection gridRayIntersection;

                private Vector3 tMax;
                private Vector3 tDelta;
                private Vector3Int step;
                private Vector3Int currentCell;
                public Vector3Int CurrentCell { get { return currentCell; } }

                public Traverse(Vector3 hitPoint, Vector3 hitDir, GridRayIntersection gridRay)
                {
                    gridRayIntersection = gridRay;
                    InitTraverse(hitPoint, hitDir);
                }

                private void InitTraverse(Vector3 hitPoint, Vector3 hitDir)
                {
                    hitDir.Normalize();
                    currentCell = gridRayIntersection.ToCoord(hitPoint);

                    step.x = hitDir.x > 0 ? 1 : -1;
                    step.y = hitDir.y > 0 ? 1 : -1;
                    step.z = hitDir.z > 0 ? 1 : -1;

                    var cellSize = gridRayIntersection.CellSize;
                    tDelta.x = CalcT(cellSize.x, hitDir.x);
                    tDelta.y = CalcT(cellSize.y, hitDir.y);
                    tDelta.z = CalcT(cellSize.z, hitDir.z);

                    float bound = CalcBoundary(CurrentCell.x, CurrentCell.x + step.x, cellSize.x);
                    tMax.x = CalcT(Mathf.Abs(hitPoint.x - bound), hitDir.x);
                    bound = CalcBoundary(CurrentCell.y, CurrentCell.y + step.y, cellSize.y);
                    tMax.y = CalcT(Mathf.Abs(hitPoint.y - bound), hitDir.y);
                    bound = CalcBoundary(CurrentCell.z, CurrentCell.z + step.z, cellSize.z);
                    tMax.z = CalcT(Mathf.Abs(hitPoint.z - bound), hitDir.z);
                }

                private float CalcT(float size, float dirValue)
                {
                    return dirValue != 0 ? Mathf.Abs(size / dirValue) : float.MaxValue;
                }

                private float CalcBoundary(int from, int to, float cellSize)
                {
                    return from < to ? from * cellSize + cellSize : from * cellSize;
                }

                public Vector3Int NextCell()
                {
                    if (tMax.x < tMax.y)
                    {
                        if (tMax.x < tMax.z)
                        {
                            currentCell.x += step.x;
                            tMax.x += tDelta.x;
                        }
                        else
                        {
                            currentCell.z += step.z;
                            tMax.z += tDelta.z;
                        }
                    }
                    else
                    {
                        if (tMax.y < tMax.z)
                        {
                            currentCell.y += step.y;
                            tMax.y += tDelta.y;
                        }
                        else
                        {
                            currentCell.z += step.z;
                            tMax.z += tDelta.z;
                        }
                    }

                    return currentCell;
                }
            }
        }

        private bool editToggle = false;
        private bool viewMenuOpen = false;
        private void DrawScreenGUI(EditableTileVolume lattice)
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(5, 5, 120, 160));
            GUILayout.BeginVertical();

            if (GUILayout.Button(editToggle ? "edit mode" : "view mode"))
            {
                editToggle = !editToggle;
            }

            if (GUILayout.Button(viewMenuOpen ? "<<<" : "view menu >>>", GUILayout.Height(20)))
            {
                viewMenuOpen = !viewMenuOpen;
            }

            if (viewMenuOpen)
            {
                if (GUILayout.Button("reset grid"))
                {
                    needsRepaint = true;
                }

            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

    }
}
