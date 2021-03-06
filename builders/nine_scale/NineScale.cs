﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    using static MeshDrawer;

    [System.Serializable]
    public class NineScale
    {
        private static readonly Matrix4x4 Rotate90 = Matrix4x4.Rotate(Quaternion.AngleAxis(90, Vector3.up));
        private static readonly Matrix4x4 Rotate180 = Matrix4x4.Rotate(Quaternion.AngleAxis(180, Vector3.up));
        private static readonly Matrix4x4 Rotate270 = Matrix4x4.Rotate(Quaternion.AngleAxis(270, Vector3.up));

        public enum ScaleType
        {
            Stretch,
            Repeat
        }

        public enum GetterType
        {
            Sequential,
            Random
        }

        static private T GetRandom<T>(T[] elems) => elems[Random.Range(0, elems.Length)];
        static private T GetElem<T>(T[] elems, int index) => elems[index % elems.Length];

        static private T Get<T>(T[] elems, ref int counter, GetterType getterType) where T : class
        {
            T elem = null;
            if (elems != null && elems.Length > 0)
            {
                if (getterType == GetterType.Sequential)
                {
                    elem = GetElem(elems, counter);
                    ++counter;
                }
                else
                {
                    elem = GetRandom(elems);
                }
            }
            return elem;
        }

        [Header("Top")]
        [SerializeField] private bool hasTop = true;
        [SerializeField] private float topHeight = 0.25f;
        [SerializeField] private LayerSettings TopLayerSetting = null;
        [Header("Middle")]
        [SerializeField] private bool hasMiddle = true;
        [SerializeField] private ScaleType middleVerticalScale = ScaleType.Stretch;
        [SerializeField] private GetterType middleSettingsGetter = GetterType.Sequential;
        [SerializeField] private LayerSettings[] MiddeLayerSetting = null;
        [Header("Bottom")]
        [SerializeField] private bool hasBottom = true;
        [SerializeField] private float bottomHeight = 0.25f;
        [SerializeField] private LayerSettings BottomLayerSetting = null;

        public Layer TopLayer { get; private set; }
        public Layer[] MiddleLayers { get; private set; }
        public Layer BottomLayer { get; private set; }

        private int middleLayerCounter = 0;

        private int seed = (int)System.DateTime.Now.ToBinary();

        private Vector3 lastCenter = Vector3.zero;
        private Quaternion lastRot = Quaternion.identity;
        private Vector3 lastSize = Vector3.zero;

        public NineScale()
        {
            TopLayer = new Layer();
            BottomLayer = new Layer();
        }

        public bool ShouldRecalculate(Vector3 center, Quaternion rot, Vector3 size)
        {
            return lastCenter != center || lastRot != rot || lastSize != size;
        }

        public void Recalculate(Vector3 center, Quaternion rot, Vector3 size)
        {
            Random.InitState(seed);
            middleLayerCounter = 0;

            lastCenter = center;
            lastRot = rot;
            lastSize = size;

            Vector3 pos = center;
            if (hasTop)
            {
                pos.y = center.y + size.y * 0.5f;
                TopLayerSetting.Reset();
                TopLayer.Recalculate(pos, rot, new Vector3(size.x, topHeight, size.z), TopLayerSetting);
            }
            if (hasMiddle)
            {
                float height = size.y - (hasTop ? topHeight : 0) - (hasBottom ? bottomHeight : 0);

                if (middleVerticalScale == ScaleType.Stretch)
                {
                    pos.y = center.y;
                    if (hasTop)
                    {
                        pos.y = center.y + size.y * 0.5f - topHeight - height / 2;
                    }
                    else if (hasBottom)
                    {
                        pos.y = center.y - size.y * 0.5f + bottomHeight + height / 2;
                    }

                    if (MiddleLayers == null)
                    {
                        MiddleLayers = new Layer[1];
                    }
                    MiddleLayers[0] = new Layer();
                    LayerSettings midSetting = Get(MiddeLayerSetting, ref middleLayerCounter, middleSettingsGetter);
                    if (midSetting != null)
                    {
                        height = Mathf.Max(height, 0.01f);
                        midSetting.Reset();
                        MiddleLayers[0].Recalculate(pos, rot, new Vector3(size.x, height, size.z), midSetting);
                    }
                }
                else
                {
                    pos.y = center.y - size.y * 0.5f;
                    pos.y += hasBottom ? bottomHeight : 0;

                    List<LayerSettings> mids = new List<LayerSettings>();
                    LayerSettings midSetting = Get(MiddeLayerSetting, ref middleLayerCounter, middleSettingsGetter);
                    float heightLeft = height;
                    if (heightLeft > midSetting.CornerSize.y)
                    {
                        while (heightLeft > midSetting.CornerSize.y)
                        {
                            mids.Add(midSetting);
                            heightLeft -= midSetting.CornerSize.y;
                            midSetting = Get(MiddeLayerSetting, ref middleLayerCounter, middleSettingsGetter);
                        }
                    }
                    else
                    {
                        mids.Add(midSetting);
                    }

                    if (MiddleLayers == null || MiddleLayers.Length != mids.Count)
                    {
                        MiddleLayers = new Layer[mids.Count];
                    }

                    float remaining = heightLeft / mids.Count;
                    Vector3 midSize = size;
                    for(int i = 0; i < mids.Count; ++i)
                    {
                        MiddleLayers[i] = new Layer();
                        mids[i].Reset();
                        midSize.y = (mids.Count == 1) ? height : mids[i].CornerSize.y + remaining;
                        pos.y += midSize.y * 0.5f;
                        MiddleLayers[i].Recalculate(pos, rot, midSize, mids[i]);
                        pos.y += midSize.y * 0.5f;
                    }
                }
            }
            if (hasBottom)
            {
                pos.y = center.y - size.y * 0.5f;
                BottomLayerSetting.Reset();
                BottomLayer.Recalculate(pos, rot, new Vector3(size.x, bottomHeight, size.z), BottomLayerSetting);
            }
        }

        public void Render(RenderInfo renderInfo, Camera camera, int goLayer)
        {
            if (hasTop)
            {
                TopLayer.Render(renderInfo, camera, goLayer);
            }
            if (hasMiddle && MiddleLayers != null)
            {
                foreach(var layer in MiddleLayers)
                {
                    layer.Render(renderInfo, camera, goLayer);
                }
            }
            if (hasBottom)
            {
                BottomLayer.Render(renderInfo, camera, goLayer);
            }
        }

        public Part[] GatherParts()
        {
            int count = TopLayer.PartCount + BottomLayer.PartCount;
            if (MiddleLayers != null && MiddleLayers.Length > 0)
            {
                foreach (var layer in MiddleLayers)
                {
                    count += layer.PartCount;
                }
            }

            if (count == 0)
            {
                return null;
            }

            Part[] parts = new Part[count];
            int start = 0;

            CopyLayerParts(TopLayer, parts, ref start);
            if (MiddleLayers != null && MiddleLayers.Length > 0)
            {
                foreach (var layer in MiddleLayers)
                {
                    CopyLayerParts(layer, parts, ref start);
                }
            }
            CopyLayerParts(BottomLayer, parts, ref start);

            return parts;
        }

        static private void CopyLayerParts(Layer layer, Part[] buffer, ref int bufferStartIndex)
        {
            if (layer.PartCount > 0)
            {
                System.Array.Copy(layer.Parts, 0, buffer, bufferStartIndex, layer.Parts.Length);
                bufferStartIndex += layer.Parts.Length;
            }
        }

        public class Part
        {
            public Mesh Mesh;
            public Matrix4x4 Matrix;

            public Part() { }
            public Part(Matrix4x4 matrix, Mesh mesh) { Matrix = matrix; Mesh = mesh; }

            public void Render(RenderInfo renderInfo, Camera cam, int layer)
            {
                renderInfo.Draw(Matrix, Mesh, cam, layer);
            }
        }

        public class PartGroup
        {
            public Mesh Mesh;
            public Matrix4x4[] Matrix;

            public void Render(RenderInfo renderInfo, Camera cam, int layer)
            {
                renderInfo.DrawInstanced(Matrix, Mesh, cam, layer);
            }
        }

        public class Layer
        {
            public Part[] Parts = null;

            public int PartCount => Parts == null ? 0 : Parts.Length;

            public void Render(RenderInfo renderInfo, Camera cam, int layer)
            {
                if (Parts != null)
                {
                    foreach (var part in Parts)
                    {
                        part.Render(renderInfo, cam, layer);
                    }
                }
            }

            public void Recalculate(Vector3 pos, Quaternion rot, Vector3 size, LayerSettings layerSettings)
            {
                Matrix4x4 origin = Matrix4x4.TRS(pos, rot, Vector3.one);

                Vector3 scaledCornerSize = layerSettings.CornerSize;
                scaledCornerSize.Scale(layerSettings.CornerScale);

                float centerTargetSizeX = size.x - scaledCornerSize.x * 2;
                float centerTargetSizeZ = size.z - scaledCornerSize.z * 2;

                var parts = new List<Part>();

                // center
                Vector3 centerScale = new Vector3(centerTargetSizeX / layerSettings.CenterSize.x, 1, centerTargetSizeZ / layerSettings.CenterSize.z);
                Matrix4x4 scale;

                float sideX = size.x * 0.5f;
                float sideZ = size.z * 0.5f;
                float borderY = size.y / scaledCornerSize.y;

                int countX = Mathf.CeilToInt(centerTargetSizeX / layerSettings.CenterSize.x);
                int countZ = Mathf.CeilToInt(centerTargetSizeZ / layerSettings.CenterSize.z);
                float pieceScaleX = centerScale.x / countX;
                float pieceScaleZ = centerScale.z / countZ;
                float stepX = centerTargetSizeX / countX;
                float stepZ = centerTargetSizeZ / countZ;
                float startX = -centerTargetSizeX * 0.5f + pieceScaleX;
                float startZ = -centerTargetSizeZ * 0.5f + pieceScaleZ;

                if (layerSettings.CenterScale == ScaleType.Stretch)
                {
                    scale = Scale(centerScale);
                    AddPart(layerSettings.GetCenter(), origin, 0, 0, scale, parts);
                }
                else
                {
                    scale = Scale(pieceScaleX, centerScale.y, pieceScaleZ);
                    for (int z = 0; z < countZ; ++z)
                    {
                        for (int x = 0; x < countX; ++x)
                        {
                            float xPos = startX + x * stepX;
                            float zPos = startZ + z * stepZ;
                            AddPart(layerSettings.GetCenter(), origin, xPos, zPos, scale, parts);
                        }
                    }
                }

                if (layerSettings.EdgeScale == ScaleType.Stretch)
                {
                    scale = Scale(layerSettings.CornerScale.x, borderY, centerScale.z);
                    AddPart(layerSettings.GetRightSide(), origin, sideX, 0, scale, parts);
                    AddPart(layerSettings.GetLeftSide(), origin, -sideX, 0, Rotate180 * scale, parts);
                    scale = Scale(layerSettings.CornerScale.z, borderY, centerScale.x);
                    AddPart(layerSettings.GetFrontSide(), origin, 0, sideZ, Rotate270 * scale, parts);
                    AddPart(layerSettings.GetBackSide(), origin, 0, -sideZ, Rotate90 * scale, parts);
                }
                else
                {
                    // the sides are looped separately so the pieces are placed the same way on every side
                    // for example if you have two pieces then 0101|0101 instead if 0000|1111
                    // perhaps I could just pass an offset to the Getter function insted?
                    scale = Scale(layerSettings.CornerScale.x, borderY, pieceScaleZ);
                    for (int i = 0; i < countZ; ++i)
                    {
                        float z = startZ + i * stepZ;
                        AddPart(layerSettings.GetRightSide(), origin, sideX, z, scale, parts);
                    }
                    for (int i = 0; i < countZ; ++i)
                    {
                        float z = startZ + i * stepZ;
                        AddPart(layerSettings.GetLeftSide(), origin, -sideX, z, Rotate180 * scale, parts);
                    }

                    scale = Scale(layerSettings.CornerScale.z, borderY, pieceScaleX);
                    for (int i = 0; i < countX; ++i)
                    {
                        float x = startX + i * stepX;
                        AddPart(layerSettings.GetFrontSide(), origin, x, sideZ, Rotate270 * scale, parts);
                    }
                    for (int i = 0; i < countX; ++i)
                    {
                        float x = startX + i * stepX;
                        AddPart(layerSettings.GetBackSide(), origin, x, -sideZ, Rotate90 * scale, parts);
                    }
                }

                // corner
                scale = Scale(layerSettings.CornerScale.x, borderY, layerSettings.CornerScale.z);
                AddPart(layerSettings.GetFrontRightCorner(), origin, sideX, sideZ, scale, parts);
                AddPart(layerSettings.GetBackLeftCorner(), origin, -sideX, -sideZ, Rotate180 * scale, parts);
                scale = Scale(layerSettings.CornerScale.z, borderY, layerSettings.CornerScale.x);
                AddPart(layerSettings.GetBackRightCorner(), origin, sideX, -sideZ, Rotate90 * scale, parts);
                AddPart(layerSettings.GetFrontLeftCorner(), origin, -sideX, sideZ, Rotate270 * scale, parts);

                Parts = parts.ToArray();
            }

            static private void AddPart(Mesh mesh, Matrix4x4 origin, float offsetX, float offsetZ, Matrix4x4 transform, List<Part> parts)
            {
                if (mesh != null)
                {
                    var matrix = origin * Matrix4x4.Translate(new Vector3(offsetX, 0, offsetZ)) * transform;
                    parts.Add(new Part(matrix, mesh));
                }
            }

            static private Matrix4x4 Scale(Vector3 scale) => Matrix4x4.Scale(scale);
            static private Matrix4x4 Scale(float x, float y, float z) => Matrix4x4.Scale(new Vector3(x, y, z));
        }

        [System.Serializable]
        public class LayerSettings
        {
            public enum Type
            {
                Simple,
                Separate,
            }

            [SerializeField] private Vector3 centerSize = Vector3.one;
            public Vector3 CenterSize { get => centerSize; set => centerSize = value; }

            [SerializeField] private Vector3 cornerSize = Vector3.one;
            public Vector3 CornerSize { get => cornerSize; set => cornerSize = value; }
            [SerializeField] private Vector3 cornerScale = Vector3.one;
            public Vector3 CornerScale { get => cornerScale; set => cornerScale = value; }

            [SerializeField] private Type settingsType = Type.Simple;
            [SerializeField] private ScaleType edgeScale = ScaleType.Stretch;
            public ScaleType EdgeScale => edgeScale;
            [SerializeField] private ScaleType centerScale = ScaleType.Stretch;
            public ScaleType CenterScale => centerScale;

            [SerializeField] private Simple simple = null;
            public Simple SimpleSettings => simple;
            [SerializeField] private Separate separate = null;
            public Separate SeparateSettings => separate;

            private MeshCollection Current => (settingsType == Type.Simple) ? (MeshCollection)simple : (MeshCollection)separate;

            public void Reset()
            {
                Current?.Reset();
            }

            public Mesh GetCenter() => Current.GetCenter();

            public Mesh GetFrontLeftCorner()    => Current.GetFrontLeftCorner();
            public Mesh GetFrontRightCorner()   => Current.GetFrontRightCorner();
            public Mesh GetBackLeftCorner()     => Current.GetBackLeftCorner();
            public Mesh GetBackRightCorner()    => Current.GetBackRightCorner();

            public Mesh GetFrontSide()  => Current.GetFront();
            public Mesh GetBackSide()   => Current.GetBack();
            public Mesh GetLeftSide()   => Current.GetLeft();
            public Mesh GetRightSide()  => Current.GetRight();

            public abstract class MeshCollection
            {
                [SerializeField] private GetterType meshGetter = GetterType.Sequential;
                public GetterType MeshGetter { get => meshGetter; set => meshGetter = value; }

                public abstract void Reset();

                public abstract Mesh GetCenter();

                public abstract Mesh GetFrontLeftCorner();
                public abstract Mesh GetFrontRightCorner();
                public abstract Mesh GetBackLeftCorner();
                public abstract Mesh GetBackRightCorner();

                public abstract Mesh GetLeft();
                public abstract Mesh GetRight();
                public abstract Mesh GetFront();
                public abstract Mesh GetBack();
            }

            [System.Serializable]
            public class Simple : MeshCollection
            {
                public Mesh[] Center;
                public Mesh[] FrontRightCorner;
                public Mesh[] RightSide;

                private int centerCounter = 0;
                private int cornerCounter = 0;
                private int sideCounter = 0;

                public override void Reset()
                {
                    centerCounter = 0;
                    cornerCounter = 0;
                    sideCounter = 0;
                }

                public override Mesh GetCenter() => Get(Center, ref centerCounter, MeshGetter);

                public override Mesh GetFrontLeftCorner() => Get(FrontRightCorner, ref cornerCounter, MeshGetter);
                public override Mesh GetFrontRightCorner() => Get(FrontRightCorner, ref cornerCounter, MeshGetter);
                public override Mesh GetBackLeftCorner() => Get(FrontRightCorner, ref cornerCounter, MeshGetter);
                public override Mesh GetBackRightCorner() => Get(FrontRightCorner, ref cornerCounter, MeshGetter);

                public override Mesh GetLeft() => Get(RightSide, ref sideCounter, MeshGetter);
                public override Mesh GetRight() => Get(RightSide, ref sideCounter, MeshGetter);
                public override Mesh GetFront() => Get(RightSide, ref sideCounter, MeshGetter);
                public override Mesh GetBack() => Get(RightSide, ref sideCounter, MeshGetter);
            }

            [System.Serializable]
            public class Separate : MeshCollection
            {
                public Mesh[] Center;

                public Mesh[] FrontLeft;
                public Mesh[] FrontRight;
                public Mesh[] BackLeft;
                public Mesh[] BackRight;

                public Mesh[] Front;
                public Mesh[] Back;
                public Mesh[] Left;
                public Mesh[] Right;

                private int centerCounter = 0;
                private int frCounter = 0;
                private int flCounter = 0;
                private int brCounter = 0;
                private int blCounter = 0;
                private int frontCounter = 0;
                private int backCounter = 0;
                private int leftCounter = 0;
                private int rightCounter = 0;

                public override void Reset()
                {
                    centerCounter = 0;
                    frCounter = 0;
                    flCounter = 0;
                    brCounter = 0;
                    blCounter = 0;
                    frontCounter = 0;
                    backCounter = 0;
                    leftCounter = 0;
                    rightCounter = 0;
                }

                public override Mesh GetCenter() => Get(Center, ref centerCounter, MeshGetter);

                public override Mesh GetFrontLeftCorner() => Get(FrontLeft, ref flCounter, MeshGetter);
                public override Mesh GetFrontRightCorner() => Get(FrontRight, ref frCounter, MeshGetter);
                public override Mesh GetBackLeftCorner() => Get(BackLeft, ref blCounter, MeshGetter);
                public override Mesh GetBackRightCorner() => Get(BackRight, ref brCounter, MeshGetter);

                public override Mesh GetLeft() => Get(Front, ref leftCounter, MeshGetter);
                public override Mesh GetRight() => Get(Back, ref rightCounter, MeshGetter);
                public override Mesh GetFront() => Get(Left, ref frontCounter, MeshGetter);
                public override Mesh GetBack() => Get(Right, ref backCounter, MeshGetter);
            }
        }
    }
}
