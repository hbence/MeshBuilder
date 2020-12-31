using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;

namespace MeshBuilder
{
    using TopInfo = MarchingSquaresMesher.SimpleTopCellMesher.CornerInfo;
    using SideInfo = MarchingSquaresMesher.SimpleSideMesher.CornerInfo;

    using FullCellMesher = MarchingSquaresMesher.ModularFullCellMesher<
        MarchingSquaresMesher.SimpleTopCellMesher.CornerInfo,   MarchingSquaresMesher.SimpleTopCellMesher,
        MarchingSquaresMesher.SimpleSideMesher.CornerInfo,      MarchingSquaresMesher.SimpleSideMesher,
        MarchingSquaresMesher.SimpleTopCellMesher.CornerInfo,   MarchingSquaresMesher.SimpleBottomCellMesher>;

    using NoBottomCellMesher = MarchingSquaresMesher.ModularFullCellMesher<
        MarchingSquaresMesher.SimpleTopCellMesher.CornerInfo, MarchingSquaresMesher.SimpleTopCellMesher,
        MarchingSquaresMesher.SimpleSideMesher.CornerInfo, MarchingSquaresMesher.SimpleSideMesher,
        MarchingSquaresMesher.NullMesher.CornerInfo, MarchingSquaresMesher.NullMesher>;

    public partial class MarchingSquaresMesher : Builder
    {
        /// <summary>
        /// Generates a full mesh. The top, side and bottom can be set separately.
        /// It's pretty annoying to instantiate so there are helper functions for creation, and a StartGeneration
        /// method to handle the template arguments
        /// </summary>
        public struct ModularFullCellMesher<TopInfo, TopMesher, SideInfo, SideMesher, BottomInfo, BottomMesher> 
            : ICellMesher<ModularFullCellMesher<TopInfo, TopMesher, SideInfo, SideMesher, BottomInfo, BottomMesher>.CornerInfo>
            where TopInfo : struct
            where TopMesher : struct, ICellMesher<TopInfo>
            where SideInfo : struct
            where SideMesher : struct, ICellMesher<SideInfo>
            where BottomInfo : struct
            where BottomMesher : struct, ICellMesher<BottomInfo>
        {
            public struct CornerInfo
            {
                public TopInfo top;
                public SideInfo side;
                public BottomInfo bottom;
            }

            public TopMesher topMesher;
            public SideMesher sideMesher;
            public BottomMesher bottomMesher;

            public CornerInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
            {
                CornerInfo info = new CornerInfo();
                info.top = topMesher.GenerateInfo(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, hasCellTriangles);
                info.side = sideMesher.GenerateInfo(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, hasCellTriangles);
                info.bottom = bottomMesher.GenerateInfo(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, hasCellTriangles);
                return info;
            }

            public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, NativeArray<float3> vertices)
            {
                topMesher.CalculateVertices(x, y, cellSize, info.top, vertices);
                sideMesher.CalculateVertices(x, y, cellSize, info.side, vertices);
                bottomMesher.CalculateVertices(x, y, cellSize, info.bottom, vertices);
            }

            public void CalculateIndices(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
            {
                topMesher.CalculateIndices(bl.top, br.top, tr.top, tl.top, triangles);
                sideMesher.CalculateIndices(bl.side, br.side, tr.side, tl.side, triangles);
                bottomMesher.CalculateIndices(bl.bottom, br.bottom, tr.bottom, tl.bottom, triangles);
            }

            public JobHandle StartGeneration(JobHandle handle, MarchingSquaresMesher mesher)
            {
                return mesher.StartGeneration<CornerInfo, ModularFullCellMesher<TopInfo, TopMesher, SideInfo, SideMesher, BottomInfo, BottomMesher>>(handle, this);
            }
        }

        /// <summary>
        /// This does nothing, only used for the modular mesher to skip unneeded parts.
        /// </summary>
        public struct NullMesher : ICellMesher<NullMesher.CornerInfo>
        {
            public struct CornerInfo { }

            public CornerInfo GenerateInfo(float cornerDist, float rightDist, float topRightDist, float topDist, ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
            {
                // do nothing
                return default;
            }

            public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, NativeArray<float3> vertices)
            {
                // do nothing
            }
            
            public void CalculateIndices(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
            {
                // do nothing
            }
        }

        public struct SimpleBottomCellMesher : ICellMesher<TopInfo>
        {
            private SimpleTopCellMesher topCellMesher;
            public float heightOffset 
            {
                get => topCellMesher.heightOffset;
                set => topCellMesher.heightOffset = value;
            }

            public TopInfo GenerateInfo(float cornerDist, float rightDist, float topRightDist, float topDist, ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
             => topCellMesher.GenerateInfo(cornerDist, rightDist, topRightDist, topDist, ref nextVertices, ref nextTriIndex, hasCellTriangles);

            public void CalculateVertices(int x, int y, float cellSize, TopInfo info, NativeArray<float3> vertices)
             => topCellMesher.CalculateVertices(x, y, cellSize, info, vertices);

            public void CalculateIndices(TopInfo bl, TopInfo br, TopInfo tr, TopInfo tl, NativeArray<int> triangles)
             => SimpleTopCellMesher.CalculateIndicesReverse(bl, br, tr, tl, triangles);
        }

        private static FullCellMesher CreateFullCellMesher(float height)
        {
            var mesher = new FullCellMesher();
            mesher.topMesher.heightOffset = height * 0.5f;
            mesher.sideMesher.height = height;
            mesher.bottomMesher.heightOffset = height * -0.5f;
            return mesher;
        }

        private static NoBottomCellMesher CreateNoBottomCellMesher(float height)
        {
            var mesher = new NoBottomCellMesher();
            mesher.topMesher.heightOffset = height * 0.5f;
            mesher.sideMesher.height = height;
            return mesher;
        }

        private static ModularFullCellMesher<TopInfo, SimpleTopCellMesher,
            NullMesher.CornerInfo, NullMesher,
            NullMesher.CornerInfo, NullMesher>
            CreateTestMesher(float height)
        {
            var mesher = new ModularFullCellMesher<
                                        TopInfo, SimpleTopCellMesher,
                                        NullMesher.CornerInfo, NullMesher,
                                        NullMesher.CornerInfo, NullMesher>();
            mesher.topMesher.heightOffset = height * 0.5f;
            //mesher.sideMesher.height = height;
            return mesher;
        }
    }
}
