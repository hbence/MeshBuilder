using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

using static MeshBuilder.Utils;
using Data = MeshBuilder.MarchingSquaresMesherData;

namespace MeshBuilder
{
    public class TopCellMesher : CellMesher
    {
        public struct TopCellInfo
        {
            public CellInfo info;
            public CellVertices verts;
            public IndexSpan tris;
        }

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

            CreateMeshData();

            bool useHeightData = info.UseHeightData && data.HasHeights;

            var infoArray = new NativeArray<TopCellInfo>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(infoArray);

            lastHandle = ScheduleCalculateInfoJob(data, info, infoArray, vertices, triangles, normals, uvs, lastHandle);

            JobHandle vertexHandle = ScheduleCalculateVerticesJob(data, info, useHeightData, cellSize, infoArray, vertices, lastHandle);

            if (info.GenerateUvs)
            {
                vertexHandle = ScheduleCalculateUVJob(data, info, cellSize, infoArray, vertices, uvs, vertexHandle);
            }

            JobHandle triangleHandle = ScheduleCalculateTrianglesJob(data.ColNum, data.RowNum, infoArray, triangles, info.IsFlipped, lastHandle);

            lastHandle = JobHandle.CombineDependencies(vertexHandle, triangleHandle);

            if (info.GenerateNormals && useHeightData)
            {
                lastHandle = CalculateNormals.ScheduleDeferred(vertices, triangles, normals, lastHandle);
            }

            return lastHandle;
        }

        protected struct TopCellInfoGenerator : InfoGenerator<TopCellInfo>
        {
            public TopCellInfo GenerateInfoWithTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => TopCellMesher.GenerateInfoWithTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);

            public TopCellInfo GenerateInfoNoTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => TopCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);
        }

        protected struct CulledTopCellInfoGenerator : InfoGenerator<TopCellInfo>
        {
            [ReadOnly] public NativeArray<bool> cullingArray;

            public TopCellInfo GenerateInfoWithTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => cullingArray[index] ? 
                        TopCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex) :
                        TopCellMesher.GenerateInfoWithTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);

            public TopCellInfo GenerateInfoNoTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => TopCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);
        }

        static public TopCellInfo GenerateInfoWithTriangles(float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
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

        static public TopCellInfo GenerateInfoNoTriangles(float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
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

        // TODO: look at this, I think it is possible to get rid of the branches
        static public CellVertices CreateCellVertices(byte config, ref int nextVertex)
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

        static public JobHandle ScheduleCalculateInfoJob(Data data, Info info, NativeArray<TopCellInfo> infoArray, NativeList<float3> vertices, NativeList<int> triangles, NativeList<float3> normals, NativeList<float2> uvs, JobHandle lastHandle = default)
        {
            float3 normal = info.IsFlipped ? new float3(0, -1, 0) : new float3(0, 1, 0);

            if (info.UseCullingData && data.HasCullingData)
            {
                var generator = new CulledTopCellInfoGenerator();
                generator.cullingArray = data.CullingDataRawData;
                lastHandle = CalculateInfoJob<CulledTopCellInfoGenerator, TopCellInfo>.Schedule(generator, data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }
            else
            {
                var generator = new TopCellInfoGenerator();
                lastHandle = CalculateInfoJob<TopCellInfoGenerator, TopCellInfo>.Schedule(generator, data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }

            return lastHandle;
        }

        [BurstCompile]
        public struct CalculateVerticesJob<VertexCalculator> : IJobParallelFor
            where VertexCalculator : struct, IVertexCalculator
        {
            public VertexCalculator vertexCalculator;
            
            [ReadOnly] public NativeArray<TopCellInfo> infoArray;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                TopCellInfo info = infoArray[index];
                vertexCalculator.CalculateVertices(index, info.info, info.verts, vertices);
            }

            public static JobHandle Schedule(VertexCalculator vertexCalculator, NativeArray<TopCellInfo> infoArray, NativeList<float3> vertices, JobHandle dependOn)
            {
                var vertexJob = new CalculateVerticesJob<VertexCalculator>
                {
                    vertexCalculator = vertexCalculator,

                    infoArray = infoArray,
                    vertices = vertices.AsDeferredJobArray()
                };
                return vertexJob.Schedule(infoArray.Length, CalculateVertexBatchNum, dependOn);
            }
        }

        public static JobHandle ScheduleCalculateVerticesJob(Data data, Info info, bool useHeightData, float cellSize, NativeArray<TopCellInfo> infoArray, NativeList<float3> vertices, JobHandle lastHandle)
        {
            if (useHeightData)
            {
                if (info.LerpToExactEdge == 1f)
                {
                    var vertexCalculator = new BasicHeightVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, heightOffset = info.OffsetY, heights = data.HeightsRawData, heightScale = info.HeightScale };
                    return ScheduleCalculateVerticesJob(vertexCalculator, infoArray, vertices, lastHandle);
                }
                else
                {
                    var vertexCalculator = new LerpedHeightVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, lerpToEdge = info.LerpToExactEdge, heightOffset = info.OffsetY, heights = data.HeightsRawData, heightScale = info.HeightScale };
                    return ScheduleCalculateVerticesJob(vertexCalculator, infoArray, vertices, lastHandle);
                }
            }
            else
            {
                if (info.LerpToExactEdge == 1f)
                {
                    var vertexCalculator = new BasicVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, heightOffset = info.OffsetY };
                    return ScheduleCalculateVerticesJob(vertexCalculator, infoArray, vertices, lastHandle);
                }
                else
                {
                    var vertexCalculator = new LerpedVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, lerpToEdge = info.LerpToExactEdge, heightOffset = info.OffsetY };
                    return ScheduleCalculateVerticesJob(vertexCalculator, infoArray, vertices, lastHandle);
                }
            }
        }

        public static JobHandle ScheduleCalculateVerticesJob<T>(T vertexCalculator, NativeArray<TopCellInfo> infoArray, NativeList<float3> vertices, JobHandle lastHandle)
            where T : struct, IVertexCalculator
                => CalculateVerticesJob<T>.Schedule(vertexCalculator, infoArray, vertices, lastHandle);

        [BurstCompile]
        private struct CalculateTrianglesJob<Orderer> : IJobParallelFor
            where Orderer : struct, ITriangleOrderer
        {
            public int cornerColNum;
            public Orderer orderer;

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
                CalculateIndices(orderer, bl.info.config, bl.tris.start, bl.tris, bl.verts, br.verts, tr.verts, tl.verts, triangles);
            }

            public static JobHandle Schedule(int colNum, int rowNum, NativeArray<TopCellInfo> infoArray, NativeList<int> triangles, JobHandle dependOn)
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

        public static JobHandle ScheduleCalculateTrianglesJob(int colNum, int rowNum, NativeArray<TopCellInfo> infoArray, NativeList<int> triangles, bool isFlipped, JobHandle dependOn)
            => isFlipped ?
                 CalculateTrianglesJob<ReverseTriangleOrderer>.Schedule(colNum, rowNum, infoArray, triangles, dependOn) :
                 CalculateTrianglesJob<TriangleOrderer>.Schedule(colNum, rowNum, infoArray, triangles, dependOn);

        public static int CalculateIndices<TriangleOrderer>(TriangleOrderer orderer, byte config, int triangleIndex, IndexSpan tris, CellVertices bl, CellVertices br, CellVertices tr, CellVertices tl, NativeArray<int> triangles)
                where TriangleOrderer : struct, ITriangleOrderer
        {
            if (tris.length == 0)
            {
                return triangleIndex;
            }

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
            return triangleIndex;
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
        public struct CalculateUVsJob : IJobParallelFor
        {
            public float scaleU;
            public float scaleV;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<TopCellInfo> infos;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<float3> vertices;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float2> uvs;

            public void Execute(int index)
                => TopCalculateUvs(infos[index].verts, scaleU, scaleV, vertices, uvs);

            static public void TopCalculateUvs(CellVertices verts, float scaleX, float scaleY, NativeArray<float3> vertices, NativeArray<float2> uvs)
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

        public static JobHandle ScheduleCalculateUVJob(Data data, Info info, float cellSize, NativeArray<TopCellInfo> infoArray, NativeList<float3> vertices, NativeList<float2> uvs, JobHandle lastHandle = default)
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

        public static byte CalcTriIndexCount(byte config)
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
    }
}
