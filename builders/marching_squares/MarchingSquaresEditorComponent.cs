using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    public class MarchingSquaresEditorComponent : MonoBehaviour
    {
        public enum Shape { Circle, Rectange }
        public enum Mode { Add, Erase, HeightChange }
        public enum HeightBrushMode { Smooth, Flat }
        public enum HeightChangeMode { Absolute, Relative }

        [SerializeField] private MarchingSquaresDataComponent dataComponent = null;
        public MarchingSquaresDataComponent DataComponent
        {
            get => dataComponent;
            set => dataComponent = value;
        }

        [SerializeField] private MarchingSquaresComponent[] meshers = null;
        public MarchingSquaresComponent[] Meshers
        {
            get => meshers;
            set => meshers = value;
        }

        [SerializeField] private Mode editMode = Mode.Add;
        public Mode EditMode
        {
            get => editMode;
            set => editMode = value;
        }

        [SerializeField] private Shape brushShape = Shape.Circle;
        public Shape BrushShape 
        {
            get => brushShape; 
            set => brushShape = value; 
        }

        [Range(0.1f, 10f)]
        [SerializeField] private float cellSize = 1f;
        public float CellSize
        {
            get => cellSize;
            set => cellSize = value;
        }

        [Range(0.1f, 10f)]
        [SerializeField] private float brushRadius = 1f;
        public float BrushRadius 
        { 
            get => brushRadius;
            set => brushRadius = value;
        }

        [SerializeField] private HeightBrushMode heightBrush = HeightBrushMode.Smooth;
        public HeightBrushMode HeightBrush
        {
            get => heightBrush;
            set => heightBrush = value;
        }

        [SerializeField] private HeightChangeMode heightChange = HeightChangeMode.Absolute;
        public HeightChangeMode HeightChange
        {
            get => heightChange;
            set => heightChange = value;
        }

        [Range(0, 0.1f)]
        [SerializeField] private float changeScale = 1f;
        public float ChangeScale
        {
            get => changeScale;
            set => changeScale = value;
        }

        [SerializeField] private float heightChangeValue = 1f;
        public float HeightChangeValue
        {
            get => heightChangeValue;
            set => heightChangeValue = value;
        }

        [SerializeField] private float minHeightLevel = -2f;
        public float MinHeightLevel
        {
            get => minHeightLevel;
            set => minHeightLevel = value;
        }

        [SerializeField] private float maxHeightLevel = 2f;
        public float MaxHeightLevel
        {
            get => maxHeightLevel;
            set => maxHeightLevel = value;
        }

        [SerializeField] private float absoluteHeightLevel = 1f;
        public float AbsoluteHeightLevel
        {
            get => absoluteHeightLevel;
            set => absoluteHeightLevel = value;
        }

        [SerializeField] private bool limitDistance = false;
        public bool LimitDistance
        {
            get => limitDistance;
            set => limitDistance = value;
        }

        [SerializeField] private float minLimitDistance = -1;
        public float MinLimitDistance
        {
            get => minLimitDistance;
            set => minLimitDistance = value;
        }

        [SerializeField] private float maxLimitDistance = 1;
        public float MaxLimitDistance
        {
            get => maxLimitDistance;
            set => maxLimitDistance = value;
        }

        [SerializeField] private float clearValue = MarchingSquaresMesher.Data.DefaultClearDistance;
        public float ClearValue
        {
            get => clearValue;
            set => clearValue = value;
        }

        public void DrawAt(float x, float y)
        {
            if (dataComponent != null && dataComponent.Data != null)
            {
                DrawAt(x, y, dataComponent.Data, this);
            }

            DataChanged();
        }

        public static void DrawAt(float x, float y, MarchingSquaresMesher.Data data, MarchingSquaresEditorComponent editor)
        {
            Shape brushShape = editor.brushShape;
            float brushRadius = editor.brushRadius;
            float cellSize = editor.cellSize;
            float absoluteHeight = editor.absoluteHeightLevel;
            float heightChange = editor.heightChangeValue;
            float minHeight = editor.minHeightLevel;
            float maxHeight = editor.maxHeightLevel;
            float changeScale = editor.changeScale;

            switch (editor.editMode)
            {
                case Mode.Add:
                    {
                        if (brushShape == Shape.Circle)
                        {
                            data.ApplyCircle(x, y, brushRadius, cellSize);
                        }
                        else if (brushShape == Shape.Rectange)
                        {
                            data.ApplyRectangle(x, y, brushRadius, brushRadius, cellSize);
                        }
                        break;
                    }
                case Mode.Erase:
                    {
                        if (brushShape == Shape.Circle)
                        {
                            data.RemoveCircle(x, y, brushRadius, cellSize);
                        }
                        else if (brushShape == Shape.Rectange)
                        {
                            data.RemoveRectangle(x, y, brushRadius, brushRadius, cellSize);
                        }
                        break;
                    }
                case Mode.HeightChange:
                    {
                        if (data.HasHeights)
                        {
                            if (editor.heightChange == HeightChangeMode.Absolute)
                            {
                                if (brushShape == Shape.Circle)
                                {
                                    if (editor.HeightBrush == HeightBrushMode.Smooth)
                                    {
                                        data.SetHeightCircleSmooth(x, y, brushRadius, absoluteHeight, cellSize, changeScale);
                                    }
                                    else
                                    {
                                        data.SetHeightCircleFlat(x, y, brushRadius, absoluteHeight, cellSize, changeScale);
                                    }
                                }
                                else if (brushShape == Shape.Rectange)
                                {
                                    if (editor.HeightBrush == HeightBrushMode.Smooth)
                                    {
                                        data.SetHeightRectangleSmooth(x, y, brushRadius, brushRadius, absoluteHeight, cellSize, changeScale);
                                    }
                                    else
                                    {
                                        data.SetHeightRectangleFlat(x, y, brushRadius, brushRadius, absoluteHeight, cellSize, changeScale);
                                    }
                                }
                            }
                            else
                            {
                                if (brushShape == Shape.Circle)
                                {
                                    if (editor.HeightBrush == HeightBrushMode.Smooth)
                                    {
                                        data.ChangeHeightCircleSmooth(x, y, brushRadius, heightChange * changeScale, cellSize, minHeight, maxHeight);
                                    }
                                    else
                                    {
                                        data.ChangeHeightCircleFlat(x, y, brushRadius, heightChange * changeScale, cellSize, minHeight, maxHeight);
                                    }
                                }
                                else if (brushShape == Shape.Rectange)
                                {
                                    if (editor.HeightBrush == HeightBrushMode.Smooth)
                                    {
                                        data.ChangeHeightRectangleSmooth(x, y, brushRadius, brushRadius, heightChange * changeScale, cellSize, minHeight, maxHeight);
                                    }
                                    else
                                    {
                                        data.ChangeHeightRectangleFlat(x, y, brushRadius, brushRadius, heightChange * changeScale, cellSize, minHeight, maxHeight);
                                    }
                                }
                            }
                        }
                        else
                        {
                            Debug.LogWarning("data has no height array!");
                        }
                        break;
                    }
            }

            if (editor.limitDistance)
            {
                LimitDataDistance(data, editor.minLimitDistance, editor.maxLimitDistance);
            }
        }

        public static void LimitDataDistance(MarchingSquaresMesher.Data data, float min, float max)
        {
            var dist = data.RawData;
            for (int i = 0; i < dist.Length; ++i)
            {
                dist[i] = Mathf.Clamp(dist[i], min, max);
            }
        }

        public void RemoveBorder()
        {
            if (dataComponent != null && dataComponent.Data != null)
            {
                dataComponent.Data.RemoveBorder();
            }

            DataChanged();
        }

        public void InitMeshers()
        {
            foreach(var mesher in meshers)
            {
                mesher?.Init();
            }
        }

        public void DataChanged()
        {
            foreach(var mesher in meshers)
            {
                mesher?.UpdateData(DataComponent.Data);
            }
        }
    }
}