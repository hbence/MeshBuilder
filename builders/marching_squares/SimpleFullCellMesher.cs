using Unity.Collections;
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
            private SimpleTopCellMesher topMesher;

            public float height;

            public SideInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
            {
                var info = new SideInfo();
                info.top = topMesher.GenerateInfo(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, hasCellTriangles);

                if (hasCellTriangles)
                {
                    info.triIndexStart = nextTriIndex;
                    info.triIndexLength = SimpleSideMesher.SideCalcTriIndexCount(info.top.config);
                    nextTriIndex += info.triIndexLength;
                }

                info.bottom = topMesher.GenerateInfo(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, hasCellTriangles);
                return info;
            }

            public void CalculateVertices(int x, int y, float cellSize, SideInfo info, NativeArray<float3> vertices)
            {
                topMesher.heightOffset = height * 0.5f;
                topMesher.CalculateVertices(x, y, cellSize, info.top, vertices);
                topMesher.heightOffset = height * -0.5f;
                topMesher.CalculateVertices(x, y, cellSize, info.bottom, vertices);
            }

            public void CalculateIndices(SideInfo bl, SideInfo br, SideInfo tr, SideInfo tl, NativeArray<int> triangles)
            {
                SimpleTopCellMesher.CalculateIndicesNormal(bl.top, br.top, tr.top, tl.top, triangles);
                SimpleSideMesher.CalculateSideIndices(bl, br, tr, tl, triangles);
                SimpleTopCellMesher.CalculateIndicesReverse(bl.bottom, br.bottom, tr.bottom, tl.bottom, triangles);
            }

            public bool NeedUpdateInfo { get => false; }

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref SideInfo cell, ref SideInfo top, ref SideInfo right)
            {
                // do nothing
            }
        }
    }
}
