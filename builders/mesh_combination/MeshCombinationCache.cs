using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    using Offset = Utils.Offset;
    using MeshDataOffset = MeshCombinationBuilder.MeshDataOffsets;

    using static Utils;

    public partial class MeshCombinationBuilder
    {
        public class MeshCache : System.IDisposable
        {
            private NativeArray<MeshDataOffset> dataOffsets;
            public NativeArray<MeshDataOffset> DataOffsets { get => dataOffsets; }

            public MeshData MeshData { get; private set; }

            public uint MeshDataBufferFlags { get => MeshData.BufferFlags; }

            public MeshCache(Mesh[] meshes, Allocator allocator)
            {
                var dataOffsetList = new List<MeshDataOffset>();

                int vertexCount = 0;
                int trianglesCount = 0;
                uint meshDataFlags = 0;

                foreach (Mesh mesh in meshes)
                {
                    if (mesh == null)
                    {
                        Debug.LogError("mesh is null!");
                        continue;
                    }
                    /*
                    if (mesh.vertexCount == 0)
                    {
                        Debug.LogError("mesh has zero vertices in:" + mesh.name);
                        continue;
                    }
                    */
                    dataOffsetList.Add(CreateMeshDataOffset(mesh, vertexCount, trianglesCount));

                    UpdateMeshInfo(mesh, ref vertexCount, ref trianglesCount, ref meshDataFlags);
                }

                dataOffsets = new NativeArray<MeshDataOffset>(dataOffsetList.ToArray(), allocator);
                MeshData = new MeshData(vertexCount, trianglesCount, allocator, meshDataFlags);

                for (int i = 0; i < dataOffsets.Length; ++i)
                {
                    MeshDataOffset offset = dataOffsetList[i];
                    if (offset.vertices.length > 0)
                    {
                        CopyData(MeshData, meshes[i], offset);
                    }
                }
            }

            public void Dispose()
            {
                SafeDispose(ref dataOffsets);
                MeshData.Dispose();
            }

            public MeshDataOffset GetMeshDataOffset(int index)
            {
                return dataOffsets[index];
            }

            static private void UpdateMeshInfo(Mesh mesh, ref int verticesCount, ref int trianglesCount, ref uint bufferFlags)
            {
                verticesCount += mesh.vertexCount;
                trianglesCount += mesh.triangles.Length;

                bufferFlags |= MeshData.GetMeshFlags(mesh);
            }

            static public void CreateCombinationData(MeshFilter[] meshes,
                out MeshCache meshCache, Allocator meshCacheAllocator,
                out NativeArray<DataInstance> dataInstances, Allocator instanceArrayAllocator)
            {
                List<Mesh> uniqueMeshes = new List<Mesh>();

                foreach(var meshfilter in meshes)
                {
                    uniqueMeshes.Add(meshfilter.sharedMesh);
                }

                uniqueMeshes.Sort((Mesh a, Mesh b) => { return a.GetHashCode() - b.GetHashCode(); });

                for(int i = uniqueMeshes.Count - 1; i > 0 ; --i)
                {
                    if (uniqueMeshes[i] == uniqueMeshes[i - 1])
                    {
                        uniqueMeshes.RemoveAt(i);
                    }
                }

                meshCache = new MeshCache(uniqueMeshes.ToArray(), meshCacheAllocator);

                dataInstances = new NativeArray<DataInstance>(meshes.Length, instanceArrayAllocator, NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < dataInstances.Length; ++i)
                {
                    // TODO: this index could be cached, and wouldn't need to be searched for
                    int uniqueIndex = uniqueMeshes.FindIndex((Mesh mesh) => (mesh == meshes[i].sharedMesh));
                    dataInstances[i] = new DataInstance()
                    {
                        dataOffsets = meshCache.GetMeshDataOffset(uniqueIndex),
                        transform = meshes[i].transform.localToWorldMatrix
                    };
                }
            }

            static public void CreateCombinationData(Mesh[] meshes, Matrix4x4[] transforms,
                out MeshCache meshCache, Allocator meshCacheAllocator,
                out NativeArray<DataInstance> dataInstances, Allocator instanceArrayAllocator)
            {
                List<Mesh> uniqueMeshes = new List<Mesh>(meshes);

                uniqueMeshes.Sort((Mesh a, Mesh b) => { return a.GetHashCode() - b.GetHashCode(); });

                for (int i = uniqueMeshes.Count - 1; i > 0; --i)
                {
                    if (uniqueMeshes[i] == uniqueMeshes[i - 1])
                    {
                        uniqueMeshes.RemoveAt(i);
                    }
                }

                meshCache = new MeshCache(uniqueMeshes.ToArray(), meshCacheAllocator);

                dataInstances = new NativeArray<DataInstance>(meshes.Length, instanceArrayAllocator, NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < dataInstances.Length; ++i)
                {
                    // TODO: this index could be cached, and wouldn't need to be searched for
                    int uniqueIndex = uniqueMeshes.FindIndex((Mesh mesh) => (mesh == meshes[i]));
                    dataInstances[i] = new DataInstance()
                    {
                        dataOffsets = meshCache.GetMeshDataOffset(uniqueIndex),
                        transform = transforms[i]
                    };
                }
            }
        }
    }
}
