using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshBuilder
{
    public class SplineMesher : Builder
    {
        private const uint DefaultMeshBufferFlags = (uint)(MeshData.Buffer.Vertex | MeshData.Buffer.Triangle);

        private const int DefaultVertexOffsetInnerBatchNum = 1024;

        public int VertexOffsetInnerBatchnum = DefaultVertexOffsetInnerBatchNum;

        private float3 meshVertexOffset;
        private SplineCache splineCache;

        private GenerationHandler generationHandler = new GenerationHandler();
        
        private MeshData meshData;
        private SplineModifier splineModifier = new SplineModifier();

        public void Init(SplineCache splineCache, int cellColCount, float meshCellWidth, float meshCellLength, float3 positionOffset = default)
        {
            this.splineCache = splineCache;
            meshVertexOffset = positionOffset;

            int rowNum = Mathf.CeilToInt(splineCache.Distance / meshCellLength) + 1;
            generationHandler.InitSimpleGrid(meshCellWidth, meshCellLength, cellColCount, rowNum);

            splineModifier.Init(this.splineCache, meshCellLength);

            Inited();
        }

        public void Init(SplineCache splineCache, float3[] crossSection, float meshCellLength, float3 positionOffset = default)
        {
            this.splineCache = splineCache;
            meshVertexOffset = positionOffset;

            int rowNum = Mathf.CeilToInt(splineCache.Distance / meshCellLength) + 1;
            generationHandler.InitSubmeshEdges(meshCellLength, rowNum);
            generationHandler.AddSubmeshEdge(0, crossSection);

            splineModifier.Init(this.splineCache, meshCellLength);

            Inited();
        }

        public void Init(SplineCache splineCache, float3[][] crossSections, float meshCellLength, float3 positionOffset = default)
        {
            this.splineCache = splineCache;
            meshVertexOffset = positionOffset;

            int rowNum = Mathf.CeilToInt(splineCache.Distance / meshCellLength) + 1;
            generationHandler.InitSubmeshEdges(meshCellLength, rowNum);

            foreach (float3[] cross in crossSections)
            {
                generationHandler.AddSubmeshEdge(0, cross);
            }

            splineModifier.Init(this.splineCache, meshCellLength);

            Inited();
        }

        public void Init(SplineCache splineCache, float3[][][] crossSections, float meshCellLength, float3 positionOffset = default)
        {
            this.splineCache = splineCache;
            meshVertexOffset = positionOffset;

            int rowNum = Mathf.CeilToInt(splineCache.Distance / meshCellLength) + 1;
            generationHandler.InitSubmeshEdges(meshCellLength, rowNum);

            for (int submesh = 0; submesh < crossSections.Length; ++submesh)
            {
                foreach (float3[] cross in crossSections[submesh])
                {
                    generationHandler.AddSubmeshEdge(submesh, cross);
                }
            }

            splineModifier.Init(this.splineCache, meshCellLength);

            Inited();
        }

        public void InitForMultipleSubmeshes(SplineCache splineCache, float meshCellLength, float3 positionOffset = default)
        {
            this.splineCache = splineCache;
            meshVertexOffset = positionOffset;

            int rowNum = Mathf.CeilToInt(splineCache.Distance / meshCellLength) + 1;
            generationHandler.InitSubmeshEdges(meshCellLength, rowNum);

            splineModifier.Init(this.splineCache, meshCellLength);

            Inited();
        }

        public void AddSubmesh(int submesh, float3[] crossSection)
        {
            Debug.Assert(IsInitialized && generationHandler.IsInitedForSubmeshes, "not initized for submeshes");
            generationHandler.AddSubmeshEdge(submesh, crossSection);
        }

        public void AddSubmesh(int submesh, float3[][] crossSections)
        {
            Debug.Assert(IsInitialized && generationHandler.IsInitedForSubmeshes, "not initized for submeshes");

            foreach (var cross in crossSections)
            {
                generationHandler.AddSubmeshEdge(submesh, cross);
            }
        }

        protected override JobHandle StartGeneration(JobHandle dependOn)
        {
            dependOn = generationHandler.StartGeneration(dependOn, this);

            dependOn = splineModifier.Start(meshData, dependOn);

            if (!meshVertexOffset.Equals(default))
            {
                dependOn = Utils.VertexOffsetJob.Schedule(meshData.Vertices, meshVertexOffset, 1024, dependOn);
            }
            
            return dependOn;
        }

        protected override void EndGeneration(Mesh mesh)
        {
            splineModifier.Complete();

            meshData.UpdateMesh(mesh);
        }

        private class GenerationHandler
        {
            private enum Type { Simple, SubmeshEdges }

            private Type type = Type.Simple;
            public bool IsInitedForSubmeshes => type == Type.SubmeshEdges;

            public uint MeshDataBufferFlags { get; set; } = DefaultMeshBufferFlags;

            private List<SubmeshEdge> submeshEdges = new List<SubmeshEdge>();

            public float CellWidth { get; private set; }
            public float CellLength { get; private set; }

            public int ColNum { get; private set; }
            public int RowNum { get; private set; }

            public void InitSimpleGrid(float cellWidth, float cellLength, int colNum, int rowNum)
            {
                type = Type.Simple;

                submeshEdges.Clear();

                CellWidth = cellWidth;
                CellLength = cellLength;
                ColNum = colNum;
                RowNum = rowNum;
            }

            public void InitSubmeshEdges(float cellLength, int rowNum)
            {
                InitSimpleGrid(0, cellLength, 0, rowNum);
                type = Type.SubmeshEdges;
            }
            public void AddSubmeshEdge(int submeshIndex, float3[] edge)
            {
                Debug.Assert(edge.Length > 1, "a cross section needs more than one vertex");
                submeshEdges.Add(new SubmeshEdge(submeshIndex, edge));
            }
            public JobHandle StartGeneration(JobHandle dependOn, SplineMesher mesher)
            {
                Debug.Assert(CellLength > 0, "cell length is not set!");
                Debug.Assert(RowNum > 0, "row num is not set");

                if (type == Type.Simple)
                {
                    Debug.Assert(CellWidth > 0, "cell width is not set!");
                    Debug.Assert(ColNum > 0, "col num is not set");

                    int vertexCount = GenerateGrid.CalculateVertexCount(ColNum, RowNum);
                    int indexCount = GenerateGrid.CalculateIndexCount(ColNum, RowNum);

                    mesher.meshData = new MeshData(vertexCount, indexCount, Allocator.TempJob, MeshDataBufferFlags);
                    mesher.AddTemp(mesher.meshData);

                    dependOn = GenerateGrid.Schedule(ColNum, RowNum, CellWidth, CellLength, mesher.meshData, dependOn);
                }
                else
                {
                    Debug.Assert(submeshEdges.Count > 0, "submeshes are not set!");

                    if (submeshEdges.Count == 1)
                    {
                        NativeArray<float3> edge = new NativeArray<float3>(submeshEdges[0].EdgeVertices, Allocator.TempJob);
                        mesher.AddTemp(edge);

                        int vertexCount = GenerateVertexEdgeGrid.CalculateVertexCount(edge.Length, RowNum);
                        int indexCount = GenerateVertexEdgeGrid.CalculateIndexCount(edge.Length, RowNum);

                        mesher.meshData = new MeshData(vertexCount, indexCount, Allocator.TempJob, MeshDataBufferFlags);
                        mesher.AddTemp(mesher.meshData);

                        dependOn = GenerateVertexEdgeGrid.Schedule(edge, RowNum, CellLength, 0, 0, mesher.meshData, dependOn);
                    }
                    else
                    {
                        submeshEdges.Sort((SubmeshEdge a, SubmeshEdge b) => a.SubmeshIndex.CompareTo(b.SubmeshIndex));

                        VertexEdgeInfo[] edges = new VertexEdgeInfo[submeshEdges.Count];
                        List<Utils.Offset> submeshOffsets = new List<Utils.Offset>();
                        int vertexCount = 0;
                        int indexCount = 0;
                        Utils.Offset submeshOffset = new Utils.Offset { index = 0, length = 0 };
                        int lastSubmesh = submeshEdges[0].SubmeshIndex;
                        for (int i = 0; i < submeshEdges.Count; ++i)
                        {
                            var edge = new NativeArray<float3>(submeshEdges[i].EdgeVertices, Allocator.TempJob);
                            mesher.AddTemp(edge);

                            edges[i] = new VertexEdgeInfo(edge, vertexCount, indexCount);

                            vertexCount += GenerateVertexEdgeGrid.CalculateVertexCount(edge.Length, RowNum);
                            indexCount += GenerateVertexEdgeGrid.CalculateIndexCount(edge.Length, RowNum);

                            submeshOffset.length += GenerateVertexEdgeGrid.CalculateIndexCount(edge.Length, RowNum);

                            int nextSubmesh = (i + 1 >= submeshEdges.Count) ? -1 : submeshEdges[i + 1].SubmeshIndex;
                            if (lastSubmesh != nextSubmesh)
                            {
                                lastSubmesh = nextSubmesh;

                                submeshOffsets.Add(submeshOffset);
                                submeshOffset = new Utils.Offset { index = indexCount, length = 0 };
                            }
                        }

                        mesher.meshData = new MeshData(vertexCount, indexCount, submeshOffsets.ToArray(), Allocator.TempJob, MeshDataBufferFlags);
                        mesher.AddTemp(mesher.meshData);

                        JobHandle genResult = default;
                        foreach(var edgeInfo in edges)
                        {
                            genResult = JobHandle.CombineDependencies(genResult, edgeInfo.Generate(RowNum, CellLength, mesher.meshData, dependOn));
                        }
                        dependOn = genResult;
                    }
                }

                return dependOn;
            }

            private class VertexEdgeInfo
            {
                public NativeArray<float3> edge;
                public int vertexStart;
                public int indexStart;

                public VertexEdgeInfo(NativeArray<float3> edge, int vertexStart, int indexStart)
                {
                    this.edge = edge;
                    this.vertexStart = vertexStart;
                    this.indexStart = indexStart;
                }

                public JobHandle Generate(int rowNum, float cellLength, MeshData meshData, JobHandle dependOn)
                    => GenerateVertexEdgeGrid.Schedule(edge, rowNum, cellLength, vertexStart, indexStart, meshData, dependOn);
            }
        }

        private class SubmeshEdge
        {
            public int SubmeshIndex { get; private set; }
            public float3[] EdgeVertices { get; private set; }
            public SubmeshEdge(int submeshIndex, float3[] edge)
            {
                SubmeshIndex = submeshIndex;
                EdgeVertices = edge;
            }
        }

        [BurstCompile]
        private struct GenerateGrid : IJob
        {
            public int colNum;
            public int rowNum;
            public float cellWidth;
            public float cellLength;

            [WriteOnly] public NativeArray<float3> vertices;
            [WriteOnly] public NativeArray<int> indices;

            public void Execute()
            {
                int vertexColNum = colNum + 1;

                float3 offset = new float3(colNum * cellWidth * -0.5f, 0, 0);
                for (int row = 0; row <= rowNum; ++row)
                {
                    for (int col = 0; col <= colNum; ++col)
                    {
                        vertices[row * vertexColNum + col] = new float3(col * cellWidth, 0, row * cellLength) + offset;
                    }
                }

                for (int row = 0; row < rowNum; ++row)
                {
                    for (int col = 0; col < colNum; ++col)
                    {
                        SetIndices(col, row, vertexColNum);
                    }
                }
            }

            private void SetIndices(int col, int row, int vertexColNum)
            {
                int vertex = row * vertexColNum + col;
                int start = ((row * colNum) + col) * 6;

                indices[start] = vertex;
                indices[start + 1] = vertex + vertexColNum;
                indices[start + 2] = vertex + 1;

                indices[start + 3] = vertex + vertexColNum;
                indices[start + 4] = vertex + vertexColNum + 1;
                indices[start + 5] = vertex + 1;
            }

            public static JobHandle Schedule(int colNum, int rowNum, float cellWidth, float cellHeight, MeshData meshData, JobHandle dependOn)
            {
                var generateGrid = new GenerateGrid
                {
                    colNum = colNum,
                    rowNum = rowNum,
                    cellWidth = cellWidth,
                    cellLength = cellHeight,
                    vertices = meshData.Vertices,
                    indices = meshData.Triangles
                };
                return generateGrid.Schedule(dependOn);
            }

            public static int CalculateVertexCount(int cellColNum, int cellRowNum)
                => (cellColNum + 1) * (cellRowNum + 1);

            public static int CalculateIndexCount(int cellColNum, int cellRowNum)
                => (cellColNum * cellRowNum) * 2 * 3;
        }

        [BurstCompile]
        private struct GenerateVertexEdgeGrid : IJob
        {
            public int cellRowNum;
            public float cellLength;

            public int vertexStart;
            public int indexStart;

            [ReadOnly] public NativeArray<float3> edgeVertices;

            // this job is used to write parts of the mesh so it should be safe 
            // to write mesh data at the same time, there is no overlap
            [NativeDisableContainerSafetyRestriction][WriteOnly] public NativeArray<float3> vertices;
            [NativeDisableContainerSafetyRestriction][WriteOnly] public NativeArray<int> indices;

            public void Execute()
            {
                int vertexColNum = edgeVertices.Length;
                float distance = 0;
                for (int row = 0; row <= cellRowNum; ++row)
                {
                    for (int col = 0; col < vertexColNum; ++col)
                    {
                        var v = edgeVertices[col];
                        v.z += distance;
                        vertices[vertexStart + row * vertexColNum + col] = v;
                    }
                    distance += cellLength;
                }

                for (int row = 0; row < cellRowNum; ++row)
                {
                    for (int col = 0; col < vertexColNum - 1; ++col)
                    {
                        SetIndices(col, row, vertexColNum);
                    }
                }
            }

            private void SetIndices(int col, int row, int vertexColNum)
            {
                int vertex = vertexStart + row * vertexColNum + col;
                int start = indexStart + ((row * (vertexColNum - 1)) + col) * 6;

                indices[start] = vertex;
                indices[start + 1] = vertex + vertexColNum;
                indices[start + 2] = vertex + 1;

                indices[start + 3] = vertex + vertexColNum;
                indices[start + 4] = vertex + vertexColNum + 1;
                indices[start + 5] = vertex + 1;
            }

            public static JobHandle Schedule(NativeArray<float3> edgeVertices, int cellRowNum, float cellLength, int vertexStart, int indexStart, MeshData meshData, JobHandle dependOn)
            {
                var generateGrid = new GenerateVertexEdgeGrid
                {
                    cellRowNum = cellRowNum,
                    cellLength = cellLength,
                    vertexStart = vertexStart,
                    indexStart = indexStart,
                    edgeVertices = edgeVertices,
                    vertices = meshData.Vertices,
                    indices = meshData.Triangles
                };
                return generateGrid.Schedule(dependOn);
            }

            public static int CalculateVertexCount(int vertexEdgeLength, int cellRowCount)
                => vertexEdgeLength * (cellRowCount + 1);

            public static int CalculateIndexCount(int vertexEdgeLength, int cellRowCount)
                => (vertexEdgeLength - 1) * cellRowCount * 6;
        }
    }
}
