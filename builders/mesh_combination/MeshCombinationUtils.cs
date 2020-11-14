using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using ShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode;

namespace MeshBuilder
{
    public class MeshCombinationUtils
    {
        static public GameObject CreateMergedMesh(GameObject[] gameObjects, bool turnOffOriginalRenderes)
        {
            GameObject root = new GameObject("merged");

            List<MeshFilter> meshFilters = new List<MeshFilter>();

            foreach (var go in gameObjects)
            {
                var children = go.GetComponentsInChildren<MeshFilter>();
                meshFilters.AddRange(children);
            }

            if (meshFilters.Count > 0)
            {
                List<MeshGroup> meshGroups = new List<MeshGroup>();

                MeshRenderer[] renderers = new MeshRenderer[meshFilters.Count];
                for (int i = 0; i < meshFilters.Count; ++i)
                {
                    var renderer = meshFilters[i].gameObject.GetComponent<MeshRenderer>();

                    renderers[i] = renderer;
                    AddToMeshGroup(meshGroups, renderer, i);

                    if (turnOffOriginalRenderes)
                    {
                        renderer.enabled = false;
                    }
                }

                foreach (var group in meshGroups)
                {
                    MeshFilter[] filters = group.CreateFiltersArray(meshFilters);

                    var groupGo = CreateMerged(group.CreateName(), filters, group.Materials, group.Layer, group.ReceiveShadow, group.CastShadow);
                    groupGo.transform.SetParent(root.transform);

                }
            }

            return root;
        }

        private static void AddToMeshGroup(List<MeshGroup> groups, MeshRenderer renderer, int index)
        {
            bool added = false;
            foreach (var group in groups)
            {
                if (group.DoesMatch(renderer))
                {
                    group.AddIndex(index);
                    added = true;
                    break;
                }
            }
            if (!added)
            {
                var group = MeshGroup.CreateFromRenderer(renderer);
                group.AddIndex(index);
                groups.Add(group);
            }
        }

        private class MeshGroup
        {
            public int Layer { get; }
            public bool ReceiveShadow { get; }
            public ShadowCastingMode CastShadow { get; }
            public Material[] Materials { get; }

            public List<int> Indices { get; }

            public MeshGroup(int layer, Material[] materials, bool receiveShadow, ShadowCastingMode castShadow)
            {
                Layer = layer;
                Materials = materials;
                ReceiveShadow = receiveShadow;
                CastShadow = castShadow;
                Indices = new List<int>();
            }

            public bool DoesMatch(MeshRenderer render)
            {
                if (Layer == render.gameObject.layer && ReceiveShadow == render.receiveShadows && CastShadow == render.shadowCastingMode)
                {
                    var sharedMats = render.sharedMaterials;
                    if (Materials != null && sharedMats != null && Materials.Length == sharedMats.Length)
                    {
                        for (int i = 0; i < Materials.Length; ++i)
                        {
                            if (Materials[i] != sharedMats[i])
                            {
                                return false;
                            }
                        }
                        return true;
                    }
                }
                return false;
            }

            public MeshFilter[] CreateFiltersArray(List<MeshFilter> filters)
            {
                var result = new MeshFilter[Indices.Count];
                for (int i = 0; i < result.Length; ++i)
                {
                    int filterIndex = Indices[i];
                    result[i] = filters[filterIndex];
                }
                return result;
            }

            public string CreateName()
            {
                string res = ">|";

                foreach (var mat in Materials)
                {
                    res += " mat:" + mat.name;
                }

                res += " | layer:" + LayerMask.LayerToName(Layer);
                res += " | rec_shadow:" + (ReceiveShadow ? "Y" : "N");
                res += " | cast_shadow:" + CastShadow;
                return res;
            }

            public static MeshGroup CreateFromRenderer(MeshRenderer renderer)
            {
                return new MeshGroup(renderer.gameObject.layer, renderer.sharedMaterials, renderer.receiveShadows, renderer.shadowCastingMode);
            }

            public void AddIndex(int index) { Indices.Add(index); }
        }

        static private GameObject CreateMerged(string name, MeshFilter[] meshFilters, Material[] materials, int layer, bool receiveShadow, ShadowCastingMode shadowCasting)
        {
            GameObject go = new GameObject(name);
            go.layer = layer;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.receiveShadows = receiveShadow;
            renderer.shadowCastingMode = shadowCasting;

            Mesh mesh = new Mesh();

            var combinationBuilder = new MeshCombinationBuilder();
            combinationBuilder.Init(meshFilters);
            combinationBuilder.Start();
            combinationBuilder.Complete(mesh);
            combinationBuilder.Dispose();

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            return go;
        }
    }
}