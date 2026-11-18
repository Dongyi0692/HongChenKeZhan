using UnityEditor;
using UnityEngine;

public sealed class FootstepImportSettings : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if(!assetPath.StartsWith("Assets/_Game/Resources/Footsteps/")) return;
        var importer=(AudioImporter)assetImporter;
        importer.forceToMono=true;importer.loadInBackground=false;
        var settings=importer.defaultSampleSettings;
        settings.preloadAudioData=true;
        settings.loadType=AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat=AudioCompressionFormat.PCM;
        settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
        importer.defaultSampleSettings=settings;
    }
}
