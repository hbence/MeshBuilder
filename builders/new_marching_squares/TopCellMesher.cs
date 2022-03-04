using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

using static MeshBuilder.Utils;
using Data = MeshBuilder.MarchingSquaresMesher.Data;

namespace MeshBuilder.New
{
    public class TopCellMesher : CellMesher
    {
        private float cellSize;
        private Data data;
        private Info info;

        public void Init(Data data, float cellSize, Info info)
        {
            this.data = data;
            this.cellSize = cellSize;
            this.info = info;

            CheckData(data, info);

            Inited();
        }

        protected override JobHandle StartGeneration(JobHandle lastHandle = default)
        {
            Dispose();
            CheckData(data, info);

            CreateMeshData(info.GenerateNormals, info.GenerateUvs);

            bool useHeightData = info.UseHeightData && data.HasHeights;

            var infoArray = new NativeArray<TopCellInfo>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(infoArray);

            float3 normal = info.IsFlipped ? new float3(0, -1, 0) : new float3(0, 1, 0);

            if (info.UseCullingData && data.HasCullingData)
            {
                lastHandle = CalculateInfoJob.Schedule(data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }
            else
            {
                lastHandle = CalculateInfoNoCullingJob.Schedule(data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }

            JobHandle vertexHandle = ScheduleCalculateVerticesJob(data, info, useHeightData, cellSize, infoArray, vertices, lastHandle);

            if (info.GenerateUvs)
            {
                float scaleU = info.UScale;
                float scaleV = info.VScale;
                if (info.NormalizeUV)
                {
                    scaleU /= (data.ColNum - 1) * cellSize;
                    scaleV /= (data.RowNum - 1) * cellSize;
                }
                vertexHandle = CalculateUVsJob.Schedule(scaleU, scaleV, infoArray, vertices, uvs, vertexHandle);
            }

            JobHandle triangleHandle = ScheduleCalculateTrianglesJob(data.ColNum, data.RowNum, infoArray, triangles, info.IsFlipped, lastHandle);

            lastHandle = JobHandle.CombineDependencies(vertexHandle, triangleHandle);

            if (info.GenerateNormals && useHeightData)
            {
                lastHandle = CalculateNormals.ScheduleDeferred(vertices, triangles, normals, lastHandle);
            }

            return lastHandle;
        }

        private struct TopCellInfo
        {
            public CellInfo info;
            public CellVertices verts;
            public IndexSpan tris;
        }

        public struct IndexSpan
        {
            public int start;
            public byte length;

            public IndexSpan(int s, byte l)
            {
                start = s;
                length = l;
            }

            public int End => start + length;
            public bool Has => length > 0;
        }

        [BurstCompile]
        private struct CalculateInfoJob : IJob
        {
            public int distanceColNum;
            public int distanceRowNum;

            [ReadOnly] public NativeArray<float> distances;
            [ReadOnly] public NativeArray<bool> cullingData;

            public NativeList<float3> vertices;
            public NativeList<int> indices;

            public bool generateNormals;
            public float3 normal;
            public NativeList<float3> normals;

            public bool generateUVs;
            public NativeList<float2> uvs;

            [WriteOnly] public NativeArray<TopCellInfo> info;

            public void Execute()
            {
                int nextVertex = 0;
                int nextTriangleIndex = 0;

                // inner cells
                for (int y = 0; y < distanceRowNum - 1; ++y)
                {
                    for (int x = 0; x < distanceColNum - 1; ++x)
                    {
                        int index = y * distanceColNum + x;
                        float corner = distances[index];
                        float right = distances[index + 1];
                        float topRight = distances[index + 1 + distanceColNum];
                        float top = distances[index + distanceColNum];

                        bool hasCell = !cullingData[index];
                        info[index] = GenerateInfo(corner, right, topRight, top, ref nextVertex, ref nextTriangleIndex, hasCell);
                    }
                }                

                GenerateBorderInfo(distanceColNum, distanceRowNum, distances, ref nextVertex, ref nextTriangleIndex, info);

                InitializeMeshData(nextVertex, nextTriangleIndex, ref vertices, ref indices, generateNormals, ref normals, generateUVs, ref uvs);
                if (generateNormals)
                {
                    for (int i = 0; i < normals.Length; ++i)
                    {
                        normals[i] = normal;
                    }
                }
            }

            public static JobHandle Schedule(Data data, NativeList<float3> vertices, NativeList<int> triangles, bool generateNormals, float3 normal, NativeList<float3> normals, bool generateUVs, NativeList<float2> uvs, NativeArray<TopCellInfo> info, JobHandle dependOn = default)
            {
                var cornerJob = new CalculateInfoJob
                {
                    distanceColNum = data.ColNum, 
                    distanceRowNum = data.RowNum,

                    distances = data.RawData, 
                    cullingData = data.CullingDataRawData,

                    vertices = vertices, 
                    indices = triangles,

                    generateNormals = generateNormals, 
                    normals = normals,
                    normal = normal,
                    generateUVs = generateUVs, 
                    uvs = uvs,

                    info = info
                };
                return cornerJob.Schedule(dependOn);
            }
        }

        [BurstCompile]
        private struct CalculateInfoNoCullingJob : IJob
        {
            public int distanceColNum;
            public int distanceRowNum;

            [ReadOnly] public NativeArray<float> distances;

            public NativeList<float3> vertices;
            public NativeList<int> indices;

            public bool generateNormals;
            public float3 normal;
            public NativeList<float3> normals;

            public bool generateUVs;
            public NativeList<float2> uvs;

            [WriteOnly] public NativeArray<TopCellInfo> info;

            public void Execute()
            {
                int nextVertex = 0;
                int nextTriangleIndex = 0;

                for (int y = 0; y < distanceRowNum - 1; ++y)
                {
                    for (int x = 0; x < distanceColNum - 1; ++x)
                    {
                        int index = y * distanceColNum + x;
                        float corner = distances[index];
                        float right = distances[index + 1];
                        float topRight = distances[index + 1 + distanceColNum];
                        float top = distances[index + distanceColNum];

                        info[index] = GenerateInfoWithTriangles(corner, right, topRight, top, ref nextVertex, ref nextTriangleIndex);
                    }
                }

                GenerateBorderInfo(distanceColNum, distanceRowNum, distances, ref nextVertex, ref nextTriangleIndex, info);

                InitializeMeshData(nextVertex, nextTriangleIndex, ref vertices, ref indices, generateNormals, ref normals, generateUVs, ref uvs);
                if (generateNormals)
                {
                    for (int i = 0; i < normals.Length; ++i)
                    {
                        normals[i] = normal;
                    }
                }
            }

            public static JobHandle Schedule(Data data, NativeList<float3> vertices, NativeList<int> triangles, bool generateNormals, float3 normal, NativeList<float3> normals, bool generateUVs, NativeList<float2> uvs, NativeArray<TopCellInfo> info, JobHandle dependOn = default)
            {
                var cornerJob = new CalculateInfoNoCullingJob
                {
                    distanceColNum = data.ColNum,
                    distanceRowNum = data.RowNum,

                    distances = data.RawData,

                    vertices = vertices,
                    indices = triangles,

                    generateNormals = generateNormals,
                    normals = normals,
                    normal = normal,
                    generateUVs = generateUVs,
                    uvs = uvs,

                    info = info
                };
                return cornerJob.Schedule(dependOn);
            }
        }

        static private void GenerateBorderInfo(int distanceColNum, int distanceRowNum, NativeArray<float> distances, ref int nextVertex, ref int nextTriangleIndex, NativeArray<TopCellInfo> info)
        {
            // top border
            for (int x = 0, y = distanceRowNum - 1; x < distanceColNum - 1; ++x)
            {
                int index = y * distanceColNum + x;
                float corner = distances[index];
                float right = distances[index + 1];
                info[index] = GenerateInfoNoTriangles(corner, right, -1, -1, ref nextVertex, ref nextTriangleIndex);
            }
            // right border
            for (int x = distanceColNum - 1, y = 0; y < distanceRowNum - 1; ++y)
            {
                int index = y * distanceColNum + x;
                float corner = distances[index];
                float top = distances[index + distanceColNum];
                info[index] = GenerateInfoNoTriangles(corner, -1, -1, top, ref nextVertex, ref nextTriangleIndex);
            }
            // top right corner
            int last = distanceColNum * distanceRowNum - 1;
            info[last] = GenerateInfoNoTriangles(distances[last], -1, -1, -1, ref nextVertex, ref nextTriangleIndex);
        }

        static private TopCellInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                            ref int nextVertex, ref int nextTriIndex, bool hasCellTriangles)
        {
            byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);
            TopCellInfo info = new TopCellInfo
            {
                info = new CellInfo { config = config, cornerDist = cornerDistance, rightDist = rightDistance, topDist = topDistance },
                verts = CreateCellVertices(config, ref nextVertex),
                tris = new IndexSpan(nextTriIndex, 0)
            };

            if (hasCellTriangles)
            {
                info.tris.length = CalcTriIndexCount(config);
                nextTriIndex += info.tris.length;
            }

            return info;
        }

        static private TopCellInfo GenerateInfoNoTriangles(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                            ref int nextVertex, ref int nextTriIndex)
        {
            byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);
            TopCellInfo info = new TopCellInfo
            {
                info = new CellInfo { config = config, cornerDist = cornerDistance, rightDist = rightDistance, topDist = topDistance },
                verts = CreateCellVertices(config, ref nextVertex),
                tris = new IndexSpan(nextTriIndex, 0)
            };
            return info;
        }

        static private TopCellInfo GenerateInfoWithTriangles(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                            ref int nextVertex, ref int nextTriIndex)
        {
            byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);
            TopCellInfo info = new TopCellInfo
            {
                info = new CellInfo { config = config, cornerDist = cornerDistance, rightDist = rightDistance, topDist = topDistance },
                verts = CreateCellVertices(config, ref nextVertex),
                tris = new IndexSpan(nextTriIndex, 0)
            };

            info.tris.length = CalcTriIndexCount(config);
            nextTriIndex += info.tris.length;

            return info;
        }

        // TODO: look at this, I think it is possible to get rid of the branches
        static private CellVertices CreateCellVertices(byte config, ref int nextVertex)
        {
            var verts = new CellVertices() { bottomEdge = -1, corner = -1, leftEdge = -1 };
            bool hasBL = HasMask(config, MaskBL);
            if (hasBL)
            {
                verts.corner = nextVertex;
                ++nextVertex;
            }

            if (hasBL != HasMask(config, MaskTL))
            {
                verts.leftEdge = nextVertex;
                ++nextVertex;
            }

            if (hasBL != HasMask(config, MaskBR))
            {
                verts.bottomEdge = nextVertex;
                ++nextVertex;
            }
            return verts;
        }

        [BurstCompile]
        private struct CalculateVerticesJob<VertexCalculator> : IJobParallelFor
            where VertexCalculator : struct, IVertexCalculator
        {
            public int colNum;

            public VertexCalculator vertexCalculator;
            
            public float offsetY;

            [ReadOnly] public NativeArray<TopCellInfo> infoArray;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                TopCellInfo info = infoArray[index];
                int x = index % colNum;
                int y = index / colNum;
                vertexCalculator.CalculateVertices(x, y, offsetY, info.info, info.verts, vertices);
            }

            public static JobHandle Schedule(int colNum, VertexCalculator vertexCalculator, float offsetY, NativeArray<TopCellInfo> infoArray, NativeList<float3> vertices, JobHandle dependOn)
            {
                var vertexJob = new CalculateVerticesJob<VertexCalculator>
                {
                    colNum = colNum,

                    vertexCalculator = vertexCalculator,

                    offsetY = offsetY,

                    infoArray = infoArray,
                    vertices = vertices.AsDeferredJobArray()
                };
                return vertexJob.Schedule(infoArray.Length, CalculateVertexBatchNum, dependOn);
            }
        }

        [BurstCompile]
        private struct CalculateVerticesWithHeightJob<VertexCalculator> : IJobParallelFor
            where VertexCalculator : struct, IVertexCalculator
        {
            public int colNum;

            public VertexCalculator vertexCalculator;

            public float offsetY;
            public float heightScale;

            [ReadOnly] public NativeArray<TopCellInfo> infoArray;
            [ReadOnly] public NativeArray<float> heights;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                TopCellInfo info = infoArray[index];
                int x = index % colNum;
                int y = index / colNum;
                vertexCalculator.CalculateVertices(x, y, offsetY + heights[index] * heightScale, info.info, info.verts, vertices);
            }

            public static JobHandle Schedule(int colNum, VertexCalculator vertexCalculator, float offsetY, NativeArray<TopCellInfo> infoArray, NativeArray<float> heights, float heightScale, NativeList<float3> vertices, JobHandle dependOn)
            {
                var vertexJob = new CalculateVerticesWithHeightJob<VertexCalculator>
                {
                    colNum = colNum,

                    vertexCalculator = vertexCalculator,

                    offsetY = offsetY,
                    heights = heights,
                    heightScale = heightScale,

                    infoArray = infoArray,
                    vertices = vertices.AsDeferredJobArray()
                };
                return vertexJob.Schedule(infoArray.Length, CalculateVertexBatchNum, dependOn);
            }
        }

        private static JobHandle ScheduleCalculateVerticesJob(Data data, Info info, bool useHeightData, float cellSize, NativeArray<TopCellInfo> infoArray, NativeList<float3> vertices, JobHandle lastHandle)
        {
            if (useHeightData)
            {
                if (info.LerpToExactEdge == 1f)
                {
                    var vertexCalculator = new BasicVertexCalculator() { cellSize = cellSize };
                    return CalculateVerticesWithHeightJob<BasicVertexCalculator>.Schedule(data.ColNum, vertexCalculator, info.OffsetY, infoArray, data.HeightsRawData, info.HeightScale, vertices, lastHandle);
                }
                else
                {
                    var vertexCalculator = new LerpedVertexCalculator() { cellSize = cellSize, lerpToEdge = info.LerpToExactEdge };
                    return CalculateVerticesWithHeightJob<LerpedVertexCalculator>.Schedule(data.ColNum, vertexCalculator, info.OffsetY, infoArray, data.HeightsRawData, info.HeightScale, vertices, lastHandle);
                }
            }
            else
            {
                if (info.LerpToExactEdge == 1f)
                {
                    var vertexCalculator = new BasicVertexCalculator() { cellSize = cellSize };
                    return CalculateVerticesJob<BasicVertexCalculator>.Schedule(data.ColNum, vertexCalculator, info.OffsetY, infoArray, vertices, lastHandle);
                }
                else
                {
                    var vertexCalculator = new LerpedVertexCalculator() { cellSize = cellSize, lerpToEdge = info.LerpToExactEdge };
                    return CalculateVerticesJob<LerpedVertexCalculator>.Schedule(data.ColNum, vertexCalculator, info.OffsetY, infoArray, vertices, lastHandle);
                }
            }
        }

        [BurstCompile]
        private struct CalculateTrianglesJob<Orderer> : IJobParallelFor
            where Orderer : struct, ITriangleOrderer
        {
            public int cornerColNum;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<TopCellInfo> infoArray;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<int> triangles;

            public void Execute(int index)
            {
                int cellColNum = cornerColNum - 1;
                int x = index % cellColNum;
                int y = index / cellColNum;
                index = y * cornerColNum + x;

                TopCellInfo bl = infoArray[index];
                TopCellInfo br = infoArray[index + 1];
                TopCellInfo tr = infoArray[index + 1 + cornerColNum];
                TopCellInfo tl = infoArray[index + cornerColNum];
                CalculateIndices<Orderer>(bl.info.config, bl.tris, bl.verts, br.verts, tr.verts, tl.verts, triangles);
            }

            public static JobHandle Schedule(int colNum, int rowNum, NativeArray<TopCellInfo> infoArray, NativeList<int> triangles, JobHandle dependOn)
            {
                int cellCount = (colNum - 1) * (rowNum - 1);
                var trianglesJob = new CalculateTrianglesJob<Orderer>
                {
                    cornerColNum = colNum,
                    infoArray = infoArray,
                    triangles = triangles.AsDeferredJobArray()
                };
                return trianglesJob.Schedule(cellCount, MeshTriangleBatchNum, dependOn);
            }
        }

        private static JobHandle ScheduleCalculateTrianglesJob(int colNum, int rowNum, NativeArray<TopCellInfo> infoArray, NativeList<int> triangles, bool isFlipped, JobHandle dependOn)
            => isFlipped ?
                 CalculateTrianglesJob<ReverseTriangleOrderer>.Schedule(colNum, rowNum, infoArray, triangles, dependOn) :
                 CalculateTrianglesJob<TriangleOrderer>.Schedule(colNum, rowNum, infoArray, triangles, dependOn);

        protected static void CalculateIndices<TriangleOrderer>(byte config, IndexSpan tris, CellVertices bl, CellVertices br, CellVertices tr, CellVertices tl, NativeArray<int> triangles)
                where TriangleOrderer : struct, ITriangleOrderer
        {
            if (tris.length == 0)
            {
                return;
            }

            TriangleOrderer orderer = new TriangleOrderer();
            int triangleIndex = tris.start;
            switch (config)
            {
                // full
                case MaskBL | MaskBR | MaskTR | MaskTL:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), Vertex(tr));
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), Vertex(tr), Vertex(br));
                        break;
                    }
                // corners
                case MaskBL:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), LeftEdge(bl), BottomEdge(bl));
                        break;
                    }
                case MaskBR:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(br), Vertex(br));
                        break;
                    }
                case MaskTR:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                        break;
                    }
                case MaskTL:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tl), BottomEdge(tl), LeftEdge(bl));
                        break;
                    }
                // halves
                case MaskBL | MaskBR:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), LeftEdge(bl), Vertex(br));
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(br), LeftEdge(bl), LeftEdge(br));
                        break;
                    }
                case MaskTL | MaskTR:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tl), Vertex(tr), LeftEdge(bl));
                        orderer.AddTriangle(triangles, ref triangleIndex, LeftEdge(bl), Vertex(tr), LeftEdge(br));
                        break;
                    }
                case MaskBL | MaskTL:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), BottomEdge(tl), BottomEdge(bl));
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), BottomEdge(tl));
                        break;
                    }
                case MaskBR | MaskTR:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), Vertex(tr), Vertex(br));
                        orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), BottomEdge(tl), Vertex(tr));
                        break;
                    }
                // diagonals
                case MaskBL | MaskTR:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), Vertex(bl), LeftEdge(bl));
                        orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(bl), LeftEdge(br));
                        orderer.AddTriangle(triangles, ref triangleIndex, LeftEdge(br), LeftEdge(bl), BottomEdge(tl));
                        orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                        break;
                    }
                case MaskTL | MaskBR:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, LeftEdge(bl), Vertex(tl), BottomEdge(tl));
                        orderer.AddTriangle(triangles, ref triangleIndex, LeftEdge(bl), BottomEdge(tl), BottomEdge(bl));
                        orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), BottomEdge(tl), LeftEdge(br));
                        orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(br), Vertex(br));
                        break;
                    }
                // three quarters
                case MaskBL | MaskTR | MaskBR:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(br), Vertex(bl), LeftEdge(bl));
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(br), LeftEdge(bl), BottomEdge(tl));
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(br), BottomEdge(tl), Vertex(tr));
                        break;
                    }
                case MaskBL | MaskTL | MaskBR:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), BottomEdge(tl));
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), BottomEdge(tl), LeftEdge(br));
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), LeftEdge(br), Vertex(br));
                        break;
                    }
                case MaskBL | MaskTL | MaskTR:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tl), BottomEdge(bl), Vertex(bl));
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tl), LeftEdge(br), BottomEdge(bl));
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tl), Vertex(tr), LeftEdge(br));
                        break;
                    }
                case MaskTL | MaskTR | MaskBR:
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tr), LeftEdge(bl), Vertex(tl));
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tr), BottomEdge(bl), LeftEdge(bl));
                        orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tr), Vertex(br), BottomEdge(bl));
                        break;
                    }
            }
        }

        private static int Vertex(CellVertices verts) => verts.corner;
        private static int LeftEdge(CellVertices verts) => verts.leftEdge;
        private static int BottomEdge(CellVertices verts) => verts.bottomEdge;

        public interface ITriangleOrderer
        {
            void AddTriangle(NativeArray<int> triangles, ref int nextIndex, int a, int b, int c);
        }

        public struct TriangleOrderer : ITriangleOrderer
        {
            public void AddTriangle(NativeArray<int> triangles, ref int nextIndex, int a, int b, int c)
                => AddTri(triangles, ref nextIndex, a, b, c);
        }

        public struct ReverseTriangleOrderer : ITriangleOrderer
        {
            public void AddTriangle(NativeArray<int> triangles, ref int nextIndex, int a, int b, int c)
                => AddTriReverse(triangles, ref nextIndex, a, b, c);
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

        private static void AddTriReverse(NativeArray<int> triangles, ref int nextIndex, int a, int b, int c)
        {
            triangles[nextIndex] = c;
            ++nextIndex;
            triangles[nextIndex] = b;
            ++nextIndex;
            triangles[nextIndex] = a;
            ++nextIndex;
        }

        [BurstCompile]
        private struct CalculateUVsJob : IJobParallelFor
        {
            public float scaleU;
            public float scaleV;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<TopCellInfo> infos;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<float3> vertices;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float2> uvs;

            public void Execute(int index)
                => TopCalculateUvs(infos[index].verts, scaleU, scaleV, vertices, uvs);

            static private void TopCalculateUvs(CellVertices verts, float scaleX, float scaleY, NativeArray<float3> vertices, NativeArray<float2> uvs)
            {
                if (verts.corner >= 0) { SetUV(verts.corner, vertices, scaleX, scaleY, uvs); }
                if (verts.leftEdge >= 0) { SetUV(verts.leftEdge, vertices, scaleX, scaleY, uvs); }
                if (verts.bottomEdge >= 0) { SetUV(verts.bottomEdge, vertices, scaleX, scaleY, uvs); }
            }

            static private void SetUV(int index, NativeArray<float3> vertices, float scaleX, float scaleY, NativeArray<float2> uvs)
            {
                var v = vertices[index];
                uvs[index] = new float2(v.x * scaleX, v.z * scaleY);
            }

            public static JobHandle Schedule(float scaleU, float scaleV, NativeArray<TopCellInfo> infos, NativeList<float3> vertices, NativeList<float2> uvs, JobHandle dependOn)
            {
                var uvJob = new CalculateUVsJob
                {
                    scaleU = scaleU,
                    scaleV = scaleV,

                    infos = infos,
                    vertices = vertices.AsDeferredJobArray(),
                    uvs = uvs.AsDeferredJobArray()
                };
                return uvJob.Schedule(infos.Length, CalculateVertexBatchNum, dependOn);
            }
        }
    }
}
