MeshBuilder
=========

Some algorithms for mesh generation in Unity using the Job system and Burst where possible. Mainly built around generating a mesh from premade tiles. The data contains the tile indices, a TileMesher generates a mesh for a given tile index. A GridMesher can be used the same way (water, ground etc. can be drawn as a grid instead if compiled from pieces). 

There is a NineScale builder which works like 9 slice scaling for sprites but it generates 3d meshes. The corners keep their scale end the edges and sides can be stretched or repeated. There is a version which renders the pieces separately (instanced or non-instanced) and a version which merges the pieces into a single mesh.

The MeshCombination builder can be used to merge mesh pieces. MeshCombinationUtils.CreateMergedMesh can automatically merge meshes and group them based on their render info (material, shadow, layer).

The MarchingSquaresMesher can generate different kinds of meshes based on distance field data.

**NOTE:** The library still misses some features and isn't properly tested. I only recommend it for testing/reference.

There is a separate [MeshBuilderExamples repository](https://github.com/hbence/MeshBuilderExamples/) with some test scenes showing how it can be used.

### Requirements

*   Unity 2019.1.8f1 - 2020.1.16f (probably also works on earlier versions, I just haven't tested)
*   Burst 1.0.4 - 1.4.3
*   Mathematics 1.01 - 1.2.1 
*   Collections 0.0.9 - 0.14.0-preview.16