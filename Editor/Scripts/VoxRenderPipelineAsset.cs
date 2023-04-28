﻿using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System;

namespace Fluorite.Vox.Editor
{
    [CreateAssetMenu(fileName = "VRP", menuName = "Vox/Render Pipeline Asset")]
    public sealed class VoxRenderPipelineAsset : ScriptableObject
    {
        const float metalIORDivider = 3.0f;
        const float metalSpecularDivider = 2.0f;
        const float glassIORDivider = 2.0f;

        #region Fields
        [Header("Shader")]
        [SerializeField] Shader diffuseShader;
        [SerializeField] Shader metalShader;
        [SerializeField] Shader metalCombinedShader;
        [SerializeField] Shader emitShader;
        [SerializeField] Shader glassShader;

        [Header("Base")]
        [SerializeField] string baseMapProperty = "_MainTex";
        [SerializeField] string maskMapProperty = "_MaskMap";
        [SerializeField] string colorProperty = "_Color";

        [Header("Metallic")]
        [SerializeField] string roughnessProperty = "_Roughness";
        [SerializeField] string iorProperty = "_IOR";
        [SerializeField] string specularProperty = "_Specular";
        [SerializeField] string metallicProperty = "_Metallic";

        [Header("Emissive")]
        [SerializeField] string emissionProperty = "_Emission";
        [SerializeField] string fluxProperty = "_Flux";
        [SerializeField] string lowDynamicRangeProperty = "_LowDynamicRange";

        [Header("Transparent")]
        [SerializeField] string transparencyProperty = "_Transparency";
        #endregion

        #region Properties
        public string BaseMapProperty => baseMapProperty;
        public string MaskMapProperty => maskMapProperty;
        public string ColorProperty => colorProperty;
        public string RoughnessProperty => roughnessProperty;
        public string IorProperty => iorProperty;
        public string SpecularProperty => specularProperty;
        public string MetallicProperty => metallicProperty;
        public string EmissionProperty => emissionProperty;
        public string FluxProperty => fluxProperty;
        public string LowDynamicRangeProperty => lowDynamicRangeProperty;
        public string TransparencyProperty => transparencyProperty;
        #endregion

        #region Methods
        public Shader GetShader(MaterialType type, bool combined = false)
        {
            return type switch
            {
                MaterialType.Diffuse => diffuseShader,
                MaterialType.Metal => combined ? metalCombinedShader : metalShader,
                MaterialType.Glass when combined => throw new NotImplementedException(),
                MaterialType.Glass => glassShader,
                MaterialType.Emission when combined => throw new NotImplementedException(),
                MaterialType.Emission => emitShader,
                _ => throw new ArgumentOutOfRangeException()
            };
        }
        public static (Texture2D, Rect[]) PackTextures(MaterialType type, IEnumerable<Texture2D> textures, bool baseMap, int padding = 1)
        {
            Texture2D texture = CreateTexture(GetTextureName(type, true, baseMap), 2, 2);
            Rect[] rects = texture.PackTextures(textures.ToArray(), padding);
            return (texture, rects);
        }
        public static Texture2D CreateTexture(string name, int width, int height) => new(width, height, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        public Material CreateMaterial(int index, MaterialType type, Color color, float roughness, float ior, float specular, float metal, float emission, float flux, float lowDynamicRange, float transparency)
        {
            Material material = new(GetShader(type)) { name = GetMaterialName(type, false, index) };
            switch (type)
            {
                case MaterialType.Metal:
                    material.SetColor(colorProperty, color);
                    material.SetFloat(roughnessProperty, roughness);
                    material.SetFloat(iorProperty, ior / metalIORDivider);
                    material.SetFloat(specularProperty, specular / metalSpecularDivider);
                    material.SetFloat(metallicProperty, metal);
                    break;

                case MaterialType.Emission:
                    material.SetColor(colorProperty, color);
                    material.SetFloat(emissionProperty, emission);
                    material.SetFloat(fluxProperty, flux);
                    material.SetFloat(lowDynamicRangeProperty, lowDynamicRange);
                    break;

                case MaterialType.Glass:
                    material.SetColor(colorProperty, color);
                    material.SetFloat(roughnessProperty, roughness);
                    material.SetFloat(iorProperty, ior / glassIORDivider);
                    material.SetFloat(transparencyProperty, transparency);
                    break;
            }
            return material;
        }
        public Material CreateCombinedMaterial(MaterialType type, Texture2D baseMap, Texture2D mask)
        {
            Material material = new(GetShader(type, true)) { name = GetMaterialName(type, true) };
            material.SetTexture(baseMapProperty, baseMap);
            if (mask) material.SetTexture(maskMapProperty, mask);
            return material;
        }
        public static Texture2D CreateCombinedBaseMap(Vector3Int xyz, int[] dir, int width, int height, Func<Vector3Int, Color> colorAt)
        {
            Texture2D texture = CreateTexture("", width, height);
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    Vector3Int sweep = default;
                    sweep[dir[1]] = x;
                    sweep[dir[2]] = y;
                    pixels[y * width + x] = colorAt(xyz + sweep);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
        public Texture2D CreateCombinedMask(MaterialType type, Vector3Int xyz, int[] dir, int width, int height, Func<Vector3Int, Material> materialAt)
        {
            Texture2D texture = CreateTexture("", width, height);
            Color32[] colors = new Color32[width * height];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    Vector3Int sweep = default;
                    sweep[dir[1]] = x;
                    sweep[dir[2]] = y;
                    Material material = materialAt(xyz + sweep);
                    int index = y * width + x;
                    switch (type)
                    {
                        case MaterialType.Metal:
                            if (material)
                            {
                                colors[index].r = (byte)(material.GetFloat(roughnessProperty) * 255);
                                colors[index].g = (byte)(material.GetFloat(iorProperty) * 255);
                                colors[index].b = (byte)(material.GetFloat(specularProperty) * 255);
                                colors[index].a = (byte)(material.GetFloat(metallicProperty) * 255);
                            }
                            else
                            {
                                colors[index].r = 0;
                                colors[index].g = 0;
                                colors[index].b = 0;
                                colors[index].a = 0;
                            }
                            break;

                        case MaterialType.Emission:
                            if (material)
                            {
                                colors[index].r = (byte)(material.GetFloat(emissionProperty) * 255);
                                colors[index].g = (byte)(material.GetFloat(fluxProperty) * 255);
                                colors[index].b = (byte)(material.GetFloat(lowDynamicRangeProperty) * 255);
                            }
                            break;

                        case MaterialType.Glass:
                            if (material)
                            {
                                colors[index].r = (byte)(material.GetFloat(roughnessProperty) * 255);
                                colors[index].g = (byte)(material.GetFloat(iorProperty) * 255);
                                colors[index].b = (byte)(material.GetFloat(transparencyProperty) * 255);
                            }
                            break;
                    }
                }
            }
            texture.SetPixels32(colors);
            texture.Apply();
            return texture;
        }
        #endregion

        #region Support Methods
        static string GetTextureName(MaterialType type, bool combined, bool baseMap) => $"{type}{(combined ? " Combined" : "")} {(baseMap ? "Base" : "Mask")}";
        static string GetMaterialName(MaterialType type, bool combined = false, int index = -1) => $"{type}{(combined ? " Combined" : "")}{(index == -1 ? "" : $" {index + 1}")}";
        #endregion
    }
}