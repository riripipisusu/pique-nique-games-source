using UnityEditor;
using UnityEngine;

// Sons du Uno (Resources/UnoSfx) : musiques lues en streaming (pas decompressees en memoire), bruitages decompresses.
class UnoAudioImport : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith("Assets/Resources/UnoSfx/")) return;
        var a = (AudioImporter)assetImporter;
        string n = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        bool music = n.StartsWith("Mus_") || n.StartsWith("MU_");
        var s = a.defaultSampleSettings;
        s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = music ? 0.6f : 0.7f;
        a.defaultSampleSettings = s;
        a.loadInBackground = music;
    }
}
