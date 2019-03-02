using System.Collections.Generic;
using UnityEngine;

using Piece = MeshBuilder.Tile.Piece;
using PieceTransform = MeshBuilder.Tile.PieceTransform;

namespace MeshBuilder
{
    public sealed partial class TileTheme
    {
        private partial class TileConfigurationBuilder
        {
            // x axis -
            // z axis |
            //
            // 7 6
            // 5 4
            //----- top/bottom part along y axis
            // 3 2
            // 1 0

            private const byte TopMask = 0b11110000;
            private const byte BottomMask = 0b00001111;

            private const bool DontInvert = false;
            private const bool FindInverse = true;

            static public ConfigTransformGroup Decompose(byte config, BaseMeshVariants[] basicConfigs)
            {
                ConfigTransform configTransform;
                bool isBasicTransform = FindBasicElemTransform(config, basicConfigs, out configTransform);
                if (isBasicTransform)
                {
                    return new ConfigTransformGroup(configTransform);
                }
                else
                {
                    var result = DecomposeComplexConfig(config, DontInvert, basicConfigs);

                    if (result.IsNullConfig)
                    {
                        result = DecomposeComplexConfig(config, FindInverse, basicConfigs);
                    }

                    if (!result.IsNullConfig)
                    {
                        if (result.Count == 1)
                        {
                            Debug.LogWarning("this shouldn't happen! complex decomposition produced 1 part piece! - " + System.Convert.ToString(config, 2).PadLeft(8,'0'));
                        }

                        return result;
                    }
                }
                
                return new ConfigTransformGroup { };
            }

            static private ConfigTransformGroup DecomposeComplexConfig(byte config, bool findInverse, BaseMeshVariants[] basics)
            {
                List<ConfigTransform> result = new List<ConfigTransform>();

                const int MaxElems = 4;
                for (int i = 0; i < MaxElems; ++i)
                {
                    if (CountCells(config) == 0)
                    {
                        break;
                    }

                    byte elem = 0;
                    if (FindFirstDistinct(config, out elem))
                    {
                        config = RemoveCells(config, elem);

                        if (findInverse)
                        {
                            elem = Invert(elem);
                        }

                        ConfigTransform configTransform;
                        if (FindBasicElemTransform(elem, basics, out configTransform))
                        {
                            result.Add(configTransform);
                        }
                    }
                    else
                    {
                        Debug.LogError("this shouldn't happen, non zero number of cells and no distinct piece");
                    }
                }

                return new ConfigTransformGroup(result);
            }

            /// <summary>
            /// Find a distinct piece in the configuration. Distinct in this case means not having filled adjacent cells. (e.g. two diagonal corners)
            /// </summary>
            /// <param name="value">the configuration where we are searching</param>
            /// <param name="distinctConfig">the found distinct configuration piece</param>
            /// <returns>true if found a distinct piece</returns>
            static private bool FindFirstDistinct(byte value, out byte distinctConfig)
            {
                distinctConfig = 0;
                int startIndex = FindFirstFullCell(value);
                if (startIndex >= 0)
                {
                    distinctConfig = FindConnected(value, startIndex);
                    return true;
                }
                return false;
            }

            /// <summary>
            /// find the first filled cel, it starts from the low bits, but it shouldn't matter
            /// </summary>
            /// <param name="value"></param>
            /// <returns>the index of the filled cell</returns>
            static int FindFirstFullCell(byte value)
            {
                for (int i = 0; i < 8; ++i)
                {
                    int mask = 1 << i;
                    if ((value & mask) > 0)
                    {
                        return i;
                    }
                }
                return -1;
            }

            /// <summary>
            /// start from a filled cell and find every cell which can be connected through adjacent steps
            /// </summary>
            /// <param name="value">the configuration where we search</param>
            /// <param name="startIndex">the index where we start the search</param>
            /// <returns>the piece of the configuration which is connected to the start index cell</returns>
            static byte FindConnected(byte value, int startIndex)
            {
                if (IsFilled(value, startIndex) == false)
                {
                    Debug.LogWarning("can't find connected if start index is empty");
                    return 0;
                }
                byte result = (byte)(1 << startIndex);

                result |= FindConnectedSameLevel(value, startIndex);

                int otherSideStart = -1;
                int test = startIndex;
                for (int i = 0; i < 4; ++i)
                {
                    int opposite = OppositeIndex(test);
                    if (IsFilled(value, test) && IsFilled(value, opposite))
                    {
                        startIndex = opposite;
                        result |= FillCell(result, opposite);
                    }
                }
                
                if (otherSideStart >= 0)
                {
                    result |= FindConnectedSameLevel(value, otherSideStart);
                }

                return result;
            }

            /// <summary>
            /// find which cells are connected to the one at startIndex, check only the same level (on the y axis)
            /// </summary>
            /// <param name="value">the value where we are searching</param>
            /// <param name="startIndex">where to start the search, this has to be a filled cell</param>
            /// <returns></returns>
            static byte FindConnectedSameLevel(byte value, int startIndex)
            {
                if (IsFilled(value, startIndex) == false)
                {
                    Debug.LogWarning("can't find connected if start index is empty");
                    return 0;
                }
                byte result = (byte)(1 << startIndex);

                int next = NextIndex(startIndex);
                if (IsFilled(value, next))
                {
                    result = FillCell(result, next);

                    next = NextIndex(next);
                    if (IsFilled(value, next))
                    {
                        result = FillCell(result, next);
                    }
                }

                int prev = PrevIndex(startIndex);
                if (IsFilled(value, prev))
                {
                    result = FillCell(result, prev);

                    prev = PrevIndex(prev);
                    if (IsFilled(value, prev))
                    {
                        result = FillCell(result, prev);
                    }
                }

                return result;
            }

            /// <summary>
            /// can these basic configurations turned into the given value?
            /// </summary>
            /// <param name="value">the value we are trying to match</param>
            /// <param name="basicConfigs">the basic configurations we are testing</param>
            /// <param name="configTransform">the result which contains the basic configuration and the needed transformation</param>
            /// <returns>true if found a transformation</returns>
            static private bool FindBasicElemTransform(byte value, BaseMeshVariants[] basicConfigs, out ConfigTransform configTransform)
            {
                configTransform = NullTransform;

                // first try to match every base shape, so if multiple could be used with transformations
                // then use the one which doesn't require any transformations
                for (byte i = 0; i < basicConfigs.Length; ++i)
                {
                    var basic = basicConfigs[i];
                    if (value == (byte)basic.PieceConfig)
                    {
                        configTransform = new ConfigTransform(i, PieceTransform.None);
                        return true;
                    }
                }

                // no direct match, so try to transform them
                PieceTransform transform;
                for (byte i = 0; i < basicConfigs.Length; ++i)
                {
                    var basic = basicConfigs[i];
                    if (FindTransform(value, (byte)basic.PieceConfig, out transform))
                    {
                        configTransform = new ConfigTransform(i, transform);
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// can value configuration be transformed into target configuration
            /// </summary>
            /// <param name="value"></param>
            /// <param name="target"></param>
            /// <param name="mirrorDirection"></param>
            /// <returns></returns>
            static private bool FindTransform(byte value, byte target, out PieceTransform transform)
            {
                transform = PieceTransform.None;
                
                if (value == target)
                {
                    return true;
                }

                byte value90 = RotateCW90(value);
                byte value180 = RotateCW90(value90);
                byte value270 = RotateCW90(value180);

                byte valueMirX = MirrorX(value);
                byte valueMirX90 = RotateCW90(valueMirX);
                byte valueMirX180 = RotateCW90(valueMirX90);
                byte valueMirX270 = RotateCW90(valueMirX180);
                
                byte[] values = { value90, value180, value270, valueMirX, valueMirX90, valueMirX180, valueMirX270 };
                PieceTransform[] dirs = 
                {
                    PieceTransform.Rotate90, PieceTransform.Rotate180, PieceTransform.Rotate270,
                    PieceTransform.MirrorX, PieceTransform.MirrorX | PieceTransform.Rotate90, PieceTransform.MirrorX | PieceTransform.Rotate180, PieceTransform.MirrorX | PieceTransform.Rotate270
                };

                for (int i = 0; i < values.Length; ++i)
                {
                    if (values[i] == target)
                    {
                        transform = dirs[i];
                        return true;
                    }
                }

                // this is handled separately because y mirroring will be optional,
                // there will be tile sets where it is banned (and for 2d it wouldn't even make sense)
                for (int i = 0; i < values.Length; ++i)
                {
                    if (MirrorY(values[i]) == target)
                    {
                        transform = dirs[i] | PieceTransform.MirrorY;
                        return true;
                    }
                }

                return false;
            }

            static private byte RemoveCells(byte from, byte value)
            {
                return (byte)(from ^ (from & value));
            }

            static private byte Invert(byte value)
            {
                return (byte)~value;
            }

            static private byte MirrorX(byte value)
            {
                byte result = 0;
                result = FillCell(result, 7, IsFilled(value, 6));
                result = FillCell(result, 6, IsFilled(value, 7));

                result = FillCell(result, 5, IsFilled(value, 4));
                result = FillCell(result, 4, IsFilled(value, 5));

                result = FillCell(result, 3, IsFilled(value, 2));
                result = FillCell(result, 2, IsFilled(value, 3));

                result = FillCell(result, 1, IsFilled(value, 0));
                result = FillCell(result, 0, IsFilled(value, 1));
                return result;
            }

            static private byte MirrorY(byte value)
            {
                byte result = 0;
                result = FillCell(result, 7, IsFilled(value, 3));
                result = FillCell(result, 6, IsFilled(value, 2));

                result = FillCell(result, 5, IsFilled(value, 1));
                result = FillCell(result, 4, IsFilled(value, 0));

                result = FillCell(result, 3, IsFilled(value, 7));
                result = FillCell(result, 2, IsFilled(value, 6));

                result = FillCell(result, 1, IsFilled(value, 5));
                result = FillCell(result, 0, IsFilled(value, 4));
                return result;
            }

            static private byte MirrorZ(byte value)
            {
                byte result = 0;
                result = FillCell(result, 7, IsFilled(value, 5));
                result = FillCell(result, 6, IsFilled(value, 4));

                result = FillCell(result, 5, IsFilled(value, 7));
                result = FillCell(result, 4, IsFilled(value, 6));

                result = FillCell(result, 3, IsFilled(value, 1));
                result = FillCell(result, 2, IsFilled(value, 0));

                result = FillCell(result, 1, IsFilled(value, 3));
                result = FillCell(result, 0, IsFilled(value, 2));
                return result;
            }

            static private byte RotateCW90(byte value)
            {
                byte result = 0;
                result = FillCell(result, 7, IsFilled(value, 5));
                result = FillCell(result, 6, IsFilled(value, 7));
                result = FillCell(result, 5, IsFilled(value, 4));
                result = FillCell(result, 4, IsFilled(value, 6));

                result = FillCell(result, 3, IsFilled(value, 1));
                result = FillCell(result, 2, IsFilled(value, 3));
                result = FillCell(result, 1, IsFilled(value, 0));
                result = FillCell(result, 0, IsFilled(value, 2));
                return result;
            }

            static private bool IsFilled(byte value, int index) { return (value & (1 << index)) > 0; }
            static private byte FillCell(byte value, int index) { return FillCell(value, index, true); }
            static private byte FillCell(byte value, int index, bool fill) { return fill ? (byte)(value | (byte)(1 << index)) : value; }

            static private int CountBottomCells(byte value)
            {
                int result = 0;
                result += value & 1; value >>= 1;
                result += value & 1; value >>= 1;
                result += value & 1; value >>= 1;
                result += value & 1;
                return result;
            }

            static private int CountTopCells(byte value)
            {
                value >>= 4;

                int result = 0;
                result += value & 1; value >>= 1;
                result += value & 1; value >>= 1;
                result += value & 1; value >>= 1;
                result += value & 1;
                return result;
            }

            static private int CountCells(byte value)
            {
                int result = 0;
                result += value & 1; value >>= 1;
                result += value & 1; value >>= 1;
                result += value & 1; value >>= 1;
                result += value & 1; value >>= 1;
                result += value & 1; value >>= 1;
                result += value & 1; value >>= 1;
                result += value & 1; value >>= 1;
                result += value & 1;
                return result;
            }

            // next index of the flag if rotated clockwise
            static private int NextIndex(int index)
            {
                switch (index)
                {
                    case 0: return 1;
                    case 1: return 3;
                    case 3: return 2;
                    case 2: return 0;

                    case 4: return 5;
                    case 5: return 7;
                    case 7: return 6;
                    case 6: return 4;

                    default:
                        {
                            Debug.LogWarning("invalid index");
                            break;
                        }
                }
                return index;
            }

            // index of the flag if rotated counter clockwise
            static private int PrevIndex(int index)
            {
                switch (index)
                {
                    case 0: return 2;
                    case 2: return 3;
                    case 3: return 1;
                    case 1: return 0;

                    case 4: return 6;
                    case 6: return 7;
                    case 7: return 5;
                    case 5: return 4;

                    default:
                        {
                            Debug.LogWarning("invalid index");
                            break;
                        }
                }
                return index;
            }

            // corresponding index on the other level (y axis)
            static private int OppositeIndex(int index)
            {
                switch (index)
                {
                    case 0: return 4;
                    case 1: return 5;
                    case 2: return 6;
                    case 3: return 7;

                    case 4: return 0;
                    case 5: return 1;
                    case 6: return 2;
                    case 7: return 3;

                    default:
                        {
                            Debug.LogWarning("invalid index");
                            break;
                        }
                }
                return index;
            }
        }
    }
}
