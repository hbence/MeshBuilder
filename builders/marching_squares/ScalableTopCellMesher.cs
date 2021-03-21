﻿using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MeshBuilder
{
    using CornerInfo = MarchingSquaresMesher.TopCellMesher.CornerInfo;
    using static MarchingSquaresMesher.TopCellMesher;
    public partial class MarchingSquaresMesher : Builder
    {
        /// <summary>
        /// Generates an XZ aligned flat mesh.
        /// </summary>
        public struct ScalableTopCellMesher : ICellMesher<ScalableTopCellMesher.CornerInfoWithNormals>
        {
            public struct CornerInfoWithNormals
            {
                public CornerInfo cornerInfo;
                public float2 leftEdgeDir;
                public float2 bottomEdgeDir;
            }

            private float3 normal;

            public float lerpToEdge;
            public float heightOffset;
            public float sideOffsetScale;

            public ScalableTopCellMesher(float heightOffset, float sideOffsetScale, bool isFlat, float lerpToEdge = 1f)
            {
                CanGenerateNormals = isFlat;
                normal = new float3(0, 1, 0);
                this.lerpToEdge = lerpToEdge;
                this.heightOffset = heightOffset;
                this.sideOffsetScale = sideOffsetScale;
            }

            public CornerInfoWithNormals GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
                => new CornerInfoWithNormals { cornerInfo = GenerateInfoSimple(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertices, ref nextTriIndex, hasCellTriangles) };

            public bool NeedUpdateInfo => true;
            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref CornerInfoWithNormals cell, ref CornerInfoWithNormals top, ref CornerInfoWithNormals right)
            {
                switch (cell.cornerInfo.config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL: break;
                    // corners
                    case MaskBL: AddNormal(0, LerpVc(cell), LerpHz(cell), 0, ref cell.bottomEdgeDir, ref cell.leftEdgeDir); break;
                    case MaskBR: AddNormal(LerpHz(cell), 0, 1, LerpVc(right), ref cell.bottomEdgeDir, ref right.leftEdgeDir); break;
                    case MaskTR: AddNormal(1, LerpVc(right), LerpHz(top), 1, ref right.leftEdgeDir, ref top.bottomEdgeDir); break;
                    case MaskTL: AddNormal(LerpHz(top), 1, 0, LerpVc(cell), ref top.bottomEdgeDir, ref cell.leftEdgeDir); break;
                    // halves
                    case MaskBL | MaskBR: AddNormal(0, LerpVc(cell), 1, LerpVc(right), ref cell.leftEdgeDir, ref right.leftEdgeDir); break;
                    case MaskTL | MaskTR: AddNormal(1, LerpVc(right), 0, LerpVc(cell), ref cell.leftEdgeDir, ref right.leftEdgeDir); break;
                    case MaskBL | MaskTL: AddNormal(LerpHz(top), 1, LerpHz(cell), 0, ref cell.bottomEdgeDir, ref top.bottomEdgeDir); break;
                    case MaskBR | MaskTR: AddNormal(LerpHz(cell), 0, LerpHz(top), 1, ref cell.bottomEdgeDir, ref top.bottomEdgeDir); break;
                    // diagonals
                    case MaskBL | MaskTR:
                        {
                            AddNormal(0, LerpVc(cell), LerpHz(top), 1, ref cell.leftEdgeDir, ref top.bottomEdgeDir);
                            AddNormal(1, LerpVc(right), LerpHz(cell), 0, ref right.leftEdgeDir, ref cell.bottomEdgeDir);
                            break;
                        }
                    case MaskTL | MaskBR:
                        {
                            AddNormal(LerpHz(top), 1, 1, LerpVc(right), ref top.bottomEdgeDir, ref right.leftEdgeDir);
                            AddNormal(LerpHz(cell), 0, 0, LerpVc(cell), ref cell.leftEdgeDir, ref cell.bottomEdgeDir);
                            break;
                        }
                    // three quarters
                    case MaskBL | MaskTR | MaskBR: AddNormal(0, LerpVc(cell), LerpHz(top), 1, ref cell.leftEdgeDir, ref top.bottomEdgeDir); break;
                    case MaskBL | MaskTL | MaskBR: AddNormal(LerpHz(top), 1, 1, LerpVc(right), ref top.bottomEdgeDir, ref right.leftEdgeDir); break;
                    case MaskBL | MaskTL | MaskTR: AddNormal(1, LerpVc(right), LerpHz(cell), 0, ref right.leftEdgeDir, ref cell.bottomEdgeDir); break;
                    case MaskTL | MaskTR | MaskBR: AddNormal(LerpHz(cell), 0, 0, LerpVc(cell), ref cell.leftEdgeDir, ref cell.bottomEdgeDir); break;
                }
            }

            private static void AddNormal(float ax, float ay, float bx, float by, ref float2 edgeDirA, ref float2 edgeDirB)
            {
                float2 dir = new float2(ay - by, bx - ax);
                dir = math.normalize(dir);
                edgeDirA += dir;
                edgeDirB += dir;
            }

            public void CalculateVertices(int x, int y, float cellSize, CornerInfoWithNormals info, float height, NativeArray<float3> vertices)
            {
                float3 pos = new float3(x * cellSize, heightOffset + height, y * cellSize);

                int index = info.cornerInfo.vertexIndex;
                if (index >= 0)
                {
                    float3 v = pos;
                    vertices[index] = v;
                }

                index = info.cornerInfo.leftEdgeIndex;
                if (index >= 0)
                {
                    float edgeLerp = cellSize * LerpT(info.cornerInfo.cornerDist, info.cornerInfo.topDist, lerpToEdge);
                    float2 offset = math.normalize(info.leftEdgeDir) * sideOffsetScale;
                    vertices[index] = pos + new float3(offset.x, 0, edgeLerp + offset.y);
                }

                index = info.cornerInfo.bottomEdgeIndex;
                if (index >= 0)
                {
                    float edgeLerp = cellSize * LerpT(info.cornerInfo.cornerDist, info.cornerInfo.rightDist, lerpToEdge);
                    float2 offset = math.normalize(info.bottomEdgeDir) * sideOffsetScale;
                    vertices[index] = pos + new float3(edgeLerp + offset.x, 0, offset.y);
                }
            }

            public void CalculateIndices(CornerInfoWithNormals bl, CornerInfoWithNormals br, CornerInfoWithNormals tr, CornerInfoWithNormals tl, NativeArray<int> triangles)
                => CalculateIndicesSimple(bl.cornerInfo, br.cornerInfo, tr.cornerInfo, tl.cornerInfo, triangles);

            public bool CanGenerateUvs => true;

            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, CornerInfoWithNormals corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
                => TopCalculateUvs(x, y, cellColNum, cellRowNum, cellSize, corner.cornerInfo, uvScale, vertices, uvs);

            public bool CanGenerateNormals { get; private set; } 

            public void CalculateNormals(CornerInfoWithNormals corner, CornerInfoWithNormals right, CornerInfoWithNormals top, NativeArray<float3> vertices, NativeArray<float3> normals)
                => SetNormals(corner.cornerInfo, normals, normal);

            private const float Epsilon = math.EPSILON;
            private float LerpVc(CornerInfoWithNormals cell) => LerpT(cell.cornerInfo.cornerDist + Epsilon, cell.cornerInfo.topDist + Epsilon, lerpToEdge);
            private float LerpHz(CornerInfoWithNormals cell) => LerpT(cell.cornerInfo.cornerDist + Epsilon, cell.cornerInfo.rightDist + Epsilon, lerpToEdge);
        }
    }
}
