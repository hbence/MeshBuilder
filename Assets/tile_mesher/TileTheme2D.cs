using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    [System.Serializable]
    public class TileTheme2D : TileTheme<TileTheme2D.Elem>
    {
        // Tile elems
        // the cases are based on how many cell is filled around them
        public enum Elem : sbyte
        {
            Null = -1,

            Case1,
            Case2,
            Case3,
            Case4
        }

        public Mesh[] Case1;
        public Mesh[] Case2;
        public Mesh[] Case3;
        public Mesh[] Case4;

        public CustomElem[] customElems;

        override protected Mesh[][] CreateMeshesArray()
        {
            return new Mesh[][]
            {
                Case1,
                Case2,
                Case3,
                Case4
            };
        }

        public CustomElem GetCustomElem(int index)
        {
            return customElems[index % customElems.Length];
        }

        public bool HasCustomElems { get { return customElems != null && customElems.Length > 0; } }
        public int CustomElemCount { get { return customElems == null ? 0 : customElems.Length; } }

        [System.Serializable]
        public class CustomElem
        {
            public Mesh[] variants;
            public byte[,] pattern;
        }
    }
}