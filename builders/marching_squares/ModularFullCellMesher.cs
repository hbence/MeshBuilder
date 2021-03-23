using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;

namespace MeshBuilder
{
    using static MarchingSquaresMesher.TopCellMesher;

    using BottomMesher = MarchingSquaresMesher.ModularTopMesher<
        MarchingSquaresMesher.TopCellMesher.FullVertexCalculator,
        MarchingSquaresMesher.TopCellMesher.TriangleReverseOrderer>;

    using FullCellMesher = MarchingSquaresMesher.ModularFullCellMesher<
        MarchingSquaresMesher.TopCellMesher.TopCellInfo, MarchingSquaresMesher.TopCellMesher,
        MarchingSquaresMesher.SimpleSideMesher.SideInfo, MarchingSquaresMesher.SimpleSideMesher,
        MarchingSquaresMesher.TopCellMesher.TopCellInfo, MarchingSquaresMesher.ModularTopMesher<
                                                        MarchingSquaresMesher.TopCellMesher.FullVertexCalculator,
                                                        MarchingSquaresMesher.TopCellMesher.TriangleReverseOrderer>>;
    
    using NoBottomScalableCellMesher = MarchingSquaresMesher.ModularFullCellMesher<
        MarchingSquaresMesher.TopCellMesher.TopCellInfo, MarchingSquaresMesher.TopCellMesher,
        MarchingSquaresMesher.ScalableSideMesher.ScalableSideInfo, MarchingSquaresMesher.ScalableSideMesher,
        MarchingSquaresMesher.NullMesher.CornerInfo, MarchingSquaresMesher.NullMesher>;

    using ScalableFullCellMesher = MarchingSquaresMesher.ModularFullCellMesher<
        MarchingSquaresMesher.TopCellMesher.TopCellInfo, MarchingSquaresMesher.TopCellMesher,
        MarchingSquaresMesher.ScalableSideMesher.ScalableSideInfo, MarchingSquaresMesher.ScalableSideMesher,
        MarchingSquaresMesher.ScalableTopCellMesher.CornerInfoWithNormals, MarchingSquaresMesher.ScalableTopCellMesher>;

    using NoBottomCellMesher = MarchingSquaresMesher.ModularFullCellMesher<
        MarchingSquaresMesher.TopCellMesher.TopCellInfo, MarchingSquaresMesher.TopCellMesher,
        MarchingSquaresMesher.SimpleSideMesher.SideInfo, MarchingSquaresMesher.SimpleSideMesher,
        MarchingSquaresMesher.NullMesher.CornerInfo, MarchingSquaresMesher.NullMesher>;

    public partial class MarchingSquaresMesher : Builder
    {
        /// <summary>
        /// Generates a full mesh. The top, side and bottom can be set separately.
        /// It's pretty annoying to instantiate so there are helper functions for creation, and a StartGeneration
        /// method to handle the template arguments
        /// 
        /// Burst can't handle reference type properties, so generics have to be used to avoid code duplication.
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

            public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, float height, NativeArray<float3> vertices)
            {
                topMesher.CalculateVertices(x, y, cellSize, info.top, height, vertices);
                sideMesher.CalculateVertices(x, y, cellSize, info.side, height, vertices);
                bottomMesher.CalculateVertices(x, y, cellSize, info.bottom, height, vertices);
            }

            public void CalculateIndices(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
            {
                topMesher.CalculateIndices(bl.top, br.top, tr.top, tl.top, triangles);
                sideMesher.CalculateIndices(bl.side, br.side, tr.side, tl.side, triangles);
                bottomMesher.CalculateIndices(bl.bottom, br.bottom, tr.bottom, tl.bottom, triangles);
            }

            public bool NeedUpdateInfo => topMesher.NeedUpdateInfo || sideMesher.NeedUpdateInfo || bottomMesher.NeedUpdateInfo; 

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref CornerInfo cell, ref CornerInfo top, ref CornerInfo right)
            {
                topMesher.UpdateInfo(x, y, cellColNum, cellRowNum, ref cell.top, ref top.top, ref right.top);
                sideMesher.UpdateInfo(x, y, cellColNum, cellRowNum, ref cell.side, ref top.side, ref right.side);
                bottomMesher.UpdateInfo(x, y, cellColNum, cellRowNum, ref cell.bottom, ref top.bottom, ref right.bottom);
            }

            // if any of them can generate uv, then allow it for the modular
            public bool CanGenerateUvs => topMesher.CanGenerateUvs || sideMesher.CanGenerateUvs || bottomMesher.CanGenerateUvs; 

            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, CornerInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
            {
                topMesher.CalculateUvs(x, y, cellColNum, cellRowNum, cellSize, corner.top, uvScale, vertices, uvs);
                sideMesher.CalculateUvs(x, y, cellColNum, cellRowNum, cellSize, corner.side, uvScale, vertices, uvs);
                bottomMesher.CalculateUvs(x, y, cellColNum, cellRowNum, cellSize, corner.bottom, uvScale, vertices, uvs);
            }

            // only generate normals if all of them can, if one can't then it's better to generate it later for the whole mesh
            public bool CanGenerateNormals => topMesher.CanGenerateNormals && sideMesher.CanGenerateNormals && bottomMesher.CanGenerateNormals; 

            public void CalculateNormals(CornerInfo corner, CornerInfo right, CornerInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
            {
                topMesher.CalculateNormals(corner.top, right.top, top.top, vertices, normals);
                sideMesher.CalculateNormals(corner.side, right.side, top.side, vertices, normals);
                bottomMesher.CalculateNormals(corner.bottom, right.bottom, top.bottom, vertices, normals);
            }

            public JobHandle StartGeneration(JobHandle handle, MarchingSquaresMesher mesher)
                => mesher.StartGeneration<CornerInfo, ModularFullCellMesher<TopInfo, TopMesher, SideInfo, SideMesher, BottomInfo, BottomMesher>>(handle, this);
        }

        /// <summary>
        /// This does nothing, only used for the modular mesher to skip unneeded parts.
        /// </summary>
        public struct NullMesher : ICellMesher<NullMesher.CornerInfo>
        {
            public struct CornerInfo { }
            public CornerInfo GenerateInfo(float cornerDist, float rightDist, float topRightDist, float topDist, ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles) => default;
            public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, float height, NativeArray<float3> vertices) { /* do nothing */ }
            public void CalculateIndices(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles) { /* do nothing */ }
            public bool NeedUpdateInfo => false;
            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref CornerInfo cell, ref CornerInfo top, ref CornerInfo right) { /* do nothing */ }
            public bool CanGenerateUvs => false;
            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, CornerInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs) { /* do nothing */ }
            public bool CanGenerateNormals => true;
            public void CalculateNormals(CornerInfo corner, CornerInfo right, CornerInfo top, NativeArray<float3> vertices, NativeArray<float3> normals) { /* do nothing */ }
        }

        public struct ModularTopMesher<VertexCalculator, TriangleOrderer> : ICellMesher<TopCellInfo>
            where VertexCalculator : struct, IVertexCalculator
            where TriangleOrderer : struct, ITriangleOrderer
        {
            private float3 normal;
            private NormalMode normalMode;
            private VertexCalculator vertexCalculator;

            public ModularTopMesher(NormalMode normalMode, VertexCalculator vertexCalculator)
            {
                this.normalMode = normalMode;
                normal = IsUp(normalMode) ? new float3(0, 1, 0) : new float3(0, -1, 0);
                this.vertexCalculator = vertexCalculator;
            }

            public TopCellInfo GenerateInfo(float cornerDist, float rightDist, float topRightDist, float topDist, ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles) 
                => GenerateInfoSimple(cornerDist, rightDist, topRightDist, topDist, ref nextVertices, ref nextTriIndex, hasCellTriangles);

            public void CalculateVertices(int x, int y, float cellSize, TopCellInfo info, float height, NativeArray<float3> vertices)
                => vertexCalculator.CalculateVertices(x, y, cellSize, info, height, vertices);

            public void CalculateIndices(TopCellInfo bl, TopCellInfo br, TopCellInfo tr, TopCellInfo tl, NativeArray<int> triangles)
                => CalculateIndicesSimple<TriangleReverseOrderer>(bl.info.config, bl.tris, bl.verts, br.verts, tr.verts, tl.verts, triangles);

            public bool NeedUpdateInfo => false;
            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref TopCellInfo cell, ref TopCellInfo top, ref TopCellInfo right) { /* do nothing */ }

            public bool CanGenerateUvs => true;
            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, TopCellInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
                => TopCalculateUvs(x, y, cellColNum, cellRowNum, cellSize, corner.verts, uvScale, vertices, uvs);

            public bool CanGenerateNormals => IsFlat(normalMode);
            public void CalculateNormals(TopCellInfo corner, TopCellInfo right, TopCellInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
                => SetNormals(corner.verts, normals, normal);
        }

        private static FullCellMesher CreateFull(float height, bool isFlat, float lerpToEdge = 1f)
            => new FullCellMesher
            {
                topMesher = new TopCellMesher(height * 0.5f, SelectUp(isFlat), lerpToEdge),
                sideMesher = new SimpleSideMesher(height, lerpToEdge),
                bottomMesher = new BottomMesher(NormalMode.DownFlat, new FullVertexCalculator { heightOffset = height * -0.5f, lerpToEdge = lerpToEdge })
            };
    
        private static NoBottomCellMesher CreateNoBottom(float height, bool isFlat, float lerpToEdge = 1f)
            => new NoBottomCellMesher
            {
                topMesher = new TopCellMesher(height * 0.5f, SelectUp(isFlat), lerpToEdge),
                sideMesher = new SimpleSideMesher(height, lerpToEdge)
            };

        private static NoBottomScalableCellMesher CreateScalableNoBottom(float height, float bottomOffsetScale, bool isFlat, float lerpToEdge = 1f)
            => new NoBottomScalableCellMesher
            {
                topMesher = new TopCellMesher(height * 0.5f, SelectUp(isFlat), lerpToEdge),
                sideMesher = new ScalableSideMesher(height, bottomOffsetScale, 0, lerpToEdge)
            };           

        private static ScalableFullCellMesher CreateScalableFull(float height, float bottomOffsetScale, bool isFlat, float lerpToEdge = 1f)
            => new ScalableFullCellMesher
            {
                topMesher = new TopCellMesher(height * 0.5f, SelectUp(isFlat), lerpToEdge),
                sideMesher = new ScalableSideMesher(height, bottomOffsetScale, 0, lerpToEdge),
                bottomMesher = new ScalableTopCellMesher(height * -0.5f, bottomOffsetScale, true, lerpToEdge)
            };

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
