using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Mavis.EditorTools
{
    /// <summary>
    /// WebGL 命令行构建：Unity.exe -batchmode -quit -executeMethod Mavis.EditorTools.WebGLBuilder.BuildFromCommandLine
    /// 压缩禁用（任意静态服务器可直接托管，无需 Content-Encoding 配置）。
    /// </summary>
    public static class WebGLBuilder
    {
        [MenuItem("Mavis/Build/Build WebGL → Build/WebGL")]
        public static void BuildFromMenu() => Build(exitEditor: false);

        public static void BuildFromCommandLine() => Build(exitEditor: true);

        private static void Build(bool exitEditor)
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            PlayerSettings.runInBackground = true;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Main.unity" },
                locationPathName = "Build/WebGL",
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[WebGLBuilder] 构建失败: {report.summary.result} / {report.summary.totalErrors} errors");
                if (exitEditor) EditorApplication.Exit(1);
                return;
            }
            Debug.Log($"[WebGLBuilder] 构建成功: {report.summary.outputPath}, " +
                      $"{report.summary.totalSize / 1048576}MB, {report.summary.totalTime.TotalMinutes:F1}min");
            if (exitEditor) EditorApplication.Exit(0);
        }
    }
}
