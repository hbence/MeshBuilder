using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    public class VolumeTheme : MonoBehaviour
    {
        public enum Elem : sbyte
        {
            Null = -1,
            TopSide = 0,
            TopCornerConvex,
            TopCornerConcave,
            TopDoubleCocanve,
            Side,
            CornerConvex,
            CornerConcave,
            DoubleConcave,
            BottomSide,
            BottomCornerConvex,
            BottomCornerConcave,
            BottomDoubleConcave,
        }

        public Mesh[] topSide;
        public Mesh[] topCornerConvex;
        public Mesh[] topCornerConcave;
        public Mesh[] topDoubleConcave;
        public Mesh[] side;
        public Mesh[] cornerConvex;
        public Mesh[] cornerConcave;
        public Mesh[] doubleConcave;
        public Mesh[] bottomSide;
        public Mesh[] bottomCornerConvex;
        public Mesh[] bottomCornerConcave;
        public Mesh[] bottomDoubleConcave;

        private Mesh[][] meshes;

        public void Awake()
        {
            meshes = new Mesh[][]
            {
            topSide,
            topCornerConvex,
            topCornerConcave,
            topDoubleConcave,

            side,
            cornerConvex,
            cornerConcave,
            doubleConcave,

            bottomSide,
            bottomCornerConvex,
            bottomCornerConcave,
            bottomDoubleConcave
            };
        }

        public Mesh GetRandomMesh(Elem elem)
        {
            var array = GetArray(elem);
            return array[Random.Range(0, array.Length)];
        }

        public Mesh GetMesh(Elem elem, int index)
        {
            var array = GetArray(elem);
            return array[index % array.Length];
        }

        public Mesh[] GetArray(Elem elem)
        {
            return meshes[(int)elem % meshes.Length];
        }
    }
}