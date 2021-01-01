﻿using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System;

namespace MeshBuilder
{
    public partial class MarchingSquaresMesher : Builder
    {
        /// <summary>
        /// Generates an XZ aligned flat mesh.
        /// </summary>
        public struct SimpleTopCellMesher : ICellMesher<SimpleTopCellMesher.CornerInfo>
        {
            public struct CornerInfo
            {
                // configuration of the cell where this
                // corner is the bottom left
                public byte config;

                // distance of the corner
                public float cornerDist;
                // distance of the right adjacent
                public float rightDist;
                // distance of the top adjacent
                public float topDist;

                public int vertexIndex;
                public int bottomEdgeIndex;
                public int leftEdgeIndex;

                public int triIndexStart;
                public int triIndexLength;
            }

            public float heightOffset;

            public CornerInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
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

            public void CalculateVertices(int x, int y, float cellSize, CornerInfo info, NativeArray<float3> vertices)
            {
                float3 pos = new float3(x * cellSize, heightOffset, y * cellSize);

                if (info.vertexIndex >= 0)
                {
                    vertices[info.vertexIndex] = pos;
                }
                if (info.leftEdgeIndex >= 0)
                {
                    vertices[info.leftEdgeIndex] = pos + new float3(0, 0, cellSize * LerpT(info.cornerDist, info.topDist));
                }
                if (info.bottomEdgeIndex >= 0)
                {
                    vertices[info.bottomEdgeIndex] = pos + new float3(cellSize * LerpT(info.cornerDist, info.rightDist), 0, 0);
                }
            }

            public void CalculateIndices(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
             => CalculateIndicesNormal(bl, br, tr, tl, triangles);

            public bool NeedUpdateInfo { get => false; }

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref CornerInfo cell, ref CornerInfo top, ref CornerInfo right)
            {
                // do nothing
            }

            static private float LerpT(float a, float b) => Mathf.Abs(a) / (Mathf.Abs(a) + Mathf.Abs(b));
                
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

            public static void CalculateIndicesNormal(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
            {
                int triangleIndex = bl.triIndexStart;
                switch (bl.config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), Vertex(tr));
                            AddTri(triangles, ref triangleIndex, Vertex(bl), Vertex(tr), Vertex(br));
                            break;
                        }
                    // corners
                    case MaskBL:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(bl), LeftEdge(bl), BottomEdge(bl));
                            break;
                        }
                    case MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    case MaskTR:
                        {
                            AddTri(triangles, ref triangleIndex, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(tl), BottomEdge(tl), LeftEdge(bl));
                            break;
                        }
                    // halves
                    case MaskBL | MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(bl), LeftEdge(bl), Vertex(br));
                            AddTri(triangles, ref triangleIndex, Vertex(br), LeftEdge(bl), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(tl), Vertex(tr), LeftEdge(bl));
                            AddTri(triangles, ref triangleIndex, LeftEdge(bl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskBL | MaskTL:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(bl), BottomEdge(tl), BottomEdge(bl));
                            AddTri(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), BottomEdge(tl));
                            break;
                        }
                    case MaskBR | MaskTR:
                        {
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), Vertex(tr), Vertex(br));
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), BottomEdge(tl), Vertex(tr));
                            break;
                        }
                    // diagonals
                    case MaskBL | MaskTR:
                        {
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), Vertex(bl), LeftEdge(bl));
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(bl), LeftEdge(br));
                            AddTri(triangles, ref triangleIndex, LeftEdge(br), LeftEdge(bl), BottomEdge(tl));
                            AddTri(triangles, ref triangleIndex, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, LeftEdge(bl), Vertex(tl), BottomEdge(tl));
                            AddTri(triangles, ref triangleIndex, LeftEdge(bl), BottomEdge(tl), BottomEdge(bl));
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), BottomEdge(tl), LeftEdge(br));
                            AddTri(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    // three quarters
                    case MaskBL | MaskTR | MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(br), Vertex(bl), LeftEdge(bl));
                            AddTri(triangles, ref triangleIndex, Vertex(br), LeftEdge(bl), BottomEdge(tl));
                            AddTri(triangles, ref triangleIndex, Vertex(br), BottomEdge(tl), Vertex(tr));
                            break;
                        }
                    case MaskBL | MaskTL | MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), BottomEdge(tl));
                            AddTri(triangles, ref triangleIndex, Vertex(bl), BottomEdge(tl), LeftEdge(br));
                            AddTri(triangles, ref triangleIndex, Vertex(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    case MaskBL | MaskTL | MaskTR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(tl), BottomEdge(bl), Vertex(bl));
                            AddTri(triangles, ref triangleIndex, Vertex(tl), LeftEdge(br), BottomEdge(bl));
                            AddTri(triangles, ref triangleIndex, Vertex(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR | MaskBR:
                        {
                            AddTri(triangles, ref triangleIndex, Vertex(tr), LeftEdge(bl), Vertex(tl));
                            AddTri(triangles, ref triangleIndex, Vertex(tr), BottomEdge(bl), LeftEdge(bl));
                            AddTri(triangles, ref triangleIndex, Vertex(tr), Vertex(br), BottomEdge(bl));
                            break;
                        }
                }
            }

            private static void AddTri(NativeArray<int> triangles, ref int nextIndex, int a, int b, int c)
            {
                triangles[nextIndex] = a;
                ++nextIndex;
                triangles[nextIndex] = b;
                ++nextIndex;
                triangles[nextIndex] = c;
                ++nextIndex;
            }

            // NOTE: burst can't handle function pointers or delegates and I didn't want branching
            // so this is just a copy of the calculate Indices with a different triangle function
            public static void CalculateIndicesReverse(CornerInfo bl, CornerInfo br, CornerInfo tr, CornerInfo tl, NativeArray<int> triangles)
            {
                int triangleIndex = bl.triIndexStart;
                switch (bl.config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL:
                        {
                            AddTriReverse(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), Vertex(tr));
                            AddTriReverse(triangles, ref triangleIndex, Vertex(bl), Vertex(tr), Vertex(br));
                            break;
                        }
                    // corners
                    case MaskBL:
                        {
                            AddTriReverse(triangles, ref triangleIndex, Vertex(bl), LeftEdge(bl), BottomEdge(bl));
                            break;
                        }
                    case MaskBR:
                        {
                            AddTriReverse(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    case MaskTR:
                        {
                            AddTriReverse(triangles, ref triangleIndex, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL:
                        {
                            AddTriReverse(triangles, ref triangleIndex, Vertex(tl), BottomEdge(tl), LeftEdge(bl));
                            break;
                        }
                    // halves
                    case MaskBL | MaskBR:
                        {
                            AddTriReverse(triangles, ref triangleIndex, Vertex(bl), LeftEdge(bl), Vertex(br));
                            AddTriReverse(triangles, ref triangleIndex, Vertex(br), LeftEdge(bl), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR:
                        {
                            AddTriReverse(triangles, ref triangleIndex, Vertex(tl), Vertex(tr), LeftEdge(bl));
                            AddTriReverse(triangles, ref triangleIndex, LeftEdge(bl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskBL | MaskTL:
                        {
                            AddTriReverse(triangles, ref triangleIndex, Vertex(bl), BottomEdge(tl), BottomEdge(bl));
                            AddTriReverse(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), BottomEdge(tl));
                            break;
                        }
                    case MaskBR | MaskTR:
                        {
                            AddTriReverse(triangles, ref triangleIndex, BottomEdge(bl), Vertex(tr), Vertex(br));
                            AddTriReverse(triangles, ref triangleIndex, BottomEdge(bl), BottomEdge(tl), Vertex(tr));
                            break;
                        }
                    // diagonals
                    case MaskBL | MaskTR:
                        {
                            AddTriReverse(triangles, ref triangleIndex, BottomEdge(bl), Vertex(bl), LeftEdge(bl));
                            AddTriReverse(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(bl), LeftEdge(br));
                            AddTriReverse(triangles, ref triangleIndex, LeftEdge(br), LeftEdge(bl), BottomEdge(tl));
                            AddTriReverse(triangles, ref triangleIndex, BottomEdge(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskBR:
                        {
                            AddTriReverse(triangles, ref triangleIndex, LeftEdge(bl), Vertex(tl), BottomEdge(tl));
                            AddTriReverse(triangles, ref triangleIndex, LeftEdge(bl), BottomEdge(tl), BottomEdge(bl));
                            AddTriReverse(triangles, ref triangleIndex, BottomEdge(bl), BottomEdge(tl), LeftEdge(br));
                            AddTriReverse(triangles, ref triangleIndex, BottomEdge(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    // three quarters
                    case MaskBL | MaskTR | MaskBR:
                        {
                            AddTriReverse(triangles, ref triangleIndex, Vertex(br), Vertex(bl), LeftEdge(bl));
                            AddTriReverse(triangles, ref triangleIndex, Vertex(br), LeftEdge(bl), BottomEdge(tl));
                            AddTriReverse(triangles, ref triangleIndex, Vertex(br), BottomEdge(tl), Vertex(tr));
                            break;
                        }
                    case MaskBL | MaskTL | MaskBR:
                        {
                            AddTriReverse(triangles, ref triangleIndex, Vertex(bl), Vertex(tl), BottomEdge(tl));
                            AddTriReverse(triangles, ref triangleIndex, Vertex(bl), BottomEdge(tl), LeftEdge(br));
                            AddTriReverse(triangles, ref triangleIndex, Vertex(bl), LeftEdge(br), Vertex(br));
                            break;
                        }
                    case MaskBL | MaskTL | MaskTR:
                        {
                            AddTriReverse(triangles, ref triangleIndex, Vertex(tl), BottomEdge(bl), Vertex(bl));
                            AddTriReverse(triangles, ref triangleIndex, Vertex(tl), LeftEdge(br), BottomEdge(bl));
                            AddTriReverse(triangles, ref triangleIndex, Vertex(tl), Vertex(tr), LeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskTR | MaskBR:
                        {
                            AddTriReverse(triangles, ref triangleIndex, Vertex(tr), LeftEdge(bl), Vertex(tl));
                            AddTriReverse(triangles, ref triangleIndex, Vertex(tr), BottomEdge(bl), LeftEdge(bl));
                            AddTriReverse(triangles, ref triangleIndex, Vertex(tr), Vertex(br), BottomEdge(bl));
                            break;
                        }
                }
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

            private static int Vertex(CornerInfo info) => info.vertexIndex;
            private static int LeftEdge(CornerInfo info) => info.leftEdgeIndex;
            private static int BottomEdge(CornerInfo info) => info.bottomEdgeIndex;
        }
    }
}
