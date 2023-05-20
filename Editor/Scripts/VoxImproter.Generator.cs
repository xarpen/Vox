using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

// ReSharper disable PossibleLossOfFraction

namespace System.Runtime.CompilerServices { public class IsExternalInit { } }

namespace Fluorite.Vox.Editor
{
    public partial class VoxImporter
    {
        // ReSharper disable once NotAccessedPositionalProperty.Local
        record Shape(Mesh Mesh, Texture[] Textures, Material[] Materials);

        class Generator
        {
            const int maxVoxels = XyziChunk.maxVoxels;
            const int maxColors = RgbaChunk.maxColors;

            static Lazy<VoxRenderPipelineAsset> PipelineAssetLazy = new(() => AssetDatabase.FindAssets($"t: {nameof(VoxRenderPipelineAsset)}").Select(x => AssetDatabase.LoadAssetAtPath<VoxRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(x))).FirstOrDefault() ?? ScriptableObject.CreateInstance<VoxRenderPipelineAsset>());
            static VoxRenderPipelineAsset PipelineAsset => PipelineAssetLazy.Value;

            class ShapeGenerator
            {
                record IndexedTextures (int Index, Texture2D Texture, Texture2D Mask);

                #region Fields
                Color32[] colors;
                Texture2D palette;
                Material paletteMaterial;
                Material[] materials;
                MaterialType[] materialType;
                byte[] voxel = new byte[maxVoxels * maxVoxels * maxVoxels];
                bool[] processed = new bool[maxVoxels * maxVoxels * maxVoxels];

                int index;
                List<Vector3> vertices = new(ushort.MaxValue);
                List<Vector3> normals = new(ushort.MaxValue);
                List<Vector2> uv0 = new(ushort.MaxValue);
                List<Vector2> uv1 = new(ushort.MaxValue);

                List<int> paletteTriangles = new();
                List<int>[] mattersTriangles = new List<int>[maxColors];
                List<int>[] combinedMattersTriangles = new List<int>[(int)MaterialType.Count];
                List<IndexedTextures>[] combinedMattersTextures = new List<IndexedTextures>[(int)MaterialType.Count];
                #endregion

                #region Constructors
                public ShapeGenerator(Color32[] colors, Texture2D palette, Material paletteMaterial, Material[] materials, MaterialType[] materialType, Color32[] points)
                {
                    this.colors = colors;
                    this.palette = palette;
                    this.paletteMaterial = paletteMaterial;
                    this.materials = materials;
                    this.materialType = materialType;

                    for (int i = 0, length = points.Length; i < length; ++i)
                    {
                        byte x = points[i].r;
                        byte y = points[i].b;
                        byte z = points[i].g;
                        byte w = points[i].a;
                        voxel[x + maxVoxels * (y + maxVoxels * z)] = w;
                    }
                    for (int i = 0; i < maxColors; ++i)
                    {
                        if (materials[i]) mattersTriangles[i] = new List<int>(ushort.MaxValue);
                    }
                    for (int i = 0; i < (int)MaterialType.Count; ++i)
                    {
                        combinedMattersTriangles[i] = new List<int>(ushort.MaxValue);
                        combinedMattersTextures[i] = new List<IndexedTextures>();
                    }
                }
                #endregion

                #region Methods
                public Shape CreateShape(Vector3Int size, float scaleFactor, ImportMaterialType importMaterials)
                {
                    Vector3 origin = -new Vector3(size.x / 2.0f, size.y / 2.0f, size.z / 2.0f);
                    AddSide(size, new[] { 0, 1, 2 }, 0, Vector3Int.left, Vector3.zero + origin, true, scaleFactor, importMaterials); // left
                    AddSide(size, new[] { 0, 1, 2 }, size.x - 1, Vector3Int.right, Vector3.right + origin, false, scaleFactor, importMaterials); // right
                    AddSide(size, new[] { 1, 0, 2 }, 0, Vector3Int.down, Vector3.zero + origin, false, scaleFactor, importMaterials); // bottom
                    AddSide(size, new[] { 1, 0, 2 }, size.y - 1, Vector3Int.up, Vector3.up + origin, true, scaleFactor, importMaterials); // top
                    AddSide(size, new[] { 2, 0, 1 }, 0, new Vector3Int(0, 0, -1), Vector3.zero + origin, true, scaleFactor, importMaterials); // forward
                    AddSide(size, new[] { 2, 0, 1 }, size.z - 1, new Vector3Int(0, 0, 1), Vector3.forward + origin, false, scaleFactor, importMaterials); // back

                    (Texture[] shapeTextures, Material[] shapeMaterials) = CreateMaterials(importMaterials);
                    return new Shape(CreateMesh(), shapeTextures, shapeMaterials);
                }
                #endregion

                #region Support Methods
                static int VoxelIndex(Vector3Int xyz) => xyz.x + maxVoxels * (xyz.y + maxVoxels * xyz.z);
                byte VoxelAt(Vector3Int xyz) => voxel[VoxelIndex(xyz)];
                Color ColorAt(Vector3Int xyz)
                {
                    int index = VoxelAt(xyz);
                    return index != 0 ? colors[index - 1] : Color.white;
                }
                Material MaterialAt(Vector3Int xyz) => materials[VoxelAt(xyz) - 1];
                MaterialType MaterialTypeAt(Vector3Int xyz)
                {
                    int index = VoxelAt(xyz);
                    return index != 0 ? materialType[index - 1] : MaterialType.Diffuse;
                }
                static bool ImportMaterials(ImportMaterialType importMaterials) => importMaterials > ImportMaterialType.None;
                static bool ImportCombinedMaterials(ImportMaterialType importMaterials) => importMaterials == ImportMaterialType.BakeEdgeToTexture;

                void AddSide(Vector3Int size, int[] dir, int limit, Vector3Int normal, Vector3 offset, bool inverse, float scaleFactor, ImportMaterialType importMaterials)
                {
                    // direction = dir[0]
                    // width     = dir[1]
                    // height    = dir[2]

                    int AddEdge(int index, Vector3Int xyz, byte vox, MaterialType materialType, bool combined, Vector3Int voxelsInWidth, Vector3Int voxelsInHeight)
                    {
                        vertices.Add((xyz + offset) * scaleFactor);
                        vertices.Add((xyz + voxelsInWidth + offset) * scaleFactor);
                        vertices.Add((xyz + voxelsInWidth + voxelsInHeight + offset) * scaleFactor);
                        vertices.Add((xyz + voxelsInHeight + offset) * scaleFactor);

                        normals.Add(normal);
                        normals.Add(normal);
                        normals.Add(normal);
                        normals.Add(normal);

                        if (combined)
                        {
                            uv0.Add(new Vector2(0, 0));
                            uv0.Add(new Vector2(1, 0));
                            uv0.Add(new Vector2(1, 1));
                            uv0.Add(new Vector2(0, 1));

                            uv1.Add(new Vector2(0, 0));
                            uv1.Add(new Vector2(1, 0));
                            uv1.Add(new Vector2(1, 1));
                            uv1.Add(new Vector2(0, 1));

                            int materialIndex = (int)materialType;
                            combinedMattersTriangles[materialIndex].Add(index);
                            combinedMattersTriangles[materialIndex].Add(index + (inverse ? 2 : 1));
                            combinedMattersTriangles[materialIndex].Add(index + (inverse ? 1 : 2));
                            combinedMattersTriangles[materialIndex].Add(index);
                            combinedMattersTriangles[materialIndex].Add(index + (inverse ? 3 : 2));
                            combinedMattersTriangles[materialIndex].Add(index + (inverse ? 2 : 3));
                        }
                        else if (materialType == MaterialType.Diffuse)
                        {
                            float u = ((vox - 1) % 8) / (float)8;
                            float v = (vox - 1) / 8 / (float)(maxColors / 8);
                            const float epsilon = 1 / (float)maxColors;

                            uv0.Add(new Vector2(u, v));
                            uv0.Add(new Vector2(u + epsilon, v));
                            uv0.Add(new Vector2(u + epsilon, v + epsilon));
                            uv0.Add(new Vector2(u, v + epsilon));

                            uv1.Add(new Vector2(0, 0));
                            uv1.Add(new Vector2(1, 0));
                            uv1.Add(new Vector2(1, 1));
                            uv1.Add(new Vector2(0, 1));

                            paletteTriangles.Add(index);
                            paletteTriangles.Add(index + (inverse ? 2 : 1));
                            paletteTriangles.Add(index + (inverse ? 1 : 2));
                            paletteTriangles.Add(index);
                            paletteTriangles.Add(index + (inverse ? 3 : 2));
                            paletteTriangles.Add(index + (inverse ? 2 : 3));
                        }
                        else
                        {
                            int uvWidth = voxelsInWidth[dir[1]];
                            int uvHeight = voxelsInHeight[dir[2]];

                            uv0.Add(new Vector2(0, 0));
                            uv0.Add(new Vector2(uvWidth, 0));
                            uv0.Add(new Vector2(uvWidth, uvHeight));
                            uv0.Add(new Vector2(0, uvHeight));

                            uv1.Add(new Vector2(0, 0));
                            uv1.Add(new Vector2(1, 0));
                            uv1.Add(new Vector2(1, 1));
                            uv1.Add(new Vector2(0, 1));

                            mattersTriangles[vox - 1].Add(index);
                            mattersTriangles[vox - 1].Add(index + (inverse ? 2 : 1));
                            mattersTriangles[vox - 1].Add(index + (inverse ? 1 : 2));
                            mattersTriangles[vox - 1].Add(index);
                            mattersTriangles[vox - 1].Add(index + (inverse ? 3 : 2));
                            mattersTriangles[vox - 1].Add(index + (inverse ? 2 : 3));
                        }

                        return index + 4;
                    }
                    void SetProcessed(Vector3Int xyz, Vector3Int processedSize)
                    {
                        for (int x = 0, sizeX = processedSize.x; x < sizeX; ++x)
                            for (int y = 0, sizeY = processedSize.y; y < sizeY; ++y)
                                for (int z = 0, sizeZ = processedSize.z; z < sizeZ; ++z)
                                    processed[VoxelIndex(xyz + new Vector3Int(x, y, z))] = true;
                    }
                    Vector3Int OffsetByDirection(Vector3Int xyz, int x, int y)
                    {
                        Vector3Int add = default;
                        add[dir[1]] = x;
                        add[dir[2]] = y;
                        return xyz + add;
                    }
                    Vector2Int GetSameVoxSize(Vector3Int xyz, byte vox, MaterialType materialType)
                    {
                        int width = 0;
                        int height = 0;
                        for (; height <= size[dir[2]] - 1 - xyz[dir[2]]; ++height)
                        {
                            Vector3Int sweep = OffsetByDirection(default, 0, height);
                            for (; sweep[dir[1]] <= size[dir[1]] - 1 - xyz[dir[1]]; ++sweep[dir[1]])
                            {
                                byte voxAtSweep = VoxelAt(xyz + sweep);
                                if (voxAtSweep == 0) break;
                                if (processed[VoxelIndex(xyz + sweep)]) break;
                                if (vox != voxAtSweep) break;
                                if (xyz[dir[0]] == limit) continue;

                                byte voxAtSweepNormal = VoxelAt(xyz + sweep + normal);
                                if (voxAtSweepNormal == 0) continue;
                                if (vox == voxAtSweepNormal) break;

                                MaterialType materialAtSweepNormal = MaterialTypeAt(xyz + sweep + normal);
                                if (materialType == materialAtSweepNormal) break;
                                if (materialType == MaterialType.Diffuse && materialAtSweepNormal == MaterialType.Metal) break;
                                if (materialType == MaterialType.Metal && materialAtSweepNormal == MaterialType.Diffuse) break;
                            }

                            if (width == 0) width = sweep[dir[1]];
                            if (width == 0) break;
                            if (sweep[dir[1]] != width) break;
                        }

                        return new Vector2Int(width, height);
                    }
                    (Vector2Int, float, float) GetSameMaterialTypeSize(Vector3Int xyz, MaterialType materialType, bool checkAtNormal = true)
                    {
                        int width = 0;
                        int height = 0;
                        for (; height <= size[dir[2]] - 1 - xyz[dir[2]]; ++height)
                        {
                            Vector3Int sweep = OffsetByDirection(default, 0, height);
                            for (; sweep[dir[1]] <= size[dir[1]] - 1 - xyz[dir[1]]; ++sweep[dir[1]])
                            {
                                byte voxAtSweep = VoxelAt(xyz + sweep);
                                if (voxAtSweep == 0) break;
                                if (processed[VoxelIndex(xyz + sweep)]) break;

                                MaterialType materialTypeAtSweep = MaterialTypeAt(xyz + sweep);
                                if (materialType != materialTypeAtSweep) break;

                                if (xyz[dir[0]] == limit || !checkAtNormal) continue;
                                byte voxAtSweepNormal = VoxelAt(xyz + sweep + normal);
                                if (voxAtSweepNormal == 0) continue;

                                if (materialType == MaterialTypeAt(xyz + sweep + normal)) break;

                                MaterialType materialAtSweepNormal = MaterialTypeAt(xyz + sweep + normal);
                                if (materialType == materialAtSweepNormal) break;
                                if (materialType == MaterialType.Diffuse && materialAtSweepNormal == MaterialType.Metal) break;
                                if (materialType == MaterialType.Metal && materialAtSweepNormal == MaterialType.Diffuse) break;
                            }

                            if (width == 0) width = sweep[dir[1]];
                            if (width == 0) break;
                            if (sweep[dir[1]] != width) break;
                        }

                        int voxSwitches = 0;
                        int maxSwitches = width * (height - 1) + (width - 1) * height;
                        for (int y = 0; y < height; ++y)
                        {
                            for (int x = 0; x < width; ++x)
                            {
                                byte centerVox = VoxelAt(OffsetByDirection(xyz, x, y));
                                if (x > 0 && centerVox != VoxelAt(OffsetByDirection(xyz, x - 1, y))) voxSwitches++;
                                if (y > 0 && centerVox != VoxelAt(OffsetByDirection(xyz, x, y - 1))) voxSwitches++;
                            }
                        }

                        return (new Vector2Int(width, height), voxSwitches, maxSwitches == 0 ? maxSwitches : (voxSwitches / (float)maxSwitches));
                    }

                    for (int x = 0, sizeX = size.x; x < sizeX; ++x)
                        for (int y = 0, sizeY = size.y; y < sizeY; ++y)
                            for (int z = 0, sizeZ = size.z; z < sizeZ; ++z)
                            {
                                Vector3Int xyz = new(x, y, z);
                                int voxelIndex = VoxelIndex(xyz);
                                byte vox = voxel[voxelIndex];

                                if (vox == 0) continue;
                                if (processed[voxelIndex]) continue;

                                MaterialType materialType = MaterialTypeAt(xyz);

                                Vector2Int sameVoxSize = GetSameVoxSize(xyz, vox, materialType);
                                (Vector2Int sameMaterialSize, float sameMaterialSwitches, float sameMaterialRatio) = GetSameMaterialTypeSize(xyz, materialType);
                                (Vector2Int sameMaterialNoLimitSize, float sameMaterialNoLimitSwitches, float _) = GetSameMaterialTypeSize(xyz, materialType, false);
                                Vector3Int voxelsInWidth = default;
                                Vector3Int voxelsInHeight = default;

                                if (ImportCombinedMaterials(importMaterials) && (sameMaterialNoLimitSwitches > 256) && sameMaterialNoLimitSize.magnitude > 8)
                                {
                                    voxelsInWidth[dir[1]] = sameMaterialNoLimitSize.x;
                                    voxelsInHeight[dir[2]] = sameMaterialNoLimitSize.y;

                                    combinedMattersTextures[(int)materialType].Add(new IndexedTextures(index, VoxRenderPipelineAsset.CreateCombinedBaseMap(xyz, dir, voxelsInWidth[dir[1]], voxelsInHeight[dir[2]], ColorAt), PipelineAsset.CreateCombinedMask(materialType, xyz, dir, voxelsInWidth[dir[1]], voxelsInHeight[dir[2]], MaterialAt)));

                                    index = AddEdge(index, xyz, vox, materialType, true, voxelsInWidth, voxelsInHeight);
                                }
                                else if (ImportCombinedMaterials(importMaterials) && (sameMaterialSwitches > 128 || sameMaterialRatio > 0.25f) && sameMaterialSize.magnitude > 8)
                                {
                                    voxelsInWidth[dir[1]] = sameMaterialSize.x;
                                    voxelsInHeight[dir[2]] = sameMaterialSize.y;

                                    combinedMattersTextures[(int)materialType].Add(new IndexedTextures(index, VoxRenderPipelineAsset.CreateCombinedBaseMap(xyz, dir, voxelsInWidth[dir[1]], voxelsInHeight[dir[2]], ColorAt), PipelineAsset.CreateCombinedMask(materialType, xyz, dir, voxelsInWidth[dir[1]], voxelsInHeight[dir[2]], MaterialAt)));

                                    index = AddEdge(index, xyz, vox, materialType, true, voxelsInWidth, voxelsInHeight);
                                }
                                else if (sameVoxSize.magnitude > 0)
                                {
                                    voxelsInWidth[dir[1]] = sameVoxSize.x;
                                    voxelsInHeight[dir[2]] = sameVoxSize.y;

                                    index = AddEdge(index, xyz, vox, materialType, false, voxelsInWidth, voxelsInHeight);
                                }
                                else
                                {
                                    continue;
                                }

                                Vector3Int processSize = default;
                                processSize[dir[0]] = 1;
                                processSize[dir[1]] = voxelsInWidth[dir[1]];
                                processSize[dir[2]] = voxelsInHeight[dir[2]];
                                SetProcessed(xyz, processSize);
                            }

                    Array.Clear(processed, 0, processed.Length);
                }

                Mesh CreateMesh()
                {
                    Mesh mesh = new();
                    mesh.SetVertices(vertices);
                    mesh.SetNormals(normals);
                    mesh.SetUVs(0, uv0);
                    mesh.SetUVs(1, uv1);

                    int subMeshIndex = 0;
                    int subMeshCount = 0;

                    if (paletteTriangles.Count > 0)
                        subMeshCount++;

                    foreach (List<int> triangles in combinedMattersTriangles)
                        if (triangles.Count > 0)
                            subMeshCount++;

                    foreach (List<int> triangles in mattersTriangles)
                        if (triangles is { Count: > 0 })
                            subMeshCount++;

                    mesh.subMeshCount = subMeshCount;

                    if (paletteTriangles.Count > 0)
                        mesh.SetTriangles(paletteTriangles, subMeshIndex++);

                    foreach (List<int> triangles in combinedMattersTriangles)
                        if (triangles.Count > 0)
                            mesh.SetTriangles(triangles, subMeshIndex++);

                    foreach (List<int> triangles in mattersTriangles)
                        if (triangles is { Count: > 0 })
                            mesh.SetTriangles(triangles, subMeshIndex++);

                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();

                    return mesh;
                }
                (Texture[], Material[]) CreateMaterials(ImportMaterialType importMaterials)
                {
                    int textureIndex = 0;
                    int textureCount = 0;
                    int materialIndex = 0;
                    int materialCount = 0;

                    if (paletteTriangles.Count > 0)
                    {
                        textureCount++;
                        materialCount++;
                    }
                    for (int i = 0; i < combinedMattersTriangles.Length; ++i)
                    {
                        if (!ImportCombinedMaterials(importMaterials) || combinedMattersTriangles[i].Count <= 0) continue;
                        if (i == 0) textureCount++;
                        else textureCount += 2;
                        materialCount++;
                    }
                    for (int i = 0; i < mattersTriangles.Length; ++i)
                        if (ImportMaterials(importMaterials) && materials[i] && mattersTriangles[i].Count > 0)
                            materialCount++;

                    Texture[] shapeTextures = new Texture[textureCount];
                    Material[] shapeMaterials = new Material[materialCount];

                    if (paletteTriangles.Count > 0)
                    {
                        shapeTextures[textureIndex++] = palette;
                        shapeMaterials[materialIndex++] = paletteMaterial;
                    }
                    for (int i = 0; i < combinedMattersTriangles.Length; ++i)
                    {
                        MaterialType type = (MaterialType)i;
                        if (!ImportCombinedMaterials(importMaterials) || combinedMattersTriangles[i].Count <= 0) continue;

                        int[] indices = combinedMattersTextures[i].Select(x => x.Index).ToArray();
                        (Texture2D texture, Rect[] rects) = VoxRenderPipelineAsset.PackTextures(type, combinedMattersTextures[i].Select(x => x.Texture), true);
                        (Texture2D mask, _) = VoxRenderPipelineAsset.PackTextures(type, combinedMattersTextures[i].Select(x => x.Mask), false);
                        for (int j = 0; j < combinedMattersTextures[i].Count; ++j)
                        {
                            int index = indices[j];
                            uv0[index + 0] = rects[j].position + Vector2.Scale(uv0[index + 0], rects[j].size);
                            uv0[index + 1] = rects[j].position + Vector2.Scale(uv0[index + 1], rects[j].size);
                            uv0[index + 2] = rects[j].position + Vector2.Scale(uv0[index + 2], rects[j].size);
                            uv0[index + 3] = rects[j].position + Vector2.Scale(uv0[index + 3], rects[j].size);
                        }

                        shapeTextures[textureIndex++] = texture;
                        if (i > 0 && mask) shapeTextures[textureIndex++] = mask;
                        shapeMaterials[materialIndex++] = PipelineAsset.CreateCombinedMaterial((MaterialType)i, texture, mask);
                    }

                    for (int i = 0; i < mattersTriangles.Length; ++i)
                        if (ImportMaterials(importMaterials) && materials[i] && mattersTriangles[i].Count > 0)
                            shapeMaterials[materialIndex++] = materials[i];

                    return (shapeTextures, shapeMaterials);
                }
                #endregion
            }

            #region Fields
            float scaleFactor;
            StaticEditorFlags staticFlags;
            int baseLayer;
            bool generateColliders;
            bool convex;
            ImportMaterialType importMaterials;
            #endregion

            #region Constructors
            internal Generator(float scaleFactor, StaticEditorFlags staticFlags, int baseLayer, bool generateColliders, bool convex, ImportMaterialType importMaterials)
            {
                this.scaleFactor = scaleFactor;
                this.generateColliders = generateColliders;
                this.convex = convex;
                this.staticFlags = staticFlags;
                this.baseLayer = baseLayer;
                this.importMaterials = importMaterials;
            }
            #endregion

            #region Methods
            internal (List<Shape>, GameObject) CreateAssets(Chunk main, string name)
            {
                Material CreatePaletteMaterial(Texture palette)
                {
                    Material material = new(PipelineAsset.GetShader(MaterialType.Diffuse, true)) { name = "Default" };
                    material.SetTexture(PipelineAsset.BaseMapProperty, palette);
                    return material;
                }

                RgbaChunk rgbaChunk = (RgbaChunk)main.Children.FirstOrDefault(x => x.ChunkType == Chunk.Type.Rgba);
                Color32[] colors = rgbaChunk == default ? Palette.Colors : rgbaChunk.Colors;
                Texture2D palette = Palette.Create(colors);
                Material paletteMaterial = CreatePaletteMaterial(palette);

                Material[] materials = new Material[maxColors];
                MaterialType[] materialType = new MaterialType[maxColors];
                bool linearColorSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;
                foreach (MaterialChunk chunk in main.Children.OfType<MaterialChunk>())
                {
                    if (chunk.MaterialType == MaterialType.Diffuse) continue;
                    byte index = (byte)(chunk.Index - 1);
                    Color color = colors[index];
                    if (linearColorSpace) color = color.linear;
                    materials[index] = PipelineAsset.CreateMaterial(index, chunk.MaterialType, color, chunk.Roughness, chunk.IOR, chunk.Specular, chunk.Metal, chunk.Emission, chunk.Flux, chunk.LowDynamicRange, chunk.Transparency);
                    materialType[index] = chunk.MaterialType;
                }

                List<Shape> shapes = new();
                for (int i = 1, count = main.Children.Count; i < count; ++i)
                {
                    if (main.Children[i - 1] is not SizeChunk sizeChunk || main.Children[i] is not XyziChunk xyziChunk) continue;

                    ShapeGenerator generator = new(colors, palette, paletteMaterial, materials, materialType, xyziChunk.Points);
                    Vector3Int size = new(sizeChunk.Size.x, sizeChunk.Size.z, sizeChunk.Size.y);
                    shapes.Add(generator.CreateShape(size, scaleFactor, importMaterials));
                }
                for (int i = 0; i < shapes.Count; ++i) shapes[i].Mesh.name = $"Shape {i + 1}";

                TransformChunk transform = (TransformChunk)main.Children.FirstOrDefault(x => x is TransformChunk);
                List<Chunk> nobjects = new();
                if (transform == default)
                {
                    transform = new TransformChunk(name, 1);
                    nobjects.Add(transform);

                    if (shapes.Count > 1)
                    {
                        GroupChunk group = new(new int[shapes.Count]);
                        nobjects.Add(group);

                        for (int i = 0; i < shapes.Count; ++i)
                        {
                            group.children[i] = 2 + i * 2;
                            nobjects.Add(new TransformChunk($"Transform {i + 1}", 2 + i * 2 + 1));
                            nobjects.Add(new ShapeChunk(i));
                        }
                    }
                    else
                    {
                        nobjects.Add(new ShapeChunk(0));
                    }
                }
                else
                {
                    foreach (Chunk chunk in main.Children)
                    {
                        switch (chunk.ChunkType)
                        {
                            case Chunk.Type.nTrn:
                            case Chunk.Type.nGrp:
                            case Chunk.Type.nShp:
                                nobjects.Add(chunk);
                                break;
                        }
                    }
                }

                return (shapes, CreateGameObject(transform, nobjects, shapes, default));
            }
            #endregion

            #region Support Methods
            GameObject CreateGameObject(string name, Vector3 position, Matrix4x4 orientation, int transformLayer, Transform parent)
            {
                GameObject gameObject = new(name ?? "Shape");
                gameObject.transform.SetParent(parent);
                GameObjectUtility.SetStaticEditorFlags(gameObject, staticFlags);

                gameObject.transform.localPosition = position * scaleFactor;
                if (orientation != default) gameObject.transform.rotation = orientation.rotation;
                if (baseLayer >= 0 && transformLayer >= 0) gameObject.layer = baseLayer + transformLayer;

                return gameObject;
            }
            GameObject CreateGameObject(string name, Vector3 position, Matrix4x4 orientation, int transformLayer, Mesh mesh, Material[] materials, Transform parent)
            {
                GameObject gameObject = CreateGameObject(name, parent ? position : Vector3.zero, parent ? orientation : Matrix4x4.identity, transformLayer, parent);

                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials;

                if (generateColliders)
                {
                    MeshCollider collider = gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                    collider.convex = convex;
                }

                if (!parent)
                {
                    Vector3 offset = position * scaleFactor;
                    Vector3[] vertices = mesh.vertices;
                    Vector3[] normals = mesh.normals;
                    Quaternion rotation = orientation.rotation;
                    for (int i = 0; i < vertices.Length; ++i) vertices[i] = rotation * vertices[i] + offset;
                    for (int i = 0; i < normals.Length; ++i) normals[i] = rotation * normals[i];
                    mesh.vertices = vertices;
                    mesh.normals = normals;
                    mesh.RecalculateBounds();
                }

                return gameObject;
            }
            GameObject CreateGameObject(TransformChunk transform, IReadOnlyList<Chunk> objects, IReadOnlyList<Shape> shapes, Transform parent)
            {
                while (true)
                {
                    if (objects[transform.Reference] is GroupChunk group)
                    {
                        if (transform.Position == default && transform.Orientation == default && group.children.Length == 1)
                        {
                            transform = (TransformChunk)objects[group.children[0]];
                            continue;
                        }

                        GameObject gameObject = CreateGameObject(transform.Name, transform.Position, transform.Orientation, transform.Layer, parent);
                        foreach (int index in group.children) CreateGameObject((TransformChunk)objects[index], objects, shapes, gameObject.transform);
                        return gameObject;
                    }
                    if (objects[transform.Reference] is ShapeChunk shape)
                    {
                        return CreateGameObject(transform.Name, transform.Position, transform.Orientation, transform.Layer, shapes[shape.ShapeIndex].Mesh, shapes[shape.ShapeIndex].Materials, parent);
                    }

                    throw new NotSupportedException();
                }
            }
            #endregion
        }
    }
}