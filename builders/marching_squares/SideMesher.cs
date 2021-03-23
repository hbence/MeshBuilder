using Unity.Collections;
using Unity.Mathematics;
using UnityEditor.Experimental.GraphView;

namespace MeshBuilder
{
    using CornerInfoWithNormals = MarchingSquaresMesher.ScalableTopCellMesher.CornerInfoWithNormals;

    using CellInfo = MarchingSquaresMesher.TopCellMesher.CellInfo;
    using CellVerts = MarchingSquaresMesher.TopCellMesher.CellVertices;
    using IndexSpan = MarchingSquaresMesher.TopCellMesher.IndexSpan;
    using EdgeNormals = MarchingSquaresMesher.ScalableTopCellMesher.EdgeNormals;

    public partial class MarchingSquaresMesher : Builder
    {
        public struct SimpleSideMesher : ICellMesher<SimpleSideMesher.SideInfo>
        {
            public struct SideInfo
            {
                public CellInfo info;
                public CellVerts top;
                public CellVerts bottom;
                public IndexSpan tris;
            }

            public float height;
            public float lerpToEdge;

            public SimpleSideMesher(float height, float lerpToEdge)
            {
                this.height = height;
                this.lerpToEdge = lerpToEdge;
            }

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
                    info.tris.start = nextTriIndex;
                    info.tris.length = SideCalcTriIndexCount(config);

                    nextTriIndex += info.tris.length;
                }

                return info;
            }

            public void CalculateVertices(int x, int y, float cellSize, SideInfo info, float vertexHeight, NativeArray<float3> vertices)
            {
                TopCellMesher.CalculateVerticesFull(x, y, cellSize, info.top, info.info, vertexHeight, vertices, height * 0.5f, lerpToEdge);
                TopCellMesher.CalculateVerticesFull(x, y, cellSize, info.bottom, info.info, 0, vertices, -height * 0.5f, lerpToEdge);
            }

            public void CalculateIndices(SideInfo bl, SideInfo br, SideInfo tr, SideInfo tl, NativeArray<int> triangles)
                => CalculateSideIndices(bl, br, tr, tl, triangles);

            public bool NeedUpdateInfo { get => false; }

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref SideInfo cell, ref SideInfo top, ref SideInfo right)
            {
                // do nothing
            }

            public static void CalculateSideIndices(SideInfo bl, SideInfo br, SideInfo tr, SideInfo tl, NativeArray<int> triangles)
            {
                if (bl.tris.length == 0)
                {
                    return;
                }

                int triangleIndex = bl.tris.start;
                switch (bl.info.config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL: break;
                    // corners
                    case MaskBL: AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                    case MaskBR: AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
                    case MaskTR: AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                    case MaskTL: AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                    // halves
                    case MaskBL | MaskBR: AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                    case MaskTL | MaskTR: AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                    case MaskBL | MaskTL: AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                    case MaskBR | MaskTR: AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
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
                    case MaskBL | MaskTR | MaskBR: AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                    case MaskBL | MaskTL | MaskBR: AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                    case MaskBL | MaskTL | MaskTR: AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                    case MaskTL | MaskTR | MaskBR: AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
                }
            }

            public static void AddFace(NativeArray<int> triangles, ref int nextIndex, int bl, int tl, int tr, int br)
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

            public static byte SideCalcTriIndexCount(byte config)
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

            private static int TopLeftEdge(SideInfo info) => info.top.leftEdge;
            private static int BottomLeftEdge(SideInfo info) => info.bottom.leftEdge;
            private static int TopBottomEdge(SideInfo info) => info.top.bottomEdge;
            private static int BottomBottomEdge(SideInfo info) => info.bottom.bottomEdge;

            public bool CanGenerateUvs { get => true; }

            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, SideInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
            {
                SetUV(corner.top.corner, 1f, vertices, uvs);
                SetUV(corner.top.leftEdge, 1f, vertices, uvs);
                SetUV(corner.top.bottomEdge, 1f, vertices, uvs);

                SetUV(corner.bottom.corner, 0f, vertices, uvs);
                SetUV(corner.bottom.leftEdge, 0f, vertices, uvs);
                SetUV(corner.bottom.bottomEdge, 0f, vertices, uvs);
            }

            static public void SetUV(int index, float v, NativeArray<float3> vertices, NativeArray<float2> uvs)
            {
                if (index >= 0)
                {
                    float3 vert = vertices[index];
                    float u = vert.x + vert.z;
                    uvs[index] = new float2(u, v);
                }
            }

            public bool CanGenerateNormals { get => true; }

            // NOTE: the normal for this is not quite correct, I would either need a second pass and calculate the normal data like in the ScalableSide
            // (but at that point it probably doesn't make sense to keep them separate)
            // or make the normals array readable and accumulate the normals cell by cell in the CalculateNormals method. (that would probably flicker if I keep the job parallel)
            // The diagonal case is especially off right now
            public void CalculateNormals(SideInfo corner, SideInfo right, SideInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
                => CalculateTriangleNormals(corner, right, top, vertices, normals);

            static public void CalculateTriangleNormals(SideInfo corner, SideInfo right, SideInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
            {
                if (corner.top.corner >= 0) normals[corner.top.corner] = new float3(0, 1, 0);
                if (corner.bottom.corner >= 0) normals[corner.bottom.corner] = new float3(0, -1, 0);

                if (corner.top.leftEdge >= 0)
                {
                    float3 normal = CalcNormalLeft(corner, right, top, vertices);
                    normals[corner.top.leftEdge] = normal;
                    normals[corner.bottom.leftEdge] = normal;
                }

                if (corner.top.bottomEdge >= 0)
                {
                    float3 normal = CalcNormalBottom(corner, right, top, vertices);
                    normals[corner.top.bottomEdge] = normal;
                    normals[corner.bottom.bottomEdge] = normal;
                }
            }

            private static float3 CalcNormalBottom(SideInfo bl, SideInfo br, SideInfo tl, NativeArray<float3> vertices)
            {
                switch (bl.info.config)
                {
                    // corners
                    case MaskBL: return CalcNormal(TopBottomEdge(bl), TopLeftEdge(bl), vertices);
                    case MaskBR: return CalcNormal(TopLeftEdge(br), TopBottomEdge(bl), vertices);
                    case MaskTR: return CalcNormal(TopBottomEdge(tl), TopLeftEdge(br), vertices);
                    case MaskTL: return CalcNormal(TopLeftEdge(bl), TopBottomEdge(tl), vertices);
                    // halves
                    case MaskBL | MaskBR: return CalcNormal(TopLeftEdge(br), TopLeftEdge(bl), vertices);
                    case MaskTL | MaskTR: return CalcNormal(TopLeftEdge(bl), TopLeftEdge(br), vertices);
                    case MaskBL | MaskTL: return CalcNormal(TopBottomEdge(bl), TopBottomEdge(tl), vertices);
                    case MaskBR | MaskTR: return CalcNormal(TopBottomEdge(tl), TopBottomEdge(bl), vertices);
                    // diagonals
                    case MaskBL | MaskTR: return CalcNormal(TopBottomEdge(bl), TopLeftEdge(br), vertices);
                    case MaskTL | MaskBR: return CalcNormal(TopLeftEdge(bl), TopBottomEdge(bl), vertices);
                    // three quarters
                    case MaskBL | MaskTR | MaskBR: return CalcNormal(TopBottomEdge(tl), TopLeftEdge(bl), vertices);
                    case MaskBL | MaskTL | MaskBR: return CalcNormal(TopLeftEdge(br), TopBottomEdge(tl), vertices);
                    case MaskBL | MaskTL | MaskTR: return CalcNormal(TopBottomEdge(bl), TopLeftEdge(br), vertices);
                    case MaskTL | MaskTR | MaskBR: return CalcNormal(TopLeftEdge(bl), TopBottomEdge(bl), vertices);
                }
                return new float3(0, 1, 0);
            }
            private static float3 CalcNormalLeft(SideInfo bl, SideInfo br, SideInfo tl, NativeArray<float3> vertices)
            {
                switch (bl.info.config)
                {
                    // corners
                    case MaskBL: return CalcNormal(TopBottomEdge(bl), TopLeftEdge(bl), vertices);
                    case MaskBR: return CalcNormal(TopLeftEdge(br), TopBottomEdge(bl), vertices);
                    case MaskTR: return CalcNormal(TopBottomEdge(tl), TopLeftEdge(br), vertices);
                    case MaskTL: return CalcNormal(TopLeftEdge(bl), TopBottomEdge(tl), vertices);
                    // halves
                    case MaskBL | MaskBR: return CalcNormal(TopLeftEdge(br), TopLeftEdge(bl), vertices);
                    case MaskTL | MaskTR: return CalcNormal(TopLeftEdge(bl), TopLeftEdge(br), vertices);
                    case MaskBL | MaskTL: return CalcNormal(TopBottomEdge(bl), TopBottomEdge(tl), vertices);
                    case MaskBR | MaskTR: return CalcNormal(TopBottomEdge(tl), TopBottomEdge(bl), vertices);
                    // diagonals
                    case MaskBL | MaskTR: return CalcNormal(TopBottomEdge(tl), TopLeftEdge(bl), vertices);
                    case MaskTL | MaskBR: return CalcNormal(TopLeftEdge(bl), TopBottomEdge(bl), vertices);
                    // three quarters
                    case MaskBL | MaskTR | MaskBR: return CalcNormal(TopBottomEdge(tl), TopLeftEdge(bl), vertices);
                    case MaskBL | MaskTL | MaskBR: return CalcNormal(TopLeftEdge(br), TopBottomEdge(tl), vertices);
                    case MaskBL | MaskTL | MaskTR: return CalcNormal(TopBottomEdge(bl), TopLeftEdge(br), vertices);
                    case MaskTL | MaskTR | MaskBR: return CalcNormal(TopLeftEdge(bl), TopBottomEdge(bl), vertices);
                }
                return new float3(0, 1, 0);
            }

            static private float3 CalcNormal(int aIndex, int bIndex, NativeArray<float3> vertices)
            {
                float3 dir = vertices[bIndex] - vertices[aIndex];
                float3 normal = -math.cross(dir, new float3(0, 1, 0));
                return math.normalize(normal);
            }
        }

        // NOTE: this is almost a copy paste of the SimpleSideMesher, probably I should refactor that a bit, or make it into a generic version
        // (but then that wouldn't really be a 'simple' mesher)
        public struct ScalableSideMesher : ICellMesher<ScalableSideMesher.ScalableSideInfo>
        {
            public struct ScalableSideInfo
            {
                public CellInfo info;
                public CellVerts top;
                public CellVerts bottom;
                public IndexSpan tris;
                public EdgeNormals normals;
            }

            public float lerpToExactEdge;

            public float height;
            public float topOffsetScale;
            public float bottomOffsetScale;

            public ScalableSideMesher(float height, float bottomOffsetScale = 0f, float topOffsetScale = 0.5f, float lerpToExactEdge = 1f)
            {
                this.height = height;
                this.bottomOffsetScale = bottomOffsetScale;
                this.topOffsetScale = topOffsetScale;
                this.lerpToExactEdge = lerpToExactEdge;
            }

            public ScalableSideInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                ref int nextVertex, ref int nextTriIndex, bool hasCellTriangles)
            {
                byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);
                ScalableSideInfo info = new ScalableSideInfo()
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
                    info.tris.start = nextTriIndex;
                    info.tris.length = SimpleSideMesher.SideCalcTriIndexCount(config);

                    nextTriIndex += info.tris.length;
                }

                return info;
            }

            public void CalculateVertices(int x, int y, float cellSize, ScalableSideInfo info, float vertexHeight, NativeArray<float3> vertices)
            {
                float heightOffset = height * 0.5f;
                ScalableTopCellMesher.CalculateVertices(x, y, cellSize, info.top, info.info, info.normals, heightOffset + vertexHeight, vertices, lerpToExactEdge, topOffsetScale);
                ScalableTopCellMesher.CalculateVertices(x, y, cellSize, info.bottom, info.info, info.normals, -heightOffset + vertexHeight, vertices, lerpToExactEdge, bottomOffsetScale);
            }

            public bool NeedUpdateInfo { get => true; }

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref ScalableSideInfo cell, ref ScalableSideInfo top, ref ScalableSideInfo right)
            {
                ScalableTopCellMesher.UpdateNormals(cell.info, top.info, right.info, ref cell.normals, ref top.normals, ref right.normals, lerpToExactEdge);
            }

            public bool CanGenerateUvs { get => false; }

            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, ScalableSideInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
            {
                SimpleSideMesher.SetUV(corner.top.corner, 1f, vertices, uvs);
                SimpleSideMesher.SetUV(corner.top.leftEdge, 1f, vertices, uvs);
                SimpleSideMesher.SetUV(corner.top.bottomEdge, 1f, vertices, uvs);

                SetUVFromDifferentVertex(corner.top.corner, corner.bottom.corner, 0f, vertices, uvs);
                SetUVFromDifferentVertex(corner.top.leftEdge, corner.bottom.leftEdge, 0f, vertices, uvs);
                SetUVFromDifferentVertex(corner.top.bottomEdge, corner.bottom.bottomEdge, 0f, vertices, uvs);
            }

            public bool CanGenerateNormals { get => true; }

            public void CalculateNormals(ScalableSideInfo corner, ScalableSideInfo right, ScalableSideInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
            {
                var topInfo = corner.top;
                var bottomInfo = corner.bottom;

                if (topInfo.corner >= 0) { normals[topInfo.corner] = new float3(0, 1, 0); }
                if (bottomInfo.corner >= 0) { normals[bottomInfo.corner] = new float3(0, 1, 0); }
                
                if (topInfo.leftEdge >= 0)
                {
                    float3 normal = CalculateNormal(topInfo.leftEdge, bottomInfo.leftEdge, vertices);
                    normal = math.normalize(normal);
                    normals[topInfo.leftEdge] = normal;
                    normals[bottomInfo.leftEdge] = normal;
                }

                if (topInfo.bottomEdge >= 0)
                {
                    float3 normal = CalculateNormal(topInfo.bottomEdge, bottomInfo.bottomEdge, vertices);
                    normal = math.normalize(normal);
                    normals[topInfo.bottomEdge] = normal;
                    normals[bottomInfo.bottomEdge] = normal;
                }
            }

            static private float3 CalculateNormal(int aIndex, int bIndex, NativeArray<float3> vertices)
            {
                float3 dir = vertices[bIndex] - vertices[aIndex];
                float3 right = math.cross(dir, new float3(0, 1, 0));
                return math.cross(right, dir);
            }

            static public void SetUVFromDifferentVertex(int sourceIndex, int targetIndex, float v, NativeArray<float3> vertices, NativeArray<float2> uvs)
            {
                if (sourceIndex >= 0 && targetIndex >= 0)
                {
                    float3 vert = vertices[sourceIndex];
                    float u = vert.x + vert.z;
                    uvs[targetIndex] = new float2(u, v);
                }
            }

            public void CalculateIndices(ScalableSideInfo bl, ScalableSideInfo br, ScalableSideInfo tr, ScalableSideInfo tl, NativeArray<int> triangles)
            {
                if (bl.tris.length == 0) 
                {
                    return; 
                }

                int triangleIndex = bl.tris.start;
                switch (bl.info.config)
                {
                    // full
                    case MaskBL | MaskBR | MaskTR | MaskTL: break;
                    // corners
                    case MaskBL: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                    case MaskBR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
                    case MaskTR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                    case MaskTL: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                    // halves
                    case MaskBL | MaskBR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                    case MaskTL | MaskTR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                    case MaskBL | MaskTL: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                    case MaskBR | MaskTR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
                    // diagonals
                    case MaskBL | MaskTR:
                        {
                            SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(bl), BottomLeftEdge(bl));
                            SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(br), BottomLeftEdge(br));
                            break;
                        }
                    case MaskTL | MaskBR:
                        {
                            SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(tl), BottomBottomEdge(tl));
                            SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(bl), BottomBottomEdge(bl));
                            break;
                        }
                    // three quarters
                    case MaskBL | MaskTR | MaskBR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl), TopBottomEdge(tl), TopLeftEdge(bl), BottomLeftEdge(bl)); break;
                    case MaskBL | MaskTL | MaskBR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(br), TopLeftEdge(br), TopBottomEdge(tl), BottomBottomEdge(tl)); break;
                    case MaskBL | MaskTL | MaskTR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl), TopBottomEdge(bl), TopLeftEdge(br), BottomLeftEdge(br)); break;
                    case MaskTL | MaskTR | MaskBR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl), TopLeftEdge(bl), TopBottomEdge(bl), BottomBottomEdge(bl)); break;
                }
            }

            private static int TopLeftEdge(ScalableSideInfo info) => info.top.leftEdge;
            private static int BottomLeftEdge(ScalableSideInfo info) => info.bottom.leftEdge;
            private static int TopBottomEdge(ScalableSideInfo info) => info.top.bottomEdge;
            private static int BottomBottomEdge(ScalableSideInfo info) => info.bottom.bottomEdge;
        }
    }
}