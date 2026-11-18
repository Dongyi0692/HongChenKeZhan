using UnityEditor;
using UnityEngine;

public sealed class UiVictoryImportSettings : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith("Assets/_Game/Resources/UiVictory/")) return;
        var importer = (AudioImporter)assetImporter;
        importer.loadInBackground = false;
        var settings = importer.defaultSampleSettings;
        settings.preloadAudioData = true;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        importer.defaultSampleSettings = settings;
    }
}
