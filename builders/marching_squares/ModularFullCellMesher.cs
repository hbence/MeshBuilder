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
        MarchingSquaresMesher.SimpleTopCellMesher.CornerInfo,   MarchingSquaresMesher.SimpleTopCellMesher>;

    using ScalableFullCellMesher = MarchingSquaresMesher.ModularFullCellMesher<
        MarchingSquaresMesher.SimpleTopCellMesher.CornerInfo, MarchingSquaresMesher.SimpleTopCellMesher,
        MarchingSquaresMesher.ScalableSideMesher.CornerInfo, MarchingSquaresMesher.ScalableSideMesher,
        MarchingSquaresMesher.NullMesher.CornerInfo, MarchingSquaresMesher.NullMesher>;

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

            public bool NeedUpdateInfo { get => topMesher.NeedUpdateInfo || sideMesher.NeedUpdateInfo || bottomMesher.NeedUpdateInfo; }

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref CornerInfo cell, ref CornerInfo top, ref CornerInfo right)
            {
                topMesher.UpdateInfo(x, y, cellColNum, cellRowNum, ref cell.top, ref top.top, ref right.top);
                sideMesher.UpdateInfo(x, y, cellColNum, cellRowNum, ref cell.side, ref top.side, ref right.side);
                bottomMesher.UpdateInfo(x, y, cellColNum, cellRowNum, ref cell.bottom, ref top.bottom, ref right.bottom);
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

            public bool NeedUpdateInfo { get => false; }

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref CornerInfo cell, ref CornerInfo top, ref CornerInfo right)
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

            public bool NeedUpdateInfo { get => false; }

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref TopInfo cell, ref TopInfo top, ref TopInfo right)
            {
                // do nothing
            }
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

        private static ScalableFullCellMesher CreateScalableFullCellMesher(float height, float bottomNormalOffset)
        {
            var mesher = new ScalableFullCellMesher();
            mesher.topMesher.heightOffset = height * 0.5f;
            mesher.sideMesher.height = height;
            mesher.sideMesher.topNormalOffset = 0;
            mesher.sideMesher.bottomNormalOffset = bottomNormalOffset;
            return mesher;
        }

        private static ModularFullCellMesher<ScalableTopCellMesher.CornerInfoWithNormals, ScalableTopCellMesher,
            NullMesher.CornerInfo, NullMesher,
            NullMesher.CornerInfo, NullMesher>
            CreateTestMesher(float height)
        {
            var mesher = new ModularFullCellMesher<
                                        ScalableTopCellMesher.CornerInfoWithNormals, ScalableTopCellMesher,
                                        NullMesher.CornerInfo, NullMesher,
                                        NullMesher.CornerInfo, NullMesher>();
            mesher.topMesher.heightOffset = height * 0.5f;
            //mesher.sideMesher.height = height;
            return mesher;
        }
    }
}
