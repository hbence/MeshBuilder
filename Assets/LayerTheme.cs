using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    public class LayerTheme : MonoBehaviour
    {
        public enum Elem : sbyte
        {
            Null = -1,
            Side,
            CornerConvex,
            CornerConcave,
            Center
        }

        public Mesh[] side;
        public Mesh[] cornerConvex;
        public Mesh[] cornerConcave;
        public Mesh[] center;

        private Mesh[][] meshes;

        public void Awake()
        {
            meshes = new Mesh[][]
            {
            side,
            cornerConvex,
            cornerConcave,
            center
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