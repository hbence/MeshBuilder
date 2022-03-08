using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

using static MeshBuilder.New.TopCellMesher;
using Data = MeshBuilder.MarchingSquaresMesher.Data;

namespace MeshBuilder.New
{
    public class ScaledTopCellMesher : CellMesher
    {
        private float cellSize;
        private Data data;
        private ScaledInfo info;

        public void Init(Data data, float cellSize, ScaledInfo info)
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
            bool needsNormalsData = info.ScaledOffset > 0;

            var infoArray = new NativeArray<TopCellInfo>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(infoArray);

            lastHandle = ScheduleCalculateInfoJob(data, info, infoArray, vertices, triangles, normals, uvs, lastHandle);

            NativeArray<EdgeNormals> edgeNormalsArray = default;
           // if (needsNormalsData)
            {
                edgeNormalsArray = new NativeArray<EdgeNormals>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                AddTemp(edgeNormalsArray);
            
                lastHandle = CalculateEdgeNormalsJob.Schedule(data.ColNum, data.RowNum, infoArray, edgeNormalsArray, info.LerpToExactEdge, lastHandle);
            }

            JobHandle vertexHandle = ScheduleCalculateVerticesJob(data, info, useHeightData, cellSize, infoArray, vertices, edgeNormalsArray, lastHandle);

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

        [BurstCompile]
        private struct CalculateEdgeNormalsJob : IJob
        {
            public int cellColNum;
            public int cellRowNum;

            public float lerpToEdge;

            [NativeDisableParallelForRestriction, ReadOnly] public NativeArray<TopCellInfo> cellInfos;
            [NativeDisableParallelForRestriction] public NativeArray<EdgeNormals> edgeNormals;

            public void Execute()
            {
                int cornerColNum = cellColNum + 1;
                for (int y = 0; y < cellRowNum; ++y)
                {
                    for (int x = 0; x < cellColNum; ++x)
                    {
                        int index = y * cornerColNum + x;
                        var corner = edgeNormals[index];
                        var right = edgeNormals[index + 1];
                        var top = edgeNormals[index + cornerColNum];

                        UpdateNormals(cellInfos[index].info, cellInfos[index + cornerColNum].info, cellInfos[index + 1].info, ref corner, ref top, ref right, lerpToEdge);

                        edgeNormals[index] = corner;
                        edgeNormals[index + 1] = right;
                        edgeNormals[index + cornerColNum] = top;
                    }
                }
            }

            public static JobHandle Schedule(int colNum, int rowNum, NativeArray<TopCellInfo> infoArray, NativeArray<EdgeNormals> edgeNormals, float lerpToEdge, JobHandle dependOn)
            {
                var infoJob = new CalculateEdgeNormalsJob
                {
                    cellColNum = colNum - 1,
                    cellRowNum = rowNum - 1,

                    lerpToEdge = lerpToEdge,

                    cellInfos = infoArray,
                    edgeNormals = edgeNormals
                };
                return infoJob.Schedule(dependOn);
            }
        }

        public static void UpdateNormals(CellInfo cell, CellInfo top, CellInfo right, ref EdgeNormals cellNormal, ref EdgeNormals topNormal, ref EdgeNormals rightNormal, float lerpToEdge)
        {
            switch (cell.config)
            {
                // full
                case MaskBL | MaskBR | MaskTR | MaskTL: break;
                // corners
                case MaskBL: AddNormal(0, LerpVc(cell, lerpToEdge), LerpHz(cell, lerpToEdge), 0, ref cellNormal.bottomEdgeDir, ref cellNormal.leftEdgeDir); break;
                case MaskBR: AddNormal(LerpHz(cell, lerpToEdge), 0, 1, LerpVc(right, lerpToEdge), ref cellNormal.bottomEdgeDir, ref rightNormal.leftEdgeDir); break;
                case MaskTR: AddNormal(1, LerpVc(right, lerpToEdge), LerpHz(top, lerpToEdge), 1, ref rightNormal.leftEdgeDir, ref topNormal.bottomEdgeDir); break;
                case MaskTL: AddNormal(LerpHz(top, lerpToEdge), 1, 0, LerpVc(cell, lerpToEdge), ref topNormal.bottomEdgeDir, ref cellNormal.leftEdgeDir); break;
                // halves
                case MaskBL | MaskBR: AddNormal(0, LerpVc(cell, lerpToEdge), 1, LerpVc(right, lerpToEdge), ref cellNormal.leftEdgeDir, ref rightNormal.leftEdgeDir); break;
                case MaskTL | MaskTR: AddNormal(1, LerpVc(right, lerpToEdge), 0, LerpVc(cell, lerpToEdge), ref cellNormal.leftEdgeDir, ref rightNormal.leftEdgeDir); break;
                case MaskBL | MaskTL: AddNormal(LerpHz(top, lerpToEdge), 1, LerpHz(cell, lerpToEdge), 0, ref cellNormal.bottomEdgeDir, ref topNormal.bottomEdgeDir); break;
                case MaskBR | MaskTR: AddNormal(LerpHz(cell, lerpToEdge), 0, LerpHz(top, lerpToEdge), 1, ref cellNormal.bottomEdgeDir, ref topNormal.bottomEdgeDir); break;
                // diagonals
                case MaskBL | MaskTR:
                    {
                        AddNormal(0, LerpVc(cell, lerpToEdge), LerpHz(top, lerpToEdge), 1, ref cellNormal.leftEdgeDir, ref topNormal.bottomEdgeDir);
                        AddNormal(1, LerpVc(right, lerpToEdge), LerpHz(cell, lerpToEdge), 0, ref rightNormal.leftEdgeDir, ref cellNormal.bottomEdgeDir);
                        break;
                    }
                case MaskTL | MaskBR:
                    {
                        AddNormal(LerpHz(top, lerpToEdge), 1, 1, LerpVc(right, lerpToEdge), ref topNormal.bottomEdgeDir, ref rightNormal.leftEdgeDir);
                        AddNormal(LerpHz(cell, lerpToEdge), 0, 0, LerpVc(cell, lerpToEdge), ref cellNormal.leftEdgeDir, ref cellNormal.bottomEdgeDir);
                        break;
                    }
                // three quarters
                case MaskBL | MaskTR | MaskBR: AddNormal(0, LerpVc(cell, lerpToEdge), LerpHz(top, lerpToEdge), 1, ref cellNormal.leftEdgeDir, ref topNormal.bottomEdgeDir); break;
                case MaskBL | MaskTL | MaskBR: AddNormal(LerpHz(top, lerpToEdge), 1, 1, LerpVc(right, lerpToEdge), ref topNormal.bottomEdgeDir, ref rightNormal.leftEdgeDir); break;
                case MaskBL | MaskTL | MaskTR: AddNormal(1, LerpVc(right, lerpToEdge), LerpHz(cell, lerpToEdge), 0, ref rightNormal.leftEdgeDir, ref cellNormal.bottomEdgeDir); break;
                case MaskTL | MaskTR | MaskBR: AddNormal(LerpHz(cell, lerpToEdge), 0, 0, LerpVc(cell, lerpToEdge), ref cellNormal.leftEdgeDir, ref cellNormal.bottomEdgeDir); break;
            }
        }

        private const float Epsilon = math.EPSILON;
        private static float LerpVc(CellInfo cornerInfo, float lerpToEdge) => LerpT(cornerInfo.cornerDist + Epsilon, cornerInfo.topDist + Epsilon, lerpToEdge);
        private static float LerpHz(CellInfo cornerInfo, float lerpToEdge) => LerpT(cornerInfo.cornerDist + Epsilon, cornerInfo.rightDist + Epsilon, lerpToEdge);

        private static void AddNormal(float ax, float ay, float bx, float by, ref float2 edgeDirA, ref float2 edgeDirB)
        {
            float2 dir = new float2(ay - by, bx - ax);
            dir = math.normalize(dir);
            edgeDirA += dir;
            edgeDirB += dir;
        }

        protected struct ScaledBasicVertexCalculator : IVertexCalculator
        {
            public int colNum;
            public float cellSize;
            public float heightOffset;
            public float sideOffsetScale;

            [ReadOnly] public NativeArray<EdgeNormals> edgeNormalsArray;

            public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices)
                => CalculateVertices(index, info, verts, vertices, colNum, cellSize, heightOffset, sideOffsetScale, edgeNormalsArray);

            static public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices, int colNum, float cellSize, float heightOffset, float sideOffsetScale, NativeArray<EdgeNormals> edgeNormalsArray)
            {
                float3 pos = BasicVertexCalculator.CalculatePosition(index, colNum, cellSize, heightOffset);

                EdgeNormals normals = edgeNormalsArray[index];

                if (verts.corner >= 0)
                {
                    vertices[verts.corner] = pos;
                }

                if (verts.leftEdge >= 0)
                {
                    float edgeLerp = cellSize * VcLerpT(info);
                    float2 offset = math.normalize(normals.leftEdgeDir) * sideOffsetScale;
                    vertices[verts.leftEdge] = new float3(pos.x + offset.x, pos.y, pos.z + edgeLerp + offset.y);
                }

                if (verts.bottomEdge >= 0)
                {
                    float edgeLerp = cellSize * HzLerpT(info);
                    float2 offset = math.normalize(normals.bottomEdgeDir) * sideOffsetScale;
                    vertices[verts.bottomEdge] = new float3(pos.x + edgeLerp + offset.x, pos.y, pos.z + offset.y);
                }
            }
        }

        protected struct ScaledLerpedVertexCalculator : IVertexCalculator
        {
            public int colNum;
            public float cellSize;
            public float heightOffset;
            public float lerpToEdge;

            public float sideOffsetScale;
            [ReadOnly] public NativeArray<EdgeNormals> edgeNormalsArray;

            public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices)
                => CalculateVertices(index, info, verts, vertices, colNum, cellSize, heightOffset, lerpToEdge, sideOffsetScale, edgeNormalsArray);

            static public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices, int colNum, float cellSize, float heightOffset, float lerpToEdge, float sideOffsetScale, NativeArray<EdgeNormals> edgeNormalsArray)
            {
                int x = index % colNum;
                int y = index / colNum;

                float3 pos = new float3(x * cellSize, heightOffset, y * cellSize);

                EdgeNormals normals = edgeNormalsArray[index];

                if (verts.corner >= 0)
                {
                    vertices[verts.corner] = pos;
                }

                if (verts.leftEdge >= 0)
                {
                    float edgeLerp = cellSize * VcLerpT(info, lerpToEdge);
                    float2 offset = math.normalize(normals.leftEdgeDir) * sideOffsetScale;
                    vertices[verts.leftEdge] = new float3(pos.x + offset.x, pos.y, pos.z + edgeLerp + offset.y);
                }

                if (verts.bottomEdge >= 0)
                {
                    float edgeLerp = cellSize * HzLerpT(info, lerpToEdge);
                    float2 offset = math.normalize(normals.bottomEdgeDir) * sideOffsetScale;
                    vertices[verts.bottomEdge] = new float3(pos.x + edgeLerp + offset.x, pos.y, pos.z + offset.y);
                }
            }
        }

        protected struct ScaledBasicHeightVertexCalculator : IVertexCalculator
        {
            public int colNum;
            public float cellSize;
            public float heightOffset;
            
            public float sideOffsetScale;
            [ReadOnly] public NativeArray<EdgeNormals> edgeNormalsArray;

            public float heightScale;
            [ReadOnly] public NativeArray<float> heights;

            public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices)
                => ScaledBasicVertexCalculator.CalculateVertices(index, info, verts, vertices, colNum, cellSize, heightOffset + heightScale * heights[index], sideOffsetScale, edgeNormalsArray);
        }

        protected struct ScaledLerpedHeightVertexCalculator : IVertexCalculator
        {
            public int colNum;
            public float cellSize;
            public float heightOffset;
            public float lerpToEdge;

            public float sideOffsetScale;
            [ReadOnly] public NativeArray<EdgeNormals> edgeNormalsArray;

            public float heightScale;
            [ReadOnly] public NativeArray<float> heights;

            public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices)
                => ScaledLerpedVertexCalculator.CalculateVertices(index, info, verts, vertices, colNum, cellSize, heightOffset + heightScale * heights[index], lerpToEdge, sideOffsetScale, edgeNormalsArray);
        }

        private static JobHandle ScheduleCalculateVerticesJob(Data data, ScaledInfo info, bool useHeightData, float cellSize, NativeArray<TopCellInfo> infoArray, NativeList<float3> vertices, NativeArray<EdgeNormals> edgeNormalsArray, JobHandle lastHandle)
        {
            if (!edgeNormalsArray.IsCreated)
            {
                return TopCellMesher.ScheduleCalculateVerticesJob(data, info, useHeightData, cellSize, infoArray, vertices, lastHandle);
            }

            if (useHeightData)
            {
                if (info.LerpToExactEdge == 1f)
                {
                    var vertexCalculator = new ScaledBasicHeightVertexCalculator() 
                    { 
                        colNum = data.ColNum, 
                        cellSize = cellSize, 
                        heightOffset = info.OffsetY, 
                        heights = data.HeightsRawData, 
                        heightScale = info.HeightScale, 
                        edgeNormalsArray = edgeNormalsArray, 
                        sideOffsetScale = info.ScaledOffset 
                    };
                    return TopCellMesher.ScheduleCalculateVerticesJob(vertexCalculator, infoArray, vertices, lastHandle);
                }
                else
                {
                    var vertexCalculator = new ScaledLerpedHeightVertexCalculator() 
                    { 
                        colNum = data.ColNum, 
                        cellSize = cellSize, 
                        lerpToEdge = info.LerpToExactEdge, 
                        heightOffset = info.OffsetY,
                        heights = data.HeightsRawData, 
                        heightScale = info.HeightScale, 
                        edgeNormalsArray = edgeNormalsArray, 
                        sideOffsetScale = info.ScaledOffset 
                    };
                    return TopCellMesher.ScheduleCalculateVerticesJob(vertexCalculator, infoArray, vertices, lastHandle);
                }
            }
            else
            {
                if (info.LerpToExactEdge == 1f)
                {
                    var vertexCalculator = new ScaledBasicVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, heightOffset = info.OffsetY, edgeNormalsArray = edgeNormalsArray, sideOffsetScale = info.ScaledOffset };
                    return TopCellMesher.ScheduleCalculateVerticesJob(vertexCalculator, infoArray, vertices, lastHandle);
                }
                else
                {
                    var vertexCalculator = new ScaledLerpedVertexCalculator() { colNum = data.ColNum, cellSize = cellSize, lerpToEdge = info.LerpToExactEdge, heightOffset = info.OffsetY, edgeNormalsArray = edgeNormalsArray, sideOffsetScale = info.ScaledOffset };
                    return TopCellMesher.ScheduleCalculateVerticesJob(vertexCalculator, infoArray, vertices, lastHandle);
                }
            }
        }
    }
}
