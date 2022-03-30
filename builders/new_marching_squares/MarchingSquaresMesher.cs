using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;


using static MeshBuilder.Utils;
using static MeshBuilder.MarchingSquaresMesher;
using MeshBuffer = MeshBuilder.MeshData.Buffer;

namespace MeshBuilder.New
{
    public class MarchingSquaresMesher : Builder
    {
        private const uint DefMeshDataBufferFlags = (uint)MeshBuffer.Vertex | (uint)MeshBuffer.Triangle | (uint)MeshBuffer.Normal;

        private static readonly float3 DefaultNormal = new float3(0, 1, 0);
        private static readonly float2 DefaultUV = new float2(0, 0);

        public enum MesherType
        {
            TopCell,
            ScaledTopCell,
            OptimizedTopCell,
            SideCell,
            SegmentedSideCell,
            FullCell
        }

        public uint MeshDataBufferFlags { get; set; } = DefMeshDataBufferFlags;

        public float CellSize { get; private set; }

        // mraching squares data
        public Data Data { get; private set; }

        private List<MesherHandler[]> submeshInfos = new List<MesherHandler[]>();
        
        public void Init(Data data, float cellSize, params MesherInfo[] mesherInfos)
        {
            Data = data;
            CellSize = cellSize;

            submeshInfos.Clear();
            AddMeshers(mesherInfos);

            Inited();
        }

        public void Init(Data data, float cellSize, MesherInfo[][] mesherInfos)
        {
            Data = data;
            CellSize = cellSize;

            submeshInfos.Clear();
            foreach(var infos in mesherInfos)
            {
                AddMeshers(infos);
            }

            Inited();
        }

        public void AddSubmesh(params MesherInfo[] mesherInfos)
        {
            AddMeshers(mesherInfos);
        }

        private void AddMeshers(MesherInfo[] mesherInfos)
        {
            if (mesherInfos != null && mesherInfos.Length > 0)
            {
                MesherHandler[] handlers = new MesherHandler[mesherInfos.Length];
                for (int i = 0; i < mesherInfos.Length; ++i)
                {
                    handlers[i] = new MesherHandler(mesherInfos[i]);
                }

                submeshInfos.Add(handlers);
            }
        }

        override protected JobHandle StartGeneration(JobHandle lastHandle)
        {
            if (submeshInfos.Count == 0)
            {
                Debug.LogWarning("There is no submesh info!!! add submeshes!");
                return lastHandle;
            }

            JobHandle meshHandle = lastHandle;
            foreach (var handlerArray in submeshInfos)
            {
                if (handlerArray.Length > 0)
                {
                    foreach(var handler in handlerArray)
                    {
                        var jobHandle = handler.StartGeneration(Data, CellSize, lastHandle);
                        meshHandle = JobHandle.CombineDependencies(meshHandle, jobHandle);
                    }
                }
            }
            return meshHandle;
        }

        protected override void EndGeneration(Mesh mesh)
        {
            var (doesGenerateUvs, doesGenerateNormals) = CheckAllInfoForMiscData();

            NativeList<float3> vertices = new NativeList<float3>(Allocator.Temp);
            NativeList<int> triangles = new NativeList<int>(Allocator.Temp);
            NativeList<float3> normals = doesGenerateNormals ? new NativeList<float3>(Allocator.Temp) : default;
            NativeList<float2> uvs = doesGenerateUvs ? new NativeList<float2>(Allocator.Temp) : default;

            List<Offset> submeshOffsets = new List<Offset>();
            int submeshStart = 0;
            int vertexOffset = 0;
            foreach (var handlerArray in submeshInfos)
            {
                int submeshLength = 0;
                foreach(var handler in handlerArray)
                {
                    var (addedVerts, addedTris) = handler.CompleteAppendData(vertices, triangles, normals, uvs);

                    OffsetTriangles(triangles, submeshStart + submeshLength, addedTris, vertexOffset);

                    submeshLength += addedTris;
                    vertexOffset += addedVerts;

                    if (normals.IsCreated && !handler.DoesGenerateNormals)
                    {
                        FillList(normals, vertices.Length - normals.Length, DefaultNormal);
                    }
                    if (uvs.IsCreated && !handler.DoesGenerateUvs)
                    {
                        FillList(uvs, vertices.Length - uvs.Length, DefaultUV);
                    }
                }
                submeshOffsets.Add(new Offset() { index = submeshStart, length = submeshLength });
                submeshStart += submeshLength;
            }

            uint flags = MeshDataBufferFlags;
            if (uvs.IsCreated) { flags |= (uint)MeshBuffer.UV; }

            using (MeshData data = new MeshData(vertices.Length, triangles.Length, submeshOffsets.ToArray(), Allocator.Temp, flags))
            {
                NativeArray<float3>.Copy(vertices, data.Vertices);
                NativeArray<int>.Copy(triangles, data.Triangles);
                if (normals.IsCreated) { NativeArray<float3>.Copy(normals, data.Normals); }
                if (uvs.IsCreated) { NativeArray<float2>.Copy(uvs, data.UVs); }

                data.UpdateMesh(mesh, MeshData.UpdateMode.Clear);
            }

            SafeDispose(ref vertices);
            SafeDispose(ref triangles);
            SafeDispose(ref normals);
            SafeDispose(ref uvs);
        }

        private (bool doesGenerateUvs, bool doesGenerateNormals) CheckAllInfoForMiscData()
        {
            bool doesGenerateUVs = false;
            bool doesGenerateNormals = false;
            foreach (var handlerArray in submeshInfos)
            {
                foreach (var handler in handlerArray)
                {
                    doesGenerateUVs |= handler.DoesGenerateUvs;
                    doesGenerateNormals |= handler.DoesGenerateNormals;
                }
            }
            return (doesGenerateUVs, doesGenerateNormals);
        }

        private static void FillList<T>(NativeList<T> list, int count, T value) where T : struct
        {
            for(int i = 0; i < count; ++i)
            {
                list.Add(value);
            }
        }

        private static void OffsetTriangles(NativeList<int> tris, int start, int length, int offset)
        {
            for(int i = start; i < start + length; ++i)
            {
                tris[i] += offset;
            }
        }

        public class MesherInfo
        { 
            public MesherType Type { get; }
            public CellMesher.Info info;
            public CellMesher.Info Info 
            {
                get => info; 
                set
                {
                    if (DoesTypeMatchInfo(Type, value))
                    {
                        info = value;
                    }
                    else
                    {
                        Debug.LogError("Type and info mismatch!");
                    }
                }
            }

            private MesherInfo(MesherType type, CellMesher.Info info)
            {
                Type = type;
                Info = info;
            }

            public CellMesher CreateMesher()
            {
                switch(Type)
                {
                    case MesherType.TopCell: return new TopCellMesher();
                    case MesherType.ScaledTopCell: return new ScaledTopCellMesher();
                    case MesherType.OptimizedTopCell: return new OptimizedTopCellMesher();
                    case MesherType.SideCell: return new SideCellMesher();
                    case MesherType.SegmentedSideCell: return new SegmentedSideCellMesher();
                    case MesherType.FullCell: return new FullCellMesher();
                }

                Debug.LogError("Not handled mesher type!");

                return null;
            }

            static public bool DoesTypeMatchInfo(MesherType type, CellMesher.Info info)
            {
                switch (type)
                {
                    case MesherType.TopCell: return true;
                    case MesherType.ScaledTopCell: return info is CellMesher.ScaledInfo;
                    case MesherType.OptimizedTopCell: return info is OptimizedTopCellMesher.OptimizedInfo;
                    case MesherType.SideCell: return info is SideCellMesher.SideInfo;
                    case MesherType.SegmentedSideCell: return info is SegmentedSideCellMesher.SegmentedSideInfo;
                    case MesherType.FullCell: return info is SideCellMesher.SideInfo;
                }

                Debug.LogError("Not handled mesher type!");

                return false;
            }

            static public MesherInfo CreateTopCell(CellMesher.Info info) => new MesherInfo(MesherType.TopCell, info);
            static public MesherInfo CreateScaledTopCell(CellMesher.ScaledInfo info) => new MesherInfo(MesherType.ScaledTopCell, info);
            static public MesherInfo CreateOptimizedTopCell(OptimizedTopCellMesher.OptimizedInfo info) => new MesherInfo(MesherType.OptimizedTopCell, info);
            static public MesherInfo CreateSideCell(SideCellMesher.SideInfo info) => new MesherInfo(MesherType.SideCell, info);
            static public MesherInfo CreateSegmentedSideCell(SegmentedSideCellMesher.SegmentedSideInfo info) => new MesherInfo(MesherType.SegmentedSideCell, info);
            static public MesherInfo CreateFullCell(SideCellMesher.SideInfo info) => new MesherInfo(MesherType.FullCell, info);
        }

        private class MesherHandler
        {
            private MesherInfo MesherInfo;

            public CellMesher.Info Info
            {
                get => MesherInfo.Info;
                set => MesherInfo.Info = value;
            }

            public CellMesher Mesher { get; private set; }

            public bool HasUVs => Mesher.HasUVs;
            public bool DoesGenerateUvs => Info.GenerateUvs;
            public bool HasNormals => Mesher.HasNormals;
            public bool DoesGenerateNormals => Info.GenerateNormals;

            public MesherHandler(MesherInfo info)
            {
                MesherInfo = info;
                Mesher = info.CreateMesher();
            }

            public JobHandle StartGeneration(Data data, float cellSize, JobHandle dependOn = default)
            {
                Init(MesherInfo.Type, Mesher, data, cellSize, MesherInfo.Info);
                return Mesher.Start(dependOn);
            }

            public (int addedVertex, int addedTriangle) CompleteAppendData(NativeList<float3> outVertices, NativeList<int> outTriangles, NativeList<float3> outNormals, NativeList<float2> outUVs)
                => Mesher.CompleteAppendData(outVertices, outTriangles, outNormals, outUVs);

            private static void Init(MesherType type, CellMesher mesher, Data data, float cellSize, CellMesher.Info info)
            {
                switch (type)
                {
                    case MesherType.TopCell:            ToMesher<TopCellMesher>().Init(data, cellSize, info); break;
                    case MesherType.ScaledTopCell:      ToMesher<ScaledTopCellMesher>().Init(data, cellSize, ToInfo<CellMesher.ScaledInfo>()); break;
                    case MesherType.OptimizedTopCell:   ToMesher<OptimizedTopCellMesher>().Init(data, cellSize, ToInfo<OptimizedTopCellMesher.OptimizedInfo>()); break;
                    case MesherType.SideCell:           ToMesher<SideCellMesher>().Init(data, cellSize, ToInfo<SideCellMesher.SideInfo>()); break;
                    case MesherType.SegmentedSideCell:  ToMesher<SegmentedSideCellMesher>().Init(data, cellSize, ToInfo<SegmentedSideCellMesher.SegmentedSideInfo>()); break;
                    case MesherType.FullCell:           ToMesher<FullCellMesher>().Init(data, cellSize, ToInfo<SideCellMesher.SideInfo>()); break;
                    default:
                        Debug.LogError("Not handled mesher type!");
                        break;
                }

                T ToMesher<T>() where T : CellMesher => (T)mesher;
                T ToInfo<T>() where T : CellMesher.Info => (T)info;
            }
        }
    }
}
