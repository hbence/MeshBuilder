using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    using CornerInfo = MarchingSquaresMesher.SimpleSideMesher.CornerInfo;
    using CornerInfoWithNormals = MarchingSquaresMesher.ScalableTopCellMesher.CornerInfoWithNormals;
    public partial class MarchingSquaresMesher : Builder
    {
        public struct SimpleSideMesher : ICellMesher<CornerInfo>
        {
            public struct CornerInfo
            {
                public SimpleTopCellMesher.CornerInfo top;
                public int triIndexStart;
                public int triIndexLength;
                public SimpleTopCellMesher.CornerInfo bottom;
            }
            private SimpleTopCellMesher topMesher;

            public float height;

            public CornerInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
            {
                CornerInfo info = new CornerInfo();
                info.top = topMesher.GenerateInfo(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, false);

                if (hasCellTriangles)
                {
                    info.triIndexStart = nextTriIndex;
                    info.triIndexLength = SideCalcTriIndexCount(info.top.config);

                    nextTriIndex += info.triIndexLength;
                }

                info.bottom = topMesher.GenerateInfo(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, false);
                return info;
            }

            public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, NativeArray<float3> vertices)
            {
                topMesher.heightOffset = height * 0.5f;
                topMesher.CalculateVertices(x, y, cellSize, info.top, vertices);
                topMesher.heightOffset = height * -0.5f;
                topMesher.CalculateVertices(x, y, cellSize, info.bottom, vertices);
            }

            public void CalculateIndices(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
             => CalculateSideIndices(bl, br, tr, tl, triangles);

            public bool NeedUpdateInfo { get => false; }

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref CornerInfo cell, ref CornerInfo top, ref CornerInfo right)
            {
                // do nothing
            }

            public static void CalculateSideIndices(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
            {
                int triangleIndex = bl.triIndexStart;
                switch (bl.top.config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL: break;
                    // corners
                    case MaskBL:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(bl), BottomLeftEdge(bl));
                            break;
                        }
                    case MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(bl), BottomBottomEdge(bl));
                            break;
                        }
                    case MaskTR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(br), BottomLeftEdge(br));
                            break;
                        }
                    case MaskTL:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl));
                            break;
                        }
                    // halves
                    case MaskBL | MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopLeftEdge(bl), BottomLeftEdge(bl));
                            break;
                        }
                    case MaskTL | MaskTR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopLeftEdge(br), BottomLeftEdge(br));
                            break;
                        }
                    case MaskBL | MaskTL:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl));
                            break;
                        }
                    case MaskBR | MaskTR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopBottomEdge(bl), BottomBottomEdge(bl));
                            break;
                        }
                    // diagonals
                    case MaskBL | MaskTR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(bl), BottomLeftEdge(bl));
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(br), BottomLeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(tl), BottomBottomEdge(tl));
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(bl), BottomBottomEdge(bl));
                            break;
                        }
                    // three quarters
                    case MaskBL | MaskTR | MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(bl), BottomLeftEdge(bl));
                            break;
                        }
                    case MaskBL | MaskTL | MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(tl), BottomBottomEdge(tl));
                            break;
                        }
                    case MaskBL | MaskTL | MaskTR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(br), BottomLeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR | MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(bl), BottomBottomEdge(bl));
                            break;
                        }
                }
            }

            private static void AddFace(NativeArray<int> triangles, ref int nextIndex, int bl, int tl, int tr, int br)
            {
                triangles[nextIndex] = bl;
                ++nextIndex;
                triangles[nextIndex] = tl;
                ++nextIndex;
                triangles[nextIndex] = tr;
                ++nextIndex;

                triangles[nextIndex] = bl;
                ++nextIndex;
                triangles[nextIndex] = tr;
                ++nextIndex;
                triangles[nextIndex] = br;
                ++nextIndex;
            }

            public static int SideCalcTriIndexCount(byte config)
            {
                switch (config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL: return 0;
                    // corners
                    case MaskBL: return 2 * 3;
                    case MaskBR: return 2 * 3;
                    case MaskTR: return 2 * 3;
                    case MaskTL: return 2 * 3;
                    // halves
                    case MaskBL | MaskBR: return 2 * 3;
                    case MaskTL | MaskTR: return 2 * 3;
                    case MaskBL | MaskTL: return 2 * 3;
                    case MaskBR | MaskTR: return 2 * 3;
                    // diagonals
                    case MaskBL | MaskTR: return 4 * 3;
                    case MaskTL | MaskBR: return 4 * 3;
                    // three quarters
                    case MaskBL | MaskTR | MaskBR: return 2 * 3;
                    case MaskBL | MaskTL | MaskBR: return 2 * 3;
                    case MaskBL | MaskTL | MaskTR: return 2 * 3;
                    case MaskTL | MaskTR | MaskBR: return 2 * 3;
                }
                return 0;
            }

            private static int TopLeftEdge(CornerInfo info) => info.top.leftEdgeIndex;
            private static int BottomLeftEdge(CornerInfo info) => info.bottom.leftEdgeIndex;
            private static int TopBottomEdge(CornerInfo info) => info.top.bottomEdgeIndex;
            private static int BottomBottomEdge(CornerInfo info) => info.bottom.bottomEdgeIndex;
        }

        // NOTE: this is almost a copy paste of the SimpleSideMesher, probably I should refactor that a bit, or make it into a generic version
        // (but then that wouldn't really be a 'simple' mesher)
        public struct ScalableSideMesher : ICellMesher<ScalableSideMesher.CornerInfo>
        {
            public struct CornerInfo
            {
                public CornerInfoWithNormals top;
                public int triIndexStart;
                public int triIndexLength;
                public CornerInfoWithNormals bottom;
            }
            private ScalableTopCellMesher topMesher;

            public float height;
            public float topNormalOffset;
            public float bottomNormalOffset;

            public CornerInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
            {
                CornerInfo info = new CornerInfo();
                info.top = topMesher.GenerateInfo(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, false);

                if (hasCellTriangles)
                {
                    info.triIndexStart = nextTriIndex;
                    info.triIndexLength = SimpleSideMesher.SideCalcTriIndexCount(info.top.cornerInfo.config);

                    nextTriIndex += info.triIndexLength;
                }

                info.bottom = topMesher.GenerateInfo(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, false);
                return info;
            }

            public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, NativeArray<float3> vertices)
            {
                topMesher.heightOffset = height * 0.5f;
                topMesher.normalOffset = topNormalOffset;
                topMesher.CalculateVertices(x, y, cellSize, info.top, vertices);
                topMesher.heightOffset = height * -0.5f;
                topMesher.normalOffset = bottomNormalOffset;
                topMesher.CalculateVertices(x, y, cellSize, info.bottom, vertices);
            }

            public bool NeedUpdateInfo { get => true; }

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref CornerInfo cell, ref CornerInfo top, ref CornerInfo right)
            {
                topMesher.UpdateInfo(x, y, cellColNum, cellRowNum, ref cell.top, ref top.top, ref right.top);
                topMesher.UpdateInfo(x, y, cellColNum, cellRowNum, ref cell.bottom, ref top.bottom, ref right.bottom);
            }

            public void CalculateIndices(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
            {
                int triangleIndex = bl.triIndexStart;
                switch (bl.top.cornerInfo.config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL: break;
                    // corners
                    case MaskBL:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(bl), BottomLeftEdge(bl));
                            break;
                        }
                    case MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(bl), BottomBottomEdge(bl));
                            break;
                        }
                    case MaskTR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(br), BottomLeftEdge(br));
                            break;
                        }
                    case MaskTL:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl));
                            break;
                        }
                    // halves
                    case MaskBL | MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopLeftEdge(bl), BottomLeftEdge(bl));
                            break;
                        }
                    case MaskTL | MaskTR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopLeftEdge(br), BottomLeftEdge(br));
                            break;
                        }
                    case MaskBL | MaskTL:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl));
                            break;
                        }
                    case MaskBR | MaskTR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopBottomEdge(bl), BottomBottomEdge(bl));
                            break;
                        }
                    // diagonals
                    case MaskBL | MaskTR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(bl), BottomLeftEdge(bl));
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(br), BottomLeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(tl), BottomBottomEdge(tl));
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(bl), BottomBottomEdge(bl));
                            break;
                        }
                    // three quarters
                    case MaskBL | MaskTR | MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(bl), BottomLeftEdge(bl));
                            break;
                        }
                    case MaskBL | MaskTL | MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(tl), BottomBottomEdge(tl));
                            break;
                        }
                    case MaskBL | MaskTL | MaskTR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(br), BottomLeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR | MaskBR:
                        {
                            AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(bl), BottomBottomEdge(bl));
                            break;
                        }
                }
            }

            private static void AddFace(NativeArray<int> triangles, ref int nextIndex, int bl, int tl, int tr, int br)
            {
                triangles[nextIndex] = bl; ++nextIndex;
                triangles[nextIndex] = tl; ++nextIndex;
                triangles[nextIndex] = tr; ++nextIndex;

                triangles[nextIndex] = bl; ++nextIndex;
                triangles[nextIndex] = tr; ++nextIndex;
                triangles[nextIndex] = br; ++nextIndex;
            }

            private static int TopLeftEdge(CornerInfo info) => info.top.cornerInfo.leftEdgeIndex;
            private static int BottomLeftEdge(CornerInfo info) => info.bottom.cornerInfo.leftEdgeIndex;
            private static int TopBottomEdge(CornerInfo info) => info.top.cornerInfo.bottomEdgeIndex;
            private static int BottomBottomEdge(CornerInfo info) => info.bottom.cornerInfo.bottomEdgeIndex;
        }
    }
}