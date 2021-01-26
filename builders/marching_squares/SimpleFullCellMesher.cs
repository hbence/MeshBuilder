﻿using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    using SideInfo = MarchingSquaresMesher.SimpleSideMesher.CornerInfo;

    public partial class MarchingSquaresMesher : Builder
    {
        /// <summary>
        /// Generates a simple closed mesh. A top,  mirrored bottom and simply connecting the edges.
        /// Since the normals would be shared on the edges, it is probably best for physics meshes or 
        /// stuff like that.
        /// </summary>
        private struct SimpleFullCellMesher : ICellMesher<SideInfo>
        {
            public float height;

            public SideInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
            {
                var info = new SideInfo();
                info.top = TopCellMesher.GenerateInfoSimple(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, hasCellTriangles);

                if (hasCellTriangles)
                {
                    info.triIndexStart = nextTriIndex;
                    info.triIndexLength = SimpleSideMesher.SideCalcTriIndexCount(info.top.config);
                    nextTriIndex += info.triIndexLength;
                }

                info.bottom = TopCellMesher.GenerateInfoSimple(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, hasCellTriangles);
                return info;
            }

            public void CalculateVertices(int x, int y, float cellSize, SideInfo info, float vertexHeight, NativeArray<float3> vertices)
            {
                TopCellMesher.CalculateVerticesSimple(x, y, cellSize, info.top, vertexHeight, vertices, height * 0.5f);
                TopCellMesher.CalculateVerticesSimple(x, y, cellSize, info.bottom, vertexHeight, vertices, height * -0.5f);
            }

            public void CalculateIndices(SideInfo bl, SideInfo br, SideInfo tr, SideInfo tl, NativeArray<int> triangles)
            {
                TopCellMesher.CalculateIndicesSimple(bl.top, br.top, tr.top, tl.top, triangles);
                SimpleSideMesher.CalculateSideIndices(bl, br, tr, tl, triangles);
                TopCellMesher.CalculateIndicesReverse(bl.bottom, br.bottom, tr.bottom, tl.bottom, triangles);
            }

            public bool NeedUpdateInfo => false;

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref SideInfo cell, ref SideInfo top, ref SideInfo right) { /* do nothing */ }

            public bool CanGenerateUvs => false;

            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, SideInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs) { /* do nothing */ }

            public bool CanGenerateNormals => true;

            public void CalculateNormals(SideInfo corner, SideInfo right, SideInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
                => SimpleSideMesher.CalculateTriangleNormals(corner, right, top, vertices, normals);
        }
    }
}
