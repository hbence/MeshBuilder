﻿using System.Runtime.InteropServices;

using UnityEngine;
using UnityEditor;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    public class Utils
    {
#if UNITY_EDITOR
        public class ComponentProperties<T> where T : System.Enum
        {
            private SerializedProperty[] props;

            public ComponentProperties(SerializedObject serializedObject)
            {
                var names = System.Enum.GetNames(typeof(T));
                int count = names.Length;
                props = new SerializedProperty[count];
                for (int i = 0; i < count; ++i)
                {
                    props[i] = serializedObject.FindProperty(names[i]);
                }
            }

            public SerializedProperty Get(T name)
                => props[(int)(object)name];

            public void Draw(T name)
                => EditorGUILayout.PropertyField(Get(name));

            public void Draw(T name, Rect rect, bool includeChildren)
                => EditorGUI.PropertyField(rect, Get(name), includeChildren);
        }

        static public void GUILabel(string text)
            => GUILayout.Label(text);

        static public void GUILabel(string text, int width)
            => GUILayout.Label(text, GUILayout.Width(width));

        public abstract class ValueEditorPref<T>
        {
            public string Key { get; }

            private T val;
            public T Value 
            { 
                get => val; 
                set
                {
                    val = value;
                    Save();
                }
            }

            public ValueEditorPref(string key, T defValue)
            {
                Key = key;
                val = defValue;
                Load();
            }

            public abstract void Load();
            public abstract void Save();

            public static implicit operator T(ValueEditorPref<T> val) => val.Value;
        }

        public class BoolEditorPref : ValueEditorPref<bool>
        {
            public BoolEditorPref(string key, bool defValue) : base(key, defValue) { }
            public override void Load() => Value = EditorPrefs.GetBool(Key, Value);
            public override void Save() => EditorPrefs.SetBool(Key, Value);

            public bool DrawSwitchButton(string onLabel, string offLabel)
            {
                if (GUILayout.Button(Value ? onLabel : offLabel))
                {
                    Value = !Value;
                }
                return Value;
            }

            public bool DrawToggle(string label)
            {
                Value = GUILayout.Toggle(Value, label);
                return Value;
            }
        }

        public class IntEditorPref : ValueEditorPref<int>
        {
            public IntEditorPref(string key, int defValue) : base(key, defValue) { }
            public override void Load() => Value = EditorPrefs.GetInt(Key, Value);
            public override void Save() => EditorPrefs.SetInt(Key, Value);
        }

        public class FloatEditorPref : ValueEditorPref<float>
        {
            public FloatEditorPref(string key, float defValue) : base(key, defValue) { }
            public override void Load() => Value = EditorPrefs.GetFloat(Key, Value);
            public override void Save() => EditorPrefs.SetFloat(Key, Value);
        }

        public class StringEditorPref : ValueEditorPref<string>
        {
            public StringEditorPref(string key, string defValue) : base(key, defValue) { }
            public override void Load() => Value = EditorPrefs.GetString(Key, Value);
            public override void Save() => EditorPrefs.SetString(Key, Value);
        }

        public class EnumEditorPref<T> : ValueEditorPref<T> where T : System.Enum
        {
            public EnumEditorPref(string key, T defValue) : base(key, defValue) { }
            public override void Load() => Value = (T)System.Enum.Parse(typeof(T), EditorPrefs.GetString(Key, Value.ToString()));
            public override void Save() => EditorPrefs.SetString(Key, Value.ToString());
        }

        public class Vector2EditorPref : ValueEditorPref<Vector2>
        {
            private string XKey => Key + "_x";
            private string YKey => Key + "_y";

            public Vector2EditorPref(string key, Vector2 defValue) : base(key, defValue) { }
            public override void Load() => Value = new Vector2(EditorPrefs.GetFloat(XKey, 0), EditorPrefs.GetFloat(YKey, 0));

            public override void Save()
            {
                EditorPrefs.SetFloat(XKey, Value.x);
                EditorPrefs.SetFloat(YKey, Value.y);
            }
        }

        static public BoolEditorPref CreatePref(string key, bool defValue) => new BoolEditorPref(key, defValue);
        static public IntEditorPref CreatePref(string key, int defValue) => new IntEditorPref(key, defValue);
        static public FloatEditorPref CreatePref(string key, float defValue) => new FloatEditorPref(key, defValue);
        static public StringEditorPref CreatePref(string key, string defValue) => new StringEditorPref(key, defValue);
        static public EnumEditorPref<T> CreatePref<T>(string key, T defValue) where T: System.Enum => new EnumEditorPref<T>(key, defValue);
        static public Vector2EditorPref CreatePref(string key, Vector2 defValue) => new Vector2EditorPref(key, defValue);

#endif
        public struct Offset
        {
            public int index;
            public int length;

            public int Start { get => index; }
            public int End { get => index + length; }
        }

        // most meshers don't have their own copy of the data they're working on,
        // so it's easy to modify data which is being used by jobs, which then give
        // an access error which is not always clear about the cause of the error.
        // This simple lock only allows access to the data when it is unlocked.
        public class DataLock<T>
        {
            public bool IsLocked { get; private set; } = false;
            private T data;
            public T Data => IsLocked ? default : data;
            public DataLock(T data) { this.data = data; }

            public void Lock() => IsLocked = true;
            public void Unlock() => IsLocked = false;
        }

        static public void SafeDispose<T>(ref Volume<T> volume) where T : struct
        {
            if (volume != null)
            {
                volume.Dispose();
                volume = null;
            }
        }

        static public void SafeDispose<T>(ref NativeArray<T> collection) where T : struct
        {
            if (collection.IsCreated)
            {
                collection.Dispose();
                collection = default;
            }
        }

        static public void SafeDispose<T>(ref NativeList<T> collection) where T : unmanaged
        {
            if (collection.IsCreated)
            {
                collection.Dispose();
                collection = default;
            }
        }

        static public void SafeDispose<TKey, TValue>(ref NativeHashMap<TKey, TValue> collection) 
            where TKey : struct, System.IEquatable<TKey>
            where TValue : struct
        {
            if (collection.IsCreated)
            {
                collection.Dispose();
                collection = default;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct MatrixConverter
        {
            [FieldOffset(0)]
            public float4x4 Float4x4;

            [FieldOffset(0)]
            public Matrix4x4 Matrix4X4;
        }

        // TODO: check back later, newer versions of Unity might allow direct use of math versions
        // of data types making these converter classes superflous
        [StructLayout(LayoutKind.Explicit)]
        public struct Vector4Converter
        {
            [FieldOffset(0)]
            public float4[] Float4Array;

            [FieldOffset(0)]
            public Vector4[] Vector4Array;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct Vector3Converter
        {
            [FieldOffset(0)]
            public float3[] Float3Array;

            [FieldOffset(0)]
            public Vector3[] Vector3Array;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct Vector2Converter
        {
            [FieldOffset(0)]
            public float2[] Float2Array;

            [FieldOffset(0)]
            public Vector2[] Vector2Array;
        }

        static public Matrix4x4 ToMatrix4x4(float4x4 m)
        {
            var c = new MatrixConverter { Float4x4 = m };
            return c.Matrix4X4;
        }

        static public float4x4 ToFloat4x4(Matrix4x4 m)
        {
            var c = new MatrixConverter { Matrix4X4 = m };
            return c.Float4x4;
        }

        static public float3x4 ToFloat3x4(Matrix4x4 m)
        {
            return new float3x4(m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23);
        }

        static public Vector2[] ToVector2Array(NativeArray<float2> array)
        {
            var data = new Vector2Converter { Float2Array = array.ToArray() };
            return data.Vector2Array;
        }

        static public Vector3[] ToVector3Array(NativeArray<float3> array)
        {
            var data = new Vector3Converter { Float3Array = array.ToArray() };
            return data.Vector3Array;
        }

        static public Vector4[] ToVector4Array(NativeArray<float4> array)
        {
            var data = new Vector4Converter { Float4Array = array.ToArray() };
            return data.Vector4Array;
        }

        static public float2[] ToFloat2Array(Vector2[] array)
        {
            var data = new Vector2Converter { Vector2Array = array };
            return data.Float2Array;
        }

        static public float3[] ToFloat3Array(Vector3[] array)
        {
            var data = new Vector3Converter { Vector3Array = array };
            return data.Float3Array;
        }

        static public float4[] ToFloat4Array(Vector4[] array)
        {
            var data = new Vector4Converter { Vector4Array = array };
            return data.Float4Array;
        }

        [BurstCompile]
        public struct VertexOffsetJob : IJobParallelFor
        {
            public float3 offset;
            public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                vertices[index] = vertices[index] + offset;
            }

            public static JobHandle Schedule(NativeArray<float3> vertices, float3 offset, int innerBatch = 1024, JobHandle dependOn = default)
            {
                var job = new VertexOffsetJob
                {
                    offset = offset,
                    vertices = vertices
                };
                return job.Schedule(vertices.Length, innerBatch,  dependOn);
            }
        }

        [BurstCompile]
        public struct UVScaleJob : IJobParallelFor
        {
            public float2 scale;
            public NativeArray<float2> uvs;

            public void Execute(int index)
            {
                uvs[index] = uvs[index] * scale;
            }

            public static JobHandle Schedule(NativeArray<float2> uvs, float2 scale, int innerBatch = 1024, JobHandle dependOn = default)
            {
                var job = new UVScaleJob
                {
                    scale = scale,
                    uvs = uvs
                };
                return job.Schedule(uvs.Length, innerBatch, dependOn);
            }
        }
    }
}
