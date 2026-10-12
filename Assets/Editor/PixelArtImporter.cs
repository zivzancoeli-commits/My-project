using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// Editor helper: any image put in Assets/Blocks or Assets/LevelItems is set up
// as crisp pixel art, and a matching Tile is made for it automatically.
// Tiles made from Assets/LevelItems have no hitbox, so you can walk through them.
public class PixelArtImporter : AssetPostprocessor
{
    static bool InFolder(string path)
    {
        return path.StartsWith("Assets/Blocks/") || path.StartsWith("Assets/LevelItems/");
    }

    void OnPreprocessTexture()
    {
        if (!InFolder(assetPath)) return;

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 16;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (string path in imported)
        {
            if (!InFolder(path) || !path.EndsWith(".png")) continue;

            string tilePath = path.Replace(".png", "_tile.asset");
            if (AssetDatabase.LoadAssetAtPath<Tile>(tilePath) != null) continue;

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) continue;

            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            if (path.StartsWith("Assets/LevelItems/")) tile.colliderType = Tile.ColliderType.None;
            AssetDatabase.CreateAsset(tile, tilePath);
        }
    }
}
