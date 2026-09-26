using UnityEditor;

// Images d'interface (Resources/UI) : taille d'origine (pas d'arrondi en puissance de 2 qui deforme), sans mipmaps.
class UiTextureImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/UI/")) return;
        var t = (TextureImporter)assetImporter;
        t.npotScale = TextureImporterNPOTScale.None;
        t.mipmapEnabled = false;
        t.alphaIsTransparency = true;
        t.textureCompression = TextureImporterCompression.Uncompressed;
    }
}
