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
    public class TileDataAsset : ScriptableObject, System.IDisposable
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

        private Volume<TileData> cachedTileData;
        public Volume<TileData> CachedTileData { get { return cachedTileData; } }

        private Volume<VariantData2D> cachedVariant2DData;
        public Volume<VariantData2D> CachedVariant2DData { get { return cachedVariant2DData; } }

        private Volume<VariantData3D> cachedVariant3DData;
        public Volume<VariantData3D> CachedVariant3DData { get { return cachedVariant3DData; } }

        public void InitCache()
        {
            Dispose();

            if (tileData != null)
            {
                cachedTileData = tileData.CreateVolume();
            }
            if (variantData2D != null)
            {
                cachedVariant2DData = variantData2D.CreateVolume();
            }
            if (variantData3D != null)
            {
                cachedVariant3DData = variantData3D.CreateVolume();
            }
        }

        public void SetData(Volume<TileData> tiles)
        {
            SetData(tiles, ref tileData, ref cachedTileData);
        }

        public void SetData(Volume<VariantData2D> variants)
        {
            SetData(variants, ref variantData2D, ref cachedVariant2DData);
        }

        public void SetData(Volume<VariantData3D> variants)
        {
            SetData(variants, ref variantData3D, ref cachedVariant3DData);
        }

        private void SetData<T>(Volume<T> source, ref VolumeContainer<T> container, ref Volume<T> cached) where T : struct
        {
            container = new VolumeContainer<T>(source);

            if (cached != null)
            {
                cached.Dispose();
                cached = container.CreateVolume();
            }
        }

        public void Dispose()
        {
            if (cachedTileData != null)
            {
                cachedTileData.Dispose();
                cachedTileData = null;
            }
            if (cachedVariant2DData != null)
            {
                cachedVariant2DData.Dispose();
                cachedVariant2DData = null;
            }
            if (cachedVariant3DData != null)
            {
                cachedVariant3DData.Dispose();
                cachedVariant3DData = null;
            }
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

