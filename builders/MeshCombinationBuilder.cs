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

    public class MeshCombinationBuilder : Builder
    {
        private uint meshDataFlags = 0;
        private int vertexCount = 0;
        private int triangleLength = 0;
        private Offset[] submeshTriangleOffsets;

        private TileTheme theme;
        private NativeArray<DataInstance> instanceArray;

        private MeshData combinedMesh;

        public void Init(NativeArray<DataInstance> instanceArray, TileTheme theme)
        {
            this.instanceArray = instanceArray;

            this.theme?.Release();
            this.theme = theme;
            this.theme.Retain();

            combinedMesh.Dispose();
            combinedMesh = default;

            InitCombinedMeshInfo(instanceArray, theme);

            Inited();
        }

        private void InitCombinedMeshInfo(NativeArray<DataInstance> instanceArray, TileTheme theme)
        {
            meshDataFlags = theme.TileThemeCache.MeshDataBufferFlags;
            vertexCount = 0;
            triangleLength = 0;

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

            submeshTriangleOffsets = new Offset[submeshLengths.Count];
            int triIndex = 0;
            for (int i = 0; i < submeshLengths.Count; ++i)
            {
                submeshTriangleOffsets[i] = new Offset { index = triIndex, length = submeshLengths[i] };
                triIndex += submeshLengths[i];
            }
        }

        protected override JobHandle StartGeneration(JobHandle dependOn)
        {
            JobHandle combined = default;
            
            combinedMesh = new MeshData(vertexCount, triangleLength, submeshTriangleOffsets, Allocator.TempJob, meshDataFlags);
            AddTemp(combinedMesh);

            var submeshOffsets = new NativeArray<Offset>(submeshTriangleOffsets, Allocator.TempJob);
            AddTemp(submeshOffsets);

            JobHandle bufferFinished = default;

            MeshData source = theme.TileThemeCache.MeshData;

            if (combinedMesh.HasVertices)
            {
                bufferFinished = ScheduleTransformedCombination(BufferType.Vertex, source.Vertices, combinedMesh.Vertices, dependOn);
                combined = JobHandle.CombineDependencies(combined, bufferFinished);
            }

            if (combinedMesh.HasTriangles)
            {
                bufferFinished = ScheduleTriangleCombination(source.Triangles, combinedMesh.Triangles, submeshOffsets, dependOn);
                combined = JobHandle.CombineDependencies(combined, bufferFinished);
            }
            
            if (combinedMesh.HasNormals)
            {
                bufferFinished = ScheduleTransformedCombination(BufferType.Normal, source.Normals, combinedMesh.Normals, dependOn);
                combined = JobHandle.CombineDependencies(combined, bufferFinished);
            }

            if (combinedMesh.HasColors)
            {
                bufferFinished = ScheduleCombination(source.Colors, combinedMesh.Colors, dependOn);
                combined = JobHandle.CombineDependencies(combined, bufferFinished);
            }

            if (combinedMesh.HasTangents)
            {
                bufferFinished = ScheduleCombination(source.Tangents, combinedMesh.Tangents, dependOn);
                combined = JobHandle.CombineDependencies(combined, bufferFinished);
            }

            if (combinedMesh.HasUVs)
            {
                bufferFinished = ScheduleCombination(source.UVs, combinedMesh.UVs, dependOn);
                combined = JobHandle.CombineDependencies(combined, bufferFinished);
            }

            if (combinedMesh.HasUVs2)
            {
                bufferFinished = ScheduleCombination(source.UVs2, combinedMesh.UVs2, dependOn);
                combined = JobHandle.CombineDependencies(combined, bufferFinished);
            }

            if (combinedMesh.HasUVs3)
            {
                bufferFinished = ScheduleCombination(source.UVs3, combinedMesh.UVs3, dependOn);
                combined = JobHandle.CombineDependencies(combined, bufferFinished);
            }

            if (combinedMesh.HasUVs4)
            {
                bufferFinished = ScheduleCombination(source.UVs4, combinedMesh.UVs4, dependOn);
                combined = JobHandle.CombineDependencies(combined, bufferFinished);
            }
            
            return combined;
        }

        protected override void EndGeneration(Mesh mesh)
        {
            combinedMesh.UpdateMesh(mesh, MeshData.UpdateMode.Clear);
        }

        private JobHandle ScheduleTransformedCombination(BufferType type, NativeArray<float3> source, NativeArray<float3> destination, JobHandle dependOn)
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

        private JobHandle ScheduleTriangleCombination(NativeArray<int> source, NativeArray<int> destination, NativeArray<Offset> submeshOffsets, JobHandle dependOn)
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

        private JobHandle ScheduleCombination<T>(NativeArray<T> source, NativeArray<T> destination, JobHandle dependOn) where T : struct
        {
            var job = new CombineBufferJob<T>
            {
                instances = instanceArray,
                source = source,
                destination = destination
            };

            return job.Schedule(dependOn);
        }

        public struct DataInstance
        {
            public MeshDataOffsets dataOffsets;
            public float4x4 transform;
        }

        // not pretty, but this needs to be a blittable type
        // TODO: I could use an unsafe fixed array, I should test that later
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
                    case BufferType.Vertex: CombineTransformed(source, destination, 1); break;
                    case BufferType.Normal: CombineTransformed(source, destination, 0); break;
                    default:
                        Debug.LogError("not handled:" + bufferType);
                        break;
                }
            }

            private void CombineTransformed(NativeArray<float3> source, NativeArray<float3> destination, float w)
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
        }

        public struct CombineTriangleBufferJob : IJob
        {
            [ReadOnly] public NativeArray<DataInstance> instances;
            [ReadOnly] public NativeArray<Offset> submeshOffsets;

            [ReadOnly] public NativeArray<int> source;
            [WriteOnly] public NativeArray<int> destination;

            public void Execute()
            {
                CombineTriangles(source, destination);
            }

            private void CombineTriangles(NativeArray<int> source, NativeArray<int> destination)
            {
                var destSubmeshStart = new int[submeshOffsets.Length];
                for (int i = 0; i < destSubmeshStart.Length; ++i)
                {
                    destSubmeshStart[i] = submeshOffsets[i].Start;
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

        }

        public struct CombineBufferJob<T> : IJob where T : struct 
        {
            [ReadOnly] public NativeArray<DataInstance> instances;

            [ReadOnly] public NativeArray<T> source;
            [WriteOnly] public NativeArray<T> destination;

            public void Execute()
            {
                Combine(source, destination);
            }

            private void Combine(NativeArray<T> source, NativeArray<T> destination)
            {
                int destIndex = 0;
                foreach (var instance in instances)
                {
                    var vOffset = instance.dataOffsets.vertices;
                    NativeArray<T>.Copy(source, vOffset.index, destination, destIndex, vOffset.length);
                    destIndex += vOffset.length;
                }
            }
        }
    }
}
