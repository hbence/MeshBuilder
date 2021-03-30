using System;
using UnityEngine;
using Unity.Collections;

using static MeshBuilder.Utils;
using Unity.Mathematics;

namespace MeshBuilder
{
    public partial class MarchingSquaresMesher : Builder
    {
        public class Data : IDisposable
        {
            private Volume<float> distanceData;
            public NativeArray<float> RawData => distanceData.Data;

            private Volume<float> heightData;
            public NativeArray<float> HeightsRawData => heightData != null ? heightData.Data : default;

            private Volume<bool> cullingData;
            public NativeArray<bool> CullingDataRawData => cullingData != null ? cullingData.Data : default;

            public bool HasCullingData => cullingData != null;

            public int ColNum => distanceData.Extents.X;
            public int RowNum => distanceData.Extents.Z;

            public float DistanceAt(int x, int y) => distanceData[x, 0, y];
            public float SetDistanceAt(int x, int y, float dist) => distanceData[x, 0, y] = dist;

            public bool HasHeights => heightData != null;
            public float HeightAt(int x, int y) => heightData[x, 0, y];
            public float SetHeightAt(int x, int y, float dist) => heightData[x, 0, y] = dist;

            public Data(int col, int row, float[] distanceData = null)
            {
                this.distanceData = new Volume<float>(col, 1, row);
                if (distanceData != null)
                {
                    if (this.distanceData.Length == distanceData.Length)
                    {
                        for (int i = 0; i < distanceData.Length; ++i)
                        {
                            this.distanceData[i] = distanceData[i];
                        }
                    }
                    else
                    {
                        Debug.LogError("distance data length mismatch!");
                        Clear();
                    }
                }
                else
                {
                    Clear();
                }
            }

            public void Dispose()
            {
                SafeDispose(ref distanceData);
                SafeDispose(ref heightData);
                SafeDispose(ref cullingData);
            }

            public void UpdateData(float[] distanceData)
            {
                if (this.distanceData.Length != distanceData.Length)
                {
                    Debug.LogWarning("distance data mismatch, clamped");
                }

                int length = Mathf.Min(distanceData.Length, this.distanceData.Length);
                for (int i = 0; i < length; ++i)
                {
                    this.distanceData[i] = distanceData[i];
                }
            }

            public void InitHeights(float[] heightData = null)
            {
                SafeDispose(ref this.heightData);

                this.heightData = new Volume<float>(ColNum, 1, RowNum);
                if (heightData != null)
                {
                    UpdateHeights(heightData);
                }
            }

            public void InitCullingData(bool[] data = null)
            {
                SafeDispose(ref cullingData);

                cullingData = new Volume<bool>(ColNum, 1, RowNum);
                if (data != null)
                {
                    UpdateCullingData(data);
                }
            }

            public void UpdateHeights(float[] heightData)
            {
                if (this.heightData.Length != heightData.Length)
                {
                    Debug.LogWarning("distance data mismatch, clamped");
                }

                int length = Mathf.Min(heightData.Length, this.heightData.Length);
                for (int i = 0; i < length; ++i)
                {
                    this.heightData[i] = heightData[i];
                }
            }

            public void UpdateCullingData(bool[] data)
            {
                if (cullingData.Length != data.Length)
                {
                    Debug.LogWarning("culling data mismatch, clamped");
                }

                int length = Mathf.Min(cullingData.Length, data.Length);
                for (int i = 0; i < length; ++i)
                {
                    cullingData[i] = data[i];
                }
            }

            public void Clear()
            {
                for (int i = 0; i < distanceData.Length; ++i)
                {
                    distanceData[i] = -1;
                }
                if (heightData != null)
                {
                    for (int i = 0; i < heightData.Length; ++i)
                    {
                        heightData[i] = 0;
                    }
                }
                if (cullingData != null)
                {
                    for (int i = 0; i < cullingData.Length; ++i)
                    {
                        cullingData[i] = false;
                    }
                }
            }

            public void ApplyCircle(float x, float y, float rad, float cellSize)
                => Apply(x - rad, y - rad, x + rad, y + rad, cellSize, (float cx, float cy) => CircleDist(cx, cy, x, y, rad, cellSize));

            public void RemoveCircle(float x, float y, float rad, float cellSize)
                => Remove(x - rad, y - rad, x + rad, y + rad, cellSize, (float cx, float cy) => CircleDist(cx, cy, x, y, rad, cellSize));

            public void ApplyRectangle(float x, float y, float halfWidth, float halfHeight, float cellSize)
                => Apply(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight, cellSize, (float cx, float cy) => RectangleDist(cx, cy, x, y, halfWidth, halfHeight, cellSize));

            public void RemoveRectangle(float x, float y, float halfWidth, float halfHeight, float cellSize)
                => Remove(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight, cellSize, (float cx, float cy) => RectangleDist(cx, cy, x, y, halfWidth, halfHeight, cellSize));

            public void Apply(float left, float bottom, float right, float top, float cellSize, Func<float, float, float> CalcValue)
            {
                RangeInt cols, rows;
                CalcAABBRanges(left, bottom, right, top, cellSize, out cols, out rows);
                for (int row = rows.start; row <= rows.end; ++row)
                {
                    float cy = row * cellSize;
                    for (int col = cols.start; col <= cols.end; ++col)
                    {
                        float cx = col * cellSize;
                        float dist = CalcValue(cx, cy);
                        distanceData[col, 0, row] = Mathf.Max(dist, distanceData[col, 0, row]);
                    }
                }
            }

            public void Remove(float left, float bottom, float right, float top, float cellSize, Func<float, float, float> CalcValue)
            {
                RangeInt cols, rows;
                CalcAABBRanges(left, bottom, right, top, cellSize, out cols, out rows);
                for (int row = rows.start; row <= rows.end; ++row)
                {
                    float cy = row * cellSize;
                    for (int col = cols.start; col <= cols.end; ++col)
                    {
                        float cx = col * cellSize;
                        float dist = CalcValue(cx, cy);
                        distanceData[col, 0, row] -= Mathf.Max(dist, 0);
                    }
                }
            }

            public void ChangeHeightCircleFlat(float x, float y, float rad, float value, float cellSize, float minLimit = float.MinValue, float maxLimit = float.MaxValue)
                => ChangeHeightCircle(x, y, rad, value, cellSize, 
                    (float dist) => { return Linear(dist, 1); }, 
                    minLimit, maxLimit);

            public void ChangeHeightCircleSmooth(float x, float y, float rad, float value, float cellSize, float minLimit = float.MinValue, float maxLimit = float.MaxValue)
                => ChangeHeightCircle(x, y, rad, value, cellSize, 
                    (float dist) => { return Linear(dist, 1f / (rad / cellSize)); }, 
                    minLimit, maxLimit);

            public void ChangeHeightCircle(float x, float y, float rad, float value, float cellSize, Func<float, float> Interpolation, float minLimit = float.MinValue, float maxLimit = float.MaxValue)
                => ChangeHeight(x - rad, y - rad, x + rad, y + rad, cellSize, value, 
                    (float cx, float cy) => CircleDist(cx, cy, x, y, rad, cellSize), 
                    Interpolation, minLimit, maxLimit);

            public void ChangeHeightRectangleFlat(float x, float y, float halfWidth, float halfHeight, float value, float cellSize, float minLimit = float.MinValue, float maxLimit = float.MaxValue)
                => ChangeHeight(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight, cellSize, value, 
                    (float cx, float cy) => RectangleDist(cx, cy, x, y, halfWidth, halfHeight, cellSize), 
                    (float dist) => { return Linear(dist, 1); },
                    minLimit, maxLimit);

            public void ChangeHeightRectangleSmooth(float x, float y, float halfWidth, float halfHeight, float value, float cellSize, float minLimit = float.MinValue, float maxLimit = float.MaxValue)
                => ChangeHeight(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight, cellSize, value, 
                    (float cx, float cy) => RectangleDist(cx, cy, x, y, halfWidth, halfHeight, cellSize), 
                    (float dist) => { return Linear(dist, 1f / (Mathf.Min(halfWidth, halfHeight) / cellSize)); },
                    minLimit, maxLimit);

            public void ChangeHeightRectangle(float x, float y, float halfWidth, float halfHeight, float value, float cellSize, Func<float, float> Interpolation, float minLimit = float.MinValue, float maxLimit = float.MaxValue)
                => ChangeHeight(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight, cellSize, value, 
                    (float cx, float cy) => RectangleDist(cx, cy, x, y, halfWidth, halfHeight, cellSize), 
                    Interpolation, minLimit, maxLimit);

            static public float Linear(float dist, float scale) => math.clamp(dist * scale, 0, 1);

            public void ChangeHeight(float left, float bottom, float right, float top, float cellSize, float heightValue, Func<float, float, float> CalcDist, Func<float, float> Interpolation, float minLimit = float.MinValue, float maxLimit = float.MaxValue)
            {
                RangeInt cols, rows;
                CalcAABBRanges(left, bottom, right, top, cellSize, out cols, out rows);
                for (int row = rows.start; row <= rows.end; ++row)
                {
                    float cy = row * cellSize;
                    for (int col = cols.start; col <= cols.end; ++col)
                    {
                        float cx = col * cellSize;
                        float dist = CalcDist(cx, cy);
                        if (dist >= 0)
                        {
                            heightData[col, 0, row] += Interpolation(dist) * heightValue;
                            heightData[col, 0, row] = Mathf.Clamp(heightData[col, 0, row], minLimit, maxLimit);
                        }
                    }
                }
            }

            public void SetHeight(float left, float bottom, float right, float top, float cellSize, float heightValue, Func<float, float, float> CalcDist, Func<float, float> Interpolation)
            {
                RangeInt cols, rows;
                CalcAABBRanges(left, bottom, right, top, cellSize, out cols, out rows);
                for (int row = rows.start; row <= rows.end; ++row)
                {
                    float cy = row * cellSize;
                    for (int col = cols.start; col <= cols.end; ++col)
                    {
                        float cx = col * cellSize;
                        float dist = CalcDist(cx, cy);
                        if (dist >= 0)
                        {
                            heightData[col, 0, row] = Interpolation(dist) * heightValue;
                        }
                    }
                }
            }

            public void LimitHeight(float minimum, float maximum)
            {
                for (int i = 0; i < heightData.Length; ++i)
                {
                    heightData[i] = math.clamp(heightData[i], minimum, maximum);
                }
            }

            public void SetHeight(float value)
            {
                for (int i = 0; i < heightData.Length; ++i)
                {
                    heightData[i] = value;
                }
            }

            public void RemoveBorder()
            {
                for (int col = 0; col < ColNum; ++col)
                {
                    distanceData[col, 0, 0] = -1;
                    distanceData[col, 0, RowNum -1] = -1;
                }
                for (int row = 0; row < RowNum - 1; ++row)
                {
                    distanceData[0, 0, row] = -1;
                    distanceData[ColNum - 1, 0, row] = -1;
                }
            }

            private static float CircleDist(float cx, float cy, float x, float y, float rad, float cellSize)
                => (rad - Mathf.Sqrt(SQ(cx - x) + SQ(cy - y))) / cellSize;

            private static float RectangleDist(float cx, float cy, float x, float y, float halfWidth, float halfHeight, float cellSize)
                => Mathf.Min(halfWidth - Mathf.Abs(cx - x), halfHeight - Mathf.Abs(cy - y)) / cellSize;

            private const int AABBBoundary = 2;

            private void CalcAABBRanges(float left, float bottom, float right, float top, float cellSize, out RangeInt cols, out RangeInt rows)
            {
                int xStart = Mathf.Clamp(Mathf.FloorToInt(left / cellSize) - AABBBoundary, 0, ColNum - 1);
                int xEnd = Mathf.Clamp(Mathf.FloorToInt(right / cellSize) + AABBBoundary, 0, ColNum - 1);
                cols = new RangeInt(xStart, xEnd - xStart);

                int yStart = Mathf.Clamp(Mathf.FloorToInt(bottom / cellSize) - AABBBoundary, 0, RowNum - 1);
                int yEnd = Mathf.Clamp(Mathf.FloorToInt(top / cellSize) + AABBBoundary, 0, RowNum - 1);
                rows = new RangeInt(yStart, yEnd - yStart);
            }

            private static float SQ(float x) => x * x;
        }
    }
}
