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
    public class SideCellMesher : CellMesher
    {
        public const byte FullCellIndexCount = 2 * 3;

        [Serializable]
        public class SideInfo : ScaledInfo
        {
            public float SideHeight = 1;
            public float BottomHeightScale = 1f;
            public float BottomScaledOffset = 0;
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
            bool needsNormalsData = info.ScaledOffset > 0 || info.BottomScaledOffset > 0;

            var infoArray = new NativeArray<SideCellInfo>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(infoArray);

            lastHandle = ScheduleCalculateInfoJob(data, info, infoArray, vertices, triangles, normals, uvs, lastHandle);

            NativeArray<EdgeNormals> edgeNormalsArray = default;
            if (needsNormalsData)
            {
                edgeNormalsArray = new NativeArray<EdgeNormals>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                AddTemp(edgeNormalsArray);

                lastHandle = ScheduleEdgeNormalsJob(new SideEdgeNormalCalculator(), data.ColNum, data.RowNum, infoArray, edgeNormalsArray, info.LerpToExactEdge, lastHandle);
            }

            JobHandle vertexHandle = ScheduleCalculateVerticesJob(data, info, useHeightData, cellSize, infoArray, edgeNormalsArray, vertices, lastHandle);

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
            byte triIndexCount = CalcTriIndexCount(config);
            SideCellInfo info = new SideCellInfo
            {
                info = new CellInfo { config = config, cornerDist = cornerDistance, rightDist = rightDistance, topDist = topDistance },
                topVerts = CreateCellVertices(config, ref nextVertex),
                bottomVerts = CreateCellVertices(config, ref nextVertex),
                tris = new IndexSpan(nextTriIndex, triIndexCount)
            };

            nextTriIndex += triIndexCount;

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

        public static byte CalcTriIndexCount(byte config)
        {
            switch (config)
            {
                // full
                case MaskBL | MaskBR | MaskTR | MaskTL: return 0;
                // corners
                case MaskBL: return 2 * 3;
                case MaskBR: return 2 * 3;
                case MaskTR: return 2 * 3;
                case MaskTL: return 2 * 3;
                // halves
                case MaskBL | MaskBR: return 2 * 3;
                case MaskTL | MaskTR: return 2 * 3;
                case MaskBL | MaskTL: return 2 * 3;
                case MaskBR | MaskTR: return 2 * 3;
                // diagonals
                case MaskBL | MaskTR: return 4 * 3;
                case MaskTL | MaskBR: return 4 * 3;
                // three quarters
                case MaskBL | MaskTR | MaskBR: return 2 * 3;
                case MaskBL | MaskTL | MaskBR: return 2 * 3;
                case MaskBL | MaskTL | MaskTR: return 2 * 3;
                case MaskTL | MaskTR | MaskBR: return 2 * 3;
            }
            return 0;
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

        public struct SideEdgeNormalCalculator : IEdgeNormalCalculator<SideCellInfo>
        {
            public void UpdateNormals(SideCellInfo cell, SideCellInfo top, SideCellInfo right, ref EdgeNormals cellNormal, ref EdgeNormals topNormal, ref EdgeNormals rightNormal, float lerpToEdge)
                => ScaledTopCellMesher.UpdateNormals(cell.info, top.info, right.info, ref cellNormal, ref topNormal, ref rightNormal, lerpToEdge);
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

        private static IVertexCalculator SelectVertexCalculator(Data data, bool useHeightData, float heightOffset, float heightScale, float lerpToEdge, float cellSize, float sideOffset, NativeArray<EdgeNormals> edgeNormals)
        {
            IVertexCalculator selected;
            if (sideOffset > 0)
            {
                if (useHeightData)
                {
                    if (lerpToEdge == 1f) { selected = new ScaledBasicHeightVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, heightOffset = heightOffset, heights = data.HeightsRawData, heightScale = heightScale, sideOffsetScale = sideOffset, edgeNormalsArray = edgeNormals }; }
                    else { selected = new ScaledLerpedHeightVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, lerpToEdge = lerpToEdge, heightOffset = heightOffset, heights = data.HeightsRawData, heightScale = heightScale, sideOffsetScale = sideOffset, edgeNormalsArray = edgeNormals }; }
                }
                else
                {
                    if (lerpToEdge == 1f) { selected = new ScaledBasicVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, heightOffset = heightOffset, sideOffsetScale = sideOffset, edgeNormalsArray = edgeNormals }; }
                    else { selected = new ScaledLerpedVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, lerpToEdge = lerpToEdge, heightOffset = heightOffset, sideOffsetScale = sideOffset, edgeNormalsArray = edgeNormals }; }
                }
            }
            else
            {
                if (useHeightData)
                {
                    if (lerpToEdge == 1f) { selected = new BasicHeightVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, heightOffset = heightOffset, heights = data.HeightsRawData, heightScale = heightScale }; }
                    else { selected = new LerpedHeightVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, lerpToEdge = lerpToEdge, heightOffset = heightOffset, heights = data.HeightsRawData, heightScale = heightScale }; }
                }
                else
                {
                    if (lerpToEdge == 1f) { selected = new BasicVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, heightOffset = heightOffset }; }
                    else { selected = new LerpedVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, lerpToEdge = lerpToEdge, heightOffset = heightOffset }; }
                }
            }
            return selected;
        }

        private static JobHandle CallScheduleCalculateMethod(IVertexCalculator top, IVertexCalculator bottom, bool isTopScaled, bool isBottomScaled, NativeArray<SideCellInfo> infoArray, NativeList<float3> vertices, JobHandle dependOn)
        {
            if (isTopScaled || isBottomScaled)
            {
                if (isTopScaled && isBottomScaled)
                {
                    if (ScheduleIfDoesMatch<ScaledBasicHeightVertexCalculator, ScaledBasicHeightVertexCalculator>(ref dependOn)) return dependOn;
                    if (ScheduleIfDoesMatch<ScaledLerpedHeightVertexCalculator, ScaledLerpedHeightVertexCalculator>(ref dependOn)) return dependOn;
                    if (ScheduleIfDoesMatch<ScaledBasicVertexCalculator, ScaledBasicVertexCalculator>(ref dependOn)) return dependOn;
                    if (ScheduleIfDoesMatch<ScaledLerpedVertexCalculator, ScaledLerpedVertexCalculator>(ref dependOn)) return dependOn;
                }
                else if (isTopScaled)
                {
                    if (ScheduleIfDoesMatch<ScaledBasicHeightVertexCalculator, BasicHeightVertexCalculator>(ref dependOn)) return dependOn;
                    if (ScheduleIfDoesMatch<ScaledLerpedHeightVertexCalculator, LerpedHeightVertexCalculator>(ref dependOn)) return dependOn;
                    if (ScheduleIfDoesMatch<ScaledBasicVertexCalculator, BasicVertexCalculator>(ref dependOn)) return dependOn;
                    if (ScheduleIfDoesMatch<ScaledLerpedVertexCalculator, LerpedVertexCalculator>(ref dependOn)) return dependOn;
                }
                else
                {
                    if (ScheduleIfDoesMatch<BasicHeightVertexCalculator, ScaledBasicHeightVertexCalculator>(ref dependOn)) return dependOn;
                    if (ScheduleIfDoesMatch<LerpedHeightVertexCalculator, ScaledLerpedHeightVertexCalculator>(ref dependOn)) return dependOn;
                    if (ScheduleIfDoesMatch<BasicVertexCalculator, ScaledBasicVertexCalculator>(ref dependOn)) return dependOn;
                    if (ScheduleIfDoesMatch<LerpedVertexCalculator, ScaledLerpedVertexCalculator>(ref dependOn)) return dependOn;
                }
            }
            else
            {
                if (ScheduleIfDoesMatch<BasicHeightVertexCalculator, BasicHeightVertexCalculator>(ref dependOn)) return dependOn;
                if (ScheduleIfDoesMatch<LerpedHeightVertexCalculator, LerpedHeightVertexCalculator>(ref dependOn)) return dependOn;
                if (ScheduleIfDoesMatch<BasicVertexCalculator, BasicVertexCalculator>(ref dependOn)) return dependOn;
                if (ScheduleIfDoesMatch<LerpedVertexCalculator, LerpedVertexCalculator>(ref dependOn)) return dependOn;
            }

            bool ScheduleIfDoesMatch<TopCalculator, BottomCalculator>(ref JobHandle dependOnHandle)
                where TopCalculator : struct, IVertexCalculator
                where BottomCalculator : struct, IVertexCalculator
            { 
                if (top is TopCalculator && bottom is BottomCalculator)
                {
                    dependOnHandle = ScheduleCalculateVerticesJob((TopCalculator)top, (BottomCalculator)bottom, infoArray, vertices, dependOnHandle);
                    return true;
                }
                return false;
            }
            
            Debug.LogError("Case not handled!");

            return dependOn;
        }

        public static JobHandle ScheduleCalculateVerticesJob(Data data, SideInfo info, bool useHeightData, float cellSize, NativeArray<SideCellInfo> infoArray, NativeArray<EdgeNormals> edgeNormals, NativeList<float3> vertices, JobHandle lastHandle)
        {
            float topHeightOffset = info.OffsetY + info.SideHeight;
            float bottomHeightOffset = info.OffsetY;

            float topHeightScale = info.HeightScale;
            float bottomHeightScale = info.BottomHeightScale;

            var top = SelectVertexCalculator(data, useHeightData, topHeightOffset, topHeightScale, info.LerpToExactEdge, cellSize, info.ScaledOffset, edgeNormals);
            var bottom = SelectVertexCalculator(data, useHeightData, bottomHeightOffset, bottomHeightScale, info.LerpToExactEdge, cellSize, info.BottomScaledOffset, edgeNormals);
            return CallScheduleCalculateMethod(top, bottom, info.ScaledOffset > 0, info.BottomScaledOffset > 0, infoArray, vertices, lastHandle);
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

            int TopLeftEdge(SideCellInfo info) => info.topVerts.leftEdge;
            int BottomLeftEdge(SideCellInfo info) => info.bottomVerts.leftEdge;
            int TopBottomEdge(SideCellInfo info) => info.topVerts.bottomEdge;
            int BottomBottomEdge(SideCellInfo info) => info.bottomVerts.bottomEdge;

            void AddFace(ref int nextIndex, int BL, int TL, int TR, int BR)
            {
                orderer.AddTriangle(triangles, ref nextIndex, BL, TL, TR);
                orderer.AddTriangle(triangles, ref nextIndex, BL, TR, BR);
            }

            return triangleIndex;
        }
    }
}
