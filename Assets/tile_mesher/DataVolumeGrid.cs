using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    public class DataVolumeGrid<T> where T : struct
    {
        public const byte NullDataValue = 0;

        private Volume<T>[,,] data;

        public DataVolumeGrid(int x, int y, int z)
        {
            data = new Volume<T>[x, y, z];
        }

        public Volume<T> this[int x, int y, int z]
        {
            get { return data[x, y, z];  }
            set { data[x, y, z] = value; }
        }

        public Volume<T> this[Vector3Int c]
        {
            get { return data[c.x, c.y, c.z]; }
            set { data[c.x, c.y, c.z] = value; }
        }

        public bool HasDataVolume(int x, int y, int z)
        {
            return this[x, y, z] != null;
        }

        public bool HasDataVolume(Vector3Int c)
        {
            return this[c] != null;
        }

        public T GetValueAt(Vector3Int chunkCoord, int x, int y, int z)
        {
            var volume = this[chunkCoord];
            return (volume != null) ? volume[x, y, z] : default(T);
        }

        public T GetValueAt(Vector3Int chunkCoord, Vector3Int valueCoord)
        {
            var volume = this[chunkCoord];
            return (volume != null) ? volume[valueCoord] : default(T);
        }

        public ChunkInfo CreateChunkInfo(int x, int y, int z)
        {
            return new ChunkInfo(this, new Vector3Int(x, y, z));
        }

        public ChunkInfo CreateChunkInfo(Vector3Int coord)
        {
            return new ChunkInfo(this, coord);
        }

        public struct ChunkInfo
        {
            public DataVolumeGrid<T> DataGrid { get; private set; }
            public Vector3Int Coord { get; private set; }
            public ChunkInfo(DataVolumeGrid<T> grid, Vector3Int c)
            {
                DataGrid = grid;
                Coord = c;
            }
        }
    }
}
