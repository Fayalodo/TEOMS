// Положи этот файл в папку Assets/Editor/
// Затем в Unity: Tools → Generate Cloud Noise Texture
// Создаст файл Assets/Art/Sky/CloudNoise.png — назначь его в материал Sky_Day_Night

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

public class GenerateCloudNoise : EditorWindow
{
    int   resolution  = 512;
    float scale       = 4f;    // масштаб облаков (больше = крупнее)
    int   octaves     = 6;     // детализация (больше = пушистее)
    float persistence = 0.5f;  // убывание амплитуды октав
    float lacunarity  = 2.1f;  // рост частоты октав
    int   seed        = 42;

    [MenuItem("Tools/Generate Cloud Noise Texture")]
    public static void ShowWindow()
    {
        GetWindow<GenerateCloudNoise>("Cloud Noise Generator");
    }

    void OnGUI()
    {
        GUILayout.Label("Cloud Noise Settings", EditorStyles.boldLabel);
        resolution  = EditorGUILayout.IntPopup("Resolution", resolution,
                          new[] { "128", "256", "512", "1024" },
                          new[] { 128, 256, 512, 1024 });
        scale       = EditorGUILayout.Slider("Cloud Scale",   scale,       1f,  16f);
        octaves     = EditorGUILayout.IntSlider("Octaves",    octaves,     1,   8);
        persistence = EditorGUILayout.Slider("Persistence",   persistence, 0.1f, 0.9f);
        lacunarity  = EditorGUILayout.Slider("Lacunarity",    lacunarity,  1.5f, 3f);
        seed        = EditorGUILayout.IntField("Seed",        seed);

        GUILayout.Space(10);
        if (GUILayout.Button("Generate & Save", GUILayout.Height(36)))
            Generate();

        GUILayout.Space(6);
        EditorGUILayout.HelpBox(
            "Сохраняет в Assets/Art/Sky/CloudNoise.png\n" +
            "Назначь в материал Sky_Day_Night → Cloud Noise (2D)\n" +
            "Tiling: X=3, Y=3 (можно крутить по вкусу)",
            MessageType.Info);
    }

    void Generate()
    {
        var tex = new Texture2D(resolution, resolution, TextureFormat.R8, false);
        var pixels = new Color[resolution * resolution];

        // Смещения для каждого октава (псевдослучайные на основе seed)
        var rng = new System.Random(seed);
        var offsets = new Vector2[octaves];
        for (int o = 0; o < octaves; o++)
            offsets[o] = new Vector2(rng.Next(-10000, 10000), rng.Next(-10000, 10000));

        float maxVal = float.MinValue;
        float minVal = float.MaxValue;
        float[] raw  = new float[resolution * resolution];

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float amp    = 1f;
                float freq   = 1f;
                float val    = 0f;

                for (int o = 0; o < octaves; o++)
                {
                    float sx = (x / (float)resolution * scale * freq) + offsets[o].x;
                    float sy = (y / (float)resolution * scale * freq) + offsets[o].y;
                    val += Mathf.PerlinNoise(sx, sy) * amp;
                    amp  *= persistence;
                    freq *= lacunarity;
                }

                raw[y * resolution + x] = val;
                if (val > maxVal) maxVal = val;
                if (val < minVal) minVal = val;
            }
        }

        // Нормализуем в 0-1
        float range = maxVal - minVal;
        for (int i = 0; i < raw.Length; i++)
        {
            float n = (raw[i] - minVal) / range;
            // Небольшое контрастирование для более чёткого края облаков
            n = Mathf.Pow(n, 1.2f);
            pixels[i] = new Color(n, n, n, 1f);
        }

        tex.SetPixels(pixels);
        tex.Apply();

        // Сохраняем
        string dir  = "Assets/Art/Sky";
        string path = dir + "/CloudNoise.png";
        Directory.CreateDirectory(Application.dataPath + "/Art/Sky");
        File.WriteAllBytes(Application.dataPath + "/Art/Sky/CloudNoise.png", tex.EncodeToPNG());

        AssetDatabase.Refresh();

        // Настраиваем импорт — Repeat wrap, no compression
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer != null)
        {
            importer.wrapMode        = TextureWrapMode.Repeat;
            importer.filterMode      = FilterMode.Bilinear;
            importer.textureType     = TextureImporterType.Default;
            importer.sRGBTexture     = false;   // linear — важно для noise
            importer.mipmapEnabled   = true;
            var settings             = importer.GetDefaultPlatformTextureSettings();
            settings.format          = TextureImporterFormat.R8;
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();
        }

        Debug.Log($"[CloudNoise] Сохранено: {path}");
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        EditorGUIUtility.PingObject(Selection.activeObject);

        DestroyImmediate(tex);
    }
}
#endif
