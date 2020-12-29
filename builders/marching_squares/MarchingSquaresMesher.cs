using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using static MeshBuilder.Utils;
using MeshBuffer = MeshBuilder.MeshData.Buffer;

namespace MeshBuilder
{
    public class MarchingSquaresMesher : Builder
    {
        private const uint DefMeshDataBufferFlags = (uint)MeshBuffer.Vertex | (uint)MeshBuffer.Triangle;

        private const int CalculateVertexBatchNum = 128;
        private const int MeshTriangleBatchNum = 128;

        public float CellSize { get; private set; }
        public Data DistanceData { get; private set; }
        public int ColNum => DistanceData.ColNum;
        public int RowNum => DistanceData.RowNum;

        public uint MeshDataBufferFlags { get; set; } = DefMeshDataBufferFlags;

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

            var cornerJob = new GenerateCorners
            {
                distanceColNum = ColNum,
                distanceRowNum = RowNum,
                
                cellMesher = cellMesher,

                distances = DistanceData.RawData,
                corners = corners,

                vertices = vertices,
                indices = triangles
            };
            lastHandle = cornerJob.Schedule(lastHandle);

            var vertexJob = new CalculateVertices
            {
                cornerColNum = ColNum,
                cellSize = CellSize,
                cellMesher = cellMesher,

                cornerInfos = corners,
                vertices = vertices.AsDeferredJobArray()
            };
            var vertexHandle = vertexJob.Schedule(corners.Length, CalculateVertexBatchNum, lastHandle);
            
            var trianglesJob = new CalculateTriangles
            {
                cornerColNum = ColNum,
                cellMesher = cellMesher,
                cornerInfos = corners,
                triangles = triangles.AsDeferredJobArray()
            };
            int cellCount = (ColNum - 1) * (RowNum - 1);
            var trianglesHandle = trianglesJob.Schedule(cellCount, MeshTriangleBatchNum, lastHandle);

            return JobHandle.CombineDependencies(vertexHandle, trianglesHandle);
        }

        protected override void EndGeneration(Mesh mesh)
        {
            using (MeshData data = new MeshData(vertices.Length, triangles.Length, Allocator.Temp, MeshDataBufferFlags))
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
                for (int row = 0; row < RowNum; ++row)
                {
                    float cy = row * cellSize;
                    for (int col = 0; col < ColNum; ++col)
                    {
                        float cx = col * cellSize;
                        float dist = (rad - Mathf.Sqrt(SQ(cx - x) + SQ(cy - y))) / cellSize;
                        distances[col, 0, row] = Mathf.Max(dist, distances[col, 0, row]);
                    }
                }
            }
 
            private static float SQ(float x) => x * x;
        }

        private struct CornerInfo
        {
            public byte config;

            public float cornerDist;
            public float rightDist;
            public float topDist;

            public int vertexIndex;
            public int bottomEdgeIndex;
            public int leftEdgeIndex;

            public int triIndexStart;
            public int triIndexLength;
        }

        [BurstCompile]
        private struct GenerateCorners : IJob
        {
            private const bool Inner = false;
            private const bool OnBorder = true;

            public int distanceColNum;
            public int distanceRowNum;
            
            public CellMesher cellMesher;

            [ReadOnly] public NativeArray<float> distances;
            
            [WriteOnly] public NativeArray<CornerInfo> corners;
            
            public NativeList<float3> vertices;
            public NativeList<int> indices;

            public void Execute()
            {
                int nextVertex = 0;
                int nextTriangleIndex = 0;
                // the border cases are separated to avoid boundary checking
                // not sure if it's worth it...
                // inner
                for (int y = 0; y < distanceRowNum - 1; ++y)
                {
                    for (int x = 0; x < distanceColNum - 1; ++x)
                    {
                        int index = y * distanceColNum + x;
                        float corner = distances[index];
                        float right = distances[index + 1];
                        float topRight = distances[index + 1 + distanceColNum];
                        float top = distances[index + distanceColNum];
                        corners[index] = cellMesher.GenerateInfo(corner, right, topRight, top, ref nextVertex, ref nextTriangleIndex, Inner);
                    }
                }
                // top border
                for (int x = 0, y = distanceRowNum - 1; x < distanceColNum - 1; ++x)
                {
                    int index = y * distanceColNum + x;
                    float corner = distances[index];
                    float right = distances[index + 1];
                    corners[index] = cellMesher.GenerateInfo(corner, right, -1, -1, ref nextVertex, ref nextTriangleIndex, OnBorder);
                }
                // right border
                for (int x = distanceColNum - 1, y = 0; y < distanceRowNum - 1; ++y)
                {
                    int index = y * distanceColNum + x;
                    float corner = distances[index];
                    float top = distances[index + distanceColNum];
                    corners[index] = cellMesher.GenerateInfo(corner, -1, -1, top, ref nextVertex, ref nextTriangleIndex, OnBorder);
                }
                // top right corner
                int last = distanceColNum * distanceRowNum - 1;
                corners[last] = cellMesher.GenerateInfo(distances[last], -1, -1, -1, ref nextVertex, ref nextTriangleIndex, OnBorder);
                
                vertices.ResizeUninitialized(nextVertex);
                indices.ResizeUninitialized(nextTriangleIndex);
            }
        }

        [BurstCompile]
        private struct CalculateVertices : IJobParallelFor
        {
            public int cornerColNum;
            public float cellSize;

            public CellMesher cellMesher;

            [ReadOnly] public NativeArray<CornerInfo> cornerInfos;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                CornerInfo info = cornerInfos[index];
                int x = index % cornerColNum;
                int y = index / cornerColNum;
                cellMesher.CalculateVertices(x, y, cellSize, info, vertices);
            }
        }

        [BurstCompile]
        private struct CalculateTriangles : IJobParallelFor
        {
            public int cornerColNum;
            
            public CellMesher cellMesher;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<CornerInfo> cornerInfos;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<int> triangles;

            public void Execute(int index)
            {
                int cellColNum = cornerColNum - 1;
                int x = index % cellColNum;
                int y = index / cellColNum;
                index = y * cornerColNum + x;

                CornerInfo bl = cornerInfos[index];
                CornerInfo br = cornerInfos[index + 1];
                CornerInfo tr = cornerInfos[index + 1 + cornerColNum];
                CornerInfo tl = cornerInfos[index + cornerColNum];
                cellMesher.CalculateIndices(bl, br, tr, tl, triangles);
            }
        }

        private struct CellMesher
        {
            public CornerInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                            ref int nextVertices, ref int nextTriIndex, bool onBorder)
            {
                CornerInfo info = new CornerInfo
                {
                    config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance),

                    cornerDist = cornerDistance,
                    rightDist = rightDistance,
                    topDist = topDistance,

                    vertexIndex = -1,
                    leftEdgeIndex = -1,
                    bottomEdgeIndex = -1,

                    triIndexStart = nextTriIndex,
                    triIndexLength = 0
                };

                if (!onBorder)
                {
                    info.triIndexLength = CalcTriIndexCount(info.config);
                    nextTriIndex += info.triIndexLength;
                }

                bool hasBL = HasMask(info.config, MaskBL);
                if (hasBL)
                {
                    info.vertexIndex = nextVertices;
                    ++nextVertices;
                }

                if (hasBL != HasMask(info.config, MaskTL))
                {
                    info.leftEdgeIndex = nextVertices;
                    ++nextVertices;
                }

                if (hasBL != HasMask(info.config, MaskBR))
                {
                    info.bottomEdgeIndex = nextVertices;
                    ++nextVertices;
                }
                
                return info;
            }

            public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, NativeArray<float3> vertices)
            {
                float3 pos = new float3(x * cellSize, 0, y * cellSize);
                if (info.vertexIndex >= 0)
                {
                    vertices[info.vertexIndex] = pos;
                }
                if (info.leftEdgeIndex >= 0)
                {
                    vertices[info.leftEdgeIndex] = pos + new float3(0, 0, cellSize * LerpT(info.cornerDist, info.topDist));
                }
                if (info.bottomEdgeIndex >= 0)
                {
                    vertices[info.bottomEdgeIndex] = pos + new float3(cellSize * LerpT(info.cornerDist, info.rightDist), 0, 0);
                }
            }

            static private float LerpT(float a, float b) => Mathf.Abs(a) / (Mathf.Abs(a) + Mathf.Abs(b));

            private static int CalcTriIndexCount(byte config)
            {
                switch (config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL: return 2 * 3;
                    // corners
                    case MaskBL: return 1 * 3;
                    case MaskBR: return 1 * 3;
                    case MaskTR: return 1 * 3;
                    case MaskTL: return 1 * 3;
                    // halves
                    case MaskBL | MaskBR: return 2 * 3;
                    case MaskTL | MaskTR: return 2 * 3;
                    case MaskBL | MaskTL: return 2 * 3;
                    case MaskBR | MaskTR: return 2 * 3;
                    // diagonals
                    case MaskBL | MaskTR: return 4 * 3;
                    case MaskTL | MaskBR: return 4 * 3;
                    // three quarters
                    case MaskBL | MaskTR | MaskBR: return 3 * 3;
                    case MaskBL | MaskTL | MaskBR: return 3 * 3;
                    case MaskBL | MaskTL | MaskTR: return 3 * 3;
                    case MaskTL | MaskTR | MaskBR: return 3 * 3;
                }
                return 0;
            }

            public void CalculateIndices(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, 
                                  NativeArray<int> triangles)
            {
                int triangleIndex = bl.triIndexStart;
                switch (bl.config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), Vertex(tr));
                            AddTri(triangles, ref triangleIndex, Vertex(bl), Vertex(tr), Vertex(br));
                            break;
                        }
                    // corners
                    case MaskBL:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(bl), LeftEdge(bl), BottomEdge(bl));
                            break;
                        }
                    case MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    case MaskTR:
                        {
                            AddTri(triangles, ref triangleIndex, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(tl), BottomEdge(tl), LeftEdge(bl));
                            break;
                        }
                    // halves
                    case MaskBL | MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(bl), LeftEdge(bl), Vertex(br));
                            AddTri(triangles, ref triangleIndex, Vertex(br), LeftEdge(bl), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(tl), Vertex(tr), LeftEdge(bl));
                            AddTri(triangles, ref triangleIndex, LeftEdge(bl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskBL | MaskTL:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(bl), BottomEdge(tl), BottomEdge(bl));
                            AddTri(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), BottomEdge(tl));
                            break;
                        }
                    case MaskBR | MaskTR:
                        {
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), Vertex(tr), Vertex(br));
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), BottomEdge(tl), Vertex(tr));
                            break;
                        }
                    // diagonals
                    case MaskBL | MaskTR:
                        {
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), Vertex(bl), LeftEdge(bl));
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(bl), LeftEdge(br));
                            AddTri(triangles, ref triangleIndex, LeftEdge(br), LeftEdge(bl), BottomEdge(tl));
                            AddTri(triangles, ref triangleIndex, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, LeftEdge(bl), Vertex(tl), BottomEdge(tl));
                            AddTri(triangles, ref triangleIndex, LeftEdge(bl), BottomEdge(tl), BottomEdge(bl));
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), BottomEdge(tl), LeftEdge(br));
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    // three quarters
                    case MaskBL | MaskTR | MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(br), Vertex(bl), LeftEdge(bl));
                            AddTri(triangles, ref triangleIndex, Vertex(br), LeftEdge(bl), BottomEdge(tl));
                            AddTri(triangles, ref triangleIndex, Vertex(br), BottomEdge(tl), Vertex(tr));
                            break;
                        }
                    case MaskBL | MaskTL | MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), BottomEdge(tl));
                            AddTri(triangles, ref triangleIndex, Vertex(bl), BottomEdge(tl), LeftEdge(br));
                            AddTri(triangles, ref triangleIndex, Vertex(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    case MaskBL | MaskTL | MaskTR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(tl), BottomEdge(bl), Vertex(bl));
                            AddTri(triangles, ref triangleIndex, Vertex(tl), LeftEdge(br), BottomEdge(bl));
                            AddTri(triangles, ref triangleIndex, Vertex(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR | MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(tr), LeftEdge(bl), Vertex(tl));
                            AddTri(triangles, ref triangleIndex, Vertex(tr), BottomEdge(bl), LeftEdge(bl));
                            AddTri(triangles, ref triangleIndex, Vertex(tr), Vertex(br), BottomEdge(bl));
                            break;
                        }
                }
            }

            private static void AddTri(NativeArray<int> triangles, ref int nextIndex, int a, int b, int c)
            {
                triangles[nextIndex] = a;
                ++nextIndex;
                triangles[nextIndex] = b;
                ++nextIndex;
                triangles[nextIndex] = c;
                ++nextIndex;
            }

            private static int Vertex(CornerInfo info) => info.vertexIndex;
            private static int LeftEdge(CornerInfo info) => info.leftEdgeIndex;
            private static int BottomEdge(CornerInfo info) => info.bottomEdgeIndex;
        }

        private const float DistanceLimit = 0f;

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
