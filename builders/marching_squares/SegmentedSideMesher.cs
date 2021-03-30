using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MeshBuilder
{
    using CellInfo = MarchingSquaresMesher.TopCellMesher.CellInfo;
    using IndexSpan = MarchingSquaresMesher.TopCellMesher.IndexSpan;
    using EdgeNormals = MarchingSquaresMesher.ScalableTopCellMesher.EdgeNormals;

    using ValueGroup = MarchingSquaresMesher.SegmentedSideMesher.Group16<float>;

    public partial class MarchingSquaresMesher : Builder
    {
        public struct SegmentedSideMesher : ICellMesher<SegmentedSideMesher.SegmentedSideInfo>
        {
            // NOTE: since this struct is used in a job, it can't have an array or NativeArray as a member,
            // everything has to be blittable. It uses spans for the vertex indices so those don't take up much
            // space at any layerCount. I guess I could make this into a generic struct with the argument type providing
            // the exact amount of data, but it would make the mesher a bit more annoying to use with little gain.
            // Still, if there was a way to store dynamic amounts of offsets and vValues, that would be a cleaner solution.
            public const int MaxLayerCount = ValueGroup.MaxLength;

            public struct SegmentedSideInfo
            {
                public CellInfo info;
                public IndexSpan tris;
                public EdgeNormals normals;

                public IndexSpan bottomVertex;
                public IndexSpan leftVertex;
            }

            public float lerpToExactEdge;
            public int layerCount;
            public OffsetInfo offsets;
            public ValueGroup vValues;

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

                vValues = new ValueGroup();
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

                bool hasBL = HasMask(config, MaskBL);
                
                if (hasBL != HasMask(config, MaskTL))
                {
                    info.leftVertex.start = nextVertex;
                    info.leftVertex.length = (byte)layerCount;
                    nextVertex += layerCount;
                }

                if (hasBL != HasMask(config, MaskBR))
                {
                    info.bottomVertex.start = nextVertex;
                    info.bottomVertex.length = (byte)layerCount;
                    nextVertex += layerCount;
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
                float3 pos = new float3(x * cellSize, vertexHeight, y * cellSize);
                
                if (info.leftVertex.Has)
                {
                    float2 normal = math.normalize(info.normals.leftEdgeDir);
                    float offsetZ = cellSize * TopCellMesher.VcLerpT(info.info, lerpToExactEdge);
                    CalculateVertices(info.leftVertex, pos, normal, 0, offsetZ, offsets, vertices);
                }

                if (info.bottomVertex.Has)
                {
                    float2 normal = math.normalize(info.normals.bottomEdgeDir);
                    float offsetX = cellSize * TopCellMesher.HzLerpT(info.info, lerpToExactEdge);
                    CalculateVertices(info.bottomVertex, pos, normal, offsetX, 0, offsets, vertices);
                }
            }

            static public void CalculateVertices(IndexSpan span, float3 pos, float2 normal, float offsetX, float offsetZ, OffsetInfo offsets, NativeArray<float3> vertices)
            {
                for (int i = 0; i < span.length; ++i)
                {
                    float hzOffset = offsets.GetHZ(i);
                    float vcOffset = offsets.GetVC(i);
                    float2 offset = normal * hzOffset;
                    vertices[span.start + i] = pos + new float3(offset.x + offsetX, vcOffset, offset.y + offsetZ);
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
                if (corner.leftVertex.Has)
                {
                    CalcUV(corner.leftVertex, vValues, vertices, uvs);
                }

                if (corner.bottomVertex.Has)
                {
                    CalcUV(corner.bottomVertex, vValues, vertices, uvs);
                }
            }

            static private void CalcUV(IndexSpan span, ValueGroup vValues, NativeArray<float3> vertices, NativeArray<float2> uvs)
            {
                float u = CalcU(span.start, vertices);
                for (int i = 0; i < span.length; ++i)
                {
                    uvs[span.start + i] = new float2(u, vValues.Get(i));
                }
            }

            static private float CalcU(int index, NativeArray<float3> vertices)
            {
                return vertices[index].x + vertices[index].z;
            }

            public bool CanGenerateNormals { get => true; }

            public void CalculateNormals(SegmentedSideInfo corner, SegmentedSideInfo right, SegmentedSideInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
            {
                if (corner.leftVertex.Has)
                {
                    SetNormals(corner.leftVertex, corner.normals.leftEdgeDir, vertices, normals);
                }

                if (corner.bottomVertex.Has)
                {
                    SetNormals(corner.bottomVertex, corner.normals.bottomEdgeDir, vertices, normals);
                }
            }

            static private void SetNormals(IndexSpan span, float2 dir, NativeArray<float3> vertices, NativeArray<float3> normals)
            {
                float3 away = new float3(dir.x, 0, dir.y);

                float3 normal = new float3(0, 1, 0);
                for (int i = 0; i < span.length - 1; ++i)
                {
                    int topIndex = span.start + i;
                    int bottomIndex = span.start + i + 1;
                    normal = CalculateNormal(topIndex, bottomIndex, vertices, away);
                    normals[topIndex] = normal;
                }
                normals[span.start + span.length - 1] = normal;
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

            private static int TopLeftEdge(SegmentedSideInfo info, int layer) => info.leftVertex.start + layer;
            private static int BottomLeftEdge(SegmentedSideInfo info, int layer) => info.leftVertex.start + layer + 1;
            private static int TopBottomEdge(SegmentedSideInfo info, int layer) => info.bottomVertex.start + layer;
            private static int BottomBottomEdge(SegmentedSideInfo info, int layer) => info.bottomVertex.start + layer + 1;

            public struct Group16<T> where T : struct
            {
                public const int MaxLength = 16;

                public T value0, value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15;

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
                        case 8: return value8;
                        case 9: return value9;
                        case 10: return value10;
                        case 11: return value11;
                        case 12: return value12;
                        case 13: return value13;
                        case 14: return value14;
                        case 15: return value15;
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
                        case 8: value8 = val; break;
                        case 9: value9 = val; break;
                        case 10: value10 = val; break;
                        case 11: value11 = val; break;
                        case 12: value12 = val; break;
                        case 13: value13 = val; break;
                        case 14: value14 = val; break;
                        case 15: value15 = val; break;
                    }
                }
            }

            public struct OffsetInfo
            {
                public ValueGroup hz;
                public ValueGroup vc;

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
