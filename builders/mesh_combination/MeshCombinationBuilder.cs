using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MeshBuilder
{
    using BufferType = MeshData.Buffer;
    using Offset = Utils.Offset;
    using static Utils;

    public partial class MeshCombinationBuilder : Builder
    {
        private enum SourceType
        {
            FromTheme, // themes have mesh caches so that can be used
            FromMeshes // for other meshes we need to generate a cache
        }

        private SourceType sourceType;

        private bool deferred = false;
        private MeshData SourceMeshData { get; set; }

        private MeshFilter[] meshFilters;

        private Immediate immediateHandler;
        private Deferred deferredHandler;

        public void Init(NativeArray<DataInstance> instanceArray, TileTheme theme)
        {
            sourceType = SourceType.FromTheme;

            deferred = false;
            immediateHandler = new Immediate(instanceArray);
            deferredHandler = null;

            SourceMeshData = theme.TileThemeCache.MeshData;

            Inited();
        }

        public void InitDeferred(NativeList<DataInstance> instanceList, TileTheme theme)
        {
            sourceType = SourceType.FromTheme;

            deferred = true;
            deferredHandler = new Deferred(instanceList);
            immediateHandler = null;

            SourceMeshData = theme.TileThemeCache.MeshData;

            Inited();
        }

        public void Init(MeshFilter[] meshFilters)
        {
            sourceType = SourceType.FromMeshes;

            this.meshFilters = meshFilters;

            deferred = false;
            deferredHandler = null;
            immediateHandler = null;

            SourceMeshData = default;

            Inited();
        }

        protected override JobHandle StartGeneration(JobHandle dependOn)
        {
            if (sourceType == SourceType.FromTheme)
            {
                return deferred ? deferredHandler.ScheduleDeferredJobs(SourceMeshData, Temps, dependOn) :
                            immediateHandler.ScheduleImmediateJobs(SourceMeshData, Temps, dependOn);
            }
            else if (sourceType == SourceType.FromMeshes)
            {
                MeshCache meshCache;
                NativeArray<DataInstance> dataInstances;
                MeshCache.CreateCombinationData(meshFilters, out meshCache, Allocator.TempJob, out dataInstances, Allocator.TempJob);

                Temps.Add(meshCache);
                Temps.Add(dataInstances);

                immediateHandler = new Immediate(dataInstances);
                return immediateHandler.ScheduleImmediateJobs(meshCache.MeshData, Temps, dependOn);
            }

            return dependOn;
        }

        private class Immediate
        {
            public NativeArray<DataInstance> instanceArray;
            public MeshData combinedMesh;

            public Immediate(NativeArray<DataInstance> instArray)
            {
                instanceArray = instArray;
            }

            public void UpdateMesh(Mesh mesh)
            {
                combinedMesh.UpdateMesh(mesh, MeshData.UpdateMode.Clear);
            }

            public JobHandle ScheduleImmediateJobs(MeshData sourceMeshData, List<System.IDisposable> tempList, JobHandle dependOn)
            {
                var combinedInfo = CalcCombinedMeshInfo(instanceArray, sourceMeshData.BufferFlags);
                combinedMesh = combinedInfo.CreateMeshData(Allocator.TempJob);
                tempList.Add(combinedMesh);

                var submeshOffsets = new NativeArray<Offset>(combinedInfo.submeshTriangleOffsets, Allocator.TempJob);
                tempList.Add(submeshOffsets);

                JobHandle combined = default;

                if (combinedMesh.HasVertices)
                {
                    var h = CombineTransformedBufferJob.Schedule(BufferType.Vertex, sourceMeshData.Vertices, combinedMesh.Vertices, instanceArray, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (combinedMesh.HasTriangles)
                {
                    var h = CombineTriangleBufferJob.Schedule(sourceMeshData.Triangles, combinedMesh.Triangles, submeshOffsets, instanceArray, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (combinedMesh.HasNormals)
                {
                    var h = CombineTransformedBufferJob.Schedule(BufferType.Normal, sourceMeshData.Normals, combinedMesh.Normals, instanceArray, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (combinedMesh.HasColors)
                {
                    var h = CombineBufferJob<Color>.Schedule(sourceMeshData.Colors, combinedMesh.Colors, instanceArray, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (combinedMesh.HasTangents)
                {
                    var h = CombineBufferJob<float4>.Schedule(sourceMeshData.Tangents, combinedMesh.Tangents, instanceArray, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (combinedMesh.HasUVs)
                {
                    var h = CombineBufferJob<float2>.Schedule(sourceMeshData.UVs, combinedMesh.UVs, instanceArray, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (combinedMesh.HasUVs2)
                {
                    var h = CombineBufferJob<float2>.Schedule(sourceMeshData.UVs2, combinedMesh.UVs2, instanceArray, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (combinedMesh.HasUVs3)
                {
                    var h = CombineBufferJob<float2>.Schedule(sourceMeshData.UVs3, combinedMesh.UVs3, instanceArray, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (combinedMesh.HasUVs4)
                {
                    var h = CombineBufferJob<float2>.Schedule(sourceMeshData.UVs4, combinedMesh.UVs4, instanceArray, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                return combined;
            }
        }

        private class Deferred
        {
            public NativeList<DataInstance> instanceList;
            public DeferredMeshData deferredCombinedMesh;

            public Deferred(NativeList<DataInstance> instances)
            {
                instanceList = instances;
            }

            public void UpdateMesh(Mesh mesh, ResultMeshInfo meshInfo)
            {
                deferredCombinedMesh.UpdateMesh(mesh, meshInfo);
            }

            public JobHandle ScheduleDeferredJobs(MeshData sourceMeshData, List<System.IDisposable> tempList, JobHandle dependOn)
            {
                uint meshFlags = sourceMeshData.BufferFlags;
                deferredCombinedMesh = new DeferredMeshData(meshFlags, Allocator.TempJob);
                tempList.Add(deferredCombinedMesh);

                JobHandle combined = default;

                if (deferredCombinedMesh.HasVertices)
                {
                    var h = CombineTransformedBufferJob.ScheduleDeferred(BufferType.Vertex, sourceMeshData.Vertices, deferredCombinedMesh.Vertices, instanceList, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (deferredCombinedMesh.HasTriangles)
                {
                    var h = CombineTriangleBufferJob.ScheduleDeferred(sourceMeshData.Triangles, deferredCombinedMesh.Triangles, meshFlags, instanceList, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (deferredCombinedMesh.HasNormals)
                {
                    var h = CombineTransformedBufferJob.ScheduleDeferred(BufferType.Normal, sourceMeshData.Normals, deferredCombinedMesh.Normals, instanceList, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (deferredCombinedMesh.HasColors)
                {
                    var h = CombineBufferJob<Color>.ScheduleDeferred(sourceMeshData.Colors, deferredCombinedMesh.Colors, instanceList, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (deferredCombinedMesh.HasTangents)
                {
                    var h = CombineBufferJob<float4>.ScheduleDeferred(sourceMeshData.Tangents, deferredCombinedMesh.Tangents, instanceList, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (deferredCombinedMesh.HasUVs)
                {
                    var h = CombineBufferJob<float2>.ScheduleDeferred(sourceMeshData.UVs, deferredCombinedMesh.UVs, instanceList, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (deferredCombinedMesh.HasUVs2)
                {
                    var h = CombineBufferJob<float2>.ScheduleDeferred(sourceMeshData.UVs2, deferredCombinedMesh.UVs2, instanceList, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (deferredCombinedMesh.HasUVs3)
                {
                    var h = CombineBufferJob<float2>.ScheduleDeferred(sourceMeshData.UVs3, deferredCombinedMesh.UVs3, instanceList, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                if (deferredCombinedMesh.HasUVs4)
                {
                    var h = CombineBufferJob<float2>.ScheduleDeferred(sourceMeshData.UVs4, deferredCombinedMesh.UVs4, instanceList, dependOn);
                    combined = JobHandle.CombineDependencies(combined, h);
                }

                return combined;
            }
        }

        protected override void EndGeneration(Mesh mesh)
        {
            if (deferred)
            {
                var meshInfo = CalcCombinedMeshInfo(deferredHandler.instanceList, SourceMeshData.BufferFlags);
                deferredHandler.UpdateMesh(mesh, meshInfo);
            }
            else
            {
                immediateHandler.UpdateMesh(mesh);
            }
        }

        static private int CountCombinedVertexCount(NativeArray<DataInstance> instanceArray)
        {
            int count = 0;
            foreach (var data in instanceArray) { count += data.dataOffsets.vertices.length; }
            return count;
        }

        static private int CountCombinedIndexCount(NativeArray<DataInstance> instanceArray)
        {
            int count = 0;
            foreach (var data in instanceArray) { count += data.dataOffsets.triangles.length; }
            return count;
        }

        static private ResultMeshInfo CalcCombinedMeshInfo(NativeArray<DataInstance> instanceArray, uint meshDataFlags)
        {
            int vertexCount = 0;
            int triangleLength = 0;

            var submeshLengths = new List<int>();

            for (int i = 0; i < instanceArray.Length; ++i)
            {
                var inst = instanceArray[i];

                vertexCount += inst.dataOffsets.vertices.length;
                triangleLength += inst.dataOffsets.triangles.length;

                int submeshCount = inst.dataOffsets.SubmeshCount;
                for (int submesh = 0; submesh < submeshCount; ++submesh)
                {
                    if (submesh >= submeshLengths.Count)
                    {
                        submeshLengths.Add(0);
                    }
                    submeshLengths[submesh] += inst.dataOffsets.SubmeshLength(submesh);
                }
            }

            var submeshTriangleOffsets = new Offset[submeshLengths.Count];
            int triIndex = 0;
            for (int i = 0; i < submeshLengths.Count; ++i)
            {
                submeshTriangleOffsets[i] = new Offset { index = triIndex, length = submeshLengths[i] };
                triIndex += submeshLengths[i];
            }

            return new ResultMeshInfo(meshDataFlags, vertexCount, triangleLength, submeshTriangleOffsets);
        }
        
        // TODO: float4x4 could be replaced to float3x4
        public struct DataInstance
        {
            public MeshDataOffsets dataOffsets;
            public float4x4 transform;
        }

        // not pretty, but this needs to be a blittable type
        // TODO: I could use an unsafe fixed array, not sure if that would be nicer, I should test that later
        public struct MeshDataOffsets
        {
            public Offset vertices; 
            public Offset triangles;

            // submesh 0 starts at triangles.index
            public int submeshOffset1;
            public int submeshOffset2;
            public int submeshOffset3;
            public int submeshOffset4;
            public int submeshOffset5;
            public int submeshOffset6;
            public int submeshOffset7;

            public int SubmeshCount
            {
                get
                {
                    if (submeshOffset1 == 0) return 1;
                    if (submeshOffset2 == 0) return 2;
                    if (submeshOffset3 == 0) return 3;
                    if (submeshOffset4 == 0) return 4;
                    if (submeshOffset5 == 0) return 5;
                    if (submeshOffset6 == 0) return 6;
                    if (submeshOffset7 == 0) return 7;

                    return 8;
                }
            }

            public int SubmeshStart(int index)
            {
                return triangles.Start + SubmeshOffset(index);
            }

            public int SubmeshEnd(int index)
            {
                int nextOffset = SubmeshOffset(index + 1);
                return triangles.Start + (nextOffset > 0 ? nextOffset : triangles.length);
            }

            public int SubmeshLength(int index)
            {
                int offset = SubmeshOffset(index);
                int next = SubmeshOffset(index + 1);
                return next > 0 ? next - offset : triangles.length - offset; 
            }

            public int SubmeshOffset(int index)
            {
                switch (index)
                {
                    case 0: return 0;
                    case 1: return submeshOffset1;
                    case 2: return submeshOffset2;
                    case 3: return submeshOffset3;
                    case 4: return submeshOffset4;
                    case 5: return submeshOffset5;
                    case 6: return submeshOffset6;
                    case 7: return submeshOffset7;
                }
                return 0;
            }
        }

        static public void CopyData(MeshData data, Mesh mesh, MeshDataOffsets offset)
        {
            int vStart = offset.vertices.index;
            int vLength = offset.vertices.length;

            Copy3V(mesh.vertices, data.Vertices);
            NativeArray<int>.Copy(mesh.triangles, 0, data.Triangles, offset.triangles.index, offset.triangles.length);

            if (Has(data.HasNormals, mesh.normals)) { Copy3V(mesh.normals, data.Normals); }
            if (Has(data.HasColors, mesh.colors)) { NativeArray<Color>.Copy(mesh.colors, 0, data.Colors, vStart, vLength); }
            if (Has(data.HasTangents, mesh.tangents)) { Copy4V(mesh.tangents, data.Tangents); }
            if (Has(data.HasUVs, mesh.uv)) { Copy2V(mesh.uv, data.UVs); }
            if (Has(data.HasUVs2, mesh.uv2)) { Copy2V(mesh.uv2, data.UVs2); }
            if (Has(data.HasUVs3, mesh.uv3)) { Copy2V(mesh.uv3, data.UVs3); }
            if (Has(data.HasUVs4, mesh.uv4)) { Copy2V(mesh.uv4, data.UVs4); }

            bool Has<T>(bool hasData, T[] a) { return hasData && a != null && a.Length > 0; }

            void Copy2V(Vector2[] src, NativeArray<float2> dst) { NativeArray<float2>.Copy(ToFloat2Array(src), 0, dst, vStart, vLength); }
            void Copy3V(Vector3[] src, NativeArray<float3> dst) { NativeArray<float3>.Copy(ToFloat3Array(src), 0, dst, vStart, vLength); }
            void Copy4V(Vector4[] src, NativeArray<float4> dst) { NativeArray<float4>.Copy(ToFloat4Array(src), 0, dst, vStart, vLength); }
        }

        static public MeshDataOffsets CreateMeshDataOffset(Mesh mesh, int startVertexIndex, int startTriangleIndex)
        {
            return new MeshDataOffsets
            {
                vertices = new Offset { index = startVertexIndex, length = mesh.vertexCount },
                triangles = new Offset { index = startTriangleIndex, length = mesh.triangles.Length },
                submeshOffset1 = (mesh.subMeshCount > 1) ? (int)mesh.GetIndexStart(1) : 0,
                submeshOffset2 = (mesh.subMeshCount > 2) ? (int)mesh.GetIndexStart(2) : 0,
                submeshOffset3 = (mesh.subMeshCount > 3) ? (int)mesh.GetIndexStart(3) : 0,
                submeshOffset4 = (mesh.subMeshCount > 4) ? (int)mesh.GetIndexStart(4) : 0,
                submeshOffset5 = (mesh.subMeshCount > 5) ? (int)mesh.GetIndexStart(5) : 0,
                submeshOffset6 = (mesh.subMeshCount > 6) ? (int)mesh.GetIndexStart(6) : 0,
                submeshOffset7 = (mesh.subMeshCount > 7) ? (int)mesh.GetIndexStart(7) : 0,
            };
        }

        public struct CombineTransformedBufferJob : IJob
        {
            public BufferType bufferType;
            [ReadOnly] public NativeArray<DataInstance> instances;

            [ReadOnly] public NativeArray<float3> source;
            [WriteOnly] public NativeArray<float3> destination;

            public void Execute()
            {
                switch (bufferType)
                {
                    case BufferType.Vertex: CombineTransformed(source, destination, 1, instances); break;
                    case BufferType.Normal: CombineTransformed(source, destination, 0, instances); break;
                    default: Debug.LogError("not handled:" + bufferType); break;
                }
            }

            static private void CombineTransformed(NativeArray<float3> source, NativeArray<float3> destination, float w, NativeArray<DataInstance> instances)
            {
                int nextVertex = 0;
                float4 vertex = new float4(0, 0, 0, w);
                foreach (var instance in instances)
                {
                    var vOffsets = instance.dataOffsets.vertices;
                    for (int i = vOffsets.Start; i < vOffsets.End; ++i)
                    {
                        vertex.xyz = source[i];
                        destination[nextVertex] = math.mul(instance.transform, vertex).xyz;
                        ++nextVertex;
                    }
                }
            }

            static public JobHandle Schedule(BufferType type, NativeArray<float3> source, NativeArray<float3> destination, NativeArray<DataInstance> instanceArray, JobHandle dependOn)
            {
                var job = new CombineTransformedBufferJob
                {
                    bufferType = type,
                    instances = instanceArray,
                    source = source,
                    destination = destination
                };
                return job.Schedule(dependOn);
            }

            static public JobHandle ScheduleDeferred(BufferType type, NativeArray<float3> source, NativeList<float3> destination, NativeList<DataInstance> instances, JobHandle dependOn)
            {
                var job = new Deferred
                {
                    bufferType = type,
                    instances = instances.AsDeferredJobArray(),
                    source = source,
                    destination = destination
                };
                return job.Schedule(dependOn);
            }

            public struct Deferred : IJob
            {
                public BufferType bufferType;
                [ReadOnly] public NativeArray<DataInstance> instances;

                [ReadOnly] public NativeArray<float3> source;
                public NativeList<float3> destination;

                public void Execute()
                {
                    int vertexCount = CountCombinedVertexCount(instances);
                    destination.ResizeUninitialized(vertexCount);
                    switch (bufferType)
                    {
                        case BufferType.Vertex: CombineTransformed(source, destination, 1, instances); break;
                        case BufferType.Normal: CombineTransformed(source, destination, 0, instances); break;
                        default: Debug.LogError("not handled:" + bufferType); break;
                    }
                }
            }
        }

        public struct CombineTriangleBufferJob : IJob
        {
            [ReadOnly] public NativeArray<DataInstance> instances;
            [ReadOnly] public NativeArray<Offset> submeshOffsets;

            [ReadOnly] public NativeArray<int> source;
            [WriteOnly] public NativeArray<int> destination;

            public void Execute()
            {
                CombineTriangles(source, destination, instances, submeshOffsets);
            }

            static private void CombineTriangles<T>(NativeArray<int> source, NativeArray<int> destination, NativeArray<DataInstance> instances, T submeshOffsets) where T : IEnumerable<Offset>
            {
                var destSubmeshStart = new List<int>();
                foreach(var elem in submeshOffsets)
                {
                    destSubmeshStart.Add(elem.Start);
                }

                int offset = 0;
                foreach (var instance in instances)
                {
                    var triOffset = instance.dataOffsets.triangles;

                    int submeshCount = instance.dataOffsets.SubmeshCount;
                    for (int submesh = 0; submesh < submeshCount; ++submesh)
                    {
                        int srcOffset = instance.dataOffsets.SubmeshStart(submesh);
                        int srcLength = instance.dataOffsets.SubmeshLength(submesh);
                        int destOffset = destSubmeshStart[submesh];
                        // some pieces can be mirrored, in those cases the triangles will have to be flipped 
                        // or they will show backface and will be culled
                        // TODO: this check seems to work with the limited amount of transformation the tile mesher does
                        // but I should probably figure out a general version
                        float xSign = math.sign(instance.transform.c0.x);
                        float zSign = math.sign(instance.transform.c2.z);
                        bool forward = (instance.transform.c1.y > 0) ? xSign == zSign : xSign != zSign;
                        if (forward)
                        {
                            for (int i = 0; i < srcLength; ++i)
                            {
                                destination[destOffset + i] = source[srcOffset + i] + offset;
                            }
                        }
                        else
                        {
                            for (int i = 0; i < srcLength; ++i)
                            {
                                destination[destOffset + i] = source[srcOffset + srcLength - 1 - i] + offset;
                            }
                        }

                        destSubmeshStart[submesh] += srcLength;
                    }

                    offset += instance.dataOffsets.vertices.length;
                }
            }

            static public JobHandle Schedule(NativeArray<int> source, NativeArray<int> destination, NativeArray<Offset> submeshOffsets, NativeArray<DataInstance> instanceArray, JobHandle dependOn)
            {
                var job = new CombineTriangleBufferJob
                {
                    instances = instanceArray,
                    submeshOffsets = submeshOffsets,
                    source = source,
                    destination = destination
                };
                return job.Schedule(dependOn);
            }

            static public JobHandle ScheduleDeferred(NativeArray<int> source, NativeList<int> destination, uint meshflags, NativeList<DataInstance> instances, JobHandle dependOn)
            {
                var job = new Deferred
                {
                    meshFlags = meshflags,
                    instances = instances.AsDeferredJobArray(),
                    source = source,
                    destination = destination
                };
                return job.Schedule(dependOn);
            }

            public struct Deferred : IJob
            {
                public uint meshFlags;
                [ReadOnly] public NativeArray<DataInstance> instances;

                [ReadOnly] public NativeArray<int> source;
                public NativeList<int> destination;

                public void Execute()
                {
                    var meshInfo = CalcCombinedMeshInfo(instances, meshFlags);
                    destination.ResizeUninitialized(meshInfo.triangleLength);
                    CombineTriangles(source, destination, instances, meshInfo.submeshTriangleOffsets);
                }
            }
        }

        public struct CombineBufferJob<T> : IJob where T : struct 
        {
            [ReadOnly] public NativeArray<DataInstance> instances;

            [ReadOnly] public NativeArray<T> source;
            [WriteOnly] public NativeArray<T> destination;

            public void Execute()
            {
                Combine(source, destination, instances);
            }

            static private void Combine(NativeArray<T> source, NativeArray<T> destination, NativeArray<DataInstance> instances)
            {
                int destIndex = 0;
                foreach (var instance in instances)
                {
                    var vOffset = instance.dataOffsets.vertices;
                    NativeArray<T>.Copy(source, vOffset.index, destination, destIndex, vOffset.length);
                    destIndex += vOffset.length;
                }
            }

            static public JobHandle Schedule(NativeArray<T> source, NativeArray<T> destination, NativeArray<DataInstance> instanceArray, JobHandle dependOn)
            {
                var job = new CombineBufferJob<T>
                {
                    instances = instanceArray,
                    source = source,
                    destination = destination
                };
                return job.Schedule(dependOn);
            }

            static public JobHandle ScheduleDeferred(NativeArray<T> source, NativeList<T> destination, NativeList<DataInstance> instances, JobHandle dependOn)
            {
                var job = new Deferred
                {
                    instances = instances.AsDeferredJobArray(),
                    source = source,
                    destination = destination
                };
                return job.Schedule(dependOn);
            }

            private struct Deferred : IJob
            {
                [ReadOnly] public NativeArray<DataInstance> instances;

                [ReadOnly] public NativeArray<T> source;
                public NativeList<T> destination;

                public void Execute()
                {
                    int count = CountCombinedVertexCount(instances);
                    destination.ResizeUninitialized(count);
                    Combine(source, destination, instances);
                }
            }
        }

        private struct ResultMeshInfo
        {
            public uint meshDataFlags;
            public int vertexCount;
            public int triangleLength;
            public Offset[] submeshTriangleOffsets;

            public ResultMeshInfo(uint flags, int vertsCount, int triCount, Offset[] submeshOffsets)
            { meshDataFlags = flags; vertexCount = vertsCount; triangleLength = triCount; submeshTriangleOffsets = submeshOffsets; }

            public int SubmeshCount { get => submeshTriangleOffsets != null ? submeshTriangleOffsets.Length : 0; }

            private bool IsSubmeshInBounds(int submesh) { return submesh >= 0 && submesh < SubmeshCount; }

            public int GetSubmeshTriangleLength(int submesh)
            {
                return (IsSubmeshInBounds(submesh)) ? submeshTriangleOffsets[submesh].length : 0;
            }

            public bool CopySubmeshTriangles(int submesh, NativeArray<int> srcBuffer, int[] dstBuffer)
            {
                if (IsSubmeshInBounds(submesh))
                {
                    int offset = submeshTriangleOffsets[submesh].index;
                    int length = submeshTriangleOffsets[submesh].length;
                    if (dstBuffer.Length >= length)
                    {
                        NativeArray<int>.Copy(srcBuffer, offset, dstBuffer, 0, length);
                    }
                    else
                    {
                        Debug.LogError("buffer is too small for copy");
                    }
                }
                else
                {
                    Debug.LogError("submesh index is out of bounds");
                }

                return false;
            }

            public MeshData CreateMeshData(Allocator allocator) { return new MeshData(vertexCount, triangleLength, submeshTriangleOffsets, allocator, meshDataFlags); }
        }

        private struct DeferredMeshData : System.IDisposable
        {
            private NativeList<float3> vertices;
            public NativeList<float3> Vertices { get => vertices; }
            public bool HasVertices { get => Has(vertices); }

            private NativeList<int> triangles;
            public NativeList<int> Triangles { get => triangles; }
            public bool HasTriangles { get => Has(triangles); }

            private NativeList<float3> normals;
            public NativeList<float3> Normals { get => normals; }
            public bool HasNormals { get => Has(normals); }

            private NativeList<Color> colors;
            public NativeList<Color> Colors { get => colors; }
            public bool HasColors { get => Has(colors); }

            private NativeList<float4> tangents;
            public NativeList<float4> Tangents { get => tangents; }
            public bool HasTangents { get => Has(tangents); }

            private NativeList<float2> uvs;
            public NativeList<float2> UVs { get => uvs; }
            public bool HasUVs { get => Has(uvs); }

            private NativeList<float2> uvs2;
            public NativeList<float2> UVs2 { get => uvs2; }
            public bool HasUVs2 { get => Has(uvs2); }

            private NativeList<float2> uvs3;
            public NativeList<float2> UVs3 { get => uvs3; }
            public bool HasUVs3 { get => Has(uvs3); }

            private NativeList<float2> uvs4;
            public NativeList<float2> UVs4 { get => uvs4; }
            public bool HasUVs4 { get => Has(uvs4); }

            public DeferredMeshData(uint meshDataBufferFlags, Allocator allocator)
            {
                vertices = Initialize<float3>(meshDataBufferFlags, BufferType.Vertex, allocator);
                triangles = Initialize<int>(meshDataBufferFlags, BufferType.Triangle, allocator);
                normals = Initialize<float3>(meshDataBufferFlags, BufferType.Normal, allocator);
                colors = Initialize<Color>(meshDataBufferFlags, BufferType.Color, allocator);
                tangents = Initialize<float4>(meshDataBufferFlags, BufferType.Tangent, allocator);
                uvs = Initialize<float2>(meshDataBufferFlags, BufferType.UV, allocator);
                uvs2 = Initialize<float2>(meshDataBufferFlags, BufferType.UV2, allocator);
                uvs3 = Initialize<float2>(meshDataBufferFlags, BufferType.UV3, allocator);
                uvs4 = Initialize<float2>(meshDataBufferFlags, BufferType.UV4, allocator);
            }

            static private NativeList<T> Initialize<T>(uint meshDataBufferFlags, BufferType type, Allocator allocator) where T : struct
            {
                return MeshData.HasFlag(meshDataBufferFlags, type) ? new NativeList<T>(allocator) : default;
            }

            public void Dispose()
            {
                SafeDispose(ref vertices);
                SafeDispose(ref triangles);
                SafeDispose(ref normals);
                SafeDispose(ref colors);
                SafeDispose(ref tangents);
                SafeDispose(ref uvs);
                SafeDispose(ref uvs2);
                SafeDispose(ref uvs3);
                SafeDispose(ref uvs4);
            }

            private bool Has<T>(NativeList<T> list) where T : struct { return list.IsCreated; }

            public void UpdateMesh(Mesh mesh, ResultMeshInfo meshInfo)
            {
                mesh.Clear();

                if (HasVertices)
                {
                    mesh.vertices = ToVector3Array(vertices);
                }

                if (HasTriangles)
                {
                    if (vertices.Length > short.MaxValue)
                    {
                        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    }

                    var offsets = meshInfo.submeshTriangleOffsets;
                    mesh.subMeshCount = meshInfo.SubmeshCount;
                    for (int submesh = 0; submesh < offsets.Length; ++submesh)
                    {
                        int triLength = meshInfo.GetSubmeshTriangleLength(submesh);
                        int[] buffer = new int[triLength];
                        meshInfo.CopySubmeshTriangles(submesh, triangles, buffer);
                        mesh.SetTriangles(buffer, submesh);
                    }
                }

                if (HasNormals) mesh.normals = ToVector3Array(normals);
                if (HasColors) mesh.colors = colors.ToArray();
                if (HasTangents) mesh.tangents = ToVector4Array(tangents);
                if (HasUVs) mesh.uv = ToVector2Array(uvs);
                if (HasUVs2) mesh.uv2 = ToVector2Array(uvs2);
                if (HasUVs3) mesh.uv3 = ToVector2Array(uvs3);
                if (HasUVs4) mesh.uv4 = ToVector2Array(uvs4);

                if (Has(vertices))
                {
                    mesh.RecalculateBounds();
                    if (!Has(normals)) { mesh.RecalculateNormals(); }
                }
            }
        }
    }
}
