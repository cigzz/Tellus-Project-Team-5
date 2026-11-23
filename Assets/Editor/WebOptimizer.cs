using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Applies the recommended Web release settings (Unity 6000.6 manual: web-optimization-player).
// Tuned for itch.io hosting: Brotli with Decompression Fallback on, because itch.io's
// Content-Encoding headers are not under our control. Turn the fallback off if the uploaded
// build's .br files arrive with "Content-Encoding: br".
public static class WebOptimizer
{
    [MenuItem("Tools/Apply Web Release Settings")]
    public static void Optimize()
    {
        var target = NamedBuildTarget.WebGL;
        PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.High);
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
        PlayerSettings.WebGL.wasm2023 = true;
        // Stored in Library/ (per machine, not in git): each teammate/CI must run this menu item.
        UnityEditor.WebGL.UserBuildSettings.codeOptimization =
            UnityEditor.WebGL.WasmCodeOptimization.DiskSizeLTO;

        // Persist to ProjectSettings.asset; without this the change lives only in memory.
        AssetDatabase.SaveAssets();

        Debug.Log(
            "Web release settings applied and saved (target WebGL):\n"
            + $"  il2cppCodeGeneration = {PlayerSettings.GetIl2CppCodeGeneration(target)}\n"
            + $"  managedStrippingLevel = {PlayerSettings.GetManagedStrippingLevel(target)}\n"
            + $"  dataCaching = {PlayerSettings.WebGL.dataCaching}\n"
            + $"  compressionFormat = {PlayerSettings.WebGL.compressionFormat}\n"
            + $"  decompressionFallback = {PlayerSettings.WebGL.decompressionFallback}\n"
            + $"  exceptionSupport = {PlayerSettings.WebGL.exceptionSupport}\n"
            + $"  debugSymbolMode = {PlayerSettings.WebGL.debugSymbolMode}\n"
            + $"  wasm2023 = {PlayerSettings.WebGL.wasm2023}\n"
            + $"  codeOptimization = {UnityEditor.WebGL.UserBuildSettings.codeOptimization}");
    }
}
