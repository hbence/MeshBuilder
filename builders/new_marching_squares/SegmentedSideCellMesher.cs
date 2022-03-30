using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

using static MeshBuilder.New.TopCellMesher;
using static MeshBuilder.New.ScaledTopCellMesher;
using Data = MeshBuilder.MarchingSquaresMesher.Data;

namespace MeshBuilder.New
{
    public class SegmentedSideCellMesher : CellMesher
    {
        public const byte FullCellIndexCount = 2 * 3;

        public static readonly Segment[] DefaultSegments = { CreateSegment(0, 0), CreateSegment(0, -1) };

        [Serializable]
        public class SegmentedSideInfo : Info
        {
            public Segment[] Segments = DefaultSegments;
        }

        public struct Segment
        {
            public float SideHzOffset;
            public float SideVcOffset;
            public float HeightScale;

            public Segment(float hzOffset, float vcOffset, float heightScale = 1)
            {
                SideHzOffset = hzOffset;
                SideVcOffset = vcOffset;
                HeightScale = heightScale;
            }
        }

        public static Segment CreateSegment(float hzOffset, float vcOffset, float heightScale = 1)
            => new Segment(hzOffset, vcOffset, heightScale);

        public static Segment[] CreateSegmentArray(params (float hz, float vc, float scale)[] elems)
        {
            var res = new Segment[elems.Length];
            for(int i = 0; i < elems.Length; ++i)
            {
                var elem = elems[i];
                res[i] = new Segment(elem.hz, elem.vc, elem.scale);
            }
            return res;
        }

        private struct SegmentedCellInfo
        {
            public CellInfo info;
            public int bottomVerticesStart;
            public int leftVerticesStart;
            public IndexSpan tris;
        }

        private float cellSize;
        private Data data;
        private SegmentedSideInfo info;

        public void Init(Data data, float cellSize, SegmentedSideInfo info)
        {
            this.data = data;
            this.cellSize = cellSize;
            this.info = info;

            if (info.Segments == null || info.Segments.Length < 2)
            {
                Debug.LogError("SegmentedSideCellMesher needs more segment data!");
            }

            CheckData(data, info);

            Inited();
        }

        protected override JobHandle StartGeneration(JobHandle lastHandle = default)
        {
            Dispose();
            CheckData(data, info);

            CreateMeshData();

            bool useHeightData = info.UseHeightData && data.HasHeights;

            var infoArray = new NativeArray<SegmentedCellInfo>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(infoArray);

            lastHandle = ScheduleCalculateInfoJob(data, info, infoArray, vertices, triangles, normals, uvs, lastHandle);

            var edgeNormalsArray = new NativeArray<EdgeNormals>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            AddTemp(edgeNormalsArray);

            lastHandle = ScheduleEdgeNormalsJob(new SegmentedEdgeNormalCalculator(), data.ColNum, data.RowNum, infoArray, edgeNormalsArray, info.LerpToExactEdge, lastHandle);

            JobHandle vertexHandle = ScheduleCalculateVerticesJob(data, info, useHeightData, cellSize, infoArray, edgeNormalsArray, vertices, lastHandle);

            if (info.GenerateUvs)
            {
                vertexHandle = ScheduleCalculateUVJob(data, info, cellSize, infoArray, vertices, uvs, vertexHandle);
            }

            JobHandle triangleHandle = ScheduleCalculateTrianglesJob(data.ColNum, data.RowNum, info.Segments.Length, infoArray, triangles, info.IsFlipped, lastHandle);

            lastHandle = JobHandle.CombineDependencies(vertexHandle, triangleHandle);

            if (info.GenerateNormals)
            {
                lastHandle = CalculateNormals.ScheduleDeferred(vertices, triangles, normals, lastHandle);
            }

            return lastHandle;
        }

        private struct SegmentedCellInfoGenerator : InfoGenerator<SegmentedCellInfo>
        {
            public int segmentCount;

            public SegmentedCellInfo GenerateInfoWithTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => SegmentedSideCellMesher.GenerateInfoWithTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex, segmentCount);

            public SegmentedCellInfo GenerateInfoNoTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => SegmentedSideCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex, segmentCount);
        }

        private struct CulledSegmentedCellInfoGenerator : InfoGenerator<SegmentedCellInfo>
        {
            public int segmentCount;
            [ReadOnly] public NativeArray<bool> cullingArray;

            public SegmentedCellInfo GenerateInfoWithTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => cullingArray[index] ?
                        SegmentedSideCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex, segmentCount) :
                        SegmentedSideCellMesher.GenerateInfoWithTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex, segmentCount);

            public SegmentedCellInfo GenerateInfoNoTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => SegmentedSideCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex, segmentCount);
        }

        static private SegmentedCellInfo GenerateInfoWithTriangles(float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex, int segmentCount)
        {
            byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);

            int leftEdgeStart, bottomEdgeStart;
            SetVerticesStart(config, ref nextVertex, segmentCount, out leftEdgeStart, out bottomEdgeStart);

            SegmentedCellInfo info = new SegmentedCellInfo
            {
                info = new CellInfo { config = config, cornerDist = cornerDistance, rightDist = rightDistance, topDist = topDistance },
                leftVerticesStart = leftEdgeStart,
                bottomVerticesStart = bottomEdgeStart,
                tris = new IndexSpan(nextTriIndex, (byte)(SideCellMesher.CalcTriIndexCount(config) * (segmentCount - 1)))
            };

            nextTriIndex += info.tris.length;

            return info;
        }

        static private SegmentedCellInfo GenerateInfoNoTriangles(float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int _, int segmentCount)
        {
            byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);

            int leftEdgeStart, bottomEdgeStart;
            SetVerticesStart(config, ref nextVertex, segmentCount, out leftEdgeStart, out bottomEdgeStart);

            return new SegmentedCellInfo
            {
                info = new CellInfo { config = config, cornerDist = cornerDistance, rightDist = rightDistance, topDist = topDistance },
                leftVerticesStart = leftEdgeStart,
                bottomVerticesStart = bottomEdgeStart,
                tris = new IndexSpan(-1, 0)
            };
        }

        static private void SetVerticesStart(byte config, ref int nextVertex, int segmentCount, out int leftEdgeStart, out int bottomEdgeStart)
        {
            leftEdgeStart = -1;
            bottomEdgeStart = -1;

            bool hasBL = HasMask(config, MaskBL);
            if (hasBL != HasMask(config, MaskTL))
            {
                leftEdgeStart = nextVertex;
                nextVertex += segmentCount;
            }

            if (hasBL != HasMask(config, MaskBR))
            {
                bottomEdgeStart = nextVertex;
                nextVertex += segmentCount;
            }
        }

        static private JobHandle ScheduleCalculateInfoJob(Data data, SegmentedSideInfo info, NativeArray<SegmentedCellInfo> infoArray, NativeList<float3> vertices, NativeList<int> triangles, NativeList<float3> normals, NativeList<float2> uvs, JobHandle lastHandle = default)
        {
            float3 normal = info.IsFlipped ? new float3(0, -1, 0) : new float3(0, 1, 0);

            if (info.UseCullingData && data.HasCullingData)
            {
                var generator = new CulledSegmentedCellInfoGenerator() { cullingArray = data.CullingDataRawData, segmentCount = info.Segments.Length };
                lastHandle = CalculateInfoJob<CulledSegmentedCellInfoGenerator, SegmentedCellInfo>.Schedule(generator, data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }
            else
            {
                var generator = new SegmentedCellInfoGenerator() { segmentCount = info.Segments.Length };
                lastHandle = CalculateInfoJob<SegmentedCellInfoGenerator, SegmentedCellInfo>.Schedule(generator, data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }

            return lastHandle;
        }

        private struct SegmentedEdgeNormalCalculator : IEdgeNormalCalculator<SegmentedCellInfo>
        {
            public void UpdateNormals(SegmentedCellInfo cell, SegmentedCellInfo top, SegmentedCellInfo right, ref EdgeNormals cellNormal, ref EdgeNormals topNormal, ref EdgeNormals rightNormal, float lerpToEdge)
                => ScaledTopCellMesher.UpdateNormals(cell.info, top.info, right.info, ref cellNormal, ref topNormal, ref rightNormal, lerpToEdge);
        }

        [BurstCompile]
        private struct CalculateUVsJob : IJobParallelFor
        {
            public float scaleU;
            public float scaleV;

            public int segmentCount;

            [ReadOnly] public NativeArray<SegmentedCellInfo> infos;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<float3> vertices;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float2> uvs;

            public void Execute(int index)
            {
                SegmentedCellInfo info = infos[index];
                for (int i = 0; i < segmentCount - 1; ++i)
                {
                    if (info.leftVerticesStart >= 0) { SetSideUV(info.leftVerticesStart + i + 1, info.leftVerticesStart + i, vertices, uvs, scaleU, scaleV); }
                    if (info.bottomVerticesStart >= 0) { SetSideUV(info.bottomVerticesStart + i + 1, info.bottomVerticesStart + i, vertices, uvs, scaleU, scaleV); }
                }
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

            public static JobHandle Schedule(float scaleU, float scaleV, int segmentCount, NativeArray<SegmentedCellInfo> infos, NativeList<float3> vertices, NativeList<float2> uvs, JobHandle dependOn)
            {
                var uvJob = new CalculateUVsJob
                {
                    scaleU = scaleU,
                    scaleV = scaleV,

                    segmentCount = segmentCount,

                    infos = infos,
                    vertices = vertices.AsDeferredJobArray(),
                    uvs = uvs.AsDeferredJobArray()
                };
                return uvJob.Schedule(infos.Length, CalculateVertexBatchNum, dependOn);
            }
        }

        private static JobHandle ScheduleCalculateUVJob(Data data, SegmentedSideInfo info, float cellSize, NativeArray<SegmentedCellInfo> infoArray, NativeList<float3> vertices, NativeList<float2> uvs, JobHandle lastHandle = default)
        {
            float scaleU = info.UScale;
            float scaleV = info.VScale * (1f / (info.Segments.Length - 1));
            if (info.NormalizeUV)
            {
                scaleU /= (data.ColNum - 1) * cellSize;
                scaleV /= (data.RowNum - 1) * cellSize;
            }
            return CalculateUVsJob.Schedule(scaleU, scaleV, info.Segments.Length, infoArray, vertices, uvs, lastHandle);
        }

        [BurstCompile]
        private struct CalculateVerticesJob<VertexCalculator> : IJobParallelFor
            where VertexCalculator : struct, IVertexCalculator, IScaleAdjustableCalculator
        {
            public float offsetY;
            public float heightScale;
            public VertexCalculator calculator;
            [ReadOnly] public NativeArray<Segment> segments;
            [ReadOnly] public NativeArray<SegmentedCellInfo> infoArray;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                SegmentedCellInfo info = infoArray[index];
                if (info.info.config != MaskZero && info.info.config != MaskFull)
                {
                    CellVertices verts = new CellVertices() { corner = -1, bottomEdge = -1, leftEdge = -1 };
                    for (int i = 0; i < segments.Length; ++i)
                    {
                        var segment = segments[i];
                        verts.bottomEdge = (info.bottomVerticesStart < 0) ? -1 : info.bottomVerticesStart + i;
                        verts.leftEdge = (info.leftVerticesStart < 0) ? -1 : info.leftVerticesStart + i;
                        calculator.UpdateScaleInfo(segment.SideHzOffset, offsetY + segment.SideVcOffset, heightScale * segment.HeightScale);
                        calculator.CalculateVertices(index, info.info, verts, vertices);
                    }
                }
            }

            public static JobHandle Schedule(VertexCalculator calculator, SegmentedSideInfo info, NativeArray<Segment> segments, NativeArray<SegmentedCellInfo> infoArray, NativeList<float3> vertices, JobHandle dependOn)
            {
                var vertexJob = new CalculateVerticesJob<VertexCalculator>
                {
                    calculator = calculator,
                    segments = segments,
                    offsetY = info.OffsetY,
                    heightScale = info.HeightScale,
                    infoArray = infoArray,
                    vertices = vertices.AsDeferredJobArray()
                };
                return vertexJob.Schedule(infoArray.Length, CalculateVertexBatchNum, dependOn);
            }
        }

        private JobHandle ScheduleCalculateVerticesJob(Data data, SegmentedSideInfo info, bool useHeightData, float cellSize, NativeArray<SegmentedCellInfo> infoArray, NativeArray<EdgeNormals> edgeNormals, NativeList<float3> vertices, JobHandle lastHandle)
        {
            var segments = new NativeArray<Segment>(info.Segments, Allocator.TempJob);
            AddTemp(segments);

            float lerpToEdge = info.LerpToExactEdge;
            if (useHeightData)
            {
                if (lerpToEdge == 1f) 
                {
                    return ScheduleWithCalculator(
                        new ScaledBasicHeightVertexCalculator() 
                        {
                            colNum = data.ColNum,
                            cellSize = cellSize,
                            heights = data.HeightsRawData,
                            edgeNormalsArray = edgeNormals,
                            heightOffset = 0,
                            heightScale = 0,
                            sideOffsetScale = 0
                        }
                    );
                }
                else
                {
                    return ScheduleWithCalculator(
                        new ScaledLerpedHeightVertexCalculator() 
                        { 
                            colNum = data.ColNum, 
                            cellSize = cellSize, 
                            lerpToEdge = lerpToEdge, 
                            heights = data.HeightsRawData,
                            edgeNormalsArray = edgeNormals,
                            heightOffset = 0,
                            heightScale = 0,
                            sideOffsetScale = 0
                        }
                    );
                }
            }
            else
            {
                if (lerpToEdge == 1f) 
                {
                    return ScheduleWithCalculator(
                        new ScaledBasicVertexCalculator() 
                        { 
                            colNum = data.ColNum, 
                            cellSize = cellSize,
                            heightOffset = 0,
                            sideOffsetScale = 0,
                            edgeNormalsArray = edgeNormals
                        }
                    );
                }
                else 
                {
                    return ScheduleWithCalculator(
                        new ScaledLerpedVertexCalculator() 
                        { 
                            colNum = data.ColNum, 
                            cellSize = cellSize, 
                            lerpToEdge = lerpToEdge,
                            heightOffset = 0,
                            sideOffsetScale = 0,
                            edgeNormalsArray = edgeNormals 
                        }
                    );
                }
            }

            JobHandle ScheduleWithCalculator<VertexCalculator>(VertexCalculator calculator)
                where VertexCalculator : struct, IVertexCalculator, IScaleAdjustableCalculator
                => CalculateVerticesJob<VertexCalculator>.Schedule(calculator, info, segments, infoArray, vertices, lastHandle);
        }

        [BurstCompile]
        private struct CalculateTrianglesJob<Orderer> : IJobParallelFor
            where Orderer : struct, ITriangleOrderer
        {
            public int cornerColNum;
            public int segmentCount;
            public Orderer orderer;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<SegmentedCellInfo> infoArray;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<int> triangles;

            public void Execute(int index)
            {
                int cellColNum = cornerColNum - 1;
                int x = index % cellColNum;
                int y = index / cellColNum;
                index = y * cornerColNum + x;

                SegmentedCellInfo bl = infoArray[index];
                SegmentedCellInfo br = infoArray[index + 1];
                SegmentedCellInfo tl = infoArray[index + cornerColNum];
                
                CalculateSideSegmentIndices(orderer, bl.tris.start, bl, br, tl, triangles, segmentCount);
            }

            public static JobHandle Schedule(int colNum, int rowNum, int segmentCount, NativeArray<SegmentedCellInfo> infoArray, NativeList<int> triangles, JobHandle dependOn)
            {
                int cellCount = (colNum - 1) * (rowNum - 1);
                var trianglesJob = new CalculateTrianglesJob<Orderer>
                {
                    cornerColNum = colNum,
                    segmentCount = segmentCount,
                    orderer = new Orderer(),
                    infoArray = infoArray,
                    triangles = triangles.AsDeferredJobArray()
                };
                return trianglesJob.Schedule(cellCount, MeshTriangleBatchNum, dependOn);
            }
        }

        private static JobHandle ScheduleCalculateTrianglesJob(int colNum, int rowNum, int segmentCount, NativeArray<SegmentedCellInfo> infoArray, NativeList<int> triangles, bool isFlipped, JobHandle dependOn)
            => isFlipped ?
                 CalculateTrianglesJob<ReverseTriangleOrderer>.Schedule(colNum, rowNum, segmentCount, infoArray, triangles, dependOn) :
                 CalculateTrianglesJob<TriangleOrderer>.Schedule(colNum, rowNum, segmentCount, infoArray, triangles, dependOn);

        private static int CalculateSideSegmentIndices<TriangleOrderer>(TriangleOrderer orderer, int triangleIndex, SegmentedCellInfo bl, SegmentedCellInfo br, SegmentedCellInfo tl, NativeArray<int> triangles, int segmentCount)
            where TriangleOrderer : struct, ITriangleOrderer
        {
            if (bl.tris.length == 0)
            {
                return triangleIndex;
            }

            switch (bl.info.config)
            {
                // full
                case 0: break;
                case MaskBL | MaskBR | MaskTR | MaskTL: break;
                // corners
                case MaskBL: AddFace(ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                case MaskBR: AddFace(ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
                case MaskTR: AddFace(ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                case MaskTL: AddFace(ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                // halves
                case MaskBL | MaskBR: AddFace(ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                case MaskTL | MaskTR: AddFace(ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                case MaskBL | MaskTL: AddFace(ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                case MaskBR | MaskTR: AddFace(ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
                // diagonals
                case MaskBL | MaskTR:
                    {
                        AddFace(ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(bl), BottomLeftEdge(bl));
                        AddFace(ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(br), BottomLeftEdge(br));
                        break;
                    }
                case MaskTL | MaskBR:
                    {
                        AddFace(ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(tl), BottomBottomEdge(tl));
                        AddFace(ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(bl), BottomBottomEdge(bl));
                        break;
                    }
                // three quarters
                case MaskBL | MaskTR | MaskBR: AddFace(ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                case MaskBL | MaskTL | MaskBR: AddFace(ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                case MaskBL | MaskTL | MaskTR: AddFace(ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                case MaskTL | MaskTR | MaskBR: AddFace(ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
            }

            int TopLeftEdge(SegmentedCellInfo info) => info.leftVerticesStart;
            int BottomLeftEdge(SegmentedCellInfo info) => info.leftVerticesStart + 1;
            int TopBottomEdge(SegmentedCellInfo info) => info.bottomVerticesStart;
            int BottomBottomEdge(SegmentedCellInfo info) => info.bottomVerticesStart + 1;

            void AddFace(ref int nextIndex, int BL, int TL, int TR, int BR)
            {
                for (int i = 0; i < segmentCount - 1; ++i)
                {
                    orderer.AddTriangle(triangles, ref nextIndex, BL + i, TL + i, TR + i);
                    orderer.AddTriangle(triangles, ref nextIndex, BL + i, TR + i, BR + i);
                }
            }

            return triangleIndex;
        }
    }
}
