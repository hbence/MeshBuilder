﻿using System.Collections;
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
            Side,
            CornerConvex,
            CornerConcave,
            BottomSide,
            BottomCornerConvex,
            BottomCornerConcave,
        }

        public Mesh[] topSide;
        public Mesh[] topCornerConvex;
        public Mesh[] topCornerConcave;
        public Mesh[] side;
        public Mesh[] cornerConvex;
        public Mesh[] cornerConcave;
        public Mesh[] bottomSide;
        public Mesh[] bottomCornerConvex;
        public Mesh[] bottomCornerConcave;

        private Mesh[][] meshes;

        public void Awake()
        {
            meshes = new Mesh[][]
            {
            topSide,
            topCornerConvex,
            topCornerConcave,

            side,
            cornerConvex,
            cornerConcave,

            bottomSide,
            bottomCornerConvex,
            bottomCornerConcave,
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