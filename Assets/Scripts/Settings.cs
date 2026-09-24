using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable]
public class Settings
{
    public int quality = 2;          // 0 Bas, 1 Moyen, 2 Haut, 3 Ultra, 4 Personnalise
    public int resW, resH;
    public int display;              // 0 plein ecran fenetre, 1 fenetre, 2 plein ecran exclusif
    public bool vsync = true;
    public int fpsCap = 1;
    public int shadows = 2;
    public int aa = 1;
    public int detail = 2;           // distance de detail des modeles (LOD)
    public bool post = true;
    public float renderScale = 1f;
    public float master = 0.8f, music = 0.5f, sfx = 0.8f, ui = 0.7f;
    public bool mute;
    public float animSpeed = 1f, camSens = 1f;
    public bool autoCam = true, tileNumbers = true;
    public string cardBack = "back_red";

    public static readonly int[] Fps = { 30, 60, 120, 144, 0 };

    public static Settings Load()
    {
        var json = PlayerPrefs.GetString("settings", "");
        var s = string.IsNullOrEmpty(json) ? new Settings() : JsonUtility.FromJson<Settings>(json);
        if (s.resW == 0) { s.resW = Screen.currentResolution.width; s.resH = Screen.currentResolution.height; }
        return s;
    }

    public void Save() { PlayerPrefs.SetString("settings", JsonUtility.ToJson(this)); PlayerPrefs.Save(); }

    public void Preset(int q)
    {
        quality = q;
        if (q > 3) return;
        shadows = q;
        detail = q;
        aa = new[] { 0, 1, 1, 2 }[q];
        post = q > 0;
        renderScale = new[] { 0.7f, 0.85f, 1f, 1f }[q];
    }

    public void Apply(Camera cam, Light sun)
    {
        var urp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        urp.msaaSampleCount = new[] { 1, 2, 4, 8 }[aa];
        urp.renderScale = renderScale;
        QualitySettings.lodBias = new[] { 0.3f, 0.45f, 0.6f, 1f }[detail]; // au-dela, les arbres lointains restent en haute definition : tres couteux
        urp.shadowDistance = new[] { 0f, 45f, 70f, 110f }[shadows];
        urp.shadowCascadeCount = shadows < 3 ? 2 : 4;
        urp.mainLightShadowmapResolution = new[] { 512, 1024, 2048, 4096 }[shadows];
        sun.shadows = shadows == 0 ? LightShadows.None : LightShadows.Soft;
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = post;
        QualitySettings.vSyncCount = vsync ? 1 : 0;
        Application.targetFrameRate = vsync || Fps[fpsCap] == 0 ? -1 : Fps[fpsCap];
        var mode = new[] { FullScreenMode.FullScreenWindow, FullScreenMode.Windowed, FullScreenMode.ExclusiveFullScreen }[display];
        if (!Application.isEditor && (Screen.width != resW || Screen.height != resH || Screen.fullScreenMode != mode))
            Screen.SetResolution(resW, resH, mode);
    }
}
