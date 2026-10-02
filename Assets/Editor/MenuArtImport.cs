using UnityEditor;

// Illustrations des menus (casting, vignettes de jeux) : taille d'origine, sans mise a l'echelle en puissance de 2
// (sinon 2560x1440 devient 2048x1024 et l'image s'affiche etiree).
public class MenuArtImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("/MMRes/Resources/UI/") && !assetPath.Contains("/Resources/LoupGarou/")) return;   // + cartes du loup-garou
        var t = (TextureImporter)assetImporter;
        t.npotScale = TextureImporterNPOTScale.None;
        t.mipmapEnabled = false;
        t.maxTextureSize = 4096;
        t.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
