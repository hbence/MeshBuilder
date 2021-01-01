using System;
using UnityEngine;
using Unity.Collections;

using static MeshBuilder.Utils;

namespace MeshBuilder
{
    public partial class MarchingSquaresMesher : Builder
    {
        public class Data : IDisposable
        {
            private Volume<float> distances;
            public NativeArray<float> RawData => distances.Data;

            public int ColNum => distances.Extents.X;
            public int RowNum => distances.Extents.Z;

            public float DistanceAt(int x, int y) => distances[x, 0, y];
            public float SetDistanceAt(int x, int y, float dist) => distances[x, 0, y] = dist;

            public Data(int col, int row, float[] distanceData = null)
            {
                distances = new Volume<float>(col, 1, row);
                if (distanceData != null)
                {
                    if (distances.Length == distanceData.Length)
                    {
                        for (int i = 0; i < distanceData.Length; ++i)
                        {
                            distances[i] = distanceData[i];
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
                SafeDispose(ref distances);
            }

            public void UpdateData(float[] distanceData)
            {
                if (distances.Length != distanceData.Length)
                {
                    Debug.LogWarning("distance data mismatch, clamped");
                }

                int length = Mathf.Min(distanceData.Length, distances.Length);
                for (int i = 0; i < length; ++i)
                {
                    distances[i] = distanceData[i];
                }
            }

            public void Clear()
            {
                for (int i = 0; i < distances.Length; ++i)
                {
                    distances[i] = -1;
                }
            }

            public void ApplyCircle(float x, float y, float rad, float cellSize)
            {
                Apply(x - rad, y - rad, x + rad, y + rad, cellSize, (float cx, float cy) => CircleDist(cx, cy, x, y, rad, cellSize));
            }

            public void RemoveCircle(float x, float y, float rad, float cellSize)
            {
                Remove(x - rad, y - rad, x + rad, y + rad, cellSize, (float cx, float cy) => CircleDist(cx, cy, x, y, rad, cellSize));
            }

            public void ApplyRectangle(float x, float y, float halfWidth, float halfHeight, float cellSize)
            {
                Apply(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight, cellSize, (float cx, float cy) => RectangleDist(cx, cy, x, y, halfWidth, halfHeight, cellSize));
            }

            public void RemoveRectangle(float x, float y, float halfWidth, float halfHeight, float cellSize)
            {
                Remove(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight, cellSize, (float cx, float cy) => RectangleDist(cx, cy, x, y, halfWidth, halfHeight, cellSize));
            }

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
                        distances[col, 0, row] = Mathf.Max(dist, distances[col, 0, row]);
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
                        distances[col, 0, row] -= Mathf.Max(dist, 0);
                    }
                }
            }

            public void RemoveBorder()
            {
                for (int col = 0; col < ColNum; ++col)
                {
                    distances[col, 0, 0] = -1;
                    distances[col, 0, RowNum -1] = -1;
                }
                for (int row = 0; row < RowNum - 1; ++row)
                {
                    distances[0, 0, row] = -1;
                    distances[ColNum - 1, 0, row] = -1;
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
