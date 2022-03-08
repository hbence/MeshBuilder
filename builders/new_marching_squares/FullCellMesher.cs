using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

using static MeshBuilder.New.TopCellMesher;
using static MeshBuilder.New.SideCellMesher;
using Data = MeshBuilder.MarchingSquaresMesher.Data;

namespace MeshBuilder.New
{
    public class FullCellMesher : CellMesher
    {
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

        protected struct FullCellInfoGenerator : InfoGenerator<SideCellInfo>
        {
            public SideCellInfo GenerateInfoWithTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => FullCellMesher.GenerateInfoWithTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);

            public SideCellInfo GenerateInfoNoTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => FullCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);
        }

        protected struct CulledFullCellInfoGenerator : InfoGenerator<SideCellInfo>
        {
            [ReadOnly] public NativeArray<bool> cullingArray;

            public SideCellInfo GenerateInfoWithTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => cullingArray[index] ?
                        FullCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex) :
                        FullCellMesher.GenerateInfoWithTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);

            public SideCellInfo GenerateInfoNoTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => FullCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);
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

            info.tris.length = CalcTriIndexCount(config);
            nextTriIndex += info.tris.length;

            return info;
        }

        static byte CalcTriIndexCount(byte config)
        {
            byte count = (byte)(2 * TopCellMesher.CalcTriIndexCount(config));
            if (config != 0 && config != MaskFull)
            {
                count += 2 * 3;
            }
            return count;
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
            float3 normal = new float3(0, 1, 0);

            if (info.UseCullingData && data.HasCullingData)
            {
                var generator = new CulledFullCellInfoGenerator();
                generator.cullingArray = data.CullingDataRawData;
                lastHandle = CalculateInfoJob<CulledFullCellInfoGenerator, SideCellInfo>.Schedule(generator, data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }
            else
            {
                var generator = new FullCellInfoGenerator();
                lastHandle = CalculateInfoJob<FullCellInfoGenerator, SideCellInfo>.Schedule(generator, data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }

            return lastHandle;
        }

        [BurstCompile]
        private struct CalculateTrianglesJob<TopOrderer, BottomOrderer> : IJobParallelFor
            where TopOrderer : struct, ITriangleOrderer
            where BottomOrderer : struct, ITriangleOrderer
        {
            public int cornerColNum;
            public TopOrderer topOrderer;
            public BottomOrderer bottomOrderer;

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
                SideCellInfo tr = infoArray[index + cornerColNum + 1];
                int triangleIndex = bl.tris.start;
                triangleIndex = CalculateSideIndices(topOrderer, triangleIndex, bl, br, tl, triangles);
                triangleIndex = CalculateIndices(topOrderer, bl.info.config, triangleIndex, bl.tris, bl.topVerts, br.topVerts, tr.topVerts, tl.topVerts, triangles);
                CalculateIndices(bottomOrderer, bl.info.config, triangleIndex, bl.tris, bl.bottomVerts, br.bottomVerts, tr.bottomVerts, tl.bottomVerts, triangles);
            }

            public static JobHandle Schedule(int colNum, int rowNum, NativeArray<SideCellInfo> infoArray, NativeList<int> triangles, JobHandle dependOn)
            {
                int cellCount = (colNum - 1) * (rowNum - 1);
                var trianglesJob = new CalculateTrianglesJob<TopOrderer, BottomOrderer>
                {
                    cornerColNum = colNum,
                    topOrderer = new TopOrderer(),
                    bottomOrderer = new BottomOrderer(),
                    infoArray = infoArray,
                    triangles = triangles.AsDeferredJobArray()
                };
                return trianglesJob.Schedule(cellCount, MeshTriangleBatchNum, dependOn);
            }
        }

        public static JobHandle ScheduleCalculateTrianglesJob(int colNum, int rowNum, NativeArray<SideCellInfo> infoArray, NativeList<int> triangles, bool isFlipped, JobHandle dependOn)
            => isFlipped ?
                 CalculateTrianglesJob<ReverseTriangleOrderer, TriangleOrderer>.Schedule(colNum, rowNum, infoArray, triangles, dependOn) :
                 CalculateTrianglesJob<TriangleOrderer, ReverseTriangleOrderer>.Schedule(colNum, rowNum, infoArray, triangles, dependOn);

        [BurstCompile]
        public struct CalculateUVsJob : IJobParallelFor
        {
            public float scaleU;
            public float scaleV;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<SideCellInfo> infos;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<float3> vertices;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float2> uvs;

            public void Execute(int index)
            {
                TopCellMesher.CalculateUVsJob.TopCalculateUvs(infos[index].topVerts, scaleU, scaleV, vertices, uvs);
                TopCellMesher.CalculateUVsJob.TopCalculateUvs(infos[index].bottomVerts, scaleU, scaleV, vertices, uvs);
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
    }
}
