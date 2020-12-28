using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using static MeshBuilder.Utils;
using System;
using JetBrains.Annotations;
using UnityEditor.Tilemaps;

namespace MeshBuilder
{
    public class MarchingSquaresMesher : Builder
    {
        private const uint DefMeshDataBufferFlags = (uint)MeshData.Buffer.Vertex | (uint)MeshData.Buffer.Triangle | (uint)MeshData.Buffer.UV;

        public float CellSize { get; private set; }
        public Data DistanceData { get; private set; }
        public int ColNum => DistanceData.ColNum;
        public int RowNum => DistanceData.RowNum;

        private NativeList<float3> vertices;
        private NativeList<int> triangles;

        public void Init(int colNum, int rowNum, float cellSize, float[] distanceData = null)
        {
            CellSize = cellSize;

            DistanceData?.Dispose();
            DistanceData = new Data(colNum, rowNum, distanceData);

            Inited();
        }

        override protected JobHandle StartGeneration(JobHandle lastHandle)
        {
            NativeArray<CornerInfo> corners = new NativeArray<CornerInfo>(ColNum * RowNum, Allocator.TempJob);
            AddTemp(corners);

            vertices = new NativeList<float3>(Allocator.TempJob);
            AddTemp(vertices);

            triangles = new NativeList<int>(Allocator.TempJob);
            AddTemp(triangles);

            CellMesher cellMesher = new CellMesher();

            var cornerJob = new GenerateCornersJob
            {
                distanceColNum = ColNum,
                distanceRowNum = RowNum,
                cellSize = CellSize,
                
                cellMesher = cellMesher,

                distances = DistanceData.RawData,
                corners = corners,
                vertices = vertices
            };
            lastHandle = cornerJob.Schedule(lastHandle);

            var generateMesh = new GenerateMesh
            {
                cornerColNum = ColNum,
                cornerRowNum = RowNum,
                cornerInfos = corners,

                cellMesher = cellMesher,

                triangles = triangles
            };
            lastHandle = generateMesh.Schedule(lastHandle);

            return lastHandle;
        }

        protected override void EndGeneration(Mesh mesh)
        {
            using (MeshData data = new MeshData(vertices.Length, triangles.Length, Allocator.Temp, (uint)MeshData.Buffer.Vertex | (uint)MeshData.Buffer.Triangle))
            {
                NativeArray<float3>.Copy(vertices, data.Vertices);
                NativeArray<int>.Copy(triangles, data.Triangles);
                data.UpdateMesh(mesh, MeshData.UpdateMode.Clear);
                mesh.RecalculateNormals();
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            DistanceData?.Dispose();
            DistanceData = null;
        }

        public class Data : IDisposable
        {
            private const float MaxDist = 100;

            private Volume<float> distances;
            public NativeArray<float> RawData => distances.Data;

            public int ColNum => distances.Extents.X;
            public int RowNum => distances.Extents.Z;

            public float DistanceAt(int x, int y) => distances[x, 0, y];

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
                    }
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
                    distances[i] = 0;
                }
            }

            public void ApplyCircle(float x, float y, float rad, float cellSize)
            {
                for (int row = 0; row < RowNum; ++row)
                {
                    float cy = row * cellSize;
                    for (int col = 0; col < ColNum; ++col)
                    {
                        float cx = col * cellSize;
                        float centerDist = Mathf.Max(SQ(cx - x) + SQ(cy - y), Mathf.Epsilon);
                        float dist = SQ(rad) / centerDist;
                        dist = Mathf.Min(dist, MaxDist);
                        distances[col, 0, row] = Mathf.Max(dist, distances[col, 0, row]);
                    }
                }
            }

            private static float SQ(float x) => x * x;
        }

        private struct CornerInfo
        {
            public byte config;
            public float3 position;
            public int vertexIndex;
            public float3 bottomEdge;
            public int bottomEdgeIndex;
            public float3 leftEdge;
            public int leftEdgeIndex;
        }

        private struct GenerateCornersJob : IJob
        {
            public int distanceColNum;
            public int distanceRowNum;
            public float cellSize;

            public CellMesher cellMesher;

            [ReadOnly] public NativeArray<float> distances;
            [WriteOnly] public NativeArray<CornerInfo> corners;
            public NativeList<float3> vertices;

            public void Execute()
            {
                for (int y = 0; y < distanceRowNum; ++y)
                {
                    for (int x = 0; x < distanceColNum; ++x)
                    {
                        float corner = GetDistance(x, y);
                        float right = GetDistance(x + 1, y);
                        float topRight = GetDistance(x + 1, y + 1);
                        float top = GetDistance(x, y + 1);
                        corners[y * distanceColNum + x] = cellMesher.GenerateInfo(
                            corner, right, topRight, top,
                            x, y, cellSize,
                            vertices
                        );
                    }
                }
            }

            private float GetDistance(int x, int y)
            {
                return (x < distanceColNum && y < distanceRowNum) ? distances[y * distanceColNum + x] : 0;
            }
        }

        private struct GenerateMesh : IJob
        {
            public int cornerColNum;
            public int cornerRowNum;
            [ReadOnly] public NativeArray<CornerInfo> cornerInfos;

            public CellMesher cellMesher;

            [WriteOnly] public NativeList<int> triangles;

            public void Execute()
            {
                int cellColNum = cornerColNum - 1;
                int cellRowNum = cornerRowNum - 1;

                for (int row = 0; row < cellRowNum; ++row)
                {
                    for (int col = 0; col < cellColNum; ++col)
                    {
                        CornerInfo bl = GetCorner(col, row);
                        CornerInfo br = GetCorner(col + 1, row);
                        CornerInfo tr = GetCorner(col + 1, row + 1);
                        CornerInfo tl = GetCorner(col, row + 1);
                        cellMesher.BuildCell(bl, br, tr, tl, triangles);
                    }
                }
            }

            private CornerInfo GetCorner(int x, int y) => cornerInfos[y * cornerColNum + x];
        }

        private const float DistanceLimit = 0.99f;

        private struct CellMesher
        {

            public CornerInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                    int x, int y, float cellSize,
                                    NativeList<float3> vertices)
            {
                CornerInfo info = new CornerInfo
                {
                    config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance),
                    vertexIndex = -1,
                    leftEdgeIndex = -1,
                    bottomEdgeIndex = -1,
                    position = new float3(x * cellSize, 0, y * cellSize),
                    leftEdge = new float3(x * cellSize, 0, y * cellSize + cellSize * 0.5f),
                    bottomEdge = new float3(x * cellSize + cellSize * 0.5f, 0, y * cellSize)
                };

                bool hasBL = HasMask(info.config, MaskBL);
                if (hasBL)
                {
                    vertices.Add(info.position);
                    info.vertexIndex = vertices.Length - 1;
                }

                if (hasBL != HasMask(info.config, MaskTL))
                {
                    vertices.Add(info.leftEdge);
                    info.leftEdgeIndex = vertices.Length - 1;
                }

                if (hasBL != HasMask(info.config, MaskBR))
                {
                    vertices.Add(info.bottomEdge);
                    info.bottomEdgeIndex = vertices.Length - 1;
                }

                return info;
            }

            public void BuildCell(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, 
                                  NativeList<int> triangles)
            {
                switch (bl.config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL:
                        {
                            AddTri(triangles, Vertex(bl), Vertex(tl), Vertex(tr));
                            AddTri(triangles, Vertex(bl), Vertex(tr), Vertex(br));
                            break;
                        }
                    // corners
                    case MaskBL:
                        {
                            AddTri(triangles, Vertex(bl), LeftEdge(bl), BottomEdge(bl));
                            break;
                        }
                    case MaskBR:
                        {
                            AddTri(triangles, BottomEdge(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    case MaskTR:
                        {
                            AddTri(triangles, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL:
                        {
                            AddTri(triangles, Vertex(tl), BottomEdge(tl), LeftEdge(bl));
                            break;
                        }
                    // halves
                    case MaskBL | MaskBR:
                        {
                            AddTri(triangles, Vertex(bl), LeftEdge(bl), Vertex(br));
                            AddTri(triangles, Vertex(br), LeftEdge(bl), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR:
                        {
                            AddTri(triangles, Vertex(tl), Vertex(tr), LeftEdge(bl));
                            AddTri(triangles, LeftEdge(bl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskBL | MaskTL:
                        {
                            AddTri(triangles, Vertex(bl), BottomEdge(tl), BottomEdge(bl));
                            AddTri(triangles, Vertex(bl), Vertex(tl), BottomEdge(tl));
                            break;
                        }
                    case MaskBR | MaskTR:
                        {
                            AddTri(triangles, BottomEdge(bl), Vertex(tr), Vertex(br));
                            AddTri(triangles, BottomEdge(bl), BottomEdge(tl), Vertex(tr));
                            break;
                        }
                    // diagonals
                    case MaskBL | MaskTR:
                        {
                            AddTri(triangles, BottomEdge(bl), Vertex(bl), LeftEdge(bl));
                            AddTri(triangles, BottomEdge(bl), LeftEdge(bl), LeftEdge(br));
                            AddTri(triangles, LeftEdge(br), LeftEdge(bl), BottomEdge(tl));
                            AddTri(triangles, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskBR:
                        {
                            AddTri(triangles, LeftEdge(bl), Vertex(tl), BottomEdge(tl));
                            AddTri(triangles, LeftEdge(bl), BottomEdge(tl), BottomEdge(bl));
                            AddTri(triangles, BottomEdge(bl), BottomEdge(tl), LeftEdge(br));
                            AddTri(triangles, BottomEdge(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    // three quarters
                    case MaskBL | MaskTR | MaskBR:
                        {
                            AddTri(triangles, Vertex(br), Vertex(bl), LeftEdge(bl));
                            AddTri(triangles, Vertex(br), LeftEdge(bl), BottomEdge(tl));
                            AddTri(triangles, Vertex(br), BottomEdge(tl), Vertex(tr));
                            break;
                        }
                    case MaskBL | MaskTL | MaskBR:
                        {
                            AddTri(triangles, Vertex(bl), Vertex(tl), BottomEdge(tl));
                            AddTri(triangles, Vertex(bl), BottomEdge(tl), LeftEdge(br));
                            AddTri(triangles, Vertex(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    case MaskBL | MaskTL | MaskTR:
                        {
                            AddTri(triangles, Vertex(tl), BottomEdge(bl), Vertex(bl));
                            AddTri(triangles, Vertex(tl), LeftEdge(br), BottomEdge(bl));
                            AddTri(triangles, Vertex(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR | MaskBR:
                        {
                            AddTri(triangles, Vertex(tr), LeftEdge(bl), Vertex(tl));
                            AddTri(triangles, Vertex(tr), BottomEdge(bl), LeftEdge(bl));
                            AddTri(triangles, Vertex(tr), Vertex(br), BottomEdge(bl));
                            break;
                        }
                }
            }

            private static void AddTri(NativeList<int> triangles, int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            private static int Vertex(CornerInfo info) => info.vertexIndex;
            private static int LeftEdge(CornerInfo info) => info.leftEdgeIndex;
            private static int BottomEdge(CornerInfo info) => info.bottomEdgeIndex;
        }

        private const byte MaskZero = 0;
        private const byte MaskBL = 1 << 0;
        private const byte MaskBR = 1 << 1;
        private const byte MaskTR = 1 << 2;
        private const byte MaskTL = 1 << 3;

        private static bool HasMask(byte config, byte mask) => (config & mask) != 0;

        private static byte CalcConfiguration(float bl, float br, float tr, float tl)
        {
            byte config = 0;

            config |= (bl >= DistanceLimit) ? MaskBL : MaskZero;
            config |= (br >= DistanceLimit) ? MaskBR : MaskZero;
            config |= (tr >= DistanceLimit) ? MaskTR : MaskZero;
            config |= (tl >= DistanceLimit) ? MaskTL : MaskZero;

            return config;
        }
    }
}
