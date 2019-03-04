using UnityEngine;

namespace MeshBuilder
{
    public class LatticeGrid : MonoBehaviour
    {
        public GameObject proto;

        [HideInInspector]
        [SerializeField]
        private VerticesGrid grid;

        private void Awake()
        {
            /*
            foreach (Vector3 pos in grid.Vertices)
            {
                var go = Instantiate(proto);
                go.transform.SetParent(transform);
                go.transform.localPosition = pos;
            }
            */
        }

        private void Reset()
        {
            grid = new VerticesGrid(5, 5, 5, Vector3.one);
            grid.PlaceVertices();
        }

        public void GenerateVertices(int xLength, int yLength, int zLength, Vector3 cellSize)
        {
            if (grid.Vertices == null || (xLength != grid.XLength || yLength != grid.YLength || zLength != grid.ZLength))
            {
                grid = new VerticesGrid(xLength, yLength, zLength, cellSize);
            }

            grid.PlaceVertices();
        }

        public VerticesGrid Grid { get { return grid; } }

        [System.Serializable]
        public struct VerticesGrid
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

            public VerticesGrid(int x, int y, int z, Vector3 cellSize)
            {
                vertices = new Vector3[x * y * z];
                xLength = x;
                yLength = y;
                zLength = z;
                this.cellSize = cellSize;
            }

            public void PlaceVertices()
            {
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
