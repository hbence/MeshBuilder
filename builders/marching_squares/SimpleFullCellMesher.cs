using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    using SideInfo = MarchingSquaresMesher.SimpleSideMesher.SideInfo;
    using CellInfo = MarchingSquaresMesher.TopCellMesher.CellInfo;
    using CellVerts = MarchingSquaresMesher.TopCellMesher.CellVertices;
    using IndexSpan = MarchingSquaresMesher.TopCellMesher.IndexSpan;

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
            public float edgeLerp;

            public SideInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                ref int nextVertex, ref int nextTriIndex, bool hasCellTriangles)
            {
                byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);

                SideInfo info = new SideInfo()
                {
                    info = new CellInfo(config, cornerDistance, rightDistance, topDistance),
                    top = new CellVerts(-1, -1, -1),
                    bottom = new CellVerts(-1, -1, -1),
                    tris = new IndexSpan(0, 0)
                };

                TopCellMesher.SetVertices(config, ref nextVertex, ref info.top);
                TopCellMesher.SetVertices(config, ref nextVertex, ref info.bottom);

                if (hasCellTriangles)
                {
                    byte topLength = TopCellMesher.CalcTriIndexCount(config);

                    info.tris.start = nextTriIndex;
                    info.tris.length = (byte)(SimpleSideMesher.SideCalcTriIndexCount(config) + 2 * topLength);

                    nextTriIndex += info.tris.length;
                }

                return info;
            }

            public void CalculateVertices(int x, int y, float cellSize, SideInfo info, float vertexHeight, NativeArray<float3> vertices)
            {
                TopCellMesher.CalculateVerticesFull(x, y, cellSize, info.top, info.info, vertexHeight, vertices, height * 0.5f, edgeLerp);
                TopCellMesher.CalculateVerticesFull(x, y, cellSize, info.bottom, info.info, vertexHeight, vertices, -height * 0.5f, edgeLerp);
            }

            public void CalculateIndices(SideInfo bl, SideInfo br, SideInfo tr, SideInfo tl, NativeArray<int> triangles)
            {
                byte config = bl.info.config;
                byte faceLength = TopCellMesher.CalcTriIndexCount(config);
                byte sideLength = SimpleSideMesher.SideCalcTriIndexCount(config);
                
                IndexSpan tris = bl.tris;
                tris.length = faceLength;
                TopCellMesher.CalculateIndicesSimple(config, tris, bl.top, br.top, tr.top, tl.top, triangles);
                
                tris.start += faceLength;
                tris.length = sideLength;
                bl.tris = tris;
                SimpleSideMesher.CalculateSideIndices(bl, br, tr, tl, triangles);

                tris.start += sideLength;
                tris.length = faceLength;
                TopCellMesher.CalculateIndicesReverse(config, tris, bl.bottom, br.bottom, tr.bottom, tl.bottom, triangles);
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
