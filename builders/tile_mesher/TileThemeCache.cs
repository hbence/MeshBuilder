using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    using Offset = Utils.Offset;
    using MeshDataOffset = MeshCombinationBuilder.MeshDataOffsets;

    using static Utils;

    public sealed partial class TileTheme
    {
        public class MeshCache : System.IDisposable
        {
            private NativeArray<Offset> baseMeshVariants;
            public NativeArray<Offset> BaseMeshVariants { get => baseMeshVariants; }
            private NativeArray<MeshDataOffset> dataOffsets;
            public NativeArray<MeshDataOffset> DataOffsets { get => dataOffsets; }
            public MeshData MeshData { get; private set; }

            public uint MeshDataBufferFlags { get => MeshData.BufferFlags; }

            public MeshCache(BaseMeshVariants[] baseVariants, Allocator allocator)
            {
                var baseMeshVariantList = new List<Offset>();
                var dataOffsetList = new List<MeshDataOffset>();

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
                dataOffsets = new NativeArray<MeshDataOffset>(dataOffsetList.ToArray(), allocator);
                MeshData = new MeshData(vertexCount, trianglesCount, allocator, meshDataFlags);

                int dataIndex = 0;
                for (int i = 0; i < baseVariants.Length; ++i)
                {
                    int variantsCount = baseVariants[i].Variants.Length;
                    for (int variant = 0; variant < variantsCount; ++variant)
                    {
                        MeshDataOffset offset = dataOffsetList[dataIndex];
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

            public MeshDataOffset GetMeshDataOffset(int baseIndex, int variantIndex)
            {
                return GetMeshDataOffset(baseIndex, variantIndex, baseMeshVariants, dataOffsets);
            }

            static private void CopyData(MeshData data, Mesh mesh, MeshDataOffset offset)
            {
                int vStart = offset.vertices.index;
                int vLength = offset.vertices.length;

                Copy3V(mesh.vertices, data.Vertices);
                NativeArray<int>.Copy(mesh.triangles, 0, data.Triangles, offset.triangles.index, offset.triangles.length);

                if (Has(data.HasNormals, mesh.normals))    { Copy3V(mesh.normals, data.Normals); }
                if (Has(data.HasColors, mesh.colors))      { NativeArray<Color>.Copy(mesh.colors, 0, data.Colors, vStart, vLength); }
                if (Has(data.HasTangents, mesh.tangents))  { Copy4V(mesh.tangents, data.Tangents); }
                if (Has(data.HasUVs, mesh.uv))             { Copy2V(mesh.uv, data.UVs);   }
                if (Has(data.HasUVs2, mesh.uv2))           { Copy2V(mesh.uv2, data.UVs2); }
                if (Has(data.HasUVs3, mesh.uv3))           { Copy2V(mesh.uv3, data.UVs3); }
                if (Has(data.HasUVs4, mesh.uv4))           { Copy2V(mesh.uv4, data.UVs4); }

                bool Has<T>(bool hasData, T[] a) { return hasData && a != null && a.Length > 0; }

                void Copy2V(Vector2[] src, NativeArray<float2> dst) { NativeArray<float2>.Copy(ToFloat2Array(src), 0, dst, vStart, vLength); }
                void Copy3V(Vector3[] src, NativeArray<float3> dst) { NativeArray<float3>.Copy(ToFloat3Array(src), 0, dst, vStart, vLength); }
                void Copy4V(Vector4[] src, NativeArray<float4> dst) { NativeArray<float4>.Copy(ToFloat4Array(src), 0, dst, vStart, vLength); }
            }

            static private MeshDataOffset CreateMeshDataOffset(Mesh mesh, int startVertexIndex, int startTriangleIndex)
            {
                return new MeshDataOffset
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

            static private void UpdateMeshInfo(Mesh mesh, ref int verticesCount, ref int trianglesCount, ref uint bufferFlags)
            {
                verticesCount += mesh.vertexCount;
                trianglesCount += mesh.triangles.Length;

                bufferFlags |= MeshData.GetMeshFlags(mesh);
            }

            static public MeshDataOffset GetMeshDataOffset(int baseIndex, int variantIndex, NativeArray<Offset> baseMeshVariants, NativeArray<MeshDataOffset> dataOffsets)
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
