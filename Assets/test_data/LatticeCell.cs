using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    [ExecuteInEditMode]
    public class LatticeCell : MonoBehaviour
    {
        public LatticeGrid lattice;
        public MeshFilter meshFilter;

        public bool doSetup;
        public bool doUpdate;

        private List<float>[] coordinates;
        private MeanValueCoordinates mvc;

        private Mesh mesh;

        private void setup()
        {
            mesh = meshFilter.mesh;
            var meshVerts = mesh.vertices;

            /*
            var tris = mesh.triangles;
            for (int t = 0; t < tris.Length; t += 3)
            {
                Debug.LogFormat("({0}, {1}, {2})", tris[t], tris[t + 1], tris[t + 2]);
            }
            */

            var verts = lattice.Grid.Vertices;

            var ind = new int[3 * 2 * 6];

            int i = 0;
            Set(ind, i, 0, 2, 3); i += 3;
            Set(ind, i, 0, 3, 1); i += 3;

            Set(ind, i, 2, 7, 3); i += 3;
            Set(ind, i, 7, 1, 3); i += 3;

            Set(ind, i, 2, 6, 7); i += 3;
            Set(ind, i, 6, 5, 7); i += 3;

            Set(ind, i, 7, 5, 1); i += 3;
            Set(ind, i, 6, 4, 5); i += 3;

            Set(ind, i, 4, 0, 1); i += 3;
            Set(ind, i, 4, 1, 5); i += 3;

            Set(ind, i, 2, 0, 4); i += 3;
            Set(ind, i, 2, 4, 6); i += 3;
            

            mvc = new MeanValueCoordinates(verts, ind);
            coordinates = new List<float>[meshVerts.Length];

            for (i = 0; i < meshVerts.Length; ++i)
            {
                var v = meshVerts[i];
                var vertexCoordinates = mvc.getCoordinates(v);
                coordinates[i] = vertexCoordinates;
            }
        }

        private void Set(int[] array, int at, int a, int b, int c)
        {
            array[at] = a;
            array[at + 1] = b;
            array[at + 2] = c;
        }

        private void Set(int[] array, int at, int i, int ir, int it, int irt)
        {
            array[at] = i;
            array[at + 1] = ir;
            array[at + 2] = it;

            array[at + 3] = ir;
            array[at + 4] = irt;
            array[at + 5] = it;
        }

        void Update()
        {
            if (doSetup)
            {
                doSetup = false;
                setup();
            }

            if (doUpdate)
            {
                doUpdate = false;
                updateVertices();
            }
        }

        private void updateVertices()
        {
            for (int i = 0; i < lattice.Grid.Vertices.Length; ++i)
            {
                mvc.Vertices[i] = lattice.Grid.Vertices[i];
            }

            var verts = mesh.vertices;

            for (int vertexIndex = 0; vertexIndex < this.coordinates.Length; vertexIndex++)
            {
                var vertexCoordinates = coordinates[vertexIndex];

                if (vertexCoordinates != null && vertexCoordinates.Count > 0)
                {
                    var position = mvc.evaluate(vertexCoordinates);
                    verts[vertexIndex] = position;
                }
            }

            mesh.vertices = verts;
            mesh.RecalculateNormals();
            meshFilter.sharedMesh = mesh;
        }

        class CoordinateCalculator
        {
            public float getCoordinate(Vector3 vertex, Vector3 boundaryVertex, List<Vector3[]> triangles)
            {
                float wI = 0;

                string msg = "c[ ";
                for (int j = 0; j < triangles.Count; j++)
                {
                    msg += ("- " + getPiFactor(vertex, triangles[j]));
                    wI += getPiFactor(vertex, triangles[j]);

                    if (j == 0)
                    {
                        getPiFactor(vertex, triangles[j], false);
                    }
                }

                msg += ("c ] sum:" + wI + " dist:" + Vector3.Distance(vertex, boundaryVertex) + " res:"  + wI / Vector3.Distance(vertex, boundaryVertex));
                //Debug.Log(msg);
                return wI / Vector3.Distance(vertex, boundaryVertex);
            }

            float getPiFactor(Vector3 v, Vector3[] triangle, bool log = false)
            {
                Vector3 vertexI = (triangle[0] - v).normalized;
                Vector3 vertexJ = (triangle[1] - v).normalized;
                Vector3 vertexK = (triangle[2] - v).normalized;

                float angleJK = angleBetweenVectors(vertexJ, vertexK);
                float angleIJ = angleBetweenVectors(vertexI, vertexJ);
                float angleKI = angleBetweenVectors(vertexK, vertexI);

                Vector3 iXj = Vector3.Cross(vertexI, vertexJ);
                Vector3 nJK = Vector3.Cross(vertexJ, vertexK);
                Vector3 nKI = Vector3.Cross(vertexK, vertexI);

                nJK.Normalize();

                if (log)
                {
                    if (log)
                    {
                        var v0 = vertexI;
                        var v1 = vertexJ;
                        var v2 = vertexK;
                        var v0c1 = iXj;
                        var v1c2 = nJK;
                        var v0c2 = nKI;
                        Debug.LogFormat("pi v0({0}, {1}, {2}) v1({3}, {4}, {5}) v2({6}, {7}, {8}) a({9}, {10}, {11}) v0c1({12}, {13}, {14}) v1c2({15}, {16}, {17}) v0c2({18}, {19}, {20})",
                            v0.x, v0.y, v0.z,
                            v1.x, v1.y, v1.z,
                            v2.x, v2.y, v2.z,
                            angleIJ, angleJK, angleKI,
                            v0c1.x, v0c1.y, v0c1.z,
                            v1c2.x, v1c2.y, v1c2.z,
                            v0c2.x, v0c2.y, v0c2.z
                            );

                        float r0 = angleJK;
                        float r1 = Vector3.Dot(iXj, nJK) * angleIJ / iXj.magnitude;
                        float r2 = Vector3.Dot(nKI, nJK) * angleKI / nKI.magnitude;
                        float r3 = (angleJK +
                    Vector3.Dot(iXj, nJK) * angleIJ / iXj.magnitude +
                    Vector3.Dot(nKI, nJK) * angleKI / nKI.magnitude);
                        float r4 = Vector3.Dot(vertexI, nJK);

                        Debug.Log("r:" + r0 + "," + r1 + "," + r2 + "," + r3 + "," + r4 + "res" + ((angleJK +
                    Vector3.Dot(iXj, nJK) * angleIJ / iXj.magnitude +
                    Vector3.Dot(nKI, nJK) * angleKI / nKI.magnitude) /
                    Vector3.Dot(vertexI, nJK) * 2)
                            );
                    }
                }

                return (angleJK + 
                    Vector3.Dot(iXj, nJK) * angleIJ / iXj.magnitude +
                    Vector3.Dot(nKI, nJK) * angleKI / nKI.magnitude) /
                    Vector3.Dot(vertexI, nJK) * 2;
            }

            float angleBetweenVectors(Vector3 v, Vector3 w)
            {
                float theta = Vector3.Dot(v, w) / Mathf.Sqrt(w.sqrMagnitude * v.sqrMagnitude);
                return Mathf.Acos(Mathf.Clamp(theta, -1, 1));
            }
        }

        class VertexTrianglesMap
        {
            private List<Vector3[]>[] vertices;

            public VertexTrianglesMap(Vector3[] verts, int[] ind)
            {
                vertices = new List<Vector3[]>[verts.Length];
                for (int i = 0; i < vertices.Length; ++i)
                {
                    vertices[i] = new List<Vector3[]>();
                }

                int triCount = ind.Length / 3;
                for (int i = 0; i < ind.Length; i += 3)
                {
                    int a = ind[i];
                    int b = ind[i + 1];
                    int c = ind[i + 2];

                    var vertexA = verts[a];
                    var vertexB = verts[b];
                    var vertexC = verts[c];

                    vertices[a].Add(new Vector3[] { vertexA, vertexB, vertexC });
                    vertices[b].Add(new Vector3[] { vertexB, vertexC, vertexA });
                    vertices[c].Add(new Vector3[] { vertexC, vertexA, vertexB });
                }
            }

            public int length()
            {
                return vertices.Length;
            }

            public List<Vector3[]> getTriangles(int index)
            {
                return vertices[index];
            }

        }

        class MeanValueCoordinates
        {
            private Vector3[] vertices;
            public Vector3[] Vertices { get { return vertices; } }
            private VertexTrianglesMap trianglesByVertex;
            private CoordinateCalculator coordinateCalculator;

            public MeanValueCoordinates(Vector3[] vertices, int [] ind)
            {
                this.vertices = vertices;
                trianglesByVertex = new VertexTrianglesMap(vertices, ind);
                coordinateCalculator = new CoordinateCalculator();
            }

            public List<float> getCoordinates(Vector3 vertex)
            {
                var result = new List<float>();
                float sum = 0;

                for (int vertexIndex = 0; vertexIndex < trianglesByVertex.length(); vertexIndex++)
                {
                    var boundaryVertex = vertices[vertexIndex];
                    var triangles = trianglesByVertex.getTriangles(vertexIndex);
                    var coordinate = coordinateCalculator.getCoordinate(vertex, boundaryVertex, triangles);

                    result.Add(coordinate);
                    sum += coordinate;
                }

                for (int i = 0; i < result.Count; i++)
                {
                    result[i] /= sum;
                }

                return result;
            }

            public Vector3 evaluate(List<float> coordinates)
            {
                Vector3 result = new Vector3();
                float total = 0;

                for (int vertexIndex = 0; vertexIndex < coordinates.Count; vertexIndex++)
                {
                    float coefficient = coordinates[vertexIndex];

                    if (coefficient > 0)
                    {
                        Vector3 boundaryVertex = vertices[vertexIndex];

                        result += boundaryVertex * coefficient;
                        total += coefficient;
                    }
                }

                result /= total;

                return result;
            }
        }
    }
}
