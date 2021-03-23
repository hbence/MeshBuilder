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
        public struct TopCellMesher : ICellMesher<TopCellMesher.TopCellInfo>
        {
            public struct TopCellInfo
            {
                public CellInfo info;
                public CellVertices verts;
                public IndexSpan tris;
            }

            public struct CellInfo
            {
                public byte config;
                public float cornerDist;
                public float rightDist;
                public float topDist;

                public CellInfo(byte conf, float corner, float right, float top)
                {
                    config = conf;
                    cornerDist = corner;
                    rightDist = right;
                    topDist = top;
                }
            }

            public struct CellVertices
            {   
                public int corner;
                public int bottomEdge;
                public int leftEdge;

                public CellVertices(int c, int bottom, int left)
                {
                    corner = c;
                    bottomEdge = bottom;
                    leftEdge = left;
                }
            }

            public struct IndexSpan
            {
                public int start;
                public byte length;

                public IndexSpan(int s, byte l)
                {
                    start = s;
                    length = l;
                }
            }

            public enum NormalMode { UpFlat, DownFlat, UpDontSetNormal, DownDontSetNormal }
            static public bool IsFlat(NormalMode mode) => mode == NormalMode.UpFlat || mode == NormalMode.DownFlat; 
            static public bool IsUp(NormalMode mode) => mode == NormalMode.UpFlat || mode == NormalMode.UpDontSetNormal; 
            static public bool IsDown(NormalMode mode) => mode == NormalMode.DownFlat || mode == NormalMode.DownDontSetNormal;
            static public NormalMode SelectUp(bool isFlat) => isFlat ? NormalMode.UpFlat : NormalMode.UpDontSetNormal;
            static public NormalMode SelectDown(bool isFlat) => isFlat ? NormalMode.DownFlat : NormalMode.DownDontSetNormal;

            private float3 normal;
            private NormalMode normalMode;
            private FullVertexCalculator vertexCalculator;

            public TopCellMesher(float heightOffset, NormalMode normalMode, float lerpToEdge)
            {
                if (IsDown(normalMode))
                {
                    Debug.LogWarning("invalid normal mode, use the BottomCellMesher for upside down mesh!");
                }

                this.normalMode = normalMode;
                normal = new float3(0, 1, 0);
                vertexCalculator = new FullVertexCalculator { heightOffset = heightOffset, lerpToEdge = lerpToEdge };
            }

            public TopCellInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                            ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
                => GenerateInfoSimple(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, hasCellTriangles);

            static public TopCellInfo GenerateInfoSimple(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                            ref int nextVertex, ref int nextTriIndex, bool hasCellTriangles)
            {
                byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);
                TopCellInfo info = new TopCellInfo
                {
                    info = new CellInfo(config, cornerDistance, rightDistance, topDistance),
                    verts = new CellVertices(-1, -1, -1),
                    tris = new IndexSpan(nextTriIndex, 0)
                };

                if (hasCellTriangles)
                {
                    info.tris.length = CalcTriIndexCount(config);
                    nextTriIndex += info.tris.length;
                }

                SetVertices(config, ref nextVertex, ref info.verts);

                return info;
            }

            static public void SetVertices(byte config, ref int nextVertex, ref CellVertices verts)
            {
                bool hasBL = HasMask(config, MaskBL);
                if (hasBL)
                {
                    verts.corner = nextVertex;
                    ++nextVertex;
                }

                if (hasBL != HasMask(config, MaskTL))
                {
                    verts.leftEdge = nextVertex;
                    ++nextVertex;
                }

                if (hasBL != HasMask(config, MaskBR))
                {
                    verts.bottomEdge = nextVertex;
                    ++nextVertex;
                }
            }

            public void CalculateVertices(int x, int y, float cellSize, TopCellInfo info, float height, NativeArray<float3> vertices)
                => vertexCalculator.CalculateVertices(x, y, cellSize, info, height, vertices);

            static public void CalculateVerticesSimple(int x, int y, float cellSize, TopCellInfo cell, float height, NativeArray<float3> vertices, float heightOffset = 0)
            {
                float3 pos = new float3(x * cellSize, height + heightOffset, y * cellSize);

                if (cell.verts.corner >= 0) { vertices[cell.verts.corner] = pos; }
                if (cell.verts.leftEdge >= 0) { vertices[cell.verts.leftEdge] = pos + new float3(0, 0, cellSize * VcLerpT(cell.info)); }
                if (cell.verts.bottomEdge >= 0) { vertices[cell.verts.bottomEdge] = pos + new float3(cellSize * HzLerpT(cell.info), 0, 0); }
            }

            static public void CalculateVerticesFull(int x, int y, float cellSize, CellVertices verts, CellInfo info, float height, NativeArray<float3> vertices, float heightOffset, float edgeLerp)
            {
                float3 pos = new float3(x * cellSize, heightOffset + height, y * cellSize);

                if (verts.corner >= 0) { vertices[verts.corner] = pos; }
                if (verts.leftEdge >= 0) { vertices[verts.leftEdge] = pos + new float3(0, 0, cellSize * VcLerpT(info, edgeLerp)); }
                if (verts.bottomEdge >= 0) { vertices[verts.bottomEdge] = pos + new float3(cellSize * HzLerpT(info, edgeLerp), 0, 0); }
            }

            public interface IVertexCalculator
            {
                void CalculateVertices(int x, int y, float cellSize, TopCellInfo info, float height, NativeArray<float3> vertices);
            }

            public struct SimpleVertexCalculator : IVertexCalculator
            {
                public float heightOffset;
                public void CalculateVertices(int x, int y, float cellSize, TopCellInfo info, float height, NativeArray<float3> vertices)
                    => CalculateVerticesSimple(x, y, cellSize, info, height, vertices, heightOffset);
            }

            public struct FullVertexCalculator : IVertexCalculator
            {
                public float heightOffset;
                public float lerpToEdge;
                public void CalculateVertices(int x, int y, float cellSize, TopCellInfo info, float height, NativeArray<float3> vertices)
                    => CalculateVerticesFull(x, y, cellSize, info.verts, info.info, height, vertices, heightOffset, lerpToEdge);
            }

            public void CalculateIndices(TopCellInfo bl, TopCellInfo br, TopCellInfo tr, TopCellInfo tl, NativeArray<int> triangles)
                => CalculateIndicesSimple<TriangleOrderer>(bl.info.config, bl.tris, bl.verts, br.verts, tr.verts, tl.verts, triangles);

            public bool NeedUpdateInfo => false;

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref TopCellInfo cell, ref TopCellInfo top, ref TopCellInfo right)
            {
                // do nothing
            }

            static public float HzLerpT(CellInfo info) => LerpT(info.cornerDist, info.rightDist);
            static public float HzLerpT(CellInfo info, float lerpToDist) => LerpT(info.cornerDist, info.rightDist, lerpToDist);

            static public float VcLerpT(CellInfo info) => LerpT(info.cornerDist, info.topDist);
            static public float VcLerpT(CellInfo info, float lerpToDist) => LerpT(info.cornerDist, info.topDist, lerpToDist);

            static public float LerpT(float a, float b) => math.abs(a) / (math.abs(a) + math.abs(b));
            static public float LerpT(float a, float b, float lerpToDist) => math.lerp(0.5f, math.abs(a) / (math.abs(a) + math.abs(b)), lerpToDist);

            public static byte CalcTriIndexCount(byte config)
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

            public static void CalculateIndicesSimple(byte config, IndexSpan tris, CellVertices bl, CellVertices br, CellVertices tr, CellVertices tl, NativeArray<int> triangles)
                => CalculateIndicesSimple<TriangleOrderer>(config, tris, bl, br, tr, tl, triangles);

            public static void CalculateIndicesReverse(byte config, IndexSpan tris, CellVertices bl, CellVertices br, CellVertices tr, CellVertices tl, NativeArray<int> triangles)
                => CalculateIndicesSimple<TriangleReverseOrderer>(config, tris, bl, br, tr, tl, triangles);

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

            public static void CalculateIndicesSimple<TriangleOrderer>(byte config, IndexSpan tris, CellVertices bl, CellVertices br, CellVertices tr, CellVertices tl, NativeArray<int> triangles)
                where TriangleOrderer : struct, ITriangleOrderer
            {
                if (tris.length == 0)
                {
                    return;
                }

                TriangleOrderer orderer = new TriangleOrderer();
                int triangleIndex = tris.start;
                switch (config)
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

            private static int Vertex(CellVertices verts) => verts.corner;
            private static int LeftEdge(CellVertices verts) => verts.leftEdge;
            private static int BottomEdge(CellVertices verts) => verts.bottomEdge;

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

            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, TopCellInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
                => TopCalculateUvs(x, y, cellColNum, cellRowNum, cellSize, corner.verts, uvScale, vertices, uvs);

            public bool CanGenerateNormals => normalMode == NormalMode.UpFlat;

            public void CalculateNormals(TopCellInfo corner, TopCellInfo right, TopCellInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
                => SetNormals(corner.verts, normals, normal);

            static public void TopCalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, CellVertices verts, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
            {
                float2 topRight = new float2((cellColNum + 1) * cellSize * uvScale, (cellRowNum + 1) * cellSize * uvScale);
                float2 uv;
                if (verts.corner >= 0)
                {
                    uv.x = vertices[verts.corner].x / topRight.x;
                    uv.y = vertices[verts.corner].z / topRight.y;
                    uvs[verts.corner] = uv;
                }
                if (verts.leftEdge >= 0)
                {
                    uv.x = vertices[verts.leftEdge].x / topRight.x;
                    uv.y = vertices[verts.leftEdge].z / topRight.y;
                    uvs[verts.leftEdge] = uv;
                }
                if (verts.bottomEdge >= 0)
                {
                    uv.x = vertices[verts.bottomEdge].x / topRight.x;
                    uv.y = vertices[verts.bottomEdge].z / topRight.y;
                    uvs[verts.bottomEdge] = uv;
                }
            }

            static public void SetNormals(CellVertices verts, NativeArray<float3> normals, float3 normal)
            {
                if (verts.corner >= 0) { normals[verts.corner] = normal; }
                if (verts.leftEdge >= 0) { normals[verts.leftEdge] = normal; }
                if (verts.bottomEdge >= 0) { normals[verts.bottomEdge] = normal; }
            }
        }
    }
}
