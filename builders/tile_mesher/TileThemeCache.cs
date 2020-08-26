using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    using Offset = Utils.Offset;

    using static Utils;
    using static MeshCombinationBuilder;

    public sealed partial class TileTheme
    {
        public class MeshCache : System.IDisposable
        {
            private NativeArray<Offset> baseMeshVariants;
            public NativeArray<Offset> BaseMeshVariants { get => baseMeshVariants; }
            private NativeArray<MeshDataOffsets> dataOffsets;
            public NativeArray<MeshDataOffsets> DataOffsets { get => dataOffsets; }
            public MeshData MeshData { get; private set; }

            public uint MeshDataBufferFlags { get => MeshData.BufferFlags; }

            public MeshCache(BaseMeshVariants[] baseVariants, Allocator allocator)
            {
                var baseMeshVariantList = new List<Offset>();
                var dataOffsetList = new List<MeshDataOffsets>();

                int vertexCount = 0;
                int trianglesCount = 0;
                uint meshDataFlags = 0;

                int count = 0;
                foreach (var baseVariant in baseVariants)
                {
                    int variantsCount = 0;
                    foreach (Mesh mesh in baseVariant.Variants)
                    {
                        if (mesh == null)
                        {
                            Debug.LogError("mesh is null in base variants");
                            continue;
                        }

                        if (mesh.vertexCount == 0)
                        {
                            Debug.LogError("mesh has zero vertices in base variants:" + mesh.name);
                            continue;
                        }

                        dataOffsetList.Add(CreateMeshDataOffset(mesh, vertexCount, trianglesCount));

                        UpdateMeshInfo(mesh, ref vertexCount, ref trianglesCount, ref meshDataFlags);
                        ++variantsCount;
                    }

                    baseMeshVariantList.Add(new Offset { index = count, length = variantsCount });
                    count += variantsCount;
                }

                baseMeshVariants = new NativeArray<Offset>(baseMeshVariantList.ToArray(), allocator);
                dataOffsets = new NativeArray<MeshDataOffsets>(dataOffsetList.ToArray(), allocator);
                MeshData = new MeshData(vertexCount, trianglesCount, allocator, meshDataFlags);

                int dataIndex = 0;
                for (int i = 0; i < baseVariants.Length; ++i)
                {
                    int variantsCount = baseVariants[i].Variants.Length;
                    for (int variant = 0; variant < variantsCount; ++variant)
                    {
                        MeshDataOffsets offset = dataOffsetList[dataIndex];
                        CopyData(MeshData, baseVariants[i].Variants[variant], offset);
                        ++dataIndex;
                    }
                }
            }

            public void Dispose()
            {
                SafeDispose(ref baseMeshVariants);
                SafeDispose(ref dataOffsets);
                MeshData.Dispose();
            }

            public MeshDataOffsets GetMeshDataOffset(int baseIndex, int variantIndex)
            {
                return GetMeshDataOffset(baseIndex, variantIndex, baseMeshVariants, dataOffsets);
            }

            static private void UpdateMeshInfo(Mesh mesh, ref int verticesCount, ref int trianglesCount, ref uint bufferFlags)
            {
                verticesCount += mesh.vertexCount;
                trianglesCount += mesh.triangles.Length;

                bufferFlags |= MeshData.GetMeshFlags(mesh);
            }

            static public MeshDataOffsets GetMeshDataOffset(int baseIndex, int variantIndex, NativeArray<Offset> baseMeshVariants, NativeArray<MeshDataOffsets> dataOffsets)
            {
                if (baseIndex >= 0 && baseIndex < baseMeshVariants.Length)
                {
                    Offset offset = baseMeshVariants[baseIndex];
                    return dataOffsets[offset.Start + variantIndex % offset.length];
                }

                return default;
            }
        }
    }
}
