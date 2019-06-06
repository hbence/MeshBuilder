using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;

namespace MeshBuilder
{
    // TODO: Jobify the code and make it into a Modifier!
    [ExecuteInEditMode]
    public class LatticeGrid : MonoBehaviour
    {
        // to avoid accidentally setting it too large,
        // arbitrary value, not sure what's a realistic maximum value
        private const int MaxVertexLength = 32; // per side

        private const float MinCellSize = 0.1f;

        // NOTE:
        // the length and cellSize values are checked by the LatticeGridEditor during editing, 
        // to make sure they always match the VerticesGrid size.
        // int play mode it should be resized with the ResizeGrid()

        [SerializeField]
        private int xLength = 5;
        public int XLength { get { return xLength; } }

        [SerializeField]
        private int yLength = 5;
        public int YLength { get { return yLength; } }

        [SerializeField]
        private int zLength = 5;
        public int ZLength { get { return zLength; } }

        [SerializeField]
        private Vector3 cellSize = Vector3.one;
        public Vector3 CellSize
        {
            get { return cellSize; }
            set
            {
                cellSize = value;
                cellSize.x = Mathf.Max(MinCellSize, cellSize.x);
                cellSize.y = Mathf.Max(MinCellSize, cellSize.y);
                cellSize.z = Mathf.Max(MinCellSize, cellSize.z);
                ResetVerticesPosition();
            }
        }

        [HideInInspector]
        [SerializeField]
        private VertexGrid grid;

        public MeshFilter target;
        private SnapShotData snapShot;

        private void Reset()
        {
            ResizeGrid(xLength, yLength, zLength);
            ClearSnapshot();
        }

        public void ResizeGrid(int x, int y, int z)
        {
            xLength = x;
            yLength = y;
            zLength = z;

            xLength = Mathf.Clamp(xLength, 2, MaxVertexLength);
            yLength = Mathf.Clamp(yLength, 2, MaxVertexLength);
            zLength = Mathf.Clamp(zLength, 2, MaxVertexLength);

            if (grid == null || grid.Vertices == null || (xLength != grid.XLength || yLength != grid.YLength || zLength != grid.ZLength))
            {
                grid = new VertexGrid(xLength, yLength, zLength, cellSize);
            }

            grid.PlaceVertices(CellSize);
        }

        public void ResetVerticesPosition()
        {
            grid.PlaceVertices(CellSize);
        }

        private void OnDestroy()
        {
            ClearSnapshot();
        }

        private void OnDisable()
        {
            ClearSnapshot();
        }

        private void OnEnable()
        {
            if (snapShot != null && snapShot.IsDisposed)
            {
                ClearSnapshot();
            }
        }

        public void TakeTargetSnapshot()
        {
            if (target != null)
            {
                TakeSnapshot(target.sharedMesh, target.transform);
            }
            else
            {
                Debug.LogError("There is no target mesh set!");
            }
        }

        public void UpdateTargetSnapshotVertices()
        {
            if (target == null)
            {
                Debug.LogError("There is no target mesh set!");
                return;
            }

            if (snapShot == null || snapShot.IsDisposed)
            {
                Debug.LogError("There is no current snapshot!");
                return;
            }

            var verts = target.mesh.vertices;
            snapShot.Evaluate(grid, transform, verts, target.transform);
            target.sharedMesh.vertices = verts;
            target.sharedMesh.RecalculateNormals();

            Debug.Log("lattice: evaluated");
        }

        public void TakeSnapshot(Mesh mesh, Transform meshTransform)
        {
            ClearSnapshot();
            snapShot = new SnapShotData(grid, transform, mesh, meshTransform);

            Debug.Log("lattice: snapshot taken");
        }

        public void ClearSnapshot()
        {
            if (snapShot != null)
            {
                snapShot.Dispose();
            }
            snapShot = null;
        }

        public void SaveGrid(string path)
        {
            var asset = ScriptableObject.CreateInstance<VertexGridAsset>();
            asset.Grid = new VertexGrid(grid);
            AssetDatabase.CreateAsset(asset, path);
            Debug.Log("lattice: grid saved as:" + path);
        }

        public void LoadGrid(string path)
        {
            int index = path.IndexOf("/Assets/") + 1;
            path = path.Substring(index);
            
            var gridAsset = AssetDatabase.LoadAssetAtPath<VertexGridAsset>(path);
            if (gridAsset != null)
            {
                CopyFrom(gridAsset.Grid);
            }
            else
            {
                Debug.LogError("lattice: couldn't load grid from: " + path);
            }
        }

        public void CopyFrom(VertexGrid sourceGrid)
        {
            xLength = sourceGrid.XLength;
            yLength = sourceGrid.YLength;
            zLength = sourceGrid.ZLength;
            cellSize = sourceGrid.CellSize;
            grid = new VertexGrid(sourceGrid);
        }

        public VertexGrid Grid { get { return grid; } }
        public bool HasSnapshot { get { return snapShot != null && !snapShot.IsDisposed; } }

        [System.Serializable]
        public class VertexGrid
        {
            [SerializeField]
            private Vector3[] vertices;
            public Vector3[] Vertices { get { return vertices; } }

            [SerializeField]
            private int xLength;
            public int XLength { get { return xLength; } }
            [SerializeField]
            private int yLength;
            public int YLength { get { return yLength; } }
            [SerializeField]
            private int zLength;
            public int ZLength { get { return zLength; } }

            [SerializeField]
            private Vector3 cellSize;
            public Vector3 CellSize { get { return cellSize; } }

            public VertexGrid(int x, int y, int z, Vector3 cellSize)
            {
                x = Mathf.Clamp(x, 2, MaxVertexLength);
                y = Mathf.Clamp(y, 2, MaxVertexLength);
                z = Mathf.Clamp(z, 2, MaxVertexLength);

                vertices = new Vector3[x * y * z];
                xLength = x;
                yLength = y;
                zLength = z;
                this.cellSize = cellSize;
            }

            public VertexGrid(VertexGrid from)
                : this(from.xLength, from.yLength, from.zLength, from.cellSize)
            {
                if (from.vertices.Length != vertices.Length)
                {
                    Debug.LogError("source vertices length mismatch!");
                }

                System.Array.Copy(from.vertices, vertices, vertices.Length);
            }

            public void PlaceVertices(Vector3 cellSize)
            {
                this.cellSize = cellSize;

                Vector3 start = new Vector3(cellSize.x * -0.5f * (xLength - 1),
                                            cellSize.y * -0.5f * (yLength - 1),
                                            cellSize.z * -0.5f * (zLength - 1));
                Vector3 pos = start;
                int index = 0;
                for (int y = 0; y < yLength; ++y)
                {
                    pos.z = start.z;
                    for (int z = 0; z < zLength; ++z)
                    {
                        pos.x = start.x;
                        for (int x = 0; x < xLength; ++x)
                        {
                            vertices[index] = pos;
                            ++index;
                            pos.x += cellSize.x;
                        }
                        pos.z += cellSize.z;
                    }
                    pos.y += cellSize.y;
                }
            }

            public int ToIndex(Vector3Int c)
            {
                return ToIndex(c.x, c.y, c.z);
            }

            public int ToIndex(int x, int y, int z)
            {
                return Extents.IndexFromCoord(x, y, z, xLength * zLength, xLength);
            }

            public int StepIndex(int index, int stepX, int stepY, int stepZ)
            {
                var c = ToCoord(index);
                c.x += stepX;
                c.y += stepY;
                c.z += stepZ;

                return IsInBounds(c) ? ToIndex(c) : -1;
            }

            public Vector3Int ToCoord(int index)
            {
                int xzLength = xLength * zLength;
                int rem = index % xzLength;
                return new Vector3Int { x = rem % xLength, y = index / xzLength, z = rem / xLength };
            }

            public bool IsInBounds(int index)
            {
                return index >= 0 && index < vertices.Length;
            }

            public bool IsInBounds(int x, int y, int z)
            {
                return x >= 0 && y >= 0 && z >= 0 && x < xLength && y < yLength && z < zLength;
            }

            public bool IsInBounds(Vector3Int c)
            {
                return IsInBounds(c.x, c.y, c.z);
            }
        }

        public class SnapShotData
        {
            private NativeArray<float3> gridVertices;
            private Extents gridCellExtents;
            private NativeArray<LatticeCell> cells;
            private NativeArray<VertexData> coordinates;

            public SnapShotData(VertexGrid grid, Transform gridTransform, Mesh mesh, Transform meshTransform)
            {
                // TODO: I could try the matrix conversion trick here with the vector3 -> float3 array, instead of for loop copy
                gridVertices = new NativeArray<float3>(grid.Vertices.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var m = meshTransform.worldToLocalMatrix * gridTransform.localToWorldMatrix;
                CopyVertices(grid.Vertices, gridVertices);

                int cellCount = (grid.XLength - 1) * (grid.YLength - 1) * (grid.ZLength - 1);
                cells = new NativeArray<LatticeCell>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                GenerateCells(grid);

                coordinates = new NativeArray<VertexData>(mesh.vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                GenerateCoordinates(grid, gridTransform, mesh, meshTransform);
            }

            public void Evaluate(VertexGrid grid, Transform gridTransform, Vector3[] meshVerts, Transform meshTransform)
            {
                if (meshVerts.Length != coordinates.Length)
                {
                    Debug.LogError("mesh vertex count doesn't match coordinates!");
                    return;
                }
                if (grid.Vertices.Length != gridVertices.Length)
                {
                    Debug.LogError("grid vertex count doesn't match set grid vertices!");
                    return;
                }

                var m = meshTransform.worldToLocalMatrix * gridTransform.localToWorldMatrix;
                CopyVerticesTransformed(grid.Vertices, gridVertices, m);

                for (int i = 0; i < meshVerts.Length; ++i)
                {
                    var c = coordinates[i];
                    if (c.cellIndex >= 0)
                    {
                        meshVerts[i] = cells[c.cellIndex].Evaluate(c.coords, gridVertices);
                    }
                }
            }

            static private void CopyVertices(Vector3[] source, NativeArray<float3> dest)
            {
                for (int i = 0; i < source.Length; ++i)
                {
                    dest[i] = source[i];
                }
            }

            static private void CopyVerticesTransformed(Vector3[] source, NativeArray<float3> dest, Matrix4x4 m)
            {
                for (int i = 0; i < source.Length; ++i)
                {
                    dest[i] = m.MultiplyPoint3x4(source[i]);
                }
            }

            private void GenerateCells(VertexGrid grid)
            {
                gridCellExtents = new Extents(grid.XLength - 1, grid.YLength - 1, grid.ZLength - 1);

                for (int y = 0; y < gridCellExtents.Y; ++y)
                {
                    for (int z = 0; z < gridCellExtents.Z; ++z)
                    {
                        for (int x = 0; x < gridCellExtents.X; ++x)
                        {
                            // TODO: the indices could be calculated with simple addition
                            int index = gridCellExtents.ToIndexAt(x, y, z);
                            cells[index] = new LatticeCell(
                                grid.ToIndex(x, y, z), grid.ToIndex(x + 1, y, z), grid.ToIndex(x, y + 1, z), grid.ToIndex(x, y, z + 1), 
                                grid.ToIndex(x + 1, y + 1, z), grid.ToIndex(x + 1, y, z + 1), grid.ToIndex(x, y + 1, z + 1), grid.ToIndex(x + 1, y + 1, z + 1));
                        }
                    }
                }
            }

            private void GenerateCoordinates(VertexGrid grid, Transform gridTransform, Mesh mesh, Transform meshTransform)
            {
                if (coordinates.Length != mesh.vertexCount)
                {
                    Debug.LogError("coordinate length doesn't match mesh size");
                    return;
                }

                var m = gridTransform.worldToLocalMatrix * meshTransform.localToWorldMatrix;

                float3 cellSize = grid.CellSize; 
                float cellOffsetX = 0.5f * cellSize.x * (grid.XLength - 1);
                float cellOffsetY = 0.5f * cellSize.y * (grid.YLength - 1);
                float cellOffsetZ = 0.5f * cellSize.z * (grid.ZLength - 1);

                for (int i = 0; i < coordinates.Length; ++i)
                {
                    float3 v = mesh.vertices[i];

                    v = m.MultiplyPoint3x4(v);

                    int3 c = new int3
                    {
                        x = Mathf.FloorToInt((v.x + cellOffsetX) / cellSize.x),
                        y = Mathf.FloorToInt((v.y + cellOffsetY) / cellSize.y),
                        z = Mathf.FloorToInt((v.z + cellOffsetZ) / cellSize.z)
                    };

                    VertexData data = new VertexData();
                    data.cellIndex = ToCellIndex(c, gridCellExtents);
                    if (data.cellIndex >= 0)
                    {
                        data.coords = cells[data.cellIndex].CalcCoordinates(v, gridVertices);
                    }

                    coordinates[i] = data;
                }
            }

            static private int ToCellIndex(int3 c, Extents extents)
            {
                return extents.IsInBounds(c) ? extents.ToIndexAt(c) : -1;
            }

            public bool IsDisposed
            {
                get
                {
                    return !(gridVertices.IsCreated && cells.IsCreated && coordinates.IsCreated);
                }
            }
            
            public void Dispose()
            {
                if (gridVertices.IsCreated) { gridVertices.Dispose(); }
                if (cells.IsCreated) { cells.Dispose(); }
                if (coordinates.IsCreated) { coordinates.Dispose(); }
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

            private const byte HasNegative  = 1 << 0;
            private const byte HasZero      = 1 << 1;
            private const byte HasPositive  = 1 << 2;
            private const byte HasBothSigns = HasNegative | HasPositive;

            static private void VectorSignCheck(float3 v, ref byte x, ref byte y, ref byte z)
            {
                x |= (v.x == 0) ? HasZero :
                     (v.x > 0)  ? HasPositive : HasNegative;

                y |= (v.y == 0) ? HasZero :
                     (v.y > 0)  ? HasPositive : HasNegative;

                z |= (v.z == 0) ? HasZero :
                     (v.z > 0) ? HasPositive : HasNegative;
            }

            private const float OffsetEpsilon = 0.0001f;

            static private bool HasFlag(byte value, byte flag)
            {
                return (value & flag) != 0;
            }

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
                    c0 = CalcCoordinate(d0, v0,v2,v3,   v0,v3,v1,   v0,v1,v4,   v0,v4,v2),
                    c1 = CalcCoordinate(d1, v1,v0,v3,   v1,v3,v7,   v1,v7,v5,   v1,v4,v0,   v1,v5,v4),
                    c2 = CalcCoordinate(d2, v2,v3,v0,   v2,v7,v3,   v2,v6,v7,   v2,v0,v4,   v2,v4,v6),
                    c3 = CalcCoordinate(d3, v3,v0,v2,   v3,v1,v0,   v3,v2,v7,   v3,v7,v1),
                    c4 = CalcCoordinate(d4, v4,v5,v6,   v4,v0,v1,   v4,v1,v5,   v4,v2,v0,   v4,v6,v2),
                    c5 = CalcCoordinate(d5, v5,v7,v6,   v5,v1,v7,   v5,v6,v4,   v5,v4,v1),
                    c6 = CalcCoordinate(d6, v6,v7,v2,   v6,v5,v7,   v6,v4,v5,   v6,v2,v4),
                    c7 = CalcCoordinate(d7, v7,v3,v2,   v7,v1,v3,   v7,v2,v6,   v7,v6,v5,   v7,v5,v1),
                };
                result.Normalize();

                return result;
            }

            private float CalcCoordinate(float latticeDist,
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

            private float CalcCoordinate(float latticeDist,
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
