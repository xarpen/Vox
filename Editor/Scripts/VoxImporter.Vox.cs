using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable MustUseReturnValue

namespace Fluorite.Vox.Editor
{
    public enum MaterialType
    {
        Diffuse,
        Metal,
        Glass,
        Emission,
        Count
    }

    public abstract class Chunk
    {
        public enum Type
        {
            Main = 1313423693,
            Pack = 1262698832,
            Size = 1163544915,
            Xyzi = 1230657880,
            nTrn = 1314018414,
            nGrp = 1347569518,
            nShp = 1346917230,
            Layr = 1381581132,
            Rgba = 1094862674,
            Matt = 1414807885,
            Matl = 1280590157,
            rLit = 1414089842,
            rAir = 1380532594,
            rLen = 1313164402,
            Post = 1414745936,
            rDis = 1397310578,
            rObj = 1245859698,
            rCam = 1296122738,
            Note = 1163153230
        }

        protected const string zero = "0";
        protected const string one = "1";

        #region Fields
        static byte[] buffer = new byte[byte.MaxValue];

        int numBytes;
        int numBytesChildren;
        byte[] bytes;
        #endregion

        #region Properties
        public Type ChunkType { get; private set; }

        protected virtual int NumBytes => numBytes;
        public int NumBytesChildren => numBytesChildren;
        public List<Chunk> Children { get; } = new();
        #endregion

        #region Methods
        public void Read(BinaryReader reader) => Read(reader, (Type)reader.ReadInt32());
        public void Read(BinaryReader reader, Type type)
        {
            ChunkType = type;

            numBytes = reader.ReadInt32();
            numBytesChildren = reader.ReadInt32();

            long bodyPosition = reader.BaseStream.Position;
            ReadBody(reader);
            if (reader.BaseStream.Position != bodyPosition + numBytes)
            {
                reader.BaseStream.Position = bodyPosition + numBytes;
                Debug.LogWarning($"[{nameof(Vox)}] Body unaligned!");
            }

            long childrenPosition = reader.BaseStream.Position;
            while (reader.BaseStream.Position < (childrenPosition + numBytesChildren))
            {
                Chunk chunk;

                switch (type = (Type)reader.ReadInt32())
                {
                    case Type.Pack:
                        chunk = new PackChunk();
                        break;

                    case Type.Size:
                        chunk = new SizeChunk();
                        break;

                    case Type.Xyzi:
                        chunk = new XyziChunk();
                        break;

                    case Type.nTrn:
                        chunk = new TransformChunk();
                        break;

                    case Type.nGrp:
                        chunk = new GroupChunk();
                        break;

                    case Type.nShp:
                        chunk = new ShapeChunk();
                        break;

                    case Type.Layr:
                        chunk = new LayerChunk();
                        break;

                    case Type.Rgba:
                        chunk = new RgbaChunk();
                        break;

                    case Type.Matt:
                        throw new NotSupportedException();

                    case Type.Matl:
                        chunk = new MaterialChunk();
                        break;

                    case Type.rLit:
                        chunk = new LightingChunk();
                        break;

                    case Type.rAir:
                        chunk = new AirChunk();
                        break;

                    case Type.rLen:
                        chunk = new LensChunk();
                        break;

                    case Type.Post:
                        chunk = new PostChunk();
                        break;

                    case Type.rDis:
                        chunk = new ViewChunk();
                        break;

                    case Type.rObj:
                        chunk = new rObjChunk();
                        break;

                    case Type.rCam:
                        chunk = new rCamChunk();
                        break;

                    case Type.Note:
                        chunk = new NoteChunk();
                        break;

                    default:
                        throw new ArgumentOutOfRangeException($"Chunk type {GetString(BitConverter.GetBytes((int)type))}={(int)type} not supported!");
                }

                chunk.Read(reader, type);
                Children.Add(chunk);
            }

            if (reader.BaseStream.Position == childrenPosition + numBytesChildren) return;
            reader.BaseStream.Position = childrenPosition + numBytesChildren;
            Debug.LogWarning($"[{nameof(Vox)}] Children unaligned!");
        }
        public void Write(BinaryWriter writer)
        {
            writer.Write((int)ChunkType);
            writer.Write(NumBytes);
            writer.Write(GetNumBytesInChildren());

            WriteBody(writer);

            for (int i = 0, count = Children.Count; i < count; ++i) Children[i].Write(writer);
        }
        protected virtual void ReadBody(BinaryReader reader) => bytes = reader.ReadBytes(numBytes);
        protected virtual void WriteBody(BinaryWriter writer) => writer.Write(bytes);
        #endregion

        #region Support Methods
        protected static int KeyValueSize(string name, string value) => sizeof(int) * 2 + (name.Length + value.Length) * sizeof(byte);
        protected KeyValuePair<string, string> ReadKeyValue(BinaryReader reader)
        {
            string key = GetString(reader);
            string value = GetString(reader);
            return new KeyValuePair<string, string>(key, value);
        }
        protected void WriteKeyValue(BinaryWriter writer, string name, string value)
        {
            writer.Write(name.Length);
            writer.Write(buffer, 0, GetBytes(name));
            writer.Write(value.Length);
            writer.Write(buffer, 0, GetBytes(value));
        }

        protected static float ReadInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);
        protected static string WriteInt(int value) => value.ToString(CultureInfo.InvariantCulture);
        protected static float ReadFloat(string value) => float.Parse(value, CultureInfo.InvariantCulture);
        protected static string WriteFloat(float value) => value.ToString(CultureInfo.InvariantCulture);
        protected static Vector3 ReadVector3(string value) { float[] array = Array.ConvertAll(value.Split(' '), ReadFloat); return new Vector3(array[0], array[1], array[2]); }
        protected static string WriteVector3(Vector3 value) => $"{value.x} {value.y} {value.z}";
        protected static Vector2 ReadVector2(string value) { float[] array = Array.ConvertAll(value.Split(' '), ReadFloat); return new Vector2(array[0], array[1]); }
        protected static string WriteVector2(Vector2 value) => $"{value.x} {value.y}";
        protected static Color32 ReadColor32(string value) { byte[] array = Array.ConvertAll(value.Split(' '), byte.Parse); return new Color32(array[0], array[1], array[2], 255); }
        protected static string WriteColor32(Color32 value) => $"{value.r} {value.g} {value.b}";

        protected static string GetString(BinaryReader reader)
        {
            int size = reader.ReadInt32();
            reader.BaseStream.Read(buffer, 0, size);
            return Encoding.ASCII.GetString(buffer, 0, size);
        }
        protected static string GetString(byte[] bytes) => Encoding.ASCII.GetString(bytes);
        protected static int GetBytes(string value) => Encoding.ASCII.GetBytes(value, 0, value.Length, buffer, 0);
        protected static int Count<T>(params T[] values)
        {
            int total = 0;
            for (int i = 0, length = values.Length / 2; i < length; ++i)
            {
                if (!values[i].Equals(values[length + i])) total++;
            }
            return total;
        }
        protected int Remainder(int bytes) => numBytes - bytes;
        protected int GetNumBytesInChildren()
        {
            int size = 0;
            for (int i = 0, count = Children.Count; i < count; ++i)
            {
                size += 3 * sizeof(int);
                size += Children[i].NumBytes;
                size += Children[i].NumBytesChildren;
            }
            return size;
        }
        #endregion
    }

    public class MainChunk : Chunk { }

    public class SizeChunk : Chunk
    {
        #region Properties
        protected override int NumBytes => Marshal.SizeOf<Vector3Int>();
        public Vector3Int Size { get; private set; }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader) => Size = new Vector3Int(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        protected override void WriteBody(BinaryWriter writer)
        {
            writer.Write(Size.x);
            writer.Write(Size.y);
            writer.Write(Size.z);
        }
        #endregion
    }

    public class PackChunk : Chunk
    {
        #region Properties
        protected override int NumBytes => sizeof(int);
        public int Length { get; private set; }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader) => Length = reader.ReadInt32();
        protected override void WriteBody(BinaryWriter writer) => writer.Write(Length);
        #endregion
    }

    public class XyziChunk : Chunk
    {
        public const int maxVoxels = 256;

        #region Fields
        static byte[] buffer = new byte[maxVoxels * maxVoxels * maxVoxels * 4];
        #endregion

        #region Properties
        protected override int NumBytes => sizeof(int) + Points.Length * Marshal.SizeOf<Color32>();
        public Color32[] Points { get; private set; }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            Points = new Color32[reader.ReadInt32()];

            int size = Points.Length * Marshal.SizeOf<Color32>();
            reader.BaseStream.Read(buffer, 0, size);

            unsafe
            {
                fixed (void* source = buffer, destination = Points) Buffer.MemoryCopy(source, destination, size, size);
            }
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            writer.Write(Points.Length);
            for (int i = 0, length = Points.Length; i < length; ++i)
            {
                writer.Write(Points[i].r);
                writer.Write(Points[i].g);
                writer.Write(Points[i].b);
                writer.Write(Points[i].a);
            }
        }
        #endregion
    }

    public class RgbaChunk : Chunk
    {
        public const int maxColors = 256;

        #region Properties
        protected override int NumBytes => maxColors * Marshal.SizeOf<Color32>();
        public Color32[] Colors { get; } = new Color32[maxColors];
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            for (int i = 0; i < maxColors; ++i) Colors[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            for (int i = 0; i < maxColors; ++i)
            {
                writer.Write(Colors[i].r);
                writer.Write(Colors[i].g);
                writer.Write(Colors[i].b);
                writer.Write(Colors[i].a);
            }
        }
        #endregion
    }

    public class NChunk : Chunk
    {
        const string name = "_name";
        const string hidden = "_hidden";

        #region Properties
        protected override int NumBytes
        {
            get
            {
                int size = 2 * sizeof(int);
                if (Name != string.Empty) size += KeyValueSize(name, Name);
                if (Hidden) size += KeyValueSize(hidden, one);
                return size;
            }
        }
        public int ElementIndex { get; private set; }
        public string Name { get; protected set; }
        public bool Hidden { get; private set; }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            ElementIndex = reader.ReadInt32();
            for (int i = 0, length = reader.ReadInt32(); i < length; ++i)
            {
                KeyValuePair<string, string> keyValue = ReadKeyValue(reader);
                if (keyValue.Key == name) Name = keyValue.Value;
                else if (keyValue.Key == hidden) Hidden = ReadInt(keyValue.Value) != 0;
                else throw new ArgumentOutOfRangeException($"Invalid key={keyValue.Key} value={keyValue.Value}");
            }
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            writer.Write(ElementIndex);
            writer.Write(Count(Name, string.Empty) + Count(Hidden, false));
            if (Name != string.Empty) WriteKeyValue(writer, name, Name);
            if (Hidden) WriteKeyValue(writer, hidden, one);
        }
        #endregion
    }

    public class GroupChunk : NChunk
    {
        #region Properties
        protected override int NumBytes => base.NumBytes + sizeof(int) + children.Length * sizeof(int);
        public int[] children { get; private set; }
        #endregion

        #region Constructors
        public GroupChunk()
        {
        }
        public GroupChunk(int[] children)
        {
            this.children = children;
        }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            base.ReadBody(reader);

            children = new int[reader.ReadInt32()];
            for (int i = 0, length = children.Length; i < length; ++i) children[i] = reader.ReadInt32();
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            base.WriteBody(writer);

            writer.Write(children.Length);
            for (int i = 0, length = children.Length; i < length; ++i) writer.Write(children[i]);
        }
        #endregion
    }

    public class TransformChunk : NChunk
    {
        [Flags]
        enum _R
        {
            r0000 = 0,
            r0001 = 1,
            r0010 = 2,
            r0011 = 3,
            r0100 = 4,
            r0101 = 5,
            r0110 = 6,
            r0111 = 7,
            r1000 = 8,
            r1001 = 9,
            r1010 = 10,
            r1011 = 11,
            r1100 = 12,
            r1101 = 13,
            r1110 = 14,
            r1111 = 15,
            ScaleX = 16,
            ScaleZ = 32,
            ScaleY = 64,
        }

        const string t = "_t";
        const string r = "_r";

        #region Properties
        protected override int NumBytes
        {
            get
            {
                int size = 5 * sizeof(int);
                if (Position != Vector3.zero) size += KeyValueSize(t, WriteVector3(Position));
                if (Euler != Vector3.zero || Scale != Vector3.one) size += KeyValueSize(r, Write_r());
                return base.NumBytes + size;
            }
        }
        public int Reference { get; private set; }
        public int Flag { get; private set; }
        public int Layer { get; private set; }
        public int Reserved { get; private set; }
        public Vector3 Position { get; private set; }
        public Vector3 Euler { get; private set; }
        public Vector3 Scale { get; private set; }
        #endregion

        #region Constructors
        public TransformChunk()
        {
        }
        public TransformChunk(string name, int reference)
        {
            Name = name;
            Reference = reference;
        }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            base.ReadBody(reader);

            Reference = reader.ReadInt32();
            Flag = reader.ReadInt32();
            Layer = reader.ReadInt32();
            Reserved = reader.ReadInt32();

            for (int i = 0, length = reader.ReadInt32(); i < length; ++i)
            {
                KeyValuePair<string, string> keyValue = ReadKeyValue(reader);
                if (keyValue.Key == r) Read_r(keyValue.Value);
                else if (keyValue.Key == t) Position = ReadVector3(keyValue.Value);
                else throw new ArgumentOutOfRangeException($"Invalid key={keyValue.Key} value={keyValue.Value}");
            }
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            base.WriteBody(writer);

            writer.Write(Reference);
            writer.Write(Flag);
            writer.Write(Layer);
            writer.Write(Reserved);
            writer.Write((Position != Vector3.zero ? 1 : 0) + (Euler != Vector3.zero || Scale != Vector3.one ? 1 : 0));
            if (Euler != Vector3.zero || Scale != Vector3.one) WriteKeyValue(writer, r, Write_r());
            if (Position != Vector3.zero) WriteKeyValue(writer, t, WriteVector3(Position));
        }
        #endregion

        #region Support Methods
        void Read_r(string value)
        {
            _R _r = (_R)ReadInt(value);
            Vector3 scale = Vector3.one;
            if ((_r & _R.ScaleX) == 0) scale.x = -1; else _r -= _R.ScaleX;
            if ((_r & _R.ScaleY) == 0) scale.y = -1; else _r -= _R.ScaleY;
            if ((_r & _R.ScaleZ) == 0) scale.z = -1; else _r -= _R.ScaleZ;
            if (_r == _R.r0000) Debug.LogWarning($"[{nameof(Vox)}] Unknown rotation _r={value} on {Name}.");
            if (_r == _R.r0001) Euler = new Vector3(180, 0, 90);
            if (_r == _R.r0010) { Euler = new Vector3(90, 90, 0); scale *= -1; }
            if (_r == _R.r0011) Debug.LogWarning($"[{nameof(Vox)}] Unknown rotation _r={value} on {Name}.");
            if (_r == _R.r0100) { Euler = new Vector3(0, 0, 0); scale *= -1; }
            if (_r == _R.r0101) Debug.LogWarning($"[{nameof(Vox)}] Unknown rotation _r={value} on {Name}.");
            if (_r == _R.r0110) Euler = new Vector3(180, -90, 0);
            if (_r == _R.r0111) Debug.LogWarning($"[{nameof(Vox)}] Unknown rotation _r={value} on {Name}.");
            if (_r == _R.r1000) Euler = new Vector3(-90, -180, 0);
            if (_r == _R.r1001) { Euler = new Vector3(-90, 0, -90); scale *= -1; }
            if (_r == _R.r1010) Debug.LogWarning($"[{nameof(Vox)}] Unknown rotation _r={value} on {Name}.");
            if (_r == _R.r1011) Debug.LogWarning($"[{nameof(Vox)}] Unknown rotation _r={value} on {Name}.");
            if (_r == _R.r1100) Debug.LogWarning($"[{nameof(Vox)}] Unknown rotation _r={value} on {Name}.");
            if (_r == _R.r1101) Debug.LogWarning($"[{nameof(Vox)}] Unknown rotation _r={value} on {Name}.");
            if (_r == _R.r1110) Debug.LogWarning($"[{nameof(Vox)}] Unknown rotation _r={value} on {Name}.");
            if (_r == _R.r1111) Debug.LogWarning($"[{nameof(Vox)}] Unknown rotation _r={value} on {Name}.");
            Scale = scale;
        }
        string Write_r()
        {
            _R _r = 0;
            if (Euler == new Vector3(180, 0, 90)) _r = _R.r0001;
            if (Euler == new Vector3(90, 90, 0)) _r = _R.r0010;
            if (Euler == new Vector3(0, 0, 0)) _r = _R.r0100;
            if (Euler == new Vector3(180, -90, 0)) _r = _R.r0110;
            if (Euler == new Vector3(-90, -180, 0)) _r = _R.r1000;
            if (Euler == new Vector3(-90, 0, -90)) _r = _R.r1001;

            Vector3 originalScale = Scale;
            if (Euler == new Vector3(90, 90, 0)) originalScale *= -1;
            if (Euler == new Vector3(0, 0, 0)) originalScale *= -1;
            if (Euler == new Vector3(-90, 0, -90)) originalScale *= -1;

            if (originalScale.x > 0) _r |= _R.ScaleX;
            if (originalScale.y > 0) _r |= _R.ScaleY;
            if (originalScale.z > 0) _r |= _R.ScaleZ;
            return WriteInt((int)_r);
        }
        #endregion
    }

    public class ShapeChunk : NChunk
    {
        #region Fields
        byte[] bytes;
        #endregion

        #region Properties
        protected override int NumBytes => base.NumBytes + 2 * sizeof(int) + bytes.Length * sizeof(byte);
        public int Flag { get; private set; }
        public int ShapeIndex { get; private set; }
        #endregion

        #region Constructors
        public ShapeChunk()
        {
        }
        public ShapeChunk(int index)
        {
            ShapeIndex = index;
        }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            long position = reader.BaseStream.Position;

            base.ReadBody(reader);

            Flag = reader.ReadInt32();
            ShapeIndex = reader.ReadInt32();

            bytes = reader.ReadBytes(Remainder((int)(reader.BaseStream.Position - position)));
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            base.WriteBody(writer);

            writer.Write(Flag);
            writer.Write(ShapeIndex);

            writer.Write(bytes);
        }
        #endregion
    }

    public class LayerChunk : Chunk
    {
        const string name = "_name";
        const string hidden = "_name";

        #region Fields
        byte[] bytes;
        #endregion

        #region Properties
        protected override int NumBytes
        {
            get
            {
                int size = 2 * sizeof(int);
                if (Name != string.Empty) size += KeyValueSize(name, Name);
                if (Hidden) size += KeyValueSize(hidden, one);
                return size + bytes.Length * sizeof(byte);
            }
        }
        public int Index { get; private set; }
        public string Name { get; private set; }
        public bool Hidden { get; private set; }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            long position = reader.BaseStream.Position;

            Index = reader.ReadInt32();
            for (int i = 0, length = reader.ReadInt32(); i < length; ++i)
            {
                KeyValuePair<string, string> keyValue = ReadKeyValue(reader);
                if (keyValue.Key == name) Name = keyValue.Value;
                else if (keyValue.Key == hidden) Hidden = ReadInt(keyValue.Value) != 0;
                else throw new ArgumentOutOfRangeException($"Invalid key={keyValue.Key} value={keyValue.Value}");
            }

            bytes = reader.ReadBytes(Remainder((int)(reader.BaseStream.Position - position)));
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            writer.Write(Index);
            writer.Write(Count(Name, string.Empty) + Count(Hidden, false));
            if (Name != string.Empty) WriteKeyValue(writer, name, Name);
            if (Hidden) WriteKeyValue(writer, hidden, one);

            writer.Write(bytes);
        }
        #endregion
    }

    public class MaterialChunk : Chunk
    {
        const string type = "_type";
        const string diffuse = "_diffuse";
        const string metal = "_metal";
        const string glass = "_glass";
        const string emit = "_emit";
        const string rough = "_rough";
        const string ior = "_ior";
        const string sp = "_sp";
        const string emission = "_emission";
        const string flux = "_flux";
        const string ldr = "_ldr";
        const string alpha = "_alpha";
        const string trans = "_trans";
        const string d = "_d";
        const string weight = "_weight";

        #region Properties
        protected override int NumBytes
        {
            get
            {
                int size = 2 * sizeof(int);
                if (MaterialType == MaterialType.Diffuse) size += KeyValueSize(type, diffuse);
                if (MaterialType == MaterialType.Metal) size += KeyValueSize(type, metal);
                if (MaterialType == MaterialType.Glass) size += KeyValueSize(type, glass);
                if (MaterialType == MaterialType.Emission) size += KeyValueSize(type, emit);
                if (Roughness != 0) size += KeyValueSize(rough, WriteFloat(Roughness));
                if (IOR != 0) size += KeyValueSize(ior, WriteFloat(IOR));
                if (Specular != 0) size += KeyValueSize(sp, WriteFloat(Specular));
                if (Metal != 0) size += KeyValueSize(metal, WriteFloat(Metal));
                if (Emission != 0) size += KeyValueSize(emission, WriteFloat(Emission));
                if (Flux != 0) size += KeyValueSize(flux, WriteFloat(Flux));
                if (LowDynamicRange != 0) size += KeyValueSize(ldr, WriteFloat(LowDynamicRange));
                if (Alpha != 0) size += KeyValueSize(alpha, WriteFloat(Alpha));
                if (Transparency != 0) size += KeyValueSize(trans, WriteFloat(Transparency));
                if (Density != 0) size += KeyValueSize(d, WriteFloat(Density));
                return size;
            }
        }
        public int Index { get; private set; }
        public MaterialType MaterialType { get; private set; }
        public float Roughness { get; private set; }
        public float IOR { get; private set; }
        public float Specular { get; private set; }
        public float Metal { get; private set; }
        public float Emission { get; private set; }
        public float Flux { get; private set; }
        public float LowDynamicRange { get; private set; }
        public float Alpha { get; private set; }
        public float Transparency { get; private set; }
        public float Density { get; private set; }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            Index = (byte)reader.ReadInt32();
            for (int i = 0, length = reader.ReadInt32(); i < length; ++i)
            {
                KeyValuePair<string, string> keyValue = ReadKeyValue(reader);
                if (keyValue.Key == type)
                {
                    if (keyValue.Value == diffuse) MaterialType = MaterialType.Diffuse;
                    else if (keyValue.Value == metal) MaterialType = MaterialType.Metal;
                    else if (keyValue.Value == glass) MaterialType = MaterialType.Glass;
                    else if (keyValue.Value == emit) MaterialType = MaterialType.Emission;
                    else throw new ArgumentOutOfRangeException($"Invalid type={MaterialType}");
                }
                else
                {
                    if (keyValue.Key == weight)
                    {
                        switch (MaterialType)
                        {
                            case MaterialType.Diffuse:
                                if (ReadFloat(keyValue.Value) != 1) Debug.LogWarning($"Invalid key={keyValue.Key} value={keyValue.Value}");
                                break;

                            case MaterialType.Glass:
                                Transparency = ReadFloat(keyValue.Value);
                                break;

                            case MaterialType.Metal:
                                Metal = ReadFloat(keyValue.Value);
                                break;

                            case MaterialType.Emission:
                                Emission = ReadFloat(keyValue.Value);
                                break;

                            default:
                                Debug.LogWarning($"Invalid key={keyValue.Key} value={keyValue.Value}");
                                break;
                        }
                    }
                    else if (keyValue.Key == rough) Roughness = ReadFloat(keyValue.Value);
                    else if (keyValue.Key == ior) IOR = ReadFloat(keyValue.Value);
                    else if (keyValue.Key == sp) Specular = ReadFloat(keyValue.Value);
                    else if (keyValue.Key == metal) Metal = ReadFloat(keyValue.Value);
                    else if (keyValue.Key == emit) Emission = ReadFloat(keyValue.Value);
                    else if (keyValue.Key == flux) Flux = ReadFloat(keyValue.Value);
                    else if (keyValue.Key == ldr) LowDynamicRange = ReadFloat(keyValue.Value);
                    else if (keyValue.Key == alpha) Alpha = ReadFloat(keyValue.Value);
                    else if (keyValue.Key == trans) Transparency = ReadFloat(keyValue.Value);
                    else if (keyValue.Key == d) Density = ReadFloat(keyValue.Value);
                    else Debug.LogWarning($"Invalid key={keyValue.Key} value={keyValue.Value}");
                }
            }
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            writer.Write(Index);
            writer.Write(1 + Count(Roughness, IOR, Specular, Metal, Emission, Flux, LowDynamicRange, 0, 0, 0, 0, 0, 0, 0));

            if (MaterialType == MaterialType.Diffuse) WriteKeyValue(writer, type, diffuse);
            if (MaterialType == MaterialType.Metal) WriteKeyValue(writer, type, metal);
            if (MaterialType == MaterialType.Glass) WriteKeyValue(writer, type, glass);
            if (MaterialType == MaterialType.Emission) WriteKeyValue(writer, type, emit);
            if (Roughness != 0) WriteKeyValue(writer, rough, WriteFloat(Roughness));
            if (IOR != 0) WriteKeyValue(writer, ior, WriteFloat(IOR));
            if (Specular != 0) WriteKeyValue(writer, sp, WriteFloat(Specular));
            if (Metal != 0) WriteKeyValue(writer, metal, WriteFloat(Metal));
            if (Emission != 0) WriteKeyValue(writer, emit, WriteFloat(Emission));
            if (Flux != 0) WriteKeyValue(writer, flux, WriteFloat(Flux));
            if (LowDynamicRange != 0) WriteKeyValue(writer, ldr, WriteFloat(LowDynamicRange));
            if (Alpha != 0) WriteKeyValue(writer, alpha, WriteFloat(Alpha));
            if (Transparency != 0) WriteKeyValue(writer, trans, WriteFloat(Transparency));
            if (Density != 0) WriteKeyValue(writer, d, WriteFloat(Density));
        }
        #endregion
    }

    public class LightingChunk : Chunk
    {
        const string type = "_type";
        const string I = "_I";
        const string color = "_color";
        const string angle = "_angle";
        const string area = "_area";

        #region Properties
        protected override int NumBytes
        {
            get
            {
                int size = sizeof(int);
                size += KeyValueSize(type, LightingType);
                size += KeyValueSize(I, WriteFloat(Intensity));
                if (Color.GetHashCode() != default(Color32).GetHashCode()) size += KeyValueSize(color, WriteColor32(Color));
                if (Angle != Vector2.zero) size += KeyValueSize(angle, WriteVector2(Angle));
                if (Area != 0.0f) size += KeyValueSize(area, WriteFloat(Area));
                return size;
            }
        }
        public string LightingType { get; private set; }
        public float Intensity { get; private set; }
        public Color32 Color { get; private set; }
        public Vector2 Angle { get; private set; }
        public float Area { get; private set; }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            for (int i = 0, length = reader.ReadInt32(); i < length; ++i)
            {
                KeyValuePair<string, string> keyValue = ReadKeyValue(reader);
                if (keyValue.Key == type) LightingType = keyValue.Value;
                else if (keyValue.Key == I) Intensity = ReadFloat(keyValue.Value);
                else if (keyValue.Key == color) Color = ReadColor32(keyValue.Value);
                else if (keyValue.Key == angle) Angle = ReadVector2(keyValue.Value);
                else if (keyValue.Key == area) Area = ReadFloat(keyValue.Value);
                else throw new ArgumentOutOfRangeException($"Invalid key={keyValue.Key} value={keyValue.Value}");
            }
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            writer.Write(2 + Count(Color.GetHashCode(), default(Color32).GetHashCode()) + Count(Angle, Vector2.zero) + Count(Area, 0.0f));
            WriteKeyValue(writer, type, LightingType);
            WriteKeyValue(writer, I, WriteFloat(Intensity));
            if (Color.GetHashCode() != default(Color32).GetHashCode()) WriteKeyValue(writer, color, WriteColor32(Color));
            if (Angle != Vector2.zero) WriteKeyValue(writer, angle, WriteVector2(Angle));
            if (Area != 0.0f) WriteKeyValue(writer, area, WriteFloat(Area));
        }
        #endregion
    }

    public class AirChunk : Chunk
    {
        const string type = "_type";
        const string density = "_density";
        const string color = "_color";
        const string scatter = "_scatter";

        #region Properties
        protected override int NumBytes
        {
            get
            {
                int size = sizeof(int);
                size += KeyValueSize(type, AirType);
                size += KeyValueSize(density, WriteFloat(Density));
                size += KeyValueSize(color, WriteColor32(Color));
                size += KeyValueSize(scatter, Scattering ? one : zero);
                return size;
            }
        }
        public string AirType { get; private set; }
        public float Density { get; private set; }
        public Color32 Color { get; private set; }
        public bool Scattering { get; private set; } = true;
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            for (int i = 0, length = reader.ReadInt32(); i < length; ++i)
            {
                KeyValuePair<string, string> keyValue = ReadKeyValue(reader);
                if (keyValue.Key == type) AirType = keyValue.Value;
                else if (keyValue.Key == density) Density = ReadFloat(keyValue.Value);
                else if (keyValue.Key == color) Color = ReadColor32(keyValue.Value);
                else if (keyValue.Key == scatter) Scattering = ReadInt(keyValue.Value) != 0;
                else throw new ArgumentOutOfRangeException($"Invalid key={keyValue.Key} value={keyValue.Value}");
            }
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            writer.Write(4);
            WriteKeyValue(writer, type, AirType);
            WriteKeyValue(writer, density, WriteFloat(Density));
            WriteKeyValue(writer, color, WriteColor32(Color));
            WriteKeyValue(writer, scatter, Scattering ? one : zero);
        }
        #endregion
    }

    public class LensChunk : Chunk
    {
        const string fov = "_fov";
        const string dof = "_dof";
        const string exp = "_exp";
        const string vig = "_vig";
        const string sg = "_sg";
        const string gam = "_gam";

        #region Properties
        protected override int NumBytes
        {
            get
            {
                int size = sizeof(int);
                size += KeyValueSize(fov, WriteFloat(Fov));
                size += KeyValueSize(dof, WriteFloat(Dof));
                size += KeyValueSize(exp, WriteFloat(Exposure));
                size += KeyValueSize(vig, WriteFloat(Vignette));
                size += KeyValueSize(sg, WriteFloat(Stereo));
                size += KeyValueSize(gam, WriteFloat(Gamma));
                return size;
            }
        }
        public float Fov { get; private set; }
        public float Dof { get; private set; }
        public float Exposure { get; private set; }
        public float Vignette { get; private set; }
        public float Stereo { get; private set; }
        public float Gamma { get; private set; }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            for (int i = 0, length = reader.ReadInt32(); i < length; ++i)
            {
                KeyValuePair<string, string> keyValue = ReadKeyValue(reader);
                if (keyValue.Key == fov) Fov = ReadFloat(keyValue.Value);
                else if (keyValue.Key == dof) Dof = ReadFloat(keyValue.Value);
                else if (keyValue.Key == exp) Exposure = ReadFloat(keyValue.Value);
                else if (keyValue.Key == vig) Vignette = ReadFloat(keyValue.Value);
                else if (keyValue.Key == sg) Stereo = ReadFloat(keyValue.Value);
                else if (keyValue.Key == gam) Gamma = ReadFloat(keyValue.Value);
                else throw new ArgumentOutOfRangeException($"Invalid key={keyValue.Key} value={keyValue.Value}");
            }
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            writer.Write(6);
            WriteKeyValue(writer, fov, WriteFloat(Fov));
            WriteKeyValue(writer, dof, WriteFloat(Dof));
            WriteKeyValue(writer, exp, WriteFloat(Exposure));
            WriteKeyValue(writer, vig, WriteFloat(Vignette));
            WriteKeyValue(writer, sg, WriteFloat(Stereo));
            WriteKeyValue(writer, gam, WriteFloat(Gamma));
        }
        #endregion
    }

    public class PostChunk : Chunk
    {
        const string type = "_type";
        const string mix = "_mix";
        const string scale = "_scale";
        const string threshold = "_threshold";

        #region Properties
        protected override int NumBytes
        {
            get
            {
                int size = sizeof(int);
                size += KeyValueSize(type, PostType);
                size += KeyValueSize(mix, WriteFloat(Mix));
                if (Scale != 0.0f) size += KeyValueSize(scale, WriteFloat(Scale));
                if (Threshold != 0.0f) size += KeyValueSize(threshold, WriteFloat(Threshold));
                return size;
            }
        }
        public string PostType { get; private set; }
        public float Mix { get; private set; }
        public float Scale { get; private set; }
        public float Threshold { get; private set; }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            for (int i = 0, length = reader.ReadInt32(); i < length; ++i)
            {
                KeyValuePair<string, string> keyValue = ReadKeyValue(reader);
                if (keyValue.Key == type) PostType = keyValue.Value;
                else if (keyValue.Key == mix) Mix = ReadFloat(keyValue.Value);
                else if (keyValue.Key == scale) Scale = ReadFloat(keyValue.Value);
                else if (keyValue.Key == threshold) Threshold = ReadFloat(keyValue.Value);
                else throw new ArgumentOutOfRangeException($"Invalid key={keyValue.Key} value={keyValue.Value}");
            }
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            writer.Write(2 + Count(Mix, Scale, Threshold, 0.0f, 0.0f, 0.0f));
            WriteKeyValue(writer, type, PostType);
            WriteKeyValue(writer, mix, WriteFloat(Mix));
            if (Scale != 0.0f) WriteKeyValue(writer, scale, WriteFloat(Scale));
            if (Threshold != 0.0f) WriteKeyValue(writer, threshold, WriteFloat(Threshold));
        }
        #endregion
    }

    public class ViewChunk : Chunk
    {
        const string gdColor = "_gd_color";
        const string bgColor = "_bg_color";
        const string edgeColor = "_edge_color";

        #region Properties
        protected override int NumBytes
        {
            get
            {
                int size = sizeof(int);
                size += KeyValueSize(gdColor, WriteColor32(Ground));
                size += KeyValueSize(bgColor, WriteColor32(Background));
                size += KeyValueSize(edgeColor, WriteColor32(Edge));
                return size;
            }
        }
        public Color32 Ground { get; private set; }
        public Color32 Background { get; private set; }
        public Color32 Edge { get; private set; }
        #endregion

        #region Methods
        protected override void ReadBody(BinaryReader reader)
        {
            for (int i = 0, length = reader.ReadInt32(); i < length; ++i)
            {
                KeyValuePair<string, string> keyValue = ReadKeyValue(reader);
                if (keyValue.Key == gdColor) Ground = ReadColor32(keyValue.Value);
                else if (keyValue.Key == bgColor) Background = ReadColor32(keyValue.Value);
                else if (keyValue.Key == edgeColor) Edge = ReadColor32(keyValue.Value);
                else throw new ArgumentOutOfRangeException($"Invalid key={keyValue.Key} value={keyValue.Value}");
            }
        }
        protected override void WriteBody(BinaryWriter writer)
        {
            writer.Write(3);
            WriteKeyValue(writer, gdColor, WriteColor32(Ground));
            WriteKeyValue(writer, bgColor, WriteColor32(Background));
            WriteKeyValue(writer, edgeColor, WriteColor32(Edge));
        }
        #endregion
    }

    public class rObjChunk : Chunk { }

    public class rCamChunk : Chunk { }

    public class NoteChunk : Chunk { }

    public class Vox
    {
        const int VOX = 542658390;

        #region Properties
        public int Magic { get; private set; }
        public int Version { get; private set; }
        public MainChunk Main { get; private set; }
        #endregion

        #region Constructors
        public Vox(string path)
        {
            BinaryReader reader = new(new FileStream(path, FileMode.Open, FileAccess.Read));
            Read(reader);
            reader.Close();
        }
        #endregion

        #region Methods
        public void Read(BinaryReader reader)
        {
            Magic = reader.ReadInt32();
            if (Magic != VOX) throw new NotSupportedException("Invalid magic number.");

            Version = reader.ReadInt32();

            Main = new MainChunk();
            Main.Read(reader);
        }
        public void Write(BinaryWriter writer)
        {
            writer.Write(Magic);
            writer.Write(Version);
            Main.Write(writer);
        }
        #endregion
    }
}