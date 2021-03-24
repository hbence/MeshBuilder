using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MeshBuilder
{
    using TopCellInfo = MarchingSquaresMesher.TopCellMesher.TopCellInfo;
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
                public TopCellInfo cornerInfo;
                public EdgeNormals normals;
            }

            public struct EdgeNormals
            {
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
                => UpdateNormals(cell.cornerInfo.info, top.cornerInfo.info, right.cornerInfo.info, ref cell.normals, ref top.normals, ref right.normals, lerpToEdge);

            public static void UpdateNormals(CellInfo cell, CellInfo top, CellInfo right, ref EdgeNormals cellNormal, ref EdgeNormals topNormal, ref EdgeNormals rightNormal, float lerpToEdge)
            {
                switch (cell.config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL: break;
                    // corners
                    case MaskBL: AddNormal(0, LerpVc(cell, lerpToEdge), LerpHz(cell, lerpToEdge), 0, ref cellNormal.bottomEdgeDir, ref cellNormal.leftEdgeDir); break;
                    case MaskBR: AddNormal(LerpHz(cell, lerpToEdge), 0, 1, LerpVc(right, lerpToEdge), ref cellNormal.bottomEdgeDir, ref rightNormal.leftEdgeDir); break;
                    case MaskTR: AddNormal(1, LerpVc(right, lerpToEdge), LerpHz(top, lerpToEdge), 1, ref rightNormal.leftEdgeDir, ref topNormal.bottomEdgeDir); break;
                    case MaskTL: AddNormal(LerpHz(top, lerpToEdge), 1, 0, LerpVc(cell, lerpToEdge), ref topNormal.bottomEdgeDir, ref cellNormal.leftEdgeDir); break;
                    // halves
                    case MaskBL | MaskBR: AddNormal(0, LerpVc(cell, lerpToEdge), 1, LerpVc(right, lerpToEdge), ref cellNormal.leftEdgeDir, ref rightNormal.leftEdgeDir); break;
                    case MaskTL | MaskTR: AddNormal(1, LerpVc(right, lerpToEdge), 0, LerpVc(cell, lerpToEdge), ref cellNormal.leftEdgeDir, ref rightNormal.leftEdgeDir); break;
                    case MaskBL | MaskTL: AddNormal(LerpHz(top, lerpToEdge), 1, LerpHz(cell, lerpToEdge), 0, ref cellNormal.bottomEdgeDir, ref topNormal.bottomEdgeDir); break;
                    case MaskBR | MaskTR: AddNormal(LerpHz(cell, lerpToEdge), 0, LerpHz(top, lerpToEdge), 1, ref cellNormal.bottomEdgeDir, ref topNormal.bottomEdgeDir); break;
                    // diagonals
                    case MaskBL | MaskTR:
                        {
                            AddNormal(0, LerpVc(cell, lerpToEdge), LerpHz(top, lerpToEdge), 1, ref cellNormal.leftEdgeDir, ref topNormal.bottomEdgeDir);
                            AddNormal(1, LerpVc(right, lerpToEdge), LerpHz(cell, lerpToEdge), 0, ref rightNormal.leftEdgeDir, ref cellNormal.bottomEdgeDir);
                            break;
                        }
                    case MaskTL | MaskBR:
                        {
                            AddNormal(LerpHz(top, lerpToEdge), 1, 1, LerpVc(right, lerpToEdge), ref topNormal.bottomEdgeDir, ref rightNormal.leftEdgeDir);
                            AddNormal(LerpHz(cell, lerpToEdge), 0, 0, LerpVc(cell, lerpToEdge), ref cellNormal.leftEdgeDir, ref cellNormal.bottomEdgeDir);
                            break;
                        }
                    // three quarters
                    case MaskBL | MaskTR | MaskBR: AddNormal(0, LerpVc(cell, lerpToEdge), LerpHz(top, lerpToEdge), 1, ref cellNormal.leftEdgeDir, ref topNormal.bottomEdgeDir); break;
                    case MaskBL | MaskTL | MaskBR: AddNormal(LerpHz(top, lerpToEdge), 1, 1, LerpVc(right, lerpToEdge), ref topNormal.bottomEdgeDir, ref rightNormal.leftEdgeDir); break;
                    case MaskBL | MaskTL | MaskTR: AddNormal(1, LerpVc(right, lerpToEdge), LerpHz(cell, lerpToEdge), 0, ref rightNormal.leftEdgeDir, ref cellNormal.bottomEdgeDir); break;
                    case MaskTL | MaskTR | MaskBR: AddNormal(LerpHz(cell, lerpToEdge), 0, 0, LerpVc(cell, lerpToEdge), ref cellNormal.leftEdgeDir, ref cellNormal.bottomEdgeDir); break;
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
                => CalculateVertices(x, y, cellSize, info.cornerInfo.verts, info.cornerInfo.info, info.normals, height + heightOffset, vertices, lerpToEdge, sideOffsetScale);

            static public void CalculateVertices(int x, int y, float cellSize, CellVertices verts, CellInfo info, EdgeNormals normals, float height, NativeArray<float3> vertices, float lerpToEdge, float sideOffsetScale)
            {
                float3 pos = new float3(x * cellSize, height, y * cellSize);

                if (verts.corner >= 0)
                {
                    vertices[verts.corner] = pos;
                }

                if (verts.leftEdge >= 0)
                {
                    float edgeLerp = cellSize * VcLerpT(info, lerpToEdge);
                    float2 offset = math.normalize(normals.leftEdgeDir) * sideOffsetScale;
                    vertices[verts.leftEdge] = pos + new float3(offset.x, 0, edgeLerp + offset.y);
                }

                if (verts.bottomEdge >= 0)
                {
                    float edgeLerp = cellSize * HzLerpT(info, lerpToEdge);
                    float2 offset = math.normalize(normals.bottomEdgeDir) * sideOffsetScale;
                    vertices[verts.bottomEdge] = pos + new float3(edgeLerp + offset.x, 0, offset.y);
                }
            }

            public void CalculateIndices(CornerInfoWithNormals bl, CornerInfoWithNormals br, CornerInfoWithNormals tr, CornerInfoWithNormals tl, NativeArray<int> triangles)
               => CalculateIndicesSimple(bl.cornerInfo.info.config, bl.cornerInfo.tris, bl.cornerInfo.verts, br.cornerInfo.verts, tr.cornerInfo.verts, tl.cornerInfo.verts, triangles);

            public bool CanGenerateUvs => true;

            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, CornerInfoWithNormals corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
                => TopCalculateUvs(x, y, cellColNum, cellRowNum, cellSize, corner.cornerInfo.verts, uvScale, vertices, uvs);

            public bool CanGenerateNormals { get; private set; } 

            public void CalculateNormals(CornerInfoWithNormals corner, CornerInfoWithNormals right, CornerInfoWithNormals top, NativeArray<float3> vertices, NativeArray<float3> normals)
                => SetNormals(corner.cornerInfo.verts, normals, normal);

            private const float Epsilon = math.EPSILON;
            private static float LerpVc(CellInfo cornerInfo, float lerpToEdge) => LerpT(cornerInfo.cornerDist + Epsilon, cornerInfo.topDist + Epsilon, lerpToEdge);
            private static float LerpHz(CellInfo cornerInfo, float lerpToEdge) => LerpT(cornerInfo.cornerDist + Epsilon, cornerInfo.rightDist + Epsilon, lerpToEdge);
        }
    }
}
