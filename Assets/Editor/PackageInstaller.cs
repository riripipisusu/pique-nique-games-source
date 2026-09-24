using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

public static class PackageInstaller
{
    static readonly string[] PackagesToAdd = { "com.unity.render-pipelines.universal", "com.unity.cloud.gltfast" };

    static AddAndRemoveRequest _request;
    static double _deadline;

    // -executeMethod PackageInstaller.Install  (sans -quit : UPM est asynchrone)
    public static void Install()
    {
        _request = Client.AddAndRemove(PackagesToAdd, new string[0]);
        _deadline = EditorApplication.timeSinceStartup + 600;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!_request.IsCompleted)
        {
            if (EditorApplication.timeSinceStartup > _deadline) { Debug.LogError("[PackageInstaller] Timeout"); EditorApplication.Exit(2); }
            return;
        }
        EditorApplication.update -= Poll;
        if (_request.Status == StatusCode.Success)
        {
            Debug.Log("[PackageInstaller] Resolved: " + string.Join(", ", _request.Result.Select(p => p.name + "@" + p.version)));
            EditorApplication.Exit(0);
        }
        else { Debug.LogError("[PackageInstaller] Failed: " + _request.Error?.message); EditorApplication.Exit(1); }
    }
}
