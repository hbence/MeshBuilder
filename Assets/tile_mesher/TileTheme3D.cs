using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    // NOTE:
    // I should add to the documentation the following, also perhaps to a custom editor
    // - the meshes need to be aligned a certain way: the meshers don't handle different orientations,
    // so instead of rotating every piece by every mesher, the original meshes should be oriented a certain way
    // - for blender, there is a -90 rotation on the X axis to get it oriented correctly in unity, the template set ('3d_tiles.blend')
    // can be used as a reference. Note that there is an unapplied rotation on every piece, so it looks more natural in blender, 
    // if you edit this file and accidentaly apply the rotation, it won't work correctly
    // - every piece is centered a certain way, imagine a box divided into four smaller boxes, the local origin is at the center,
    // the pieces cover some of these small boxes, the origin always stays at the same position (so it can be a corner, middle of an edge or
    // center of a face) pay attention to it when making custom meshes
    // - 0.5 is the default size for a tile, custom tiles should be around that size
    // - the cases for the piece names are an arbitrary organization based on how many cells they fill from the top-down view
    // - the top/bottom part in the tile name comes from the POV of the origin point, so a top part will be above the local origin, and can be viewed from below.
    // This can look unintuitive if a top part is used as the bottom of a box, but these would be also the ceiling parts of a cave for example. So, if you have problem
    // mixing them up, just consider which way you would have to look to see the part, if you have to look up, it's a top part.
    [System.Serializable]
    public class TileTheme3D : TileTheme<TileTheme3D.Elem>
    {
        // Tile elems
        // the cases are classified by how many cell is filled from a top-down
        // view, this is just for convenience, an arbitrary way to organize
        // the elements
        public enum Elem : sbyte
        {
            Null = -1,

            Case1Top = 0,
            Case1Bottom,
            Case1Full,

            Case2Top,
            Case2Bottom,
            Case2Full,
            Case2Bottom1Top,
            Case2Top1Bottom,

            Case3Top,
            Case3Bottom,
            Case3Full,
            Case3Bottom1TopSide,
            Case3Bottom1TopCenter,
            Case3Bottom2TopSide,
            Case3Top1BottomSide,
            Case3Top1BottomCenter,
            Case3Top2BottomSide,
            Case3_2Top2Bottom,

            Case4Top,
            Case4Bottom,
            Case4Bottom1Top,
            Case4Bottom2Top,
            Case4Bottom3Top,
            Case4Top1Bottom,
            Case4Top2Bottom,
            Case4Top3Bottom
        }

        public Mesh[] Case1Top;
        public Mesh[] Case1Bottom;
        public Mesh[] Case1Full;

        public Mesh[] Case2Top;
        public Mesh[] Case2Bottom;
        public Mesh[] Case2Full;
        public Mesh[] Case2Bottom1Top;
        public Mesh[] Case2Top1Bottom;

        public Mesh[] Case3Top;
        public Mesh[] Case3Bottom;
        public Mesh[] Case3Full;
        public Mesh[] Case3Bottom1TopSide;
        public Mesh[] Case3Bottom1TopCenter;
        public Mesh[] Case3Bottom2TopSide;
        public Mesh[] Case3Top1BottomSide;
        public Mesh[] Case3Top1BottomCenter;
        public Mesh[] Case3Top2BottomSide;
        public Mesh[] Case3_2Top2Bottom;

        public Mesh[] Case4Top;
        public Mesh[] Case4Bottom;
        public Mesh[] Case4Bottom1Top;
        public Mesh[] Case4Bottom2TopSide;
        public Mesh[] Case4Bottom3Top;
        public Mesh[] Case4Top1Bottom;
        public Mesh[] Case4Top2BottomSide;
        public Mesh[] Case4Top3Bottom;

        override protected Mesh[][] CreateMeshesArray()
        {
            return new Mesh[][]
            {
                Case1Top,
                Case1Bottom,
                Case1Full,

                Case2Top,
                Case2Bottom,
                Case2Full,
                Case2Bottom1Top,
                Case2Top1Bottom,

                Case3Top,
                Case3Bottom,
                Case3Full,
                Case3Bottom1TopSide,
                Case3Bottom1TopCenter,
                Case3Bottom2TopSide,
                Case3Top1BottomSide,
                Case3Top1BottomCenter,
                Case3Top2BottomSide,
                Case3_2Top2Bottom,

                Case4Top,
                Case4Bottom,
                Case4Bottom1Top,
                Case4Bottom2TopSide,
                Case4Bottom3Top,
                Case4Top1Bottom,
                Case4Top2BottomSide,
                Case4Top3Bottom
            };
        }
    }
}