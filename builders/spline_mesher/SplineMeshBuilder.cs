using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    /// <summary>
    /// Manages generating a Mesh from a Spline and CrossSection. Optionally it can use 
    /// unity Transforms at affect the mesh generation (scaling and rotation).
    /// </summary>
    public class SplineMeshBuilder
    {
        private Builder meshBuilder;
        private TransformLookupTable transformLookup;

        public Vector2 UVScale { get; set; } = new Vector2(1, 1);

        // in
        public Spline Spline { get; set; }
        public CrossSectionData CrossSection { get; set; }
        public Transform[] ControlTransforms { get; set; }

        // out
        public Mesh Mesh { get; private set; }

        public SplineMeshBuilder(Spline spline, CrossSectionData crossSection, Transform[] controlTransforms = null)
        {
            Spline = spline;
            CrossSection = crossSection;
            ControlTransforms = controlTransforms;

            meshBuilder = new Builder();
            Mesh = new Mesh();

            transformLookup = new TransformLookupTable();
        }

        public void Rebuild()
        {
            transformLookup.Recalculate(Spline, ControlTransforms);

            meshBuilder.UVScale = new Vector2(UVScale.x, transformLookup.MaxDistance * UVScale.y);

            meshBuilder.IsClosed = Spline.IsClosed;
            meshBuilder.BuildIntoMesh(Mesh, transformLookup, CrossSection);
        }

        /// <summary>
        /// For storing transformation data for spline positions.
        /// </summary>
        [System.Serializable]
        private struct PointTransform
        {
            private static Vector3 One = new Vector3(1, 1, 1);

            [SerializeField] private Vector3 position;
            public Vector3 Position { get => position; }
            [SerializeField] private Quaternion rotation;
            public Quaternion Rotation { get => rotation; }
            [SerializeField] private Vector3 scaling;
            public Vector3 Scaling { get => scaling; }

            public PointTransform(Vector3 pos, Quaternion rot)
            {
                position = pos;
                rotation = rot;
                scaling = One;
            }

            public PointTransform(Vector3 pos, Quaternion rot, Vector3 scale)
            {
                position = pos;
                rotation = rot;
                scaling = scale;
            }
        }

        /// <summary>
        /// Class to cache transformation data for the spline.
        /// </summary>
        private class TransformLookupTable : DistanceLookupTable<PointTransform>
        {
            public void Recalculate(Spline spline, Transform[] controlPointTransforms)
            {
                var positionLookup = spline.LookupTable;
                if (positionLookup.ElemCount == 0)
                {
                    Clear();
                    return;
                }
                else
                {
                    Resize(positionLookup.ElemCount);
                    int lastIndex = positionLookup.ElemCount - 1;

                    if (controlPointTransforms != null)
                    {
                        int lookupCount = spline.LookupPerSegment;
                        int segmentCount = positionLookup.ElemCount / lookupCount;
                        for (int seg = 0; seg < segmentCount; ++seg)
                        {
                            float startDistance = positionLookup.GetElemDistance(seg * lookupCount);
                            Vector3 startScale = GetTransformScale(seg, controlPointTransforms);
                            Vector3 startUp = GetTransformUp(seg, controlPointTransforms);

                            float endDistance = positionLookup.GetElemDistance(((seg + 1) * lookupCount) % positionLookup.ElemCount);
                            Vector3 endScale = GetTransformScale(seg + 1, controlPointTransforms);
                            Vector3 endUp = GetTransformUp(seg + 1, controlPointTransforms);

                            for (int lookupInd = 0; lookupInd < spline.LookupPerSegment; ++lookupInd)
                            {
                                int index = seg * lookupCount + lookupInd;

                                Vector3 cur = positionLookup.GetElemData(index);
                                Vector3 next = positionLookup.GetElemData(index + 1);
                                Vector3 forward = next - cur;

                                float elemDist = positionLookup.GetElemDistance(index);

                                Vector3 up = CalcVectorAt(elemDist, startDistance, startUp, endDistance, endUp);
                                Quaternion rotation = Quaternion.LookRotation(forward, up);
                                Vector3 scale = CalcVectorAt(elemDist, startDistance, startScale, endDistance, endScale);

                                SetElem(index, new PointTransform(cur, rotation, scale), elemDist);
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < lastIndex; ++i)
                        {
                            Vector3 cur = positionLookup.GetElemData(i);
                            Vector3 next = positionLookup.GetElemData(i + 1);
                            Vector3 forward = next - cur;
                            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
                            SetElem(i, new PointTransform(cur, rotation), positionLookup.GetElemDistance(i));
                        }
                    }

                    float lastDistance = positionLookup.GetElemDistance(lastIndex);
                    if (spline.IsClosed)
                    {
                        SetElem(lastIndex, GetElemData(0), lastDistance);
                    }
                    else
                    {
                        Vector3 last = positionLookup.GetElemData(lastIndex);
                        Quaternion lastRotation = Elems[lastIndex - 1].Data.Rotation;
                        SetElem(lastIndex, new PointTransform(last, lastRotation), lastDistance);
                    }
                }
            }

            private Vector3 GetTransformScale(int index, Transform[] transforms)
            {
                if (index < transforms.Length)
                {
                    return transforms[index].localScale;
                }
                else if (index == transforms.Length)
                {
                    return transforms[0].localScale;
                }
                return Vector3.one;
            }

            private Vector3 GetTransformUp(int index, Transform[] transforms)
            {
                if (index < transforms.Length)
                {
                    return transforms[index].up;
                }
                else if (index == transforms.Length)
                {
                    return transforms[0].up;
                }
                return Vector3.up;
            }

            static private float CalcRatio(float dist, float start, float end)
            {
                return (dist - start) / (end - start);
            }

            static private Vector3 CalcVectorAt(float distance, float start, Vector3 startScale, float end, Vector3 endScale)
            {
                return Vector3.Lerp(startScale, endScale, CalcRatio(distance, start, end));
            }

            protected override PointTransform Lerp(PointTransform a, PointTransform b, float t)
            {
                return new PointTransform(Vector3.Lerp(a.Position, b.Position, t), Quaternion.Lerp(a.Rotation, b.Rotation, t), Vector3.Lerp(a.Scaling, b.Scaling, t));
            }
        }

        /// <summary>
        /// Class for actually generating the mesh based on the spline and cross section.
        /// </summary>
        private class Builder
        {
            private const float MinStepDistance = 0.001f;
            private const int QuadIndexLength = 6;

            private class BuildTempData
            {
                public int SliceCount { get; set; }
                public int QuadCount { get; set; }
                public int SliceIndexLength { get; set; }
                public CrossSectionData CrossSectionData { get; set; }
                public TransformLookupTable TransformLookup { get; set; }
            }

            // input settings
            private float stepDistance = 0.2f;
            public float StepDistance { get => stepDistance; set { stepDistance = Mathf.Max(MinStepDistance, value); } }
            public bool ShouldGenerateUV { get; set; } = true;
            public Vector2 UVScale { get; set; } = Vector2.one;
            public bool IsClosed { get; set; } = false;

            public void BuildIntoMesh(Mesh mesh, TransformLookupTable transformLookup, CrossSectionData crossSection, MeshData.UpdateMode updateMode = MeshData.UpdateMode.Clear)
            {
                using (MeshData meshData = BuildMeshData(transformLookup, crossSection, Allocator.Temp))
                {
                    if (meshData.HasVertices)
                    {
                        meshData.UpdateMesh(mesh, updateMode);
                    }
                }
            }

            public MeshData BuildMeshData(TransformLookupTable transformLookup, CrossSectionData crossSection, Allocator allocator = Allocator.Persistent)
            {
                float splineLength = transformLookup.MaxDistance;
                if (splineLength > 0 && crossSection.Length > 0)
                {
                    BuildTempData temp = new BuildTempData();
                    temp.SliceCount = Mathf.Max(2, Mathf.CeilToInt(splineLength / StepDistance) + 1);
                    temp.QuadCount = crossSection.Length - 1;
                    temp.SliceIndexLength = QuadIndexLength * temp.QuadCount;
                    temp.TransformLookup = transformLookup;
                    temp.CrossSectionData = crossSection;

                    int vertexCount = temp.SliceCount * crossSection.Length;
                    int indexCount = (temp.SliceCount - 1) * temp.SliceIndexLength;

                    MeshData meshData = new MeshData(vertexCount, indexCount, allocator, (uint)(MeshData.Buffer.Vertex | MeshData.Buffer.Triangle | MeshData.Buffer.UV));

                    if (ShouldGenerateUV)
                    {
                        float[] uvs = SelectUCoordinates(crossSection);
                        GenerateDataWithUV(meshData, temp, uvs, 0, temp.SliceCount - 1, 0, StepDistance, UVScale);
                    }
                    else
                    {
                        GenerateData(meshData, temp, 0, temp.SliceCount - 1, 0, StepDistance);
                    }

                    return meshData;
                }
                else
                {
                    if (splineLength <= 0) { Debug.LogWarning("can't generate mesh with 0 length spline!"); }
                    if (crossSection.Length == 0) { Debug.LogWarning("can't generate mesh with 0 length cross section!"); }
                }

                return default;
            }

            private float[] SelectUCoordinates(CrossSectionData crossSection)
            {
                float[] uvs = crossSection.SliceUCoords;
                if (uvs == null)
                {
                    uvs = CrossSectionData.CalculateU(crossSection.Slice);
                }
                else if (uvs.Length != crossSection.Slice.Length)
                {
                    Debug.LogWarning("Cross section U coordinate count doesn't match position count! Calculating new U coordinates.");
                    uvs = CrossSectionData.CalculateU(crossSection.Slice);
                }

                return uvs;
            }

            static private void GenerateData(MeshData meshData, BuildTempData buildData, int fromSlice, int toSlice, float fromDist, float stepDist)
            {
                float dist = fromDist;

                int transformLookupIndex = 0;
                SetVertices(meshData, buildData, fromSlice, dist, ref transformLookupIndex);
                SetIndices(meshData, buildData, fromSlice, fromSlice, fromSlice + 1);

                float splineLength = buildData.TransformLookup.MaxDistance;
                for (int i = fromSlice + 1; i <= toSlice; ++i)
                {
                    dist += stepDist;
                    SetVertices(meshData, buildData, i, dist, ref transformLookupIndex);
                    SetIndices(meshData, buildData, i - 1, i - 1, i);
                }
            }

            static private void GenerateDataWithUV(MeshData meshData, BuildTempData buildData, float[] uCoordinates, int fromSlice, int toSlice, float fromDist, float stepDist, Vector2 uvScale)
            {
                float dist = fromDist;
                float splineLength = buildData.TransformLookup.MaxDistance;

                int transformLookupIndex = 0;
                SetVertices(meshData, buildData, fromSlice, dist, ref transformLookupIndex);
                SetUVs(meshData, buildData, uCoordinates, fromSlice, dist / splineLength, uvScale);
                SetIndices(meshData, buildData, fromSlice, fromSlice, fromSlice + 1);

                for (int i = fromSlice + 1; i <= toSlice; ++i)
                {
                    dist += stepDist;
                    SetVertices(meshData, buildData, i, dist, ref transformLookupIndex);
                    SetUVs(meshData, buildData, uCoordinates, i, dist / splineLength, uvScale);
                    SetIndices(meshData, buildData, i - 1, i - 1, i);
                }
            }

            static private void SetVertices(MeshData meshData, BuildTempData buildData, int sliceIndex, float distance, ref int transformLookupIndex)
            {
                PointTransform transform = buildData.TransformLookup.CalcAtDistanceSequential(distance, ref transformLookupIndex);
                float3 pos = transform.Position;
                int ind = sliceIndex * buildData.CrossSectionData.Length;

                NativeArray<float3> vertices = meshData.Vertices;
                for (int i = 0; i < buildData.CrossSectionData.Length; ++i)
                {
                    vertices[ind + i] = pos + (float3)(transform.Rotation * (buildData.CrossSectionData.Slice[i] * transform.Scaling));
                }
            }

            static private void SetUVs(MeshData meshData, BuildTempData buildData, float[] uCoords, int sliceIndex, float distanceNormalized, Vector2 uvScale)
            {
                int crossSectionLength = buildData.CrossSectionData.Length;
                int ind = sliceIndex * crossSectionLength;

                NativeArray<float2> uvs = meshData.UVs;
                for (int i = 0; i < crossSectionLength; ++i)
                {
                    float u = uCoords[i] * uvScale.x;
                    float v = distanceNormalized * uvScale.y;
                    uvs[ind + i] = new float2(u, v);
                }
            }

            static private void SetIndices(MeshData meshData, BuildTempData buildData, int sliceIndex, int sliceFrom, int sliceTo)
            {
                int indexOffset = sliceIndex * buildData.SliceIndexLength;
                int vStart = sliceFrom * buildData.CrossSectionData.Length;
                int vEnd = sliceTo * buildData.CrossSectionData.Length;

                for (int i = 0; i < buildData.QuadCount; ++i)
                {
                    int topLeft = vEnd + i;
                    int bottomLeft = vStart + i;
                    SetFace(meshData, indexOffset + i * QuadIndexLength, bottomLeft, topLeft, topLeft + 1, bottomLeft + 1);
                }
            }

            static private void SetFace(MeshData meshData, int start, int bl, int tl, int tr, int br)
            {
                NativeArray<int> indices = meshData.Triangles;

                indices[start] = bl;
                indices[start + 1] = tl;
                indices[start + 2] = tr;

                indices[start + 3] = bl;
                indices[start + 4] = tr;
                indices[start + 5] = br;
            }
        }

        /// <summary>
        /// Cross section positions and horizontal uv values.
        /// </summary>
        [System.Serializable]
        public class CrossSectionData
        {
            [SerializeField]
            private float3[] slicePositions = null;
            public float3[] Slice { get => slicePositions; }

            [SerializeField]
            private float[] sliceUCoords = null;
            public float[] SliceUCoords { get => sliceUCoords; }

            public int Length { get => Slice.Length; }

            public CrossSectionData(float3[] positions, float[] uCoordinates = null)
            {
                slicePositions = new float3[positions.Length];
                System.Array.Copy(positions, slicePositions, positions.Length);

                if (uCoordinates != null)
                {
                    sliceUCoords = new float[positions.Length];
                    for (int i = 0; i < sliceUCoords.Length; ++i)
                    {
                        sliceUCoords[i] = (i < uCoordinates.Length) ? uCoordinates[i] : 1;
                    }
                }
            }

            public CrossSectionData(CrossSectionData other)
                : this(other.slicePositions, other.sliceUCoords)
            {

            }

            public void CalculateU()
            {
                sliceUCoords = CalculateU(slicePositions);
            }

            static public float[] CalculateU(float3[] positions)
            {
                float sum = 0;
                for (int i = 0; i < positions.Length - 1; ++i)
                {
                    sum += Vector3.Distance(positions[i], positions[i + 1]);
                }

                var sliceUCoords = new float[positions.Length];
                float u = 0;
                for (int i = 0; i < positions.Length - 1; ++i)
                {
                    sliceUCoords[i] = u;
                    float dist = Vector3.Distance(positions[i], positions[i + 1]);
                    u += dist / sum;
                }

                sliceUCoords[sliceUCoords.Length - 1] = 1;
                return sliceUCoords;
            }
        }
    }
}
