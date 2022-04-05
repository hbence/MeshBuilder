using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;
using Unity.Collections;

using static MeshBuilder.MarchingSquaresEditorComponent;
using Data = MeshBuilder.MarchingSquaresMesherData;

using static MeshBuilder.Utils;

namespace MeshBuilder
{
    [CustomEditor(typeof(MarchingSquaresEditorComponent))]
    // beautiful class name, congrats
    public class MarchingSquaresEditorComponentEditor : Editor
    {
        private const string PrefEditMode = "MSECE_editMode";
        private const string PrefShowGrid = "MSECE_showGrid";
        private const string PrefBrushModeIndex = "MSECE_brushModeIndex";
        private const string PrefBrushShapeIndex = "MSECE_brushShapeIndex";
        private const string PrefHeightBrushModeIndex = "MSECE_heightBrushModeIndex";
        private const string PrefHeightChangeModeIndex = "MSECE_heightChangeModeIndex";
        private const string PrefHeightBrushFoldout = "MSECE_heightBrushFoldout";
        private const string PrefRemoveBorder = "MSECE_removeBorder";
        private const string PrefAutoSave = "MSECE_autoSave";

        private const int LeftMouseButton = 0;

        private const float DefButtonWidth = 100;
        private float DefButtonHeight => EditorGUIUtility.singleLineHeight;
        private const float BrushSettingsGUIBorder = 10;

        private static readonly Color DefBrushColor = Color.yellow;
        private static readonly Color InvalidBrushColor = Color.red;

        private enum PropName
        {
            dataComponent,
            meshers,
            editMode,
            brushShape,
            cellSize,
            brushRadius,
            heightBrush,
            heightChange,
            changeScale,
            heightChangeValue,
            minHeightLevel,
            maxHeightLevel,
            absoluteHeightLevel,
            limitDistance,
            minLimitDistance,
            maxLimitDistance,
            clearValue,
        }

        private ComponentProperties<PropName> props;

        private MarchingSquaresEditorComponent editor;
        private List<EditorMesher> meshers;

        private Data Data => editor.DataComponent != null ? editor.DataComponent.Data : null;

        private bool editMode = false;
        private bool showGrid = true;
        private bool showDistances = false;
        private int brushModeIndex = 0;
        private int brushShapeIndex = 0;
        private int heightBrushModeIndex = 0;
        private int heightChangeModeIndex = 0;
        private bool heightBrushFoldout = false;
        private bool removeBorder = false;
        private bool autoSave = false;

        private bool shouldDrawBrush = false;
        private Vector3 brushPosition;
        private float ribbonHeight;
        private float brushGUIY;

        private void OnEnable()
        {
            editor = (MarchingSquaresEditorComponent)target;

            if (meshers == null)
            {
                meshers = new List<EditorMesher>();
            }
            meshers.Clear();

            CreateMeshers();

            if (editor.DataComponent != null && editor.DataComponent.Data == null)
            {
                editor.DataComponent.Load();
            }

            props = new ComponentProperties<PropName>(serializedObject);

            editMode = EditorPrefs.GetBool(PrefEditMode, false);
            showGrid = EditorPrefs.GetBool(PrefShowGrid, true);
            brushModeIndex = EditorPrefs.GetInt(PrefBrushModeIndex, 0);
            brushShapeIndex = EditorPrefs.GetInt(PrefBrushShapeIndex, 0);
            heightBrushModeIndex = EditorPrefs.GetInt(PrefHeightBrushModeIndex, 0);
            heightChangeModeIndex = EditorPrefs.GetInt(PrefHeightChangeModeIndex, 0);
            heightBrushFoldout = EditorPrefs.GetBool(PrefHeightBrushFoldout, false);
            removeBorder = EditorPrefs.GetBool(PrefRemoveBorder, false);
            autoSave = EditorPrefs.GetBool(PrefAutoSave, false);

            SceneView.beforeSceneGui -= BeforeSceneGUI;
            SceneView.beforeSceneGui += BeforeSceneGUI;
        }

        private void CreateMeshers()
        {
            if (editor.Meshers != null)
            {
                for (int i = 0; i < editor.Meshers.Length; ++i)
                {
                    var mesher = new EditorMesher(editor.Meshers[i]);
                    meshers.Add(mesher);
                }
            }
        }

        private void OnDisable()
        {
            SceneView.beforeSceneGui -= BeforeSceneGUI;

            EditorPrefs.SetBool(PrefEditMode, editMode);
            EditorPrefs.SetBool(PrefShowGrid, showGrid);
            EditorPrefs.SetInt(PrefBrushModeIndex, brushModeIndex);
            EditorPrefs.SetInt(PrefBrushShapeIndex, brushShapeIndex);
            EditorPrefs.SetInt(PrefHeightBrushModeIndex, heightBrushModeIndex);
            EditorPrefs.SetInt(PrefHeightChangeModeIndex, heightChangeModeIndex);
            EditorPrefs.SetBool(PrefHeightBrushFoldout, heightBrushFoldout);
            EditorPrefs.SetBool(PrefRemoveBorder, removeBorder);
            EditorPrefs.SetBool(PrefAutoSave, autoSave);

            if (autoSave)
            {
                editor.DataComponent?.Save();
            }

            meshers?.Clear();

            editor.DataComponent?.OnDestroy();
        }

        public override void OnInspectorGUI()
        {
            if (props == null)
            {
                return;
            }

            serializedObject.Update();

            props.Draw(PropName.dataComponent);
            props.Draw(PropName.meshers);

            EditorGUILayout.LabelField("Brush Settings");

            props.Draw(PropName.editMode);
            props.Draw(PropName.brushShape);
            props.Draw(PropName.cellSize);

            if (editor.Meshers != null && editor.Meshers.Length > 0)
            {
                for (int i = 0; i < editor.Meshers.Length; ++i)
                {
                    var mesher = editor.Meshers[i];
                    if (mesher != null && mesher.CellSize != editor.CellSize)
                    {
                        EditorGUILayout.HelpBox("Editor and a mesher has a CellSize mismatch! Is it intentional?", MessageType.Warning, true);
                        break;
                    }
                }
            }

            props.Draw(PropName.brushRadius);

            Mode editMode = editor.EditMode;
            if (editMode == Mode.HeightChange)
            {
                EditorGUILayout.Space();
                heightBrushFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(heightBrushFoldout, "Height Brush Settings");
                if (heightBrushFoldout)
                {
                    props.Draw(PropName.heightBrush);
                    props.Draw(PropName.heightChange);
                    props.Draw(PropName.changeScale);

                    HeightChangeMode addMode = editor.HeightChange;
                    if (addMode == HeightChangeMode.Absolute)
                    {
                        props.Draw(PropName.absoluteHeightLevel);
                    }
                    else
                    {
                        props.Draw(PropName.heightChangeValue);
                        props.Draw(PropName.minHeightLevel);
                        props.Draw(PropName.maxHeightLevel);
                    }
                }
            }
            
            EditorGUILayout.Space();

            props.Draw(PropName.limitDistance);
            if (editor.LimitDistance)
            {
                props.Draw(PropName.minLimitDistance);
                props.Draw(PropName.maxLimitDistance);
            }

            EditorGUILayout.Space();

            if (editor.DataComponent != null && editor.DataComponent.Data != null)
            {
                EditorGUILayout.Space();

                props.Draw(PropName.clearValue);

                if (GUILayout.Button("Clear Data"))
                {
                    var rawData = editor.DataComponent.Data.RawData;
                    for (int i = 0; i < rawData.Length; ++i)
                    {
                        rawData[i] = editor.ClearValue;
                    }

                    Regenerate();
                }
                if (editor.DataComponent.Data.HasHeights)
                {
                    if (GUILayout.Button("Clear Height Data"))
                    {
                        var rawData = editor.DataComponent.Data.HeightsRawData;
                        for (int i = 0; i < rawData.Length; ++i)
                        {
                            rawData[i] = 0;
                        }

                        Regenerate();
                    }
                }
                EditorGUILayout.Space();

                if (Data != null)
                {
                    var creationInfo = editor.DataComponent.CreationInfo;
                    bool sizeMatches = Data.ColNum == creationInfo.ColNum && Data.RowNum == creationInfo.RowNum;
                    bool heightMatches = Data.HasHeights == creationInfo.HasHeightData;
                    bool cullingMatches = Data.HasCullingData == creationInfo.HasCullingData;
                    if (!sizeMatches || !heightMatches || !cullingMatches)
                    {
                        EditorGUILayout.Space();
                        if (!sizeMatches)
                        {
                            EditorGUILayout.HelpBox("Data size and creation info size are mismatched!", MessageType.Warning, true);
                        }
                        if (!heightMatches)
                        {
                            EditorGUILayout.HelpBox($"Data has heights:{Data.HasHeights} creation info hasHeights:{creationInfo.HasHeightData}, mismatch!", MessageType.Warning, true);
                        }
                        if (!cullingMatches)
                        {
                            EditorGUILayout.HelpBox($"Data has culling:{Data.HasCullingData} creation info has culling:{creationInfo.HasCullingData}, mismatch!", MessageType.Warning, true);
                        }
                        if (GUILayout.Button("Recreate Data"))
                        {
                            using (var data = creationInfo.Create())
                            {
                                var oldData = editor.DataComponent.Data;
                                int col = Math.Min(data.ColNum, oldData.ColNum);
                                int row = Math.Min(data.RowNum, oldData.RowNum);
                                for (int y = 0; y < row; ++y)
                                {
                                    for (int x = 0; x < col; ++x)
                                    {
                                        data.SetDistanceAt(x, y, oldData.DistanceAt(x, y));

                                        if (data.HasHeights && oldData.HasHeights)
                                        {
                                            data.SetHeightAt(x, y, oldData.HeightAt(x, y));
                                        }

                                        if (data.HasCullingData && oldData.HasCullingData)
                                        {
                                            data.SetCullingAt(x, y, oldData.CullingAt(x, y));
                                        }
                                    }
                                }

                                editor.DataComponent.UpdateData(data);

                                if (autoSave)
                                {
                                    editor.DataComponent.Save();
                                }

                                Regenerate();
                            }
                        }
                        EditorGUILayout.Space();
                    }
                }

                if (GUILayout.Button("Load"))
                {
                    editor.DataComponent.Load();

                    Regenerate();
                }

                if (GUILayout.Button("Save"))
                {
                    editor.DataComponent.Save();
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void BeforeSceneGUI(SceneView sceneView)
        {
            Handles.matrix = editor.transform.localToWorldMatrix;

            var style = (GUIStyle)"GV Gizmo DropDown";
            Vector2 ribbon = style.CalcSize(sceneView.titleContent);
            ribbonHeight = ribbon.y;

            if (editMode)
            {
                HandleEditMode();
            }
        }

        public void OnSceneGUI()
        {
            if (editor == null || editor.DataComponent == null || Data == null)
            {
                return;
            }

            CheckChanges();

            Handles.matrix = editor.transform.localToWorldMatrix;

            DrawBoundingBox();
            DrawBrush();
            DrawGUI();
        }

        private void CheckChanges()
        {
            if (editor.Meshers != null)
            {
                if (meshers == null || meshers.Count != editor.Meshers.Length)
                {
                    CreateMeshers();
                }
            }
        }

        static private bool DoesDataMatch(Data data, MarchingSquaresComponent.DataCreationInfo info)
            => data.ColNum == info.ColNum && data.RowNum == info.RowNum &&
            data.HasHeights == info.HasHeightData &&
            data.HasCullingData == info.HasCullingData;

        private void HandleEditMode()
        {
            var mousePos = GetMousePostion();
            Ray ray = GetRay(mousePos);
            var hitPos = CalcPlaneHitPoint(ray);
            hitPos = editor.transform.InverseTransformPoint(hitPos);

            shouldDrawBrush = false;

            float screenHeight = SceneView.lastActiveSceneView.position.height;
            float brushLimit = screenHeight - brushGUIY + BrushSettingsGUIBorder;
            if (IsOverBoard(hitPos.x, hitPos.z) && mousePos.y > brushLimit)
            {
                shouldDrawBrush = true;
                brushPosition = hitPos;

                HandleEvents(Event.current, hitPos);
                SceneView.currentDrawingSceneView.Repaint();
            }
        }

        private void HandleEvents(Event e, Vector3 hitPos)
        {
            switch (e.type)
            {
                case EventType.MouseDown:
                    {
                        if (e.button == LeftMouseButton)
                        {
                            DrawAt(hitPos.x, hitPos.z);
                            e.Use();
                        }
                        break;
                    }
                case EventType.MouseDrag:
                    {
                        if (e.button == LeftMouseButton)
                        {
                            DrawAt(hitPos.x, hitPos.z);
                            e.Use();
                        }
                        break;
                    }
                case EventType.MouseUp:
                    {
                        if (e.button == LeftMouseButton)
                        {
                            e.Use();
                        }
                        break;
                    }
            }
        }

        private void DrawAt(float x, float y)
        {
            if (editor.EditMode == Mode.HeightChange && !Data.HasHeights)
            {
                return;
            }

            MarchingSquaresEditorComponent.DrawAt(x, y, Data, editor);

            if (removeBorder)
            {
                Data.RemoveBorder();
            }

            Regenerate();
        }

        private void Regenerate()
        {
            if (meshers != null && meshers.Count > 0)
            {
                foreach(var mesher in meshers)
                {
                    mesher.Regenerate(Data);
                }
            }
        }

        private bool IsOverBoard(float x, float y)
            =>  x > -editor.BrushRadius && y > -editor.BrushRadius && 
                x < editor.DataComponent.ColNum * editor.CellSize + editor.BrushRadius &&
                y < editor.DataComponent.RowNum * editor.CellSize + editor.BrushRadius;

        private void DrawBrush()
        {
            if (shouldDrawBrush)
            {
                Handles.color = DefBrushColor;
                if (editor.EditMode == Mode.HeightChange && !Data.HasHeights)
                {
                    Handles.color = InvalidBrushColor;
                }

                if (editor.BrushShape == Shape.Circle)
                {
                    Handles.DrawWireDisc(brushPosition, editor.transform.up, editor.BrushRadius);
                }
                else
                {
                    float size = editor.BrushRadius * 2;
                    Handles.DrawWireCube(brushPosition, new Vector3(size, 0.1f, size));
                }
            }
        }

        private void DrawGUI()
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(5, 5, DefButtonWidth, 270));
            GUILayout.BeginVertical();

            if (GUILayout.Button(editMode ? "Edit Mode" : "View Mode"))
            {
                editMode = !editMode;
            }

            if (meshers == null || meshers.Count == 0)
            {
                GUILayout.Label("No meshers set to draw data!");
            }
            else
            {    
                if (GUILayout.Button("Regenerate"))
                {
                    Regenerate();
                }
            }

            showGrid = GUILayout.Toggle(showGrid, "Show Grid");

            if (editMode)
            {
                autoSave = GUILayout.Toggle(autoSave, "AutoSave");
                showDistances = GUILayout.Toggle(showDistances, "Show Distance");
                removeBorder = GUILayout.Toggle(removeBorder, "Remove Border");
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();

            if (editMode)
            {
                SceneView sceneView = SceneView.lastActiveSceneView;
                float screenHeight = sceneView.position.height - ribbonHeight;

                GUI.backgroundColor = Color.black;

                float space = EditorGUIUtility.standardVerticalSpacing;
                float border = 2 * space;

                brushGUIY = screenHeight - DefButtonHeight - space - border;
                float editorSize = DefButtonHeight + space + border;
                if (editor.EditMode == Mode.HeightChange)
                {
                    brushGUIY -= (DefButtonHeight + space) * 2;
                    editorSize += DefButtonHeight * 2;
                }

                GUILayout.BeginArea(new Rect(space, brushGUIY, sceneView.position.width, editorSize));

                if (editor.EditMode == Mode.HeightChange)
                {
                    if (Data.HasHeights)
                    {
                        GUILayout.BeginHorizontal();

                        GUILabel("Height Brush", 80);
                        editor.HeightBrush = GUIEnumButton<HeightBrushMode>(ref heightBrushModeIndex, 55);

                        GUILabel("Level Change", 85);
                        editor.HeightChange = GUIEnumButton<HeightChangeMode>(ref heightChangeModeIndex, 60);

                        GUILabel("Change Scale", 60);
                        editor.ChangeScale = FloatRangeSlider(editor.ChangeScale, 0, 1f, 40);

                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        if (editor.HeightChange == HeightChangeMode.Absolute)
                        {
                            GUILabel("Height", 45);
                            editor.AbsoluteHeightLevel = FloatRangeSlider(editor.AbsoluteHeightLevel, editor.MinHeightLevel, editor.MaxHeightLevel, 60);
                        }
                        else
                        {
                            GUILabel("Min", 30);
                            editor.MinHeightLevel = EditorGUILayout.DelayedFloatField(editor.MinHeightLevel, GUILayout.Width(25));
                            GUILabel("Max", 30);
                            editor.MaxHeightLevel = EditorGUILayout.DelayedFloatField(editor.MaxHeightLevel, GUILayout.Width(25));

                            GUILabel("Value", 45);
                            editor.HeightChangeValue = FloatRangeSlider(editor.HeightChangeValue, -0.5f, 0.5f, 60);
                        }
                        GUILayout.EndHorizontal();
                    }
                    else
                    {
                        GUILayout.BeginHorizontal();
                        GUILabel("Data has no heights data!");
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        GUILabel("---");
                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.BeginHorizontal();
                
                GUILabel("Mode", 45);
                editor.EditMode = GUIEnumButton<Mode>(ref brushModeIndex, 100);

                GUILabel("Shape", 45);
                editor.BrushShape = GUIEnumButton<Shape>(ref brushShapeIndex, 70);

                GUILabel("Radius", 45);
                editor.BrushRadius = FloatRangeSlider(editor.BrushRadius, 0.1f, 5f, 60);
  
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }

            Handles.EndGUI();
        }

        static private void CopyData(Data from, Data to)
        {
            int minCol = Mathf.Min(from.ColNum, to.ColNum);
            int minRow = Mathf.Min(from.RowNum, to.RowNum);
            for (int y = 0; y < minRow; ++y)
            {
                for (int x = 0; x < minCol; ++x)
                {
                    to.SetDistanceAt(x, y, from.DistanceAt(x, y));

                    if (from.HasHeights && to.HasHeights)
                    {
                        to.SetHeightAt(x, y, from.HeightAt(x, y));
                    }

                    if (from.HasCullingData && to.HasCullingData)
                    {
                        to.SetCullingAt(x, y, from.CullingAt(x, y));
                    }
                }
            }
        }

        private T GUIEnumButton<T>(ref int valueIndex, int width) where T : Enum
        {
            var labels = Enum.GetNames(typeof(T));
            valueIndex = Mathf.Clamp(valueIndex, 0, labels.Length);
            if (GUILayout.Button(labels[valueIndex], GUILayout.Width(width)))
            {
                valueIndex = (valueIndex + 1) % labels.Length;
            }

            var values = Enum.GetValues(typeof(T));
            return (T)values.GetValue(valueIndex);
        }

        private float FloatRangeSlider(float value, float min, float max, float sliderWidth, string format = "0.00")
        {
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(sliderWidth));
            float textRad;
            if (float.TryParse(GUILayout.TextField(value.ToString(format), GUILayout.Width(40)), out textRad))
            {
                value = textRad;
            }
            return value;
        }

        private Vector3 GetMousePostion()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            
            var style = (GUIStyle)"GV Gizmo DropDown";
            Vector2 ribbon = style.CalcSize(sceneView.titleContent);

            Vector2 sceneViewCorrectSize = sceneView.position.size;
            sceneViewCorrectSize.y -= ribbon.y;

            Vector2 mousePosFlipped = Event.current.mousePosition;
            mousePosFlipped.y = sceneViewCorrectSize.y - mousePosFlipped.y;
            
            return mousePosFlipped;
        }

        private Ray GetRay(Vector3 mousePos)
            => SceneView.lastActiveSceneView.camera.ScreenPointToRay(mousePos);

        private Vector3 CalcPlaneHitPoint(Ray ray)
        {
            Plane plane = new Plane(editor.transform.up, editor.transform.position);
            float enter;
            plane.Raycast(ray, out enter);
            return ray.GetPoint(enter);
        }

        private void DrawBoundingBox()
        {
            var size = Vector3.one;
            size.x = editor.CellSize * (Data.ColNum - 1);
            size.y = 0.1f;
            size.z = editor.CellSize * (Data.RowNum - 1);

            var pos = Vector3.zero;
            pos.x += size.x / 2f;
            pos.y -= size.y / 2f;
            pos.z += size.z / 2f;
            Handles.DrawWireCube(pos, size);

            if (showGrid)
            {
                var color = Color.white;
                color.a = 0.6f;
                Handles.color = color;

                Vector3 start = new Vector3(0, 0, 0);
                Vector3 end = new Vector3(0, 0, size.z);
                for (int i = 0; i < Data.ColNum - 1; ++i)
                {
                    start.x = i * editor.CellSize;
                    end.x = start.x;
                    Handles.DrawLine(start, end);
                }

                start = new Vector3(0, 0, 0);
                end = new Vector3(size.x, 0, 0);
                for (int i = 0; i < Data.RowNum - 1; ++i)
                {
                    start.z = i * editor.CellSize;
                    end.z = start.z;
                    Handles.DrawLine(start, end);
                }
            }

            if (showDistances)
            {
                if (editor.DataComponent != null && editor.DataComponent.Data != null)
                {
                    pos = new Vector3(0, 0.5f, 0);
                    for (int y = 0; y < Data.RowNum; ++y)
                    {
                        for (int x = 0; x < Data.ColNum; ++x)
                        {
                            pos.x = x * editor.CellSize + editor.CellSize * 0.5f;
                            pos.z = y * editor.CellSize + editor.CellSize * 0.5f;

                            if (Mathf.Abs(brushPosition.x - pos.x) < editor.BrushRadius && Mathf.Abs(brushPosition.z - pos.z) < editor.BrushRadius)
                            {
                                Handles.Label(pos, editor.DataComponent.Data.DistanceAt(x, y).ToString("0.00"));
                            }
                        }
                    }
                }
            }
        }

        private class EditorMesher
        {
            private MarchingSquaresComponent original;

            private Mesh mesh;
            private MarchingSquaresMesher mesher;

            public EditorMesher(MarchingSquaresComponent original)
            {
                this.original = original;

                mesh = new Mesh();
                mesher = new MarchingSquaresMesher();
            }

            public void Regenerate(Data data)
            {
                original.InitInfo.Init(mesher, data);
                mesher.Start();
                mesher.Complete(mesh);
                original.MeshFilter.sharedMesh = mesh;
            }
        }
    }
}