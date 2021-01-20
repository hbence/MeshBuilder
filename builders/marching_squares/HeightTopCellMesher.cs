using Unity.Collections;
using Unity.Mathematics;

using static MeshBuilder.MarchingSquaresMesher.SimpleTopCellMesher;

namespace MeshBuilder
{
    public partial class MarchingSquaresMesher : Builder
    {
        /// <summary>
        /// Generates an XZ aligned flat mesh.
        /// </summary>
        public struct HeightTopCellMesher : ICellMesher<CornerInfo>
        {
            public float lerpToExactEdge;
            public float heightOffset;

            public CornerInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                            ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
                => GenerateInfoSimple(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, hasCellTriangles);

            public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, float height, NativeArray<float3> vertices)
                => CalculateVerticesSimple(x, y, cellSize, info, height, vertices, heightOffset, lerpToExactEdge);

            public void CalculateIndices(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
             => CalculateIndicesNormal(bl, br, tr, tl, triangles);

            public bool NeedUpdateInfo { get => false; }

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref CornerInfo cell, ref CornerInfo top, ref CornerInfo right)
            {
            }

            public bool CanGenerateUvs { get => true; }

            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, CornerInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
                => TopCalculateUvs(x, y, cellColNum, cellRowNum, cellSize, corner, uvScale, vertices, uvs);

            public bool CanGenerateNormals { get => false; }

            public void CalculateNormals(CornerInfo cur, CornerInfo right, CornerInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
            {
            }
        }
    }
}
