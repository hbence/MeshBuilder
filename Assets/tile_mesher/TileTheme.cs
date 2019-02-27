using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

using Direction = MeshBuilder.Tile.Direction;
using Piece = MeshBuilder.Tile.Piece;

namespace MeshBuilder
{
    // todo: handling custom pieces, or perhaps that could be handled differently? as patches or something

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
    [System.Serializable]
    [CreateAssetMenu(fileName = "tile_theme", menuName = "Custom/TileTheme", order = 1)]
    public sealed partial class TileTheme : ScriptableObject
    {
        public const int Type2DConfigCount = 16;
        public const int Type3DConfigCount = 256;

        public enum Type
        {
            Type2DFull,
            Type2DMirrorX,
            Type2DMirrorY,
            Type2DMirrorXY,
            Type2DSimple, // 1 corner piece, 1 side piece, 1 missing corner piece, 1 full
            Type2DCustom, // create from a custom set, no warning for anything missing

            Type3DFull,
        }

        [SerializeField]
        private string themeName;
        public string ThemeName { get { return themeName; } }

        [SerializeField]
        private Type type;
        public Type ThemeType { get { return type; } }

        [SerializeField]
        private string[] openTowardsThemes;
        public string[] OpenTowardsThemes { get { return openTowardsThemes; } }

        [SerializeField]
        private BaseMeshVariants[] baseVariants;
        public BaseMeshVariants[] BaseVariants { get { return baseVariants; } }

        private NativeArray<ConfigTransformGroup> configs;
        public NativeArray<ConfigTransformGroup> Configs { get { return configs; } }

        public void Init()
        {
            if (!configs.IsCreated)
            {
                int count = GetTypeConfigCount(type);
                configs = new NativeArray<ConfigTransformGroup>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                FillConfigurations();
            }
        }

        public void Destroy()
        {
            if (configs.IsCreated)
            {
                configs.Dispose();
            }
        }

        private void FillConfigurations()
        {
            for (byte i = 0; i < configs.Length; ++i)
            {
                configs[i] = TileConfigurationBuilder.Decompose(i, baseVariants);
 //               Debug.Log(i + ". :");
 //               LogGroup(configs[i]);
//                Debug.Log("---");
            }
        }

        static private void LogGroup(ConfigTransformGroup g)
        {
            for (int i = 0; i < g.Count; ++i)
            {
               Debug.Log(" base:" + g[i].BaseMeshIndex + " mir:" + g[i].MirrorDirection);
            }
        }

        private void OnDestroy()
        {
            Destroy();
        }
        
        public ConfigTransformGroup GetConfigTransform(int config)
        {
            return configs[config];
        }

        static public int GetTypeConfigCount(Type type)
        {
            switch (type)
            {
                case Type.Type2DFull: return Type2DConfigCount;
                case Type.Type2DMirrorX: return Type2DConfigCount;
                case Type.Type2DMirrorY: return Type2DConfigCount;
                case Type.Type2DMirrorXY: return Type2DConfigCount;
                case Type.Type2DSimple: return Type2DConfigCount;
                case Type.Type2DCustom: return Type2DConfigCount;

                case Type.Type3DFull: return Type3DConfigCount;
                default:
                    {
                        Debug.LogError("Type:" + type + " has no piece count set!");
                        break;
                    }
            }

            return Type3DConfigCount;
        }

        public static readonly ConfigTransform NullTransform = new ConfigTransform(0, Direction.None);

        [System.Serializable]
        public class BaseMeshVariants
        {
            [SerializeField]
            private Piece pieceConfig;
            public Piece PieceConfig { get { return pieceConfig; } }

            [SerializeField]
            private Mesh[] variants;
            public Mesh[] Variants { get { return variants; } }
        }

        /// <summary>
        /// what transformation needs to happen to fit the meshes to the config
        /// in practice: the BaseMeshIndex is an index for base meshes, which cover some basic configurations
        /// this struct describes how to use the basics to cover different configurations
        /// </summary>
        [System.Serializable]
        public struct ConfigTransform
        {
            private byte baseMeshIndex;
            public byte BaseMeshIndex { get { return baseMeshIndex; } }

            private Direction mirrorDirection;
            public Direction MirrorDirection { get { return mirrorDirection; } }

            public ConfigTransform(byte baseMeshIndex, Direction mirrorDirection)
            {
                this.baseMeshIndex = baseMeshIndex;
                this.mirrorDirection = mirrorDirection;
            }
        }

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