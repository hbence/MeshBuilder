using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    public partial class MarchingSquaresMesher : Builder
    {
        /// <summary>
        /// Generates an XZ aligned flat mesh.
        /// </summary>
        public struct TopCellMesher : ICellMesher<TopCellMesher.CornerInfo>
        {
            public struct CornerInfo
            {
                public byte config; // configuration of the cell where this corner is the bottom left

                public float cornerDist; // distance of the corner
                public float rightDist; // distance of the right adjacent
                public float topDist; // distance of the top adjacent

                public int vertexIndex; // in the cell this is the bottom left corner
                public int bottomEdgeIndex; // in the cell this is the vertex on the bottom edge
                public int leftEdgeIndex; // in the cell this is the vertex on the left edge

                public int triIndexStart;
                public int triIndexLength;
            }

            public enum NormalMode { UpFlat, DownFlat, UpDontSetNormal, DownDontSetNormal }

            private float3 normal;
            private NormalMode normalMode;
            private FullVertexCalculator vertexCalculator;

            public TopCellMesher(float heightOffset, NormalMode normalMode, float lerpToEdge)
            {
                if (normalMode == NormalMode.DownDontSetNormal || normalMode == NormalMode.DownFlat)
                {
                    Debug.LogWarning("invalid normal mode, use the BottomCellMesher for upside down mesh!");
                }

                this.normalMode = normalMode;
                normal = new float3(0, 1, 0);
                vertexCalculator = new FullVertexCalculator { heightOffset = heightOffset, lerpToEdge = lerpToEdge };
            }

            public CornerInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                            ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
                => GenerateInfoSimple(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, hasCellTriangles);

            static public CornerInfo GenerateInfoSimple(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                            ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
            {
                CornerInfo info = new CornerInfo
                {
                    config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance),

                    cornerDist = cornerDistance,
                    rightDist = rightDistance,
                    topDist = topDistance,

                    vertexIndex = -1,
                    leftEdgeIndex = -1,
                    bottomEdgeIndex = -1,

                    triIndexStart = nextTriIndex,
                    triIndexLength = 0
                };

                if (hasCellTriangles)
                {
                    info.triIndexLength = CalcTriIndexCount(info.config);
                    nextTriIndex += info.triIndexLength;
                }

                bool hasBL = HasMask(info.config, MaskBL);
                if (hasBL)
                {
                    info.vertexIndex = nextVertices;
                    ++nextVertices;
                }

                if (hasBL != HasMask(info.config, MaskTL))
                {
                    info.leftEdgeIndex = nextVertices;
                    ++nextVertices;
                }

                if (hasBL != HasMask(info.config, MaskBR))
                {
                    info.bottomEdgeIndex = nextVertices;
                    ++nextVertices;
                }

                return info;
            }

            public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, float height, NativeArray<float3> vertices)
                => vertexCalculator.CalculateVertices(x, y, cellSize, info, height, vertices);

            static public void CalculateVerticesSimple(int x, int y, float cellSize, CornerInfo info, float height, NativeArray<float3> vertices, float heightOffset = 0)
            {
                float3 pos = new float3(x * cellSize, height + heightOffset, y * cellSize);

                if (info.vertexIndex >= 0) { vertices[info.vertexIndex] = pos; }
                if (info.leftEdgeIndex >= 0) { vertices[info.leftEdgeIndex] = pos + new float3(0, 0, cellSize * LerpT(info.cornerDist, info.topDist)); }
                if (info.bottomEdgeIndex >= 0) { vertices[info.bottomEdgeIndex] = pos + new float3(cellSize * LerpT(info.cornerDist, info.rightDist), 0, 0); }
            }

            static public void CalculateVerticesFull(int x, int y, float cellSize, CornerInfo info, float height, NativeArray<float3> vertices, float heightOffset, float edgeLerp)
            {
                float3 pos = new float3(x * cellSize, heightOffset + height, y * cellSize);

                if (info.vertexIndex >= 0) { vertices[info.vertexIndex] = pos; }
                if (info.leftEdgeIndex >= 0) { vertices[info.leftEdgeIndex] = pos + new float3(0, 0, cellSize * LerpT(info.cornerDist, info.topDist, edgeLerp)); }
                if (info.bottomEdgeIndex >= 0) { vertices[info.bottomEdgeIndex] = pos + new float3(cellSize * LerpT(info.cornerDist, info.rightDist, edgeLerp), 0, 0); }
            }

            public interface IVertexCalculator
            {
                void CalculateVertices(int x, int y, float cellSize, CornerInfo info, float height, NativeArray<float3> vertices);
            }

            public struct SimpleVertexCalculator : IVertexCalculator
            {
                public float heightOffset;
                public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, float height, NativeArray<float3> vertices)
                    => CalculateVerticesSimple(x, y, cellSize, info, height, vertices, heightOffset);
            }

            public struct FullVertexCalculator : IVertexCalculator
            {
                public float heightOffset;
                public float lerpToEdge;
                public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, float height, NativeArray<float3> vertices)
                    => CalculateVerticesFull(x, y, cellSize, info, height, vertices, heightOffset, lerpToEdge);
            }

            public void CalculateIndices(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
                => CalculateIndicesSimple<TriangleOrderer>(bl, br, tr, tl, triangles);

            public bool NeedUpdateInfo => false;

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref CornerInfo cell, ref CornerInfo top, ref CornerInfo right)
            {
                // do nothing
            }

            static public float LerpT(float a, float b) => math.abs(a) / (math.abs(a) + math.abs(b));
            static public float LerpT(float a, float b, float lerpToDist) => math.lerp(0.5f, math.abs(a) / (math.abs(a) + math.abs(b)), lerpToDist);

            public static int CalcTriIndexCount(byte config)
            {
                switch (config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL: return 2 * 3;
                    // corners
                    case MaskBL: return 1 * 3;
                    case MaskBR: return 1 * 3;
                    case MaskTR: return 1 * 3;
                    case MaskTL: return 1 * 3;
                    // halves
                    case MaskBL | MaskBR: return 2 * 3;
                    case MaskTL | MaskTR: return 2 * 3;
                    case MaskBL | MaskTL: return 2 * 3;
                    case MaskBR | MaskTR: return 2 * 3;
                    // diagonals
                    case MaskBL | MaskTR: return 4 * 3;
                    case MaskTL | MaskBR: return 4 * 3;
                    // three quarters
                    case MaskBL | MaskTR | MaskBR: return 3 * 3;
                    case MaskBL | MaskTL | MaskBR: return 3 * 3;
                    case MaskBL | MaskTL | MaskTR: return 3 * 3;
                    case MaskTL | MaskTR | MaskBR: return 3 * 3;
                }
                return 0;
            }

            public static void CalculateIndicesSimple(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
                => CalculateIndicesSimple<TriangleOrderer>(bl, br, tr, tl, triangles);

            public static void CalculateIndicesReverse(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
                => CalculateIndicesSimple<TriangleReverseOrderer>(bl, br, tr, tl, triangles);

            private static void AddTri(NativeArray<int> triangles, ref int nextIndex, int a, int b, int c)
            {
                triangles[nextIndex] = a;
                ++nextIndex;
                triangles[nextIndex] = b;
                ++nextIndex;
                triangles[nextIndex] = c;
                ++nextIndex;
            }

            private static void AddTriReverse(NativeArray<int> triangles, ref int nextIndex, int a, int b, int c)
            {
                triangles[nextIndex] = c;
                ++nextIndex;
                triangles[nextIndex] = b;
                ++nextIndex;
                triangles[nextIndex] = a;
                ++nextIndex;
            }

            public static void CalculateIndicesSimple<TriangleOrderer>(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
                where TriangleOrderer : struct, ITriangleOrderer
            {
                TriangleOrderer orderer = new TriangleOrderer();
                int triangleIndex = bl.triIndexStart;
                switch (bl.config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), Vertex(tr));
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), Vertex(tr), Vertex(br));
                            break;
                        }
                    // corners
                    case MaskBL:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), LeftEdge(bl), BottomEdge(bl));
                            break;
                        }
                    case MaskBR:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    case MaskTR:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tl), BottomEdge(tl), LeftEdge(bl));
                            break;
                        }
                    // halves
                    case MaskBL | MaskBR:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), LeftEdge(bl), Vertex(br));
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(br), LeftEdge(bl), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tl), Vertex(tr), LeftEdge(bl));
                            orderer.AddTriangle(triangles, ref triangleIndex, LeftEdge(bl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskBL | MaskTL:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), BottomEdge(tl), BottomEdge(bl));
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), BottomEdge(tl));
                            break;
                        }
                    case MaskBR | MaskTR:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), Vertex(tr), Vertex(br));
                            orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), BottomEdge(tl), Vertex(tr));
                            break;
                        }
                    // diagonals
                    case MaskBL | MaskTR:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), Vertex(bl), LeftEdge(bl));
                            orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(bl), LeftEdge(br));
                            orderer.AddTriangle(triangles, ref triangleIndex, LeftEdge(br), LeftEdge(bl), BottomEdge(tl));
                            orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskBR:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, LeftEdge(bl), Vertex(tl), BottomEdge(tl));
                            orderer.AddTriangle(triangles, ref triangleIndex, LeftEdge(bl), BottomEdge(tl), BottomEdge(bl));
                            orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), BottomEdge(tl), LeftEdge(br));
                            orderer.AddTriangle(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    // three quarters
                    case MaskBL | MaskTR | MaskBR:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(br), Vertex(bl), LeftEdge(bl));
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(br), LeftEdge(bl), BottomEdge(tl));
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(br), BottomEdge(tl), Vertex(tr));
                            break;
                        }
                    case MaskBL | MaskTL | MaskBR:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), BottomEdge(tl));
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), BottomEdge(tl), LeftEdge(br));
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    case MaskBL | MaskTL | MaskTR:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tl), BottomEdge(bl), Vertex(bl));
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tl), LeftEdge(br), BottomEdge(bl));
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR | MaskBR:
                        {
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tr), LeftEdge(bl), Vertex(tl));
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tr), BottomEdge(bl), LeftEdge(bl));
                            orderer.AddTriangle(triangles, ref triangleIndex, Vertex(tr), Vertex(br), BottomEdge(bl));
                            break;
                        }
                }
            }

            private static int Vertex(CornerInfo info) => info.vertexIndex;
            private static int LeftEdge(CornerInfo info) => info.leftEdgeIndex;
            private static int BottomEdge(CornerInfo info) => info.bottomEdgeIndex;

            public interface ITriangleOrderer
            {
                void AddTriangle(NativeArray<int> triangles, ref int nextIndex, int a, int b, int c);
            }

            public struct TriangleOrderer : ITriangleOrderer
            {
                public void AddTriangle(NativeArray<int> triangles, ref int nextIndex, int a, int b, int c)
                    => AddTri(triangles, ref nextIndex, a, b, c);
            }

            public struct TriangleReverseOrderer : ITriangleOrderer
            {
                public void AddTriangle(NativeArray<int> triangles, ref int nextIndex, int a, int b, int c)
                    => AddTriReverse(triangles, ref nextIndex, a, b, c);
            }

            public bool CanGenerateUvs => true;

            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, CornerInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
                => TopCalculateUvs(x, y, cellColNum, cellRowNum, cellSize, corner, uvScale, vertices, uvs);

            public bool CanGenerateNormals => normalMode == NormalMode.UpFlat;

            public void CalculateNormals(CornerInfo corner, CornerInfo right, CornerInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
                => SetNormals(corner, normals, normal);

            static public void TopCalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, CornerInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
            {
                float2 topRight = new float2((cellColNum + 1) * cellSize * uvScale, (cellRowNum + 1) * cellSize * uvScale);
                float2 uv;
                if (corner.vertexIndex >= 0)
                {
                    uv.x = vertices[corner.vertexIndex].x / topRight.x;
                    uv.y = vertices[corner.vertexIndex].z / topRight.y;
                    uvs[corner.vertexIndex] = uv;
                }
                if (corner.leftEdgeIndex >= 0)
                {
                    uv.x = vertices[corner.leftEdgeIndex].x / topRight.x;
                    uv.y = vertices[corner.leftEdgeIndex].z / topRight.y;
                    uvs[corner.leftEdgeIndex] = uv;
                }
                if (corner.bottomEdgeIndex >= 0)
                {
                    uv.x = vertices[corner.bottomEdgeIndex].x / topRight.x;
                    uv.y = vertices[corner.bottomEdgeIndex].z / topRight.y;
                    uvs[corner.bottomEdgeIndex] = uv;
                }
            }

            static public void SetNormals(CornerInfo corner, NativeArray<float3> normals, float3 normal)
            {
                if (corner.vertexIndex >= 0) { normals[corner.vertexIndex] = normal; }
                if (corner.leftEdgeIndex >= 0) { normals[corner.leftEdgeIndex] = normal; }
                if (corner.bottomEdgeIndex >= 0) { normals[corner.bottomEdgeIndex] = normal; }
            }
        }

        /*
        public struct ModularTopCellMesher<VertexCalculator, TriangleOrderer> : ICellMesher<TopCellMesher.CornerInfo>
            where VertexCalculator : struct, ModularTopCellMesher<VertexCalculator, TriangleOrderer>.IVertexCalculator
            where TriangleOrderer : struct, ModularTopCellMesher<VertexCalculator, TriangleOrderer>.ITriangleOrderer
        {
        }
        */
    }
}
