using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;

namespace MeshBuilder
{
    // TODO: Jobify the code and make it into a Modifier!
    [ExecuteInEditMode]
    public class LatticeGridComponent : MonoBehaviour
    {
        // to avoid accidentally setting it too large,
        // arbitrary value, not sure what's a realistic maximum value
        private const int MaxVertexLength = 32; // per side

        private const float MinCellSize = 0.1f;

        // NOTE:
        // the length and cellSize values are checked by the LatticeGridEditor during editing, 
        // to make sure they always match the VerticesGrid size.
        // in play mode it should be resized with the ResizeGrid()

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
        private LatticeGridModifier modifier;

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

            ClearSnapshot();
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
            if (modifier == null)
            {
                ClearSnapshot();
            }
        }

        private void LateUpdate()
        {
            if (modifier != null && modifier.IsGenerating)
            {
                modifier.Complete();
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

            if (!HasSnapshot)
            {
                Debug.LogError("There is no current snapshot!");
                return;
            }

            modifier.InitForEvaluation(transform, grid, target.transform);
            modifier.Start(target.sharedMesh);

            Debug.Log("lattice: evaluated");
        }

        public void TakeSnapshot(Mesh mesh, Transform meshTransform)
        {
            ClearSnapshot();

            modifier.InitForSnapshot(transform, new int3(grid.XLength, grid.YLength, grid.ZLength), grid.CellSize, meshTransform);
            modifier.Start(mesh);

            Debug.Log("lattice: snapshot taken");
        }

        public void ClearSnapshot()
        {
            modifier?.Dispose();
            modifier = new LatticeGridModifier();
        }

        public void SaveGrid(string path)
        {
#if UNITY_EDITOR
            var asset = ScriptableObject.CreateInstance<VertexGridAsset>();
            asset.Grid = new VertexGrid(grid);
            AssetDatabase.CreateAsset(asset, path);
            Debug.Log("lattice: grid saved as:" + path);
#endif
        }

        public void LoadGrid(string path)
        {
#if UNITY_EDITOR
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
#endif
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
        public bool HasSnapshot { get { return modifier != null && modifier.HasSnapShot; } }

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
    }
}
