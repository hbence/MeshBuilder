using UnityEngine;

using TileData = MeshBuilder.Tile.Data;
using VariantData2D = MeshBuilder.TileMesher2D.VariantData;
using VariantData3D = MeshBuilder.TileMesher3D.VariantData;

namespace MeshBuilder
{
    /// <summary>
    /// wrapper class to handle asset import/export
    /// </summary>
    [System.Serializable]
    public class TileDataAsset : ScriptableObject
    {
        [SerializeField]
        [HideInInspector]
        private VolumeContainer<TileData> tileData;

        [SerializeField]
        [HideInInspector]
        private VolumeContainer<VariantData2D> variantData2D;

        [SerializeField]
        [HideInInspector]
        private VolumeContainer<VariantData3D> variantData3D;

        public void SetData(Volume<TileData> tiles)
        {
            tileData = new VolumeContainer<TileData>(tiles);
        }

        public void SetData(Volume<VariantData2D> variants)
        {
            variantData2D = new VolumeContainer<VariantData2D>(variants);
        }

        public void SetData(Volume<VariantData3D> variants)
        {
            variantData3D = new VolumeContainer<VariantData3D>(variants);
        }

        public Volume<TileData> CreateTileDataVolume()
        {
            return tileData.CreateVolume();
        }

        public Volume<VariantData2D> CreateVariantData2D()
        {
            return variantData2D.CreateVolume();
        }

        public Volume<VariantData3D> CreateVariantData3D()
        {
            return variantData3D.CreateVolume();
        }

        private bool HasVolumeData<T>(VolumeContainer<T> container) where T : struct { return container != null && container.HasData; }
        public bool HasTileData { get { return HasVolumeData(tileData); } }
        public bool HasVariantData2D { get { return HasVolumeData(variantData2D); } }
        public bool HasVariantData3D { get { return HasVolumeData(variantData3D); } }

        /// <summary>
        /// wrapper class to use unity's data serialization
        /// </summary>
        /// <typeparam name="T"></typeparam>
        [System.Serializable]
        private class VolumeContainer<T> where T : struct
        {
            [HideInInspector]
            [SerializeField]
            private int xSize;

            [HideInInspector]
            [SerializeField]
            private int ySize;

            [HideInInspector]
            [SerializeField]
            private int zSize;

            [HideInInspector]
            [SerializeField]
            private T[] data;
            public T[] Data { get { return data; } set { data = value; } }

            public VolumeContainer(Volume<T> volume)
            {
                FromVolume(volume);
            }

            public Volume<T> CreateVolume()
            {
                var dataVolume = new Volume<T>(xSize, ySize, zSize);
                dataVolume.Data.CopyFrom(data);
                return dataVolume;
            }

            public void FromVolume(Volume<T> volume)
            {
                xSize = volume.XLength;
                ySize = volume.YLength;
                zSize = volume.ZLength;
                data = volume.Data.ToArray();
            }

            public bool HasData { get { return data != null; } }
        }
    }
}

