#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class BatchBuild
{
    public static void Build()
    {
        var option = GetBuildPlayerOptionFromCommandLineArgs();
        option.scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
        var buildReport = BuildPipeline.BuildPlayer(option);

        Debug.Log($"Build Summary: {buildReport.summary}");
        if (buildReport.summary.result == BuildResult.Succeeded)
        {
            EditorApplication.Exit(0);
        }
        else
        {
            EditorApplication.Exit(1);
        }
    }

    private static BuildPlayerOptions GetBuildPlayerOptionFromCommandLineArgs()
    {
        var option = new BuildPlayerOptions();
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-platform":
                    {
                        if (Enum.TryParse<BuildTarget>(args[i + 1], true, out var p))
                        {
                            option.target = p;
                        }
                        else
                        {
                            throw new ArgumentOutOfRangeException($"-platform {args[i + 1]} should match to {string.Join(",", Enum.GetNames(typeof(BuildTarget)))}");
                        }
                    }
                    break;
                case "-development":
                    option.options = BuildOptions.Development;
                    break;
                case "-locationpath":
                    option.locationPathName = args[i + 1];
                    break;
                default:
                    break;
            }
        }

        // fallback locationPath when argument missing
        if (string.IsNullOrWhiteSpace(option.locationPathName))
        {
            var projectName = PlayerSettings.productName;
            var extension = option.target == BuildTarget.Android
                ? ".apk"
                : option.target == BuildTarget.StandaloneWindows64 || option.target == BuildTarget.StandaloneWindows
                    ? ".exe"
                    : "";
            option.locationPathName = $"./build/{option.target.ToString().ToLower()}/{projectName}{extension}";
        }

        return option;
    }
}
#endif