using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldSim;

public static class ClimateTestSceneBuilder
{
    const int MapWidth = 96;
    const int MapHeight = 48;
    const string HeightmapName = "ClimateTestHeightmap.png";
    const string ScenePath = "Assets/Scenes/ClimateSimulationTest.unity";

    [MenuItem("Tools/Climate Simulation/Build Test Scene")]
    public static void Build()
    {
        CreateTestHeightmap();
        AssetDatabase.Refresh();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(MapWidth * 0.5f, MapHeight * 0.5f, -10f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = MapHeight * 0.58f;
        camera.backgroundColor = new Color(0.035f, 0.05f, 0.07f);
        camera.clearFlags = CameraClearFlags.SolidColor;

        var simulationObject = new GameObject("Climate Simulation");
        TickEngine engine = simulationObject.AddComponent<TickEngine>();
        WorldBootstrap bootstrap = simulationObject.AddComponent<WorldBootstrap>();
        ClimateSimulationTestHUD hud = simulationObject.AddComponent<ClimateSimulationTestHUD>();

        var mapObject = new GameObject("Temperature Map");
        mapObject.transform.position = Vector3.zero;
        SpriteRenderer spriteRenderer = mapObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sortingOrder = 0;
        GridTextureRenderer mapRenderer = mapObject.AddComponent<GridTextureRenderer>();
        SetSerialized(mapRenderer, "fieldName", "temperature");
        SetSerialized(mapRenderer, "minValue", -40f);
        SetSerialized(mapRenderer, "maxValue", 40f);

        SetSerialized(bootstrap, "width", MapWidth);
        SetSerialized(bootstrap, "height", MapHeight);
        SetSerialized(bootstrap, "renderer", (Object)null);
        SetSerialized(bootstrap, "heightRenderer", mapRenderer);
        SetSerialized(bootstrap, "heightmapFileName", HeightmapName);
        SetSerialized(bootstrap, "minHeight", 0f);
        SetSerialized(bootstrap, "maxHeight", 1200f);
        SetSerialized(hud, "tickEngine", engine);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"Created climate simulation test scene at {ScenePath}");
    }

    static void SetSerialized(Object target, string propertyName, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetSerialized(Object target, string propertyName, string value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetSerialized(Object target, string propertyName, int value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetSerialized(Object target, string propertyName, float value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void CreateTestHeightmap()
    {
        const int width = 256;
        const int height = 128;
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float u = x / (float)(width - 1);
            float v = y / (float)(height - 1);
            float firstHill = Mathf.Exp(-((u - 0.28f) * (u - 0.28f) / 0.018f + (v - 0.58f) * (v - 0.58f) / 0.08f));
            float secondHill = Mathf.Exp(-((u - 0.72f) * (u - 0.72f) / 0.035f + (v - 0.36f) * (v - 0.36f) / 0.05f));
            float ridge = Mathf.Exp(-Mathf.Pow((v - (0.48f + 0.12f * Mathf.Sin(u * 10f))) / 0.055f, 2f));
            float elevation = Mathf.Clamp01(0.08f + firstHill * 0.68f + secondHill * 0.48f + ridge * 0.25f);
            pixels[y * width + x] = new Color(elevation, elevation, elevation, 1f);
        }

        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.SetPixels(pixels);
        texture.Apply(false);
        string directory = "Assets/StreamingAssets";
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, HeightmapName), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }
}
