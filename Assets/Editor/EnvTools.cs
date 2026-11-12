#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Menu items for the Python bridge (see Assets/Scripts/EnvServer.cs).
//   Surf > Env Server > Start (Play mode)   start the server on the rider while the game is playing
//   Surf > Env Server > Stop                stop it (keyboard play works again)
//   Surf > Env Server > Build Windows Player   standalone SnowboardEnv.exe in Builds/SnowboardEnv/ (git-ignored)
public static class EnvTools
{
    [MenuItem("Surf/Env Server/Start (Play mode)")]
    static void StartServer()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("Press Play first, then start the env server."); return; }
        var agent = Object.FindFirstObjectByType<SnowboarderAgent>();
        if (agent == null) { Debug.LogWarning("No SnowboarderAgent in the scene."); return; }
        var s = agent.GetComponent<EnvServer>();
        if (s == null) s = agent.gameObject.AddComponent<EnvServer>();
        s.enabled = true;
    }

    [MenuItem("Surf/Env Server/Stop")]
    static void StopServer()
    {
        var s = Object.FindFirstObjectByType<EnvServer>(FindObjectsInactive.Include);
        if (s != null) s.enabled = false;
    }

    [MenuItem("Surf/Env Server/Build Windows Player")]
    public static void BuildWindows()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play mode before building."); return; }
        var scenes = new System.Collections.Generic.List<string>();
        foreach (var s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
        if (scenes.Count == 0) scenes.Add("Assets/Scenes/SampleScene.unity");

        string dir = "Builds/SnowboardEnv";
        Directory.CreateDirectory(dir);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = dir + "/SnowboardEnv.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        Debug.Log("[EnvTools] build " + report.summary.result + " -> " + Path.GetFullPath(dir) + " (" + (report.summary.totalSize / (1024 * 1024)) + " MB, "
                  + report.summary.totalTime.TotalSeconds.ToString("F0") + " s, " + report.summary.totalErrors + " errors)");
    }
}
#endif
