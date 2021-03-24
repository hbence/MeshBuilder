using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using System.Runtime.InteropServices;

namespace MeshBuilder
{
    using CellInfo = MarchingSquaresMesher.TopCellMesher.CellInfo;
    using CellVerts = MarchingSquaresMesher.TopCellMesher.CellVertices;
    using IndexSpan = MarchingSquaresMesher.TopCellMesher.IndexSpan;
    using EdgeNormals = MarchingSquaresMesher.ScalableTopCellMesher.EdgeNormals;
    using VertsLayers = MarchingSquaresMesher.SegmentedSideMesher.Group8<MarchingSquaresMesher.TopCellMesher.CellVertices>;

    public partial class MarchingSquaresMesher : Builder
    {
        public struct SegmentedSideMesher : ICellMesher<SegmentedSideMesher.SegmentedSideInfo>
        {
            // the structs have to use blittable types, so I'm not sure how to make the size dynamic,
            // perhaps with unsafe array, or the offset and vertex data struct could be generic arguments for this type
            // so the user could provide structs with exactly as much data as required.
            // Or I guess, I could use NativeArrays, small for the offsets and a large one to hold the vertex indices, and
            // the info struct could hold a span into the large array. But then the mesher would require special handling,
            // it would either have to take the arrays in the constructor or handle their lifetime.
            public const int MaxLayerCount = 8;

            public struct SegmentedSideInfo
            {
                public CellInfo info;
                public VertsLayers verts;
                public IndexSpan tris;
                public EdgeNormals normals;
            }

            public float lerpToExactEdge;
            public int layerCount;
            public OffsetInfo offsets;
            public Group8<float> vValues;

            public SegmentedSideMesher(int layerCount, OffsetInfo offsets, float lerpToExactEdge = 1f)
            {
                if (layerCount < 2)
                {
                    layerCount = 2;
                    Debug.LogError("layer count is clamped! needs at least 2!");
                }

                if (layerCount > MaxLayerCount)
                {
                    layerCount = MaxLayerCount;
                    Debug.LogError("layer count is clamped! current max is " + MaxLayerCount);
                }

                this.layerCount = layerCount;
                this.offsets = offsets;
                this.lerpToExactEdge = lerpToExactEdge;

                vValues = new Group8<float>();
                float vDist = 0;
                for (int i = 1; i < layerCount; ++i)
                {
                    vDist += math.abs(offsets.GetVC(i) - offsets.GetVC(i - 1));
                }
                if (vDist > 0)
                {
                    float v = 1;
                    vValues.Set(0, v);
                    for (int i = 1; i < layerCount; ++i)
                    {
                        float delta = math.abs(offsets.GetVC(i) - offsets.GetVC(i - 1));
                        v -= delta / vDist;
                        vValues.Set(i, v);
                    }
                }
            }

            public SegmentedSideInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                ref int nextVertex, ref int nextTriIndex, bool hasCellTriangles)
            {
                byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);
                SegmentedSideInfo info = new SegmentedSideInfo()
                {
                    info = new CellInfo(config, cornerDistance, rightDistance, topDistance),
                    tris = new IndexSpan(0, 0)
                };

                var verts = new CellVerts();
                for (int i = 0; i < layerCount; ++i)
                {
                    verts.Set(-1, -1, -1);
                    TopCellMesher.SetVertices(config, ref nextVertex, ref verts);
                    info.verts.Set(i, verts);
                }
                for (int i = layerCount; i < MaxLayerCount; ++i)
                {
                    info.verts.Set(i, new CellVerts(-1, -1, -1));
                }

                if (hasCellTriangles)
                {
                    info.tris.start = nextTriIndex;
                    info.tris.length = (byte)(SimpleSideMesher.SideCalcTriIndexCount(config) * (layerCount - 1));

                    nextTriIndex += info.tris.length;
                }

                return info;
            }

            public void CalculateVertices(int x, int y, float cellSize, SegmentedSideInfo info, float vertexHeight, NativeArray<float3> vertices)
            {
                CellVerts verts;
                for (int i = 0; i < layerCount; ++i)
                {
                    float hzOffset = offsets.GetHZ(i);
                    float vcOffset = offsets.GetVC(i) + vertexHeight;
                    verts = info.verts.Get(i);
                    ScalableTopCellMesher.CalculateVertices(x, y, cellSize, verts, info.info, info.normals, vcOffset, vertices, lerpToExactEdge, hzOffset);
                }
            }

            public bool NeedUpdateInfo { get => true; }

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref SegmentedSideInfo cell, ref SegmentedSideInfo top, ref SegmentedSideInfo right)
            {
                ScalableTopCellMesher.UpdateNormals(cell.info, top.info, right.info, ref cell.normals, ref top.normals, ref right.normals, lerpToExactEdge);
            }

            public bool CanGenerateUvs { get => true; }

            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, SegmentedSideInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
            {
                CellVerts layer0 = corner.verts.value0;
                if (layer0.corner >= 0)
                {
                    float u = CalcU(layer0.corner, vertices);
                    for (int i = 0; i < layerCount; ++i)
                    {
                        int vertex = corner.verts.Get(i).corner;
                        uvs[vertex] = new float2(u, vValues.Get(i));
                    }
                }
                if (layer0.leftEdge >= 0)
                {
                    float u = CalcU(layer0.leftEdge, vertices);
                    for (int i = 0; i < layerCount; ++i)
                    {
                        int vertex = corner.verts.Get(i).leftEdge;
                        uvs[vertex] = new float2(u, vValues.Get(i));
                    }
                }
                if (layer0.bottomEdge >= 0)
                {
                    float u = CalcU(layer0.bottomEdge, vertices);
                    for (int i = 0; i < layerCount; ++i)
                    {
                        int vertex = corner.verts.Get(i).bottomEdge;
                        uvs[vertex] = new float2(u, vValues.Get(i));
                    }
                }
            }

            static private float CalcU(int index, NativeArray<float3> vertices)
            {
                return vertices[index].x + vertices[index].z;
            }

            public bool CanGenerateNormals { get => true; }

            public void CalculateNormals(SegmentedSideInfo corner, SegmentedSideInfo right, SegmentedSideInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
            {
                float3 normal = new float3(0, 1, 0);
                CellVerts layer0 = corner.verts.value0;
                if (layer0.corner >= 0)
                {
                    for (int i = 0; i < layerCount; ++i)
                    {
                        int index = corner.verts.Get(i).corner;
                        normals[index] = normal;
                    }
                }

                if (layer0.leftEdge >= 0)
                {
                    float2 dir = corner.normals.leftEdgeDir;
                    float3 away = new float3(dir.x, 0, dir.y);
                    int bottomIndex = 0;
                    for (int i = 0; i < layerCount - 1; ++i)
                    {
                        int topIndex = corner.verts.Get(i).leftEdge;
                        bottomIndex = corner.verts.Get(i + 1).leftEdge;
                        normal = CalculateNormal(topIndex, bottomIndex, vertices, away);
                        normal = math.normalize(normal);
                        normals[topIndex] = normal;
                    }
                    normals[bottomIndex] = normal;
                }

                if (layer0.bottomEdge >= 0)
                {
                    float2 dir = corner.normals.bottomEdgeDir;
                    float3 away = new float3(dir.x, 0, dir.y);
                    int bottomIndex = 0;
                    for (int i = 0; i < layerCount - 1; ++i)
                    {
                        int topIndex = corner.verts.Get(i).bottomEdge;
                        bottomIndex = corner.verts.Get(i + 1).bottomEdge;
                        normal = CalculateNormal(topIndex, bottomIndex, vertices, away);
                        normal = math.normalize(normal);
                        normals[topIndex] = normal;
                    }
                   normals[bottomIndex] = normal;
                }
            }

            static private float3 CalculateNormal(int aIndex, int bIndex, NativeArray<float3> vertices, float3 away)
            {
                float3 up = vertices[bIndex] - vertices[aIndex];
                float3 right = math.cross(up, away);
                away = math.cross(right, up);
                return math.normalize(away);
            }

            public void CalculateIndices(SegmentedSideInfo bl, SegmentedSideInfo br, SegmentedSideInfo tr, SegmentedSideInfo tl, NativeArray<int> triangles)
            {
                if (bl.tris.length == 0)
                {
                    return;
                }

                int triangleIndex = bl.tris.start;

                for (int i = 0; i < layerCount - 1; ++i)
                {
                    switch (bl.info.config)
                    {
                        // full
                        case MaskBL | MaskBR | MaskTR | MaskTL: break;
                        // corners
                        case MaskBL: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl, i), TopBottomEdge(bl, i), TopLeftEdge(bl, i), BottomLeftEdge(bl, i)); break;
                        case MaskBR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(br, i), TopLeftEdge(br, i), TopBottomEdge(bl, i), BottomBottomEdge(bl, i)); break;
                        case MaskTR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl, i), TopBottomEdge(tl, i), TopLeftEdge(br, i), BottomLeftEdge(br, i)); break;
                        case MaskTL: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl, i), TopLeftEdge(bl, i), TopBottomEdge(tl, i), BottomBottomEdge(tl, i)); break;
                        // halves
                        case MaskBL | MaskBR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(br, i), TopLeftEdge(br, i), TopLeftEdge(bl, i), BottomLeftEdge(bl, i)); break;
                        case MaskTL | MaskTR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl, i), TopLeftEdge(bl, i), TopLeftEdge(br, i), BottomLeftEdge(br, i)); break;
                        case MaskBL | MaskTL: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl, i), TopBottomEdge(bl, i), TopBottomEdge(tl, i), BottomBottomEdge(tl, i)); break;
                        case MaskBR | MaskTR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl, i), TopBottomEdge(tl, i), TopBottomEdge(bl, i), BottomBottomEdge(bl, i)); break;
                        // diagonals
                        case MaskBL | MaskTR:
                            {
                                SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl, i), TopBottomEdge(tl, i), TopLeftEdge(bl, i), BottomLeftEdge(bl, i));
                                SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl, i), TopBottomEdge(bl, i), TopLeftEdge(br, i), BottomLeftEdge(br, i));
                                break;
                            }
                        case MaskTL | MaskBR:
                            {
                                SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(br, i), TopLeftEdge(br, i), TopBottomEdge(tl, i), BottomBottomEdge(tl, i));
                                SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl, i), TopLeftEdge(bl, i), TopBottomEdge(bl, i), BottomBottomEdge(bl, i));
                                break;
                            }
                        // three quarters
                        case MaskBL | MaskTR | MaskBR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(tl, i), TopBottomEdge(tl, i), TopLeftEdge(bl, i), BottomLeftEdge(bl, i)); break;
                        case MaskBL | MaskTL | MaskBR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(br, i), TopLeftEdge(br, i), TopBottomEdge(tl, i), BottomBottomEdge(tl, i)); break;
                        case MaskBL | MaskTL | MaskTR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomBottomEdge(bl, i), TopBottomEdge(bl, i), TopLeftEdge(br, i), BottomLeftEdge(br, i)); break;
                        case MaskTL | MaskTR | MaskBR: SimpleSideMesher.AddFace(triangles, ref triangleIndex, BottomLeftEdge(bl, i), TopLeftEdge(bl, i), TopBottomEdge(bl, i), BottomBottomEdge(bl, i)); break;
                    }
                }
            }

            private static int TopLeftEdge(SegmentedSideInfo info, int layer) => info.verts.Get(layer).leftEdge;
            private static int BottomLeftEdge(SegmentedSideInfo info, int layer) => info.verts.Get(layer + 1).leftEdge;
            private static int TopBottomEdge(SegmentedSideInfo info, int layer) => info.verts.Get(layer).bottomEdge;
            private static int BottomBottomEdge(SegmentedSideInfo info, int layer) => info.verts.Get(layer + 1).bottomEdge;

            public struct Group8<T> where T : struct
            {
                public T value0, value1, value2, value3, value4, value5, value6, value7;

                public T Get(int index)
                {
                    switch (index)
                    {
                        case 0: return value0;
                        case 1: return value1;
                        case 2: return value2;
                        case 3: return value3;
                        case 4: return value4;
                        case 5: return value5;
                        case 6: return value6;
                        case 7: return value7;
                    }
                    return value0;
                }

                public void Set(int index, T val)
                {
                    switch (index)
                    {
                        case 0: value0 = val; break;
                        case 1: value1 = val; break;
                        case 2: value2 = val; break;
                        case 3: value3 = val; break;
                        case 4: value4 = val; break;
                        case 5: value5 = val; break;
                        case 6: value6 = val; break;
                        case 7: value7 = val; break;
                    }
                }
            }

            public struct OffsetInfo
            {
                public Group8<float> hz;
                public Group8<float> vc;

                public float GetHZ(int index)
                    => hz.Get(index);
                public float GetVC(int index)
                    => vc.Get(index);

                public void Set(int index, float hzVal, float vcVal)
                {
                    hz.Set(index, hzVal);
                    vc.Set(index, vcVal);
                }
            }
        }
    }
}
