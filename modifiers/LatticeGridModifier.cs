using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
using Unity.Burst;

using static MeshBuilder.Utils;

using VertexGrid = MeshBuilder.LatticeGridComponent.VertexGrid;

namespace MeshBuilder
{
    public class LatticeGridModifier : Modifier
    {
        private enum WorkMode
        {
            Uninitialized,
            TakeSnapshot,
            Evaluate,
            ApplyChange
        }
        private WorkMode mode = WorkMode.Uninitialized;

        private SnapShotData snapShot;
        private SnapShotData SnapShot
        {
            get => snapShot;
            set
            {
                snapShot?.Dispose();
                snapShot = value;
            }
        }
        private Transform originalGridTransform;
        private Transform meshTransform;

        private Transform targetGridTransform;
        private LatticeGridData targetGrid;
        private LatticeGridData TargetGrid
        {
            get { return targetGrid; }
            set
            {
                targetGrid?.Dispose();
                targetGrid = value;
            }
        }

        private Mesh mesh;
        private MeshData tempMeshData;

        private void Uninitialized()
        {
            mode = WorkMode.Uninitialized;
            SnapShot = null;
            TargetGrid = null;
        }

        public void InitForSnapshot(Transform gridTransform, int3 gridExtents, float3 gridCellSize, Transform meshTransform)
        {
            if (!IsGridDataValid(gridExtents, gridCellSize))
            {
                Uninitialized();
                return;
            }

            mode = WorkMode.TakeSnapshot;

            SnapShot = new SnapShotData(gridExtents, gridCellSize);
            originalGridTransform = gridTransform;
            this.meshTransform = meshTransform;

            Inited();
        }

        public void InitForEvaluation(Transform gridTransform, VertexGrid grid, Transform meshTransform)
        {
            if (SnapShot == null || !SnapShot.HasCoordinates)
            {
                Debug.LogError("LatticeGridModifier needs snapshot data before evaluation can be initialized!");
                Uninitialized();
                return;
            }

            mode = WorkMode.Evaluate;

            TargetGrid = new LatticeGridData(grid);
            var m = ToFloat3x4(meshTransform.worldToLocalMatrix * gridTransform.localToWorldMatrix);
            TargetGrid.TransformVertices(m);
            targetGridTransform = gridTransform;
            this.meshTransform = meshTransform;

            Inited();
        }

        public void InitLatticeChange(Transform originalGridTransform, int3 gridExtents, float3 gridCellSize, Transform targetGridTransform, VertexGrid targetGrid, Transform meshTransform)
        {
            if (!IsGridDataValid(gridExtents, gridCellSize))
            {
                Uninitialized();
                return;
            }

            mode = WorkMode.ApplyChange;

            SnapShot = new SnapShotData(gridExtents, gridCellSize);
            TargetGrid = new LatticeGridData(targetGrid);
            var m = ToFloat3x4(meshTransform.worldToLocalMatrix * targetGridTransform.localToWorldMatrix);
            TargetGrid.TransformVertices(m);
            this.originalGridTransform = originalGridTransform;
            this.targetGridTransform = targetGridTransform;
            this.meshTransform = meshTransform;

            Inited();
        }

        private bool IsGridDataValid(int3 extents, float3 cellSize)
        {
            if (extents.x < 0 || extents.y < 0 || extents.z < 0)
            {
                Debug.LogErrorFormat("extent is invalid:{0}, {1}, {2}", extents.x, extents.y, extents.z);
                return false;
            }
            if (cellSize.x < 0 || cellSize.y < 0 || cellSize.z < 0)
            {
                Debug.LogErrorFormat("cellSize is invalid:{0}, {1}, {2}", cellSize.x, cellSize.y, cellSize.z);
                return false;
            }
            return true;
        }

        private bool DoesMatch(int3 c, VertexGrid grid) { return c.x == grid.XLength && c.y == grid.YLength && c.z == grid.ZLength; }

        public override void Dispose()
        {
            Uninitialized();

            base.Dispose();
        }

        public JobHandle Start(Mesh mesh, JobHandle dependOn = default)
        {
            this.mesh = mesh;

            tempMeshData = new MeshData(mesh, Allocator.TempJob, (int)MeshData.Buffer.Vertex);
            AddTemp(tempMeshData);

            return Start(tempMeshData, dependOn);
        }

        protected override JobHandle StartGeneration(MeshData meshData, JobHandle dependOn)
        {
            var res = dependOn;
            if (mode == WorkMode.TakeSnapshot)
            {
                res = SnapShot.ScheduleCoordinateGenaration(originalGridTransform, meshTransform, meshData, dependOn);
            }
            else if (mode == WorkMode.Evaluate)
            {
                res = SnapShot.ScheduleEvaluation(targetGridTransform, TargetGrid, meshTransform, meshData, dependOn);
            }
            else if (mode == WorkMode.ApplyChange)
            {
                res = SnapShot.ScheduleCoordinateGenaration(originalGridTransform, meshTransform, meshData, dependOn);
                res = SnapShot.ScheduleEvaluation(targetGridTransform, TargetGrid, meshTransform, meshData, res);
            }

            return res;
        }

        protected override void EndGeneration()
        {
            if (mode == WorkMode.Evaluate || mode == WorkMode.ApplyChange)
            {
                if (mesh)
                {
                    tempMeshData.UpdateMesh(mesh, MeshData.UpdateMode.DontClear, (uint)MeshData.Buffer.Vertex);
                }
            }
        }

        public bool HasSnapShot { get => SnapShot != null && SnapShot.HasCoordinates; }

        private class SnapShotData : System.IDisposable
        {
            private LatticeGridData gridData;
            private Volume<LatticeCell> cells;
            private NativeArray<VertexData> coordinates;

            public bool HasCoordinates { get => coordinates.IsCreated; }

            public SnapShotData(int3 gridExtents, float3 cellSize)
            {
                gridData = new LatticeGridData(gridExtents, cellSize);

                cells = new Volume<LatticeCell>(gridExtents.x - 1, gridExtents.y - 1, gridExtents.z - 1);
                GenerateCells(cells.Data, cells.Extents, gridData.Extents);
            }

            static private void GenerateCells(NativeArray<LatticeCell> cells, Extents cellExtents, Extents gridExtents)
            {
                int xOffset = 1;
                int yOffset = gridExtents.XZ;
                int zOffset = gridExtents.X;
                int index = 0;
                for (int y = 0; y < cellExtents.Y; ++y)
                {
                    for (int z = 0; z < cellExtents.Z; ++z)
                    {
                        for (int x = 0; x < cellExtents.X; ++x)
                        {
                            int vertIndex = gridExtents.ToIndexAt(x, y, z);
                            cells[index] = new LatticeCell(
                                vertIndex, vertIndex + xOffset, vertIndex + yOffset,
                                vertIndex + zOffset, vertIndex + xOffset + yOffset, vertIndex + xOffset + zOffset,
                                vertIndex + yOffset + zOffset, vertIndex + xOffset + yOffset + zOffset);
                            ++index;
                        }
                    }
                }
            }

            public void Dispose()
            {
                gridData?.Dispose();
                gridData = null;
                SafeDispose(ref cells);
                SafeDispose(ref coordinates);
            }

            public JobHandle ScheduleCoordinateGenaration(Transform gridTransform, Transform meshTransform, MeshData meshData, JobHandle dependOn = default)
            {
                SafeDispose(ref coordinates);

                if (meshData.VerticesLength == 0)
                {
                    Debug.LogError("mesh has no vertices");
                    return dependOn;
                }
                
                coordinates = new NativeArray<VertexData>(meshData.VerticesLength, Allocator.Persistent);

                var transformMatrix = gridTransform.worldToLocalMatrix * meshTransform.localToWorldMatrix;

                float3 cellSize = gridData.CellSize;
                float3 cellOffset = new float3(0.5f * cellSize.x * (gridData.Extents.X - 1),
                                                0.5f * cellSize.y * (gridData.Extents.Y - 1),
                                                0.5f * cellSize.z * (gridData.Extents.Z - 1));

                var job = new GenerateCoordinatesJob()
                {
                    cellSize = gridData.CellSize,
                    cellOffset = cellOffset,
                    cellExtents = cells.Extents,
                    transformMatrix = ToFloat3x4(transformMatrix),
                    gridVertices = gridData.Vertices.Data,
                    meshVertices = meshData.Vertices,
                    cells = cells.Data,
                    coordinates = coordinates
                };

                return job.Schedule(meshData.VerticesLength, 1024, dependOn);
            }

            public JobHandle ScheduleEvaluation(Transform gridTransform, LatticeGridData targetGrid, Transform meshTransform, MeshData meshData, JobHandle dependOn = default)
            {
                if (meshData.VerticesLength != coordinates.Length)
                {
                    Debug.LogError("mesh vertex count doesn't match coordinates!");
                    return dependOn;
                }
                if (gridData.Vertices.Length != targetGrid.Vertices.Length)
                {
                    Debug.LogError("grid vertex count doesn't match set grid vertices! (the snapshot lattice and the evaluation lattice size must match!)");
                    return dependOn;
                }

                var job = new EvaluateCoordinatesJob()
                {
                    gridVertices = targetGrid.Vertices.Data,
                    cells = cells.Data,
                    coordinates = coordinates,
                    meshVertices = meshData.Vertices
                };
                return job.Schedule(meshData.VerticesLength, 1024, dependOn);
            }
            
            [BurstCompile]
            private struct GenerateCoordinatesJob : IJobParallelFor
            {
                public float3 cellSize;
                public float3 cellOffset;
                public Extents cellExtents;
                public float3x4 transformMatrix;
                [ReadOnly] public NativeArray<float3> gridVertices;
                [ReadOnly] public NativeArray<float3> meshVertices;
                [ReadOnly] public NativeArray<LatticeCell> cells;
                [WriteOnly] public NativeArray<VertexData> coordinates;

                public void Execute(int index)
                {
                    float3 v = math.mul(transformMatrix, new float4(meshVertices[index], 1));

                    int3 c = new int3
                    {
                        x = (int)math.floor((v.x + cellOffset.x) / cellSize.x),
                        y = (int)math.floor((v.y + cellOffset.y) / cellSize.y),
                        z = (int)math.floor((v.z + cellOffset.z) / cellSize.z)
                    };

                    VertexData data = new VertexData();
                    data.cellIndex = ToCellIndex(c, cellExtents);
                    if (data.cellIndex >= 0)
                    {
                        data.coords = cells[data.cellIndex].CalcCoordinates(v, gridVertices);
                    }

                    coordinates[index] = data;
                }

                static private int ToCellIndex(int3 c, Extents extents)
                {
                    return extents.IsInBounds(c) ? extents.ToIndexAt(c) : -1;
                }
            }

            [BurstCompile]
            private struct EvaluateCoordinatesJob : IJobParallelFor
            {
                [ReadOnly] public NativeArray<float3> gridVertices;
                [ReadOnly] public NativeArray<LatticeCell> cells;
                [ReadOnly] public NativeArray<VertexData> coordinates;
                [WriteOnly] public NativeArray<float3> meshVertices;

                public void Execute(int index)
                {
                    var c = coordinates[index];
                    if (c.cellIndex >= 0)
                    {
                        meshVertices[index] = cells[c.cellIndex].Evaluate(c.coords, gridVertices);
                    }
                }
            }
        }

        private class LatticeGridData : System.IDisposable
        {
            public Extents Extents { get => vertices.Extents; }
            public float3 CellSize { get; private set; }

            private Volume<float3> vertices;
            public Volume<float3> Vertices { get => vertices; }

            public LatticeGridData(int3 extents, float3 cellSize, Vector3[] verticesData)
            {
                CellSize = cellSize;
                vertices = new Volume<float3>(extents.x, extents.y, extents.z, ToFloat3Array(verticesData));
            }

            public LatticeGridData(int3 extents, float3 cellSize)
            {
                CellSize = cellSize;
                vertices = new Volume<float3>(extents.x, extents.y, extents.z);

                ResetVertices();
            }

            public LatticeGridData(VertexGrid grid)
                : this(new int3(grid.XLength, grid.YLength, grid.ZLength), grid.CellSize, grid.Vertices)
            {

            }

            public void TransformVertices(float3x4 m)
            {
                float4 v = new float4(1, 1, 1, 1);
                for (int i = 0; i < vertices.Length; ++i)
                {
                    v.xyz = vertices[i];
                    vertices[i] = math.mul(m, v);
                }
            }

            public void ResetVertices()
            {
                float3 start = CellSize * -0.5f;
                start.x *= Extents.X - 1;
                start.y *= Extents.Y - 1;
                start.z *= Extents.Z - 1;

                float3 pos = start;
                int index = 0;
                for (int y = 0; y < Extents.Y; ++y)
                {
                    pos.z = start.z;
                    for (int z = 0; z < Extents.Z; ++z)
                    {
                        pos.x = start.x;
                        for (int x = 0; x < Extents.X; ++x)
                        {
                            vertices[index] = pos;
                            ++index;
                            pos.x += CellSize.x;
                        }
                        pos.z += CellSize.z;
                    }
                    pos.y += CellSize.y;
                }
            }

            public void Dispose()
            {
                SafeDispose(ref vertices);
            }
        }

        private struct LatticeCell
        {
            // corner vertex indices
            // should I use a fixed array? but then th struct would be unsafe. does that matter?
            // for naming, i is the index of the "origin" corner, the letters mean which axis the next corner is
            // in the positive direction
            public int i, ix, iy, iz, ixy, ixz, iyz, ixyz;

            public LatticeCell(int i, int ix, int iy, int iz, int ixy, int ixz, int iyz, int ixyz)
            {
                this.i = i;
                this.ix = ix;
                this.iy = iy;
                this.iz = iz;
                this.ixy = ixy;
                this.ixz = ixz;
                this.iyz = iyz;
                this.ixyz = ixyz;
            }

            private const byte HasNegative = 1 << 0;
            private const byte HasZero = 1 << 1;
            private const byte HasPositive = 1 << 2;
            private const byte HasBothSigns = HasNegative | HasPositive;

            static private void VectorSignCheck(float3 v, ref byte x, ref byte y, ref byte z)
            {
                x |= (v.x == 0) ? HasZero :
                     (v.x > 0) ? HasPositive : HasNegative;

                y |= (v.y == 0) ? HasZero :
                     (v.y > 0) ? HasPositive : HasNegative;

                z |= (v.z == 0) ? HasZero :
                     (v.z > 0) ? HasPositive : HasNegative;
            }

            private const float OffsetEpsilon = 0.0001f;

            static private bool HasFlag(byte value, byte flag) { return (value & flag) != 0; }

            static private float3 PrepareVertex(float3 v, float3 bottomLeftBack, float3 topRightForward)
            {
                float3 v0 = bottomLeftBack - v;
                float3 v7 = topRightForward - v;

                // if a vertex is on the grid, the evaluation won't work properly
                // lets check for zero components in the corner vectors
                byte xFlags = 0, yFlags = 0, zFlags = 0;
                VectorSignCheck(v0, ref xFlags, ref yFlags, ref zFlags);
                VectorSignCheck(v7, ref xFlags, ref yFlags, ref zFlags);

                // if there are zero components, that means the vertex was on the grid
                // lets offset it towards the center of the cell
                if (HasFlag(xFlags, HasZero))
                {
                    v.x += HasFlag(xFlags, HasPositive) ? -OffsetEpsilon : OffsetEpsilon;
                }
                if (HasFlag(yFlags, HasZero))
                {
                    v.y += HasFlag(yFlags, HasPositive) ? -OffsetEpsilon : OffsetEpsilon;
                }
                if (HasFlag(zFlags, HasZero))
                {
                    v.z += HasFlag(zFlags, HasPositive) ? -OffsetEpsilon : OffsetEpsilon;
                }

                return v;
            }

            public VertexCoordinates CalcCoordinates(float3 v, NativeArray<float3> gridVerts)
            {
                // offset the vertex a bit if it is on the grid
                v = PrepareVertex(v, gridVerts[i], gridVerts[ixyz]);

                float3 v0 = gridVerts[i] - v;
                float3 v1 = gridVerts[ix] - v;
                float3 v2 = gridVerts[iz] - v;
                float3 v3 = gridVerts[ixz] - v;
                float3 v4 = gridVerts[iy] - v;
                float3 v5 = gridVerts[ixy] - v;
                float3 v6 = gridVerts[iyz] - v;
                float3 v7 = gridVerts[ixyz] - v;

                float d0 = math.length(v0);
                float d1 = math.length(v1);
                float d2 = math.length(v2);
                float d3 = math.length(v3);
                float d4 = math.length(v4);
                float d5 = math.length(v5);
                float d6 = math.length(v6);
                float d7 = math.length(v7);

                v0 /= d0;
                v1 /= d1;
                v2 /= d2;
                v3 /= d3;
                v4 /= d4;
                v5 /= d5;
                v6 /= d6;
                v7 /= d7;

                VertexCoordinates result = new VertexCoordinates
                {
                    c0 = CalcCoordinate(d0, v0, v2, v3, v0, v3, v1, v0, v1, v4, v0, v4, v2),
                    c1 = CalcCoordinate(d1, v1, v0, v3, v1, v3, v7, v1, v7, v5, v1, v4, v0, v1, v5, v4),
                    c2 = CalcCoordinate(d2, v2, v3, v0, v2, v7, v3, v2, v6, v7, v2, v0, v4, v2, v4, v6),
                    c3 = CalcCoordinate(d3, v3, v0, v2, v3, v1, v0, v3, v2, v7, v3, v7, v1),
                    c4 = CalcCoordinate(d4, v4, v5, v6, v4, v0, v1, v4, v1, v5, v4, v2, v0, v4, v6, v2),
                    c5 = CalcCoordinate(d5, v5, v7, v6, v5, v1, v7, v5, v6, v4, v5, v4, v1),
                    c6 = CalcCoordinate(d6, v6, v7, v2, v6, v5, v7, v6, v4, v5, v6, v2, v4),
                    c7 = CalcCoordinate(d7, v7, v3, v2, v7, v1, v3, v7, v2, v6, v7, v6, v5, v7, v5, v1),
                };
                result.Normalize();

                return result;
            }

            static private float CalcCoordinate(float latticeDist,
                                         float3 t0a, float3 t0b, float3 t0c,
                                         float3 t1a, float3 t1b, float3 t1c,
                                         float3 t2a, float3 t2b, float3 t2c,
                                         float3 t3a, float3 t3b, float3 t3c)
            {
                float sum = CalcLambdaFactor(t0a, t0b, t0c);
                sum += CalcLambdaFactor(t1a, t1b, t1c);
                sum += CalcLambdaFactor(t2a, t2b, t2c);
                sum += CalcLambdaFactor(t3a, t3b, t3c);

                return sum / latticeDist;
            }

            static private float CalcCoordinate(float latticeDist,
                                         float3 t0a, float3 t0b, float3 t0c,
                                         float3 t1a, float3 t1b, float3 t1c,
                                         float3 t2a, float3 t2b, float3 t2c,
                                         float3 t3a, float3 t3b, float3 t3c,
                                         float3 t4a, float3 t4b, float3 t4c)
            {
                float sum = CalcLambdaFactor(t0a, t0b, t0c);
                sum += CalcLambdaFactor(t1a, t1b, t1c);
                sum += CalcLambdaFactor(t2a, t2b, t2c);
                sum += CalcLambdaFactor(t3a, t3b, t3c);
                sum += CalcLambdaFactor(t4a, t4b, t4c);

                return sum / latticeDist;
            }

            public float3 Evaluate(VertexCoordinates coords, NativeArray<float3> gridVerts)
            {
                float3 result = new float3 { x = 0, y = 0, z = 0 };
                float total = 0;

                if (coords.c0 > 0) { result += gridVerts[i] * coords.c0; total += coords.c0; }
                if (coords.c1 > 0) { result += gridVerts[ix] * coords.c1; total += coords.c1; }
                if (coords.c2 > 0) { result += gridVerts[iz] * coords.c2; total += coords.c2; }
                if (coords.c3 > 0) { result += gridVerts[ixz] * coords.c3; total += coords.c3; }
                if (coords.c4 > 0) { result += gridVerts[iy] * coords.c4; total += coords.c4; }
                if (coords.c5 > 0) { result += gridVerts[ixy] * coords.c5; total += coords.c5; }
                if (coords.c6 > 0) { result += gridVerts[iyz] * coords.c6; total += coords.c6; }
                if (coords.c7 > 0) { result += gridVerts[ixyz] * coords.c7; total += coords.c7; }

                result /= total;

                return result;
            }

            static private float CalcLambdaFactor(float3 v0, float3 v1, float3 v2)
            {
                float a01 = AngleBetween(v0, v1);
                float a12 = AngleBetween(v1, v2);
                float a02 = AngleBetween(v0, v2);

                float3 v0c1 = math.cross(v0, v1);
                float3 v1c2 = math.cross(v1, v2);
                float3 v0c2 = math.cross(v2, v0);

                v1c2 = math.normalize(v1c2);

                return (a12 +
                    math.dot(v0c1, v1c2) * a01 / math.length(v0c1) +
                    math.dot(v0c2, v1c2) * a02 / math.length(v0c2)) /
                    math.dot(v0, v1c2) * 2;
            }

            static private float AngleBetween(float3 a, float3 b)
            {
                /*   
                float theta = math.dot(a, b) / math.sqrt(math.lengthSquared(a) * math.lengthSquared(b));
                theta = math.clamp(theta, -1, 1);
                return math.acos(theta);
                // a and b are normalized here, their length is 1
                */
                return math.acos(math.dot(a, b));
            }
        }

        private struct VertexCoordinates
        {
            public float c0, c1, c2, c3, c4, c5, c6, c7;

            public void Normalize()
            {
                float sum = c0 + c1 + c2 + c3 + c4 + c5 + c6 + c7;
                c0 /= sum; c1 /= sum; c2 /= sum; c3 /= sum; c4 /= sum; c5 /= sum; c6 /= sum; c7 /= sum;
            }
        }

        private struct VertexData
        {
            public int cellIndex;
            public VertexCoordinates coords;
        }
    }
}
