using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

using static MeshBuilder.New.TopCellMesher;
using Data = MeshBuilder.MarchingSquaresMesher.Data;

namespace MeshBuilder.New
{
    public class SideCellMesher : CellMesher
    {
        public const byte FullCellIndexCount = 2 * 3;

        [Serializable]
        public class SideInfo : Info
        {
            public float SideHeight = 1;
            public float BottomHeightScale = 1f;
        }

        public struct SideCellInfo
        {
            public CellInfo info;
            public CellVertices topVerts;
            public CellVertices bottomVerts;
            public IndexSpan tris;
        }

        private float cellSize;
        private Data data;
        private SideInfo info;

        public void Init(Data data, float cellSize, SideInfo info)
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

            var infoArray = new NativeArray<SideCellInfo>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(infoArray);

            lastHandle = ScheduleCalculateInfoJob(data, info, infoArray, vertices, triangles, normals, uvs, lastHandle);

            JobHandle vertexHandle = ScheduleCalculateVerticesJob(data, info, useHeightData, cellSize, infoArray, vertices, lastHandle);

            if (info.GenerateUvs)
            {
                vertexHandle = ScheduleCalculateUVJob(data, info, cellSize, infoArray, vertices, uvs, vertexHandle);
            }

            JobHandle triangleHandle = ScheduleCalculateTrianglesJob(data.ColNum, data.RowNum, infoArray, triangles, info.IsFlipped, lastHandle);

            lastHandle = JobHandle.CombineDependencies(vertexHandle, triangleHandle);

            if (info.GenerateNormals)
            {
                lastHandle = CalculateNormals.ScheduleDeferred(vertices, triangles, normals, lastHandle);
            }
            
            return lastHandle;
        }

        protected struct SideCellInfoGenerator : InfoGenerator<SideCellInfo>
        {
            public SideCellInfo GenerateInfoWithTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => SideCellMesher.GenerateInfoWithTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);

            public SideCellInfo GenerateInfoNoTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => SideCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);
        }

        protected struct CulledSideCellInfoGenerator : InfoGenerator<SideCellInfo>
        {
            [ReadOnly] public NativeArray<bool> cullingArray;

            public SideCellInfo GenerateInfoWithTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => cullingArray[index] ?
                        SideCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex) :
                        SideCellMesher.GenerateInfoWithTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);

            public SideCellInfo GenerateInfoNoTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => SideCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);
        }

        static public SideCellInfo GenerateInfoWithTriangles(float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
        {
            byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);
            SideCellInfo info = new SideCellInfo
            {
                info = new CellInfo { config = config, cornerDist = cornerDistance, rightDist = rightDistance, topDist = topDistance },
                topVerts = CreateCellVertices(config, ref nextVertex),
                bottomVerts = CreateCellVertices(config, ref nextVertex),
                tris = new IndexSpan(nextTriIndex, 0)
            };

            info.tris.length = (byte)((config == 0 || config == MaskFull) ? 0 : 2 * 3);
            nextTriIndex += info.tris.length;

            return info;
        }

        static public SideCellInfo GenerateInfoNoTriangles(float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
        {
            byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);
            SideCellInfo info = new SideCellInfo
            {
                info = new CellInfo { config = config, cornerDist = cornerDistance, rightDist = rightDistance, topDist = topDistance },
                topVerts = CreateCellVertices(config, ref nextVertex),
                bottomVerts = CreateCellVertices(config, ref nextVertex),
                tris = new IndexSpan(nextTriIndex, 0)
            };
            return info;
        }

        static public JobHandle ScheduleCalculateInfoJob(Data data, Info info, NativeArray<SideCellInfo> infoArray, NativeList<float3> vertices, NativeList<int> triangles, NativeList<float3> normals, NativeList<float2> uvs, JobHandle lastHandle = default)
        {
            float3 normal = info.IsFlipped ? new float3(0, -1, 0) : new float3(0, 1, 0);

            if (info.UseCullingData && data.HasCullingData)
            {
                var generator = new CulledSideCellInfoGenerator();
                generator.cullingArray = data.CullingDataRawData;
                lastHandle = CalculateInfoJob<CulledSideCellInfoGenerator, SideCellInfo>.Schedule(generator, data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }
            else
            {
                var generator = new SideCellInfoGenerator();
                lastHandle = CalculateInfoJob<SideCellInfoGenerator, SideCellInfo>.Schedule(generator, data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }

            return lastHandle;
        }

        [BurstCompile]
        public struct CalculateUVsJob : IJobParallelFor
        {
            public float scaleU;
            public float scaleV;

            [ReadOnly] public NativeArray<SideCellInfo> infos;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<float3> vertices;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float2> uvs;

            public void Execute(int index)
            {
                CalculateUvs(infos[index], vertices, uvs);
            }

            public void CalculateUvs(SideCellInfo info, NativeArray<float3> vertices, NativeArray<float2> uvs)
            {
                SetSideUV(info.topVerts.corner, info.bottomVerts.corner, vertices, uvs, scaleU, scaleV);
                SetSideUV(info.topVerts.leftEdge, info.bottomVerts.leftEdge, vertices, uvs, scaleU, scaleV);
                SetSideUV(info.topVerts.bottomEdge, info.bottomVerts.bottomEdge, vertices, uvs, scaleU, scaleV);
            }

            static public void SetSideUV(int top, int bottom, NativeArray<float3> vertices, NativeArray<float2> uvs, float scaleU, float scaleV)
            {
                if (top >= 0)
                {
                    float3 vert = vertices[top];
                    float u = (vert.x + vert.z) * scaleU;
                    uvs[top] = new float2(u, scaleV);
                    uvs[bottom] = new float2(u, 0);
                }
            }

            public static JobHandle Schedule(float scaleU, float scaleV, NativeArray<SideCellInfo> infos, NativeList<float3> vertices, NativeList<float2> uvs, JobHandle dependOn)
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

        public static JobHandle ScheduleCalculateUVJob(Data data, Info info, float cellSize, NativeArray<SideCellInfo> infoArray, NativeList<float3> vertices, NativeList<float2> uvs, JobHandle lastHandle = default)
        {
            float scaleU = info.UScale;
            float scaleV = info.VScale;
            if (info.NormalizeUV)
            {
                scaleU /= (data.ColNum - 1) * cellSize;
                scaleV /= (data.RowNum - 1) * cellSize;
            }
            return CalculateUVsJob.Schedule(scaleU, scaleV, infoArray, vertices, uvs, lastHandle);
        }

        [BurstCompile]
        public struct CalculateVerticesJob<TopCalculator, BottomCalculator> : IJobParallelFor
            where TopCalculator : struct, IVertexCalculator
            where BottomCalculator : struct, IVertexCalculator
        {
            public TopCalculator topVertexCalculator;
            public BottomCalculator bottomVertexCalculator;

            [ReadOnly] public NativeArray<SideCellInfo> infoArray;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                SideCellInfo info = infoArray[index];
                topVertexCalculator.CalculateVertices(index, info.info, info.topVerts, vertices);
                bottomVertexCalculator.CalculateVertices(index, info.info, info.bottomVerts, vertices);
            }

            public static JobHandle Schedule(TopCalculator topVertexCalculator, BottomCalculator bottomVertexCalculator, NativeArray<SideCellInfo> infoArray, NativeList<float3> vertices, JobHandle dependOn)
            {
                var vertexJob = new CalculateVerticesJob<TopCalculator, BottomCalculator>
                {
                    topVertexCalculator = topVertexCalculator,
                    bottomVertexCalculator = bottomVertexCalculator,

                    infoArray = infoArray,
                    vertices = vertices.AsDeferredJobArray()
                };
                return vertexJob.Schedule(infoArray.Length, CalculateVertexBatchNum, dependOn);
            }
        }

        public static JobHandle ScheduleCalculateVerticesJob(Data data, SideInfo info, bool useHeightData, float cellSize, NativeArray<SideCellInfo> infoArray, NativeList<float3> vertices, JobHandle lastHandle)
        {
            float topHeightOffset = info.OffsetY + info.SideHeight;
            float bottomHeightOffset = info.OffsetY;

            float topHeightScale = info.HeightScale;
            float bottomHeightScale = info.BottomHeightScale;

            if (useHeightData)
            {
                if (info.LerpToExactEdge == 1f)
                {
                    var topCalculator = new BasicHeightVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, heightOffset = topHeightOffset, heights = data.HeightsRawData, heightScale = topHeightScale };
                    var bottomCalculator = new BasicHeightVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, heightOffset = bottomHeightOffset, heights = data.HeightsRawData, heightScale = bottomHeightScale };
                    return ScheduleCalculateVerticesJob(topCalculator, bottomCalculator, infoArray, vertices, lastHandle);
                }
                else
                {
                    var topCalculator = new LerpedHeightVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, lerpToEdge = info.LerpToExactEdge, heightOffset = topHeightOffset, heights = data.HeightsRawData, heightScale = topHeightScale};
                    var bottomCalculator = new LerpedHeightVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, lerpToEdge = info.LerpToExactEdge, heightOffset = bottomHeightOffset, heights = data.HeightsRawData, heightScale = bottomHeightScale };
                    return ScheduleCalculateVerticesJob(topCalculator, bottomCalculator, infoArray, vertices, lastHandle);
                }
            }
            else
            {
                if (info.LerpToExactEdge == 1f)
                {
                    var topCalculator = new BasicVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, heightOffset = topHeightOffset };
                    var bottomCalculator = new BasicVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, heightOffset = bottomHeightOffset };
                    return ScheduleCalculateVerticesJob(topCalculator, bottomCalculator, infoArray, vertices, lastHandle);
                }
                else
                {
                    var topCalculator = new LerpedVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, lerpToEdge = info.LerpToExactEdge, heightOffset = info.OffsetY };
                    var bottomCalculator = new LerpedVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, lerpToEdge = info.LerpToExactEdge, heightOffset = info.OffsetY };
                    return ScheduleCalculateVerticesJob(topCalculator, bottomCalculator, infoArray, vertices, lastHandle);
                }
            }
        }

        public static JobHandle ScheduleCalculateVerticesJob<TopCalculator, BottomCalculator>(TopCalculator topCalculator, BottomCalculator bottomCalculator, NativeArray<SideCellInfo> infoArray, NativeList<float3> vertices, JobHandle lastHandle)
            where TopCalculator : struct, IVertexCalculator
            where BottomCalculator : struct, IVertexCalculator
                => CalculateVerticesJob<TopCalculator, BottomCalculator>.Schedule(topCalculator, bottomCalculator, infoArray, vertices, lastHandle);

        [BurstCompile]
        private struct CalculateTrianglesJob<Orderer> : IJobParallelFor
            where Orderer : struct, ITriangleOrderer
        {
            public int cornerColNum;
            public Orderer orderer;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<SideCellInfo> infoArray;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<int> triangles;

            public void Execute(int index)
            {
                int cellColNum = cornerColNum - 1;
                int x = index % cellColNum;
                int y = index / cellColNum;
                index = y * cornerColNum + x;

                SideCellInfo bl = infoArray[index];
                SideCellInfo br = infoArray[index + 1];
                SideCellInfo tl = infoArray[index + cornerColNum];
                CalculateSideIndices(orderer, bl.tris.start, bl, br, tl, triangles);
            }

            public static JobHandle Schedule(int colNum, int rowNum, NativeArray<SideCellInfo> infoArray, NativeList<int> triangles, JobHandle dependOn)
            {
                int cellCount = (colNum - 1) * (rowNum - 1);
                var trianglesJob = new CalculateTrianglesJob<Orderer>
                {
                    cornerColNum = colNum,
                    orderer = new Orderer(),
                    infoArray = infoArray,
                    triangles = triangles.AsDeferredJobArray()
                };
                return trianglesJob.Schedule(cellCount, MeshTriangleBatchNum, dependOn);
            }
        }

        private static JobHandle ScheduleCalculateTrianglesJob(int colNum, int rowNum, NativeArray<SideCellInfo> infoArray, NativeList<int> triangles, bool isFlipped, JobHandle dependOn)
            => isFlipped ?
                 CalculateTrianglesJob<ReverseTriangleOrderer>.Schedule(colNum, rowNum, infoArray, triangles, dependOn) :
                 CalculateTrianglesJob<TriangleOrderer>.Schedule(colNum, rowNum, infoArray, triangles, dependOn);

        public static int CalculateSideIndices<TriangleOrderer>(TriangleOrderer orderer, int triangleIndex, SideCellInfo bl, SideCellInfo br, SideCellInfo tl, NativeArray<int> triangles)
            where TriangleOrderer : struct, ITriangleOrderer
        {
            if (bl.tris.length == 0)
            {
                return triangleIndex;
            }

            switch (bl.info.config)
            {
                // full
                case MaskBL | MaskBR | MaskTR | MaskTL: break;
                // corners
                case MaskBL: AddFace(orderer, triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                case MaskBR: AddFace(orderer, triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
                case MaskTR: AddFace(orderer, triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                case MaskTL: AddFace(orderer, triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                // halves
                case MaskBL | MaskBR: AddFace(orderer, triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                case MaskTL | MaskTR: AddFace(orderer, triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                case MaskBL | MaskTL: AddFace(orderer, triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                case MaskBR | MaskTR: AddFace(orderer, triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
                // diagonals
                case MaskBL | MaskTR:
                    {
                        AddFace(orderer, triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(bl), BottomLeftEdge(bl));
                        AddFace(orderer, triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(br), BottomLeftEdge(br));
                        break;
                    }
                case MaskTL | MaskBR:
                    {
                        AddFace(orderer, triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(tl), BottomBottomEdge(tl));
                        AddFace(orderer, triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(bl), BottomBottomEdge(bl));
                        break;
                    }
                // three quarters
                case MaskBL | MaskTR | MaskBR: AddFace(orderer, triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                case MaskBL | MaskTL | MaskBR: AddFace(orderer, triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                case MaskBL | MaskTL | MaskTR: AddFace(orderer, triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                case MaskTL | MaskTR | MaskBR: AddFace(orderer, triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
            }
            return triangleIndex;
        }

        private static int TopLeftEdge(SideCellInfo info) => info.topVerts.leftEdge;
        private static int BottomLeftEdge(SideCellInfo info) => info.bottomVerts.leftEdge;
        private static int TopBottomEdge(SideCellInfo info) => info.topVerts.bottomEdge;
        private static int BottomBottomEdge(SideCellInfo info) => info.bottomVerts.bottomEdge;

        private static void AddFace<Orderer>(Orderer orderer, NativeArray<int> triangles, ref int nextIndex, int bl, int tl, int tr, int br)
            where Orderer : struct, ITriangleOrderer
        {
            orderer.AddTriangle(triangles, ref nextIndex, bl, tl, tr);
            orderer.AddTriangle(triangles, ref nextIndex, bl, tr, br);
        }
    }
}
