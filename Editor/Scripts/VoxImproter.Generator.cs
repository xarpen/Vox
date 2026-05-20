using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;
using UnityEngine.Pool;
using UnityEngine.Rendering;

// ReSharper disable PossibleLossOfFraction
// ReSharper disable BadDeclarationBracesLineBreaks
namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}
// ReSharper restore BadDeclarationBracesLineBreaks

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

            static readonly Lazy<VoxRenderPipelineAsset> pipelineAssetLazy = new(() =>
            {
                VoxRenderPipelineAsset asset = AssetDatabase.FindAssets($"t: {nameof(VoxRenderPipelineAsset)}")
                                                            .Select(x => AssetDatabase.LoadAssetAtPath<VoxRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(x)))
                                                            .FirstOrDefault();
                return asset ? asset : ScriptableObject.CreateInstance<VoxRenderPipelineAsset>();
            });
            static VoxRenderPipelineAsset PipelineAsset => pipelineAssetLazy.Value;

            class ShapeGenerator : IDisposable
            {
                record IndexedTextures(int Index, Texture2D Texture, Texture2D Mask);

                #region Fields
                readonly Color32[] colors;
                readonly Texture2D palette;
                readonly Material paletteMaterial;
                readonly Material[] materials;
                readonly MaterialType[] materialType;
                readonly byte[] voxel;
                readonly bool[] processed;

                int index;
                readonly List<Vector3> vertices;
                readonly List<Vector3> normals;
                readonly List<Vector2> uv0;
                readonly List<Vector2> uv1;

                readonly List<int> paletteTriangles;
                readonly List<int>[] mattersTriangles = new List<int>[maxColors];
                readonly List<int>[] combinedMattersTriangles = new List<int>[(int)MaterialType.Count];
                readonly List<IndexedTextures>[] combinedMattersTextures = new List<IndexedTextures>[(int)MaterialType.Count];
                #endregion

                #region Constructors
                public ShapeGenerator(Color32[] colors, Texture2D palette, Material paletteMaterial, Material[] materials, MaterialType[] materialType, Color32[] points)
                {
                    this.colors = colors;
                    this.palette = palette;
                    this.paletteMaterial = paletteMaterial;
                    this.materials = materials;
                    this.materialType = materialType;

                    voxel = ArrayPool<byte>.Shared.Rent(maxVoxels * maxVoxels * maxVoxels);
                    processed = ArrayPool<bool>.Shared.Rent(maxVoxels * maxVoxels * maxVoxels);

                    Array.Clear(voxel, 0, voxel.Length);
                    Array.Clear(processed, 0, processed.Length);

                    vertices = ListPool<Vector3>.Get();
                    normals = ListPool<Vector3>.Get();
                    uv0 = ListPool<Vector2>.Get();
                    uv1 = ListPool<Vector2>.Get();

                    paletteTriangles = ListPool<int>.Get();

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
                        if (materials[i]) mattersTriangles[i] = ListPool<int>.Get();
                    }

                    for (int i = 0; i < (int)MaterialType.Count; ++i)
                    {
                        combinedMattersTriangles[i] = ListPool<int>.Get();
                        combinedMattersTextures[i] = ListPool<IndexedTextures>.Get();
                    }
                }
                public void Dispose()
                {
                    if (voxel is not null) ArrayPool<byte>.Shared.Return(voxel);
                    if (processed is not null) ArrayPool<bool>.Shared.Return(processed);
                    if (vertices is not null) ListPool<Vector3>.Release(vertices);
                    if (normals is not null) ListPool<Vector3>.Release(normals);
                    if (uv0 is not null) ListPool<Vector2>.Release(uv0);
                    if (uv1 is not null) ListPool<Vector2>.Release(uv1);
                    if (paletteTriangles is not null) ListPool<int>.Release(paletteTriangles);

                    for (int i = 0; i < maxColors; ++i)
                    {
                        if (materials[i]) ListPool<int>.Release(mattersTriangles[i]);
                    }

                    for (int i = 0; i < (int)MaterialType.Count; ++i)
                    {
                        if (combinedMattersTriangles[i] is not null) ListPool<int>.Release(combinedMattersTriangles[i]);
                        if (combinedMattersTextures[i] is not null) ListPool<IndexedTextures>.Release(combinedMattersTextures[i]);
                    }
                }
                #endregion

                #region Methods
                public Shape CreateShape(Vector3Int size, float scaleFactor, ImportMaterialType importMaterials)
                {
                    Vector3 origin = -new Vector3(size.x / 2.0f, size.y / 2.0f, size.z / 2.0f);
                    AddSide(size, new[] { 0, 1, 2 }, 0, Vector3Int.left, Vector3.zero + origin, true, scaleFactor, importMaterials);                      // left
                    AddSide(size, new[] { 0, 1, 2 }, size.x - 1, Vector3Int.right, Vector3.right + origin, false, scaleFactor, importMaterials);          // right
                    AddSide(size, new[] { 1, 0, 2 }, 0, Vector3Int.down, Vector3.zero + origin, false, scaleFactor, importMaterials);                     // bottom
                    AddSide(size, new[] { 1, 0, 2 }, size.y - 1, Vector3Int.up, Vector3.up + origin, true, scaleFactor, importMaterials);                 // top
                    AddSide(size, new[] { 2, 0, 1 }, 0, new Vector3Int(0, 0, -1), Vector3.zero + origin, true, scaleFactor, importMaterials);             // forward
                    AddSide(size, new[] { 2, 0, 1 }, size.z - 1, new Vector3Int(0, 0, 1), Vector3.forward + origin, false, scaleFactor, importMaterials); // back

                    (Texture[] shapeTextures, Material[] shapeMaterials) = CreateMaterials(importMaterials);
                    return new Shape(CreateMesh(size), shapeTextures, shapeMaterials);
                }
                #endregion

                #region Support Methods
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                static int VoxelIndex(Vector3Int xyz) => xyz.x + maxVoxels * (xyz.y + maxVoxels * xyz.z);
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                byte VoxelAt(Vector3Int xyz) => voxel[VoxelIndex(xyz)];
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                Color ColorAt(Vector3Int xyz)
                {
                    int index = VoxelAt(xyz);
                    return index != 0 ? colors[index - 1] : Color.white;
                }
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                Material MaterialAt(Vector3Int xyz) => materials[VoxelAt(xyz) - 1];
                static bool ImportMaterials(ImportMaterialType importMaterials) => importMaterials > ImportMaterialType.None;
                static bool ImportCombinedMaterials(ImportMaterialType importMaterials) => importMaterials == ImportMaterialType.BakeEdgeToTexture;
                void AddSide(Vector3Int size, int[] dir, int limit, Vector3Int normal, Vector3 offset, bool inverse, float scaleFactor, ImportMaterialType importMaterials)
                {
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    static bool ShouldRenderFace(byte voxelValue, MaterialType materialAtVoxel, byte neighborVoxel, MaterialType neighborMaterial)
                    {
                        if (neighborVoxel == 0) return true;

                        bool isGlass = materialAtVoxel == MaterialType.Glass;
                        bool isNeighborGlass = neighborMaterial == MaterialType.Glass;

                        if (isGlass && isNeighborGlass)
                            return voxelValue != neighborVoxel;

                        return isGlass || isNeighborGlass;
                    }

                    int AddEdge(int index, int ay, int az, Vector3Int xyz, byte vox, MaterialType material, bool combined, Vector3Int voxelsInWidth, Vector3Int voxelsInHeight)
                    {
                        const float inv8 = 1.0f / 8.0f;
                        const float invMaxColors = 1.0f / maxColors;

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

                            int materialIndex = (int)material;
                            combinedMattersTriangles[materialIndex].Add(index);
                            combinedMattersTriangles[materialIndex].Add(index + (inverse ? 2 : 1));
                            combinedMattersTriangles[materialIndex].Add(index + (inverse ? 1 : 2));
                            combinedMattersTriangles[materialIndex].Add(index);
                            combinedMattersTriangles[materialIndex].Add(index + (inverse ? 3 : 2));
                            combinedMattersTriangles[materialIndex].Add(index + (inverse ? 2 : 3));
                        }
                        else if (material == MaterialType.Diffuse)
                        {
                            float u = ((vox - 1) % 8) * inv8;
                            float v = (vox - 1) / 8 / (float)(maxColors / 8);
                            float epsilon = invMaxColors;

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
                            int uvWidth = voxelsInWidth[ay];
                            int uvHeight = voxelsInHeight[az];

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
                        Vector3Int p = xyz;
                        int baseX = xyz.x;
                        int baseY = xyz.y;
                        int baseZ = xyz.z;

                        for (int x = 0, sizeX = processedSize.x; x < sizeX; ++x)
                        {
                            p.x = baseX + x;
                            for (int y = 0, sizeY = processedSize.y; y < sizeY; ++y)
                            {
                                p.y = baseY + y;
                                for (int z = 0, sizeZ = processedSize.z; z < sizeZ; ++z)
                                {
                                    p.z = baseZ + z;
                                    processed[VoxelIndex(p)] = true;
                                }
                            }
                        }
                    }

                    // direction = dir[0]
                    // width     = dir[1]
                    // height    = dir[2]
                    int ax = dir[0];
                    int ay = dir[1];
                    int az = dir[2];
                    int sizeAy = size[ay];
                    int sizeAz = size[az];
                    bool doCombined = ImportCombinedMaterials(importMaterials);
                    const int combinedHardSwitchThreshold = 256;
                    const int combinedSoftSwitchThreshold = 128;
                    const float combinedSwitchRatioThreshold = 0.25f;
                    const int combinedMinArea = 64;

                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    Vector2Int GetSameVoxSize(int originX, int originY, int originZ, byte targetVoxel, MaterialType targetMaterial)
                    {
                        int originAlongNormal = ax == 0 ? originX : ax == 1 ? originY : originZ;
                        int originAlongWidth = ay == 0 ? originX : ay == 1 ? originY : originZ;
                        int originAlongHeight = az == 0 ? originX : az == 1 ? originY : originZ;

                        int width = 0;
                        int height = 0;

                        for (; height <= sizeAz - 1 - originAlongHeight; ++height)
                        {
                            int currentWidth = 0;

                            for (; currentWidth <= sizeAy - 1 - originAlongWidth; ++currentWidth)
                            {
                                int sampleX = originX;
                                int sampleY = originY;
                                int sampleZ = originZ;

                                if (ay == 0) sampleX += currentWidth;
                                else if (ay == 1) sampleY += currentWidth;
                                else sampleZ += currentWidth;
                                if (az == 0) sampleX += height;
                                else if (az == 1) sampleY += height;
                                else sampleZ += height;

                                int voxelIndex = sampleX + maxVoxels * (sampleY + maxVoxels * sampleZ);

                                byte voxelValue = voxel[voxelIndex];
                                if (voxelValue == 0) break;
                                if (processed[voxelIndex]) break;
                                if (voxelValue != targetVoxel) break;

                                if (originAlongNormal == limit) continue;

                                int neighborIndex = (sampleX + normal.x) + maxVoxels * ((sampleY + normal.y) + maxVoxels * (sampleZ + normal.z));
                                byte neighborVoxel = voxel[neighborIndex];
                                if (neighborVoxel == 0) continue;
                                if (neighborVoxel == targetVoxel) break;

                                MaterialType neighborMaterial = materialType[neighborVoxel - 1];
                                if (!ShouldRenderFace(voxelValue, targetMaterial, neighborVoxel, neighborMaterial)) break;

                            }

                            if (width == 0) width = currentWidth;
                            if (width == 0) break;
                            if (currentWidth != width) break;
                        }

                        return new Vector2Int(width, height);
                    }
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void GetSameMaterialTypeSize(int originX, int originY, int originZ, MaterialType material, bool checkAtNormal, out int width, out int height, out int voxelSwitches, out float switchRatio)
                    {
                        int originAlongNormal = ax == 0 ? originX : ax == 1 ? originY : originZ;
                        int originAlongWidth = ay == 0 ? originX : ay == 1 ? originY : originZ;
                        int originAlongHeight = az == 0 ? originX : az == 1 ? originY : originZ;

                        width = 0;
                        height = 0;
                        voxelSwitches = 0;

                        for (; height <= sizeAz - 1 - originAlongHeight; ++height)
                        {
                            int currentWidth = 0;
                            byte prevInRow = 0;

                            for (; currentWidth <= sizeAy - 1 - originAlongWidth; ++currentWidth)
                            {
                                int sampleX = originX;
                                int sampleY = originY;
                                int sampleZ = originZ;

                                if (ay == 0) sampleX += currentWidth;
                                else if (ay == 1) sampleY += currentWidth;
                                else sampleZ += currentWidth;
                                if (az == 0) sampleX += height;
                                else if (az == 1) sampleY += height;
                                else sampleZ += height;

                                int voxelIndex = sampleX + maxVoxels * (sampleY + maxVoxels * sampleZ);

                                byte voxelValue = voxel[voxelIndex];
                                if (voxelValue == 0) break;
                                if (processed[voxelIndex]) break;
                                if (materialType[voxelValue - 1] != material) break;

                                if (currentWidth > 0 && voxelValue != prevInRow) voxelSwitches++;
                                prevInRow = voxelValue;

                                if (height > 0)
                                {
                                    int downIndex = (sampleX - (az == 0 ? 1 : 0)) + maxVoxels * ((sampleY - (az == 1 ? 1 : 0)) + maxVoxels * (sampleZ - (az == 2 ? 1 : 0)));
                                    if (voxelValue != voxel[downIndex]) voxelSwitches++;
                                }

                                if (originAlongNormal == limit || !checkAtNormal) continue;

                                int neighborIndex = (sampleX + normal.x) + maxVoxels * ((sampleY + normal.y) + maxVoxels * (sampleZ + normal.z));
                                byte neighborVoxel = voxel[neighborIndex];
                                if (neighborVoxel == 0) continue;
                                MaterialType neighborMaterial = materialType[neighborVoxel - 1];
                                if (!ShouldRenderFace(voxelValue, material, neighborVoxel, neighborMaterial)) break;
                            }

                            if (width == 0) width = currentWidth;
                            if (width == 0) break;
                            if (currentWidth != width) break;
                        }

                        int maxPossibleSwitches = width * (height - 1) + (width - 1) * height;
                        switchRatio = maxPossibleSwitches == 0 ? 0f : voxelSwitches / (float)maxPossibleSwitches;
                    }

                    int normalStride = normal.x + maxVoxels * (normal.y + maxVoxels * normal.z);

                    for (int z = 0, sizeZ = size.z; z < sizeZ; ++z)
                    {
                        int zBase = maxVoxels * maxVoxels * z;

                        for (int y = 0, sizeY = size.y; y < sizeY; ++y)
                        {
                            int yzBase = zBase + maxVoxels * y;

                            for (int x = 0, sizeX = size.x; x < sizeX; ++x)
                            {
                                int voxelIndex = yzBase + x;
                                byte voxelValue = voxel[voxelIndex];

                                if (voxelValue == 0) continue;
                                if (processed[voxelIndex]) continue;

                                Vector3Int xyz = new(x, y, z);
                                MaterialType materialAtVoxel = materialType[voxelValue - 1];

                                int alongNormal = ax == 0 ? x : ax == 1 ? y : z;
                                if (alongNormal != limit)
                                {
                                    byte neighborVoxel = voxel[voxelIndex + normalStride];
                                    MaterialType neighborMaterial = neighborVoxel == 0 ? default : materialType[neighborVoxel - 1];
                                    if (!ShouldRenderFace(voxelValue, materialAtVoxel, neighborVoxel, neighborMaterial)) continue;
                                }

                                Vector3Int voxelsInWidth = default;
                                Vector3Int voxelsInHeight = default;

                                bool canCombine = doCombined && (materialAtVoxel == MaterialType.Diffuse || materialAtVoxel == MaterialType.Metal);
                                if (canCombine)
                                {
                                    GetSameMaterialTypeSize(x, y, z, materialAtVoxel, false, out int noLimitMaterialWidth, out int noLimitMaterialHeight, out int noLimitMaterialSwitches, out _);
                                    Vector2Int sameMaterialNoLimitRegion = new(noLimitMaterialWidth, noLimitMaterialHeight);

                                    bool hardCombinedCandidate = noLimitMaterialSwitches > combinedHardSwitchThreshold && sameMaterialNoLimitRegion.sqrMagnitude > combinedMinArea;
                                    if (hardCombinedCandidate)
                                    {
                                        voxelsInWidth[ay] = sameMaterialNoLimitRegion.x;
                                        voxelsInHeight[az] = sameMaterialNoLimitRegion.y;

                                        combinedMattersTextures[(int)materialAtVoxel].Add(new IndexedTextures(index, VoxRenderPipelineAsset.CreateCombinedBaseMap(xyz, dir, voxelsInWidth[ay], voxelsInHeight[az], ColorAt), PipelineAsset.CreateCombinedMask(materialAtVoxel, xyz, dir, voxelsInWidth[ay], voxelsInHeight[az], MaterialAt)));
                                        index = AddEdge(index, ay, az, xyz, voxelValue, materialAtVoxel, true, voxelsInWidth, voxelsInHeight);

                                        goto setProcessed;
                                    }

                                    GetSameMaterialTypeSize(x, y, z, materialAtVoxel, true, out int materialWidth, out int materialHeight, out int materialSwitches, out float materialSwitchRatio);
                                    Vector2Int sameMaterialRegion = new(materialWidth, materialHeight);

                                    bool softCombinedCandidate = (materialSwitches > combinedSoftSwitchThreshold || materialSwitchRatio > combinedSwitchRatioThreshold) && sameMaterialRegion.sqrMagnitude > combinedMinArea;
                                    if (softCombinedCandidate)
                                    {
                                        voxelsInWidth[ay] = sameMaterialRegion.x;
                                        voxelsInHeight[az] = sameMaterialRegion.y;

                                        combinedMattersTextures[(int)materialAtVoxel].Add(new IndexedTextures(index, VoxRenderPipelineAsset.CreateCombinedBaseMap(xyz, dir, voxelsInWidth[ay], voxelsInHeight[az], ColorAt), PipelineAsset.CreateCombinedMask(materialAtVoxel, xyz, dir, voxelsInWidth[ay], voxelsInHeight[az], MaterialAt)));
                                        index = AddEdge(index, ay, az, xyz, voxelValue, materialAtVoxel, true, voxelsInWidth, voxelsInHeight);

                                        goto setProcessed;
                                    }
                                }

                                Vector2Int sameVoxelRegion = GetSameVoxSize(x, y, z, voxelValue, materialAtVoxel);
                                if (sameVoxelRegion.sqrMagnitude <= 0) continue;

                                voxelsInWidth[ay] = sameVoxelRegion.x;
                                voxelsInHeight[az] = sameVoxelRegion.y;

                                index = AddEdge(index, ay, az, xyz, voxelValue, materialAtVoxel, false, voxelsInWidth, voxelsInHeight);

                            setProcessed:
                                Vector3Int processSize = default;
                                processSize[ax] = 1;
                                processSize[ay] = voxelsInWidth[ay];
                                processSize[az] = voxelsInHeight[az];
                                SetProcessed(xyz, processSize);
                            }
                        }
                    }

                    Array.Clear(processed, 0, processed.Length);
                }
                Mesh CreateMesh(Vector3Int size)
                {
                    Mesh mesh = new() { name = $"{size.x}x{size.y}x{size.z}" };

                    if (vertices.Count >= ushort.MaxValue)
                        mesh.indexFormat = IndexFormat.UInt32;

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
            readonly float scaleFactor;
            readonly StaticEditorFlags staticFlags;
            readonly int baseLayer;
            readonly bool generateColliders;
            readonly bool convex;
            readonly ImportMaterialType importMaterials;
            readonly uint renderingLayerMask;
            #endregion

            #region Constructors
            internal Generator(float scaleFactor, StaticEditorFlags staticFlags, int baseLayer, bool generateColliders, bool convex, ImportMaterialType importMaterials, uint renderingLayerMask)
            {
                this.scaleFactor = scaleFactor;
                this.generateColliders = generateColliders;
                this.convex = convex;
                this.staticFlags = staticFlags;
                this.baseLayer = baseLayer;
                this.importMaterials = importMaterials;
                this.renderingLayerMask = renderingLayerMask;
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
                foreach (MaterialChunk chunk in main.Children.OfType<MaterialChunk>())
                {
                    if (chunk.MaterialType == MaterialType.Diffuse) continue;

                    byte index = (byte)(chunk.Index - 1);
                    Color color = colors[index];
                    materials[index] = PipelineAsset.CreateMaterial(index, chunk.MaterialType, color, chunk.Roughness, chunk.IOR, chunk.Specular, chunk.Metal, chunk.Emission, chunk.Flux, chunk.LowDynamicRange, chunk.Transparency);
                    materialType[index] = chunk.MaterialType;
                }

                List<Shape> shapes = new();
                for (int i = 1, count = main.Children.Count; i < count; ++i)
                {
                    if (main.Children[i - 1] is not SizeChunk sizeChunk || main.Children[i] is not XyziChunk xyziChunk) continue;

                    using ShapeGenerator generator = new(colors, palette, paletteMaterial, materials, materialType, xyziChunk.Points);
                    Vector3Int size = new(sizeChunk.Size.x, sizeChunk.Size.z, sizeChunk.Size.y);
                    shapes.Add(generator.CreateShape(size, scaleFactor, importMaterials));
                }

                foreach (Shape shape in shapes)
                    shape.Mesh.name = $"Shape {shape.Mesh.name}";

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
            GameObject CreateGameObject(string name, Vector3 position, Vector3 rotation, Vector3 scale, int transformLayer, Transform parent)
            {
                GameObject gameObject = new(name ?? "Shape" + (parent ? $" {parent.childCount}" : ""));
                gameObject.transform.SetParent(parent);
                GameObjectUtility.SetStaticEditorFlags(gameObject, staticFlags);

                gameObject.transform.localPosition = position * scaleFactor;
                gameObject.transform.localRotation = Quaternion.Euler(rotation);
                gameObject.transform.localScale = scale;
                if (baseLayer >= 0 && transformLayer >= 0) gameObject.layer = baseLayer + transformLayer;

                return gameObject;
            }
            GameObject CreateGameObject(string name, Vector3 position, Vector3 rotation, Vector3 scale, int transformLayer, uint renderingLayerMask, Mesh mesh, Material[] materials, Transform parent)
            {
                GameObject gameObject = CreateGameObject(name,
                                                         parent ? position : Vector3.zero,
                                                         rotation,
                                                         scale,
                                                         transformLayer, parent);

                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

                MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterials = materials;
                if (renderingLayerMask > 0) meshRenderer.renderingLayerMask = renderingLayerMask;

                if (generateColliders)
                {
                    MeshCollider collider = gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                    collider.convex = convex;
                }

                if (!parent) // Bake position and rotation for root object
                {
                    Vector3 offset = position * scaleFactor;
                    //if ((int)(mesh.bounds.size.x / scaleFactor) % 2 == 1) offset += scaleFactor / 2 * Vector3.right;
                    if ((int)(mesh.bounds.size.y / scaleFactor) % 2 == 1) offset += scaleFactor / 2 * Vector3.up;
                    //if ((int)(mesh.bounds.size.z / scaleFactor) % 2 == 1) offset += scaleFactor / 2 * Vector3.forward;

                    Vector3[] vertices = mesh.vertices;
                    Vector3[] normals = mesh.normals;
                    Quaternion rot = Quaternion.Euler(rotation);
                    for (int i = 0; i < vertices.Length; ++i) vertices[i] = rot * vertices[i] + offset;
                    for (int i = 0; i < normals.Length; ++i) normals[i] = rot * normals[i];
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
                        if (transform.Position == default && transform.EulerAngles == default && transform.Scale == Vector3.one && group.children.Length == 1)
                        {
                            transform = (TransformChunk)objects[group.children[0]];
                            continue;
                        }

                        GameObject gameObject = CreateGameObject(transform.Name, transform.Position, transform.EulerAngles, transform.Scale, transform.Layer, parent);
                        foreach (int index in group.children) CreateGameObject((TransformChunk)objects[index], objects, shapes, gameObject.transform);
                        return gameObject;
                    }

                    if (objects[transform.Reference] is ShapeChunk shape)
                    {
                        return CreateGameObject(transform.Name, transform.Position, transform.EulerAngles, transform.Scale, transform.Layer, renderingLayerMask, shapes[shape.ShapeIndex].Mesh, shapes[shape.ShapeIndex].Materials, parent);
                    }

                    throw new NotSupportedException();
                }
            }
            #endregion
        }
    }
}