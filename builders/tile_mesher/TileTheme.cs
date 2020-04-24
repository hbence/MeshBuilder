using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

using static MeshBuilder.Utils;
using Piece = MeshBuilder.Tile.Piece;
using PieceTransform = MeshBuilder.Tile.PieceTransform;

namespace MeshBuilder
{
    // TODO: option to set debug piece so when a nullconfig would be used, it gets replaced by the debug piece
    // TODO: handling custom pieces, or perhaps that could be handled differently? as patches or something


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
    [System.Serializable]
    [CreateAssetMenu(fileName = "tile_theme", menuName = "Custom/TileTheme", order = 1)]
    public sealed partial class TileTheme : ScriptableObject, System.IDisposable
    {
        public const int Type2DConfigCount = 16;
        public const int Type3DConfigCount = 256;

        public enum Type
        {
            Type2DFull,
            Type2DSimple, // 1 corner piece, 1 side piece, 1 missing corner piece, 1 full
            Type2DCustom, // create from a custom set, no warning for anything missing

            Type3DFull,
            Type3DCustom
        }

        [SerializeField]
        private string themeName = "theme";
        public string ThemeName { get { return themeName; } }

        [SerializeField]
        private Type type = Type.Type3DCustom;
        public Type ThemeType { get { return type; } }

        [SerializeField]
        private BaseMeshVariants[] baseVariants;
        public BaseMeshVariants[] BaseVariants { get { return baseVariants; } }

        private NativeArray<ConfigTransformGroup> configs;
        public NativeArray<ConfigTransformGroup> Configs { get { return configs; } }

        public MeshCache TileThemeCache { get; private set; }

        private int refCount = 0;

        public void Init()
        {
            VerifyBaseVariants();

            if (!configs.IsCreated)
            {
                int count = GetTypeConfigCount(type);
                configs = new NativeArray<ConfigTransformGroup>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                FillConfigurations();
            }

            if (TileThemeCache == null)
            {
                foreach (var configVariants in baseVariants)
                {
                    foreach (var variant in configVariants.Variants)
                    {
                        if (variant == null)
                        {
                            Debug.LogError("theme: " + themeName + " config:"+ ConfigToString(configVariants.PieceConfig) + " has null mesh!");
                        }
                    }
                }

                TileThemeCache = new MeshCache(baseVariants, Allocator.Persistent);
            }
        }

        public void Dispose()
        {
            SafeDispose(ref configs);
            TileThemeCache?.Dispose();
            TileThemeCache = null;

            refCount = 0;
        }

        private void OnEnable()
        {
            refCount = 0;
        }

        private void OnDisable()
        {
            Dispose();
        }

        public void Retain()
        {
            if (!configs.IsCreated)
            {
                Init();
            }

            ++refCount;
        }

        public void Release()
        {
            --refCount;

            if (refCount <= 0)
            {
                Dispose();
            }
        }

        public void VerifyBaseVariants()
        {
            if (baseVariants == null || baseVariants.Length == 0)
            {
                Debug.LogWarning(themeName + ": theme has no base mesh pieces!");
                return;
            }

            for (int i = 0; i < baseVariants.Length; ++i)
            {
                var baseVar = BaseVariants[i];
                if (baseVar.Variants == null || baseVar.Variants.Length == 0)
                {
                    Debug.LogWarning(themeName + ": theme has base variant with zero meshes!");
                }

                for (int j = i + 1; j < baseVariants.Length; ++j)
                {
                    if (baseVar.PieceConfig == baseVariants[j].PieceConfig)
                    {
                        Debug.LogWarning(themeName + ": theme has multiple base variants with matching configuration!");
                    }
                }
            }
        }

        private void FillConfigurations()
        {
            List<int> nullConfigs = new List<int>();
            // the first and last cases are empty (void and inside a mesh)
            for (int i = 1; i < configs.Length; ++i)
            {
                configs[i] = TileConfigurationBuilder.Decompose((byte)i, baseVariants);
                if (i > 0 && configs[i].Count == 0)
                {
                    nullConfigs.Add(i);
                }
            }

            configs[0] = NullTransformGroup;

            if (nullConfigs.Count > 0)
            {
                Debug.LogWarning(themeName + ": There are uncovered configurations!");
                foreach (var c in nullConfigs)
                {
                    Debug.LogWarning("config: " + c);
                }
            }
        }

        private void OnDestroy()
        {
            Dispose();
        }
        
        public ConfigTransformGroup GetConfigTransform(int config)
        {
            return configs[config];
        }

        public void FillBaseVariantsFromMeshList(List<Mesh> meshes, string filter, string configPrefix)
        {
            List<VariantInfo> results = new List<VariantInfo>();
            for (int i = 0; i < meshes.Count; ++i)
            {
                var mesh = meshes[i];

                if (filter != null && filter.Length > 0 && !mesh.name.Contains(filter))
                {
                    continue;
                }

                byte top = 0;
                byte bottom = 0;
                if (FindConfigFromMeshName(mesh.name, out top, out bottom, configPrefix))
                {
                    bool added = false;
                    top = (byte)(top >> 4);
                    Piece topPiece = Tile.ToPiece(top);
                    Piece bottomPiece = Tile.ToPiece(bottom);
                    foreach (var info in results)
                    {
                        if (info.IsVariant(topPiece, bottomPiece))
                        {
                            info.Add(mesh);
                            added = true;
                            break;
                        }
                    }

                    if (!added)
                    {
                        results.Add(new VariantInfo(topPiece, bottomPiece, mesh));
                    }
                }
                else
                {
                    Debug.LogWarning(themeName + ": Couldn't find configuration in mesh name:" + mesh.name);
                }
            }

            if (results.Count > 0)
            {
                baseVariants = new BaseMeshVariants[results.Count];

                for (int i = 0; i < baseVariants.Length; ++i)
                {
                    baseVariants[i] = results[i].ToBaseMeshVariants();
                }

                VerifyBaseVariants();
            }
            else
            {
                Debug.LogWarning(themeName + ": Couldn't find any mesh variants!");
            }
        }

        public bool Is2DTheme { get { return Is2DType(type); } }
        public bool Is3DTheme { get { return !Is2DType(type); } }

        private static string ConfigToString(byte config)
        {
            string result = "";
            byte testFlag = 1 << 7;
            for (int i = 0; i < 8; ++i)
            {
                result += ((config & testFlag) != 0) ? '1' : '0';
                testFlag >>= 1;
            }
            result = result.Insert(3, "-");
            return result;
        }

        private class VariantInfo
        {
            public Piece Top { get; private set; } 
            public Piece Bottom { get; private set; } 
            public List<Mesh> Meshes { get; private set; }

            public VariantInfo(Piece top, Piece bottom, Mesh mesh)
            {
                Top = top;
                Bottom = bottom;
                Meshes = new List<Mesh>();
                Meshes.Add(mesh);
            }

            public bool IsVariant(Piece top, Piece bottom) { return Top == top && Bottom == bottom; }
            public void Add(Mesh mesh) { Meshes.Add(mesh); }
            public BaseMeshVariants ToBaseMeshVariants() { return new BaseMeshVariants(Top, Bottom, Meshes.ToArray()); }
        }

        private const byte TopMask = 0b11110000;
        private const byte BottomMask = 0b00001111;

        /// <summary>
        /// Find the configuration of a piece based on the mesh name.
        /// The configuration in the name has to be an uninterrupted substring of 1s and 0s,
        /// 4 or 8 character long. If there are multiple substrings like that, the first will be used.
        /// </summary>
        /// <param name="name">The name of the mesh where it is checking.</param>
        /// <param name="config">The configuration it found.</param>
        /// <returns>true if found a valid configuration</returns>
        static private bool FindConfigFromMeshName(string name, out byte top, out byte bottom, string prefix)
        {
            bool found = false;
            top = 0;
            bottom = 0;

            byte current = 0;
            int count = 0;

            int startIndex = (prefix == null || prefix.Length == 0) ? 0 : name.IndexOf(prefix) + prefix.Length;
            for (int i = startIndex; i < name.Length; ++i)
            {
                var c = name[i];
                if (c == '0')
                {
                    current = (byte)(current << 1);
                    ++count;
                }
                else if (c == '1')
                {
                    current = (byte)(current << 1);
                    current |= 1;
                    ++count;
                }
                else
                {
                    if (count >= 8)
                    {
                        top = (byte)(current & TopMask);
                        bottom = (byte)(current & BottomMask);
                        found = true;
                    }
                    else if (count == 4)
                    {
                        top = bottom;
                        bottom = (byte)(current & BottomMask);
                        found = true;
                    }

                    if (count >= 8)
                    {
                        break;
                    }
                }
            }

            if (count >= 8)
            {
                top = (byte)(current & TopMask);
                bottom = (byte)(current & BottomMask);
                found = true;
            }
            else if (count == 4)
            {
                top = bottom;
                bottom = (byte)(current & BottomMask);
                found = true;
            }

            return found;
        }

        static public int GetTypeConfigCount(Type type)
        {
            return Is2DType(type) ? Type2DConfigCount : Type3DConfigCount;
        }

        static public bool Is2DType(Type type)
        {
            switch (type)
            {
                case Type.Type2DFull: return true;
                case Type.Type2DSimple: return true;
                case Type.Type2DCustom: return true;

                case Type.Type3DFull: return false;
                case Type.Type3DCustom: return false;
            }
            Debug.LogWarning("case not handled!");
            return false;
        }

        [System.Serializable]
        public class BaseMeshVariants
        {
            [SerializeField]
            private Piece topConfig;

            [SerializeField]
            private Piece bottomConfig;
            
            public byte PieceConfig { get { return (byte)((byte)bottomConfig | ((byte)topConfig << 4)); } }

            [SerializeField]
            private Mesh[] variants;
            public Mesh[] Variants { get { return variants; } }

            public BaseMeshVariants(Piece top, Piece bottom, Mesh[] variantMeshes)
            {
                topConfig = top;
                bottomConfig = bottom;

                variants = new Mesh[variantMeshes.Length];

                System.Array.Copy(variantMeshes, variants, variants.Length);
            }
        }

        /// <summary>
        /// what transformation needs to happen to fit the meshes to the config
        /// in practice: the BaseMeshIndex is an index for base meshes, which cover some basic configurations
        /// this struct describes how to use the basics to cover different configurations
        /// </summary>
        [System.Serializable]
        public struct ConfigTransform
        {
            private int baseMeshIndex;
            public int BaseMeshIndex { get { return baseMeshIndex; } }

            private PieceTransform pieceTransform;
            public PieceTransform PieceTransform { get { return pieceTransform; } }

            public ConfigTransform(int baseMeshIndex, PieceTransform pieceTransform)
            {
                this.baseMeshIndex = baseMeshIndex;
                this.pieceTransform = pieceTransform;
            }
        }

        public static readonly ConfigTransform NullTransform = new ConfigTransform(-1, PieceTransform.None);
        public static readonly ConfigTransformGroup NullTransformGroup = new ConfigTransformGroup(new ConfigTransform(-1, PieceTransform.None)); 

        /// <summary>
        /// Multiple meshes can be used to handle a configuration. For example, the "diagonal corners" case
        /// can be handled automatically by using a single corner twice with different transformations.
        /// 
        /// NOTE: this has to be blittable, so no array 
        /// TODO: (should there be a smaller version for 2d tiles?)
        /// </summary>
        public struct ConfigTransformGroup
        {
            private const int MaxCount = 4;

            private byte count;
            private ConfigTransform configTransform0; 
            private ConfigTransform configTransform1; 
            private ConfigTransform configTransform2; 
            private ConfigTransform configTransform3;

            public ConfigTransformGroup(ConfigTransform p0) : this(1, p0, NullTransform, NullTransform, NullTransform) { }
            public ConfigTransformGroup(ConfigTransform p0, ConfigTransform p1) : this(2, p0, p1, NullTransform, NullTransform) { }
            public ConfigTransformGroup(ConfigTransform p0, ConfigTransform p1, ConfigTransform p2) : this(3, p0, p1, p2, NullTransform){ }
            public ConfigTransformGroup(ConfigTransform p0, ConfigTransform p1, ConfigTransform p2, ConfigTransform p3) : this(4, p0, p1, p2, p3) { }

            private ConfigTransformGroup(byte count, ConfigTransform p0, ConfigTransform p1, ConfigTransform p2, ConfigTransform p3)
            {
                this.count = count;
                configTransform0 = p0;
                configTransform1 = p1;
                configTransform2 = p2;
                configTransform3 = p3;
            }

            public ConfigTransformGroup(ConfigTransform[] p)
            {
                if (p.Length > MaxCount)
                {
                    Debug.LogError("more than 4 config transforms!");
                }

                configTransform0 = p.Length > 0 ? p[0] : NullTransform;
                configTransform1 = p.Length > 1 ? p[1] : NullTransform;
                configTransform2 = p.Length > 2 ? p[2] : NullTransform;
                configTransform3 = p.Length > 3 ? p[3] : NullTransform;
                count = (byte)Mathf.Min(p.Length, MaxCount);
            }

            public ConfigTransformGroup(List<ConfigTransform> p)
            {
                if (p.Count > 4)
                {
                    Debug.LogError("more than 4 config transforms");
                }

                configTransform0 = p.Count > 0 ? p[0] : NullTransform;
                configTransform1 = p.Count > 1 ? p[1] : NullTransform;
                configTransform2 = p.Count > 2 ? p[2] : NullTransform;
                configTransform3 = p.Count > 3 ? p[3] : NullTransform;
                count = (byte)Mathf.Min(p.Count, MaxCount);
            }

            public ConfigTransform this[int i]
            {
                get
                {
                    switch(i)
                    {
                        case 0: return configTransform0;
                        case 1: return configTransform1;
                        case 2: return configTransform2;
                        case 3: return configTransform3;
                        default:
                            // TODO: Can't use this in Burst, any way to warn user?
                            // Debug.LogWarning("invalid index");
                            break;
                    }
                    return configTransform0;
                }
            }

            public bool IsNullConfig { get { return count == 0; } }
            public int Count { get { return count; } }
        }
    }
}