using System.IO;
using UnityEngine;

public class SeamlessNoiseGenerator : MonoBehaviour
{
    [Header("Noise Settings")]
    [Tooltip("基础噪声缩放。数值越大，纹理中的噪声图案越密。")]
    [Range(1f, 50f)]
    public float noiseScale = 10f;

    [Tooltip("随机种子。修改后会重新生成一张不同分布的噪声贴图。")]
    public int seed = 42;

    [Tooltip("RGB 三个通道各自使用的随机偏移种子。")]
    public Vector3Int channelOffsets = new Vector3Int(0, 100, 200);
    [Tooltip("RGB 三个通道各自的缩放倍率，可用于做出不同频率的混合噪声。")]
    public Vector3 channelScales = new Vector3(1.0f, 1.2f, 0.8f);

    [Tooltip("输出噪声贴图的分辨率，会自动修正到 2 的幂。")]
    [Range(32, 1024)]
    public int textureSize = 512;

    [Header("Generated Texture")]
    [Tooltip("当前生成的噪声贴图。可直接被草地风场系统读取。")]
    public Texture2D generatedTexture;

    private void OnEnable()
    {
        if (generatedTexture == null)
        {
            GenerateNoiseTexture();
        }
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled)
            return;

        GenerateNoiseTexture();
    }

    [ContextMenu("Generate Noise Texture")]
    public void GenerateNoiseTexture()
    {
        textureSize = Mathf.Clamp(Mathf.ClosestPowerOfTwo(textureSize), 32, 1024);

        if (generatedTexture == null ||
            generatedTexture.width != textureSize ||
            generatedTexture.height != textureSize ||
            generatedTexture.format != TextureFormat.RGB24)
        {
            generatedTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGB24, false);
            generatedTexture.name = "GeneratedNoiseTexture";
        }

        generatedTexture.wrapMode = TextureWrapMode.Repeat;
        generatedTexture.filterMode = FilterMode.Bilinear;

        Random.State previousState = Random.state;
        Random.InitState(seed);

        Vector2[] channelOffsetVectors = new Vector2[3];
        for (int channel = 0; channel < 3; channel++)
        {
            int channelSeed = seed + GetChannelOffset(channel);
            Random.InitState(channelSeed);

            channelOffsetVectors[channel] = new Vector2(
                Random.Range(-10000f, 10000f),
                Random.Range(-10000f, 10000f));
        }

        Color[] colorMap = new Color[textureSize * textureSize];

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                Vector3 channelNoise = Vector3.zero;
                Vector2 uv = new Vector2(
                    textureSize > 1 ? x / (float)(textureSize - 1) : 0.0f,
                    textureSize > 1 ? y / (float)(textureSize - 1) : 0.0f);

                for (int channel = 0; channel < 3; channel++)
                {
                    float scale = noiseScale * GetChannelScale(channel);
                    float noiseValue = SampleSeamlessPerlinNoise(uv, channelOffsetVectors[channel], scale);

                    switch (channel)
                    {
                        case 0:
                            channelNoise.x = noiseValue;
                            break;
                        case 1:
                            channelNoise.y = noiseValue;
                            break;
                        case 2:
                            channelNoise.z = noiseValue;
                            break;
                    }
                }

                colorMap[y * textureSize + x] = new Color(
                    channelNoise.x,
                    channelNoise.y,
                    channelNoise.z,
                    1f);
            }
        }

        generatedTexture.SetPixels(colorMap);
        generatedTexture.Apply();
        Random.state = previousState;
    }

    [ContextMenu("Randomize Seed")]
    public void RandomizeSeed()
    {
        seed = Random.Range(0, 10000);
        GenerateNoiseTexture();
    }

    public void ExportTextureToPNG(string path)
    {
        if (generatedTexture == null)
        {
            Debug.LogError("No texture available to export.");
            return;
        }

        byte[] bytes = generatedTexture.EncodeToPNG();
        File.WriteAllBytes(path, bytes);
    }

    private int GetChannelOffset(int channel)
    {
        switch (channel)
        {
            case 0:
                return channelOffsets.x;
            case 1:
                return channelOffsets.y;
            case 2:
                return channelOffsets.z;
            default:
                return 0;
        }
    }

    private float GetChannelScale(int channel)
    {
        switch (channel)
        {
            case 0:
                return channelScales.x;
            case 1:
                return channelScales.y;
            case 2:
                return channelScales.z;
            default:
                return 1f;
        }
    }

    private static float SampleSeamlessPerlinNoise(Vector2 uv, Vector2 offset, float scale)
    {
        int baseFrequency = Mathf.Max(1, Mathf.RoundToInt(scale));
        float amplitude = 0.5f;
        float amplitudeSum = 0.0f;
        float noiseSum = 0.0f;
        int frequency = baseFrequency;

        // Build the wind map from explicitly periodic octaves so every edge closes cleanly.
        for (int octave = 0; octave < 4; octave++)
        {
            noiseSum += SampleTileableValueNoise(uv, offset, frequency) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= 0.5f;
            frequency *= 2;
        }

        if (amplitudeSum <= 0.0f)
            return 0.0f;

        return noiseSum / amplitudeSum;
    }

    private static float SampleTileableValueNoise(Vector2 uv, Vector2 offset, int frequency)
    {
        float sampleX = uv.x * frequency + offset.x;
        float sampleY = uv.y * frequency + offset.y;

        int x0 = Mathf.FloorToInt(sampleX);
        int y0 = Mathf.FloorToInt(sampleY);
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        float tx = sampleX - x0;
        float ty = sampleY - y0;

        float wx = SmoothQuintic(tx);
        float wy = SmoothQuintic(ty);

        float value00 = HashToUnitFloat(PositiveModulo(x0, frequency), PositiveModulo(y0, frequency));
        float value10 = HashToUnitFloat(PositiveModulo(x1, frequency), PositiveModulo(y0, frequency));
        float value01 = HashToUnitFloat(PositiveModulo(x0, frequency), PositiveModulo(y1, frequency));
        float value11 = HashToUnitFloat(PositiveModulo(x1, frequency), PositiveModulo(y1, frequency));

        float blendX0 = Mathf.Lerp(value00, value10, wx);
        float blendX1 = Mathf.Lerp(value01, value11, wx);
        return Mathf.Lerp(blendX0, blendX1, wy);
    }

    private static int PositiveModulo(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static float SmoothQuintic(float value)
    {
        return value * value * value * (value * (value * 6.0f - 15.0f) + 10.0f);
    }

    private static float HashToUnitFloat(int x, int y)
    {
        unchecked
        {
            uint hash = (uint)x;
            hash ^= 374761393u;
            hash += (uint)y * 668265263u;
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            hash ^= hash >> 16;
            return hash / 4294967295.0f;
        }
    }
}
