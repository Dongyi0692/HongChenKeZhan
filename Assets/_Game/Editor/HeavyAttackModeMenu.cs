using FengChenInn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Independent feature rollback: leaves input mapping, camera, stamina and story intact.
public static class HeavyAttackModeMenu
{
    [MenuItem("红尘客栈/战斗/启用逐次重击")]
    static void Enable() => Set(true);
    [MenuItem("红尘客栈/战斗/恢复整套四连招")]
    static void Disable() => Set(false);
    [MenuItem("红尘客栈/战斗/启用逐次重击", true)]
    [MenuItem("红尘客栈/战斗/恢复整套四连招", true)]
    static bool CanChange() => !EditorApplication.isPlaying && Object.FindFirstObjectByType<PrototypeFighter>() != null;
    static void Set(bool enabled)
    {
        if (!CanChange()) return;
        var fighter = Object.FindFirstObjectByType<PrototypeFighter>();
        Undo.RecordObject(fighter, "切换重击分段模式");
        fighter.useSequentialHeavy = enabled;
        EditorUtility.SetDirty(fighter);
        PrefabUtility.RecordPrefabInstancePropertyModifications(fighter);
        EditorSceneManager.MarkSceneDirty(fighter.gameObject.scene);
        Debug.Log(enabled ? "已启用逐次重击。请保存场景后试玩；教学同步切换。" : "已恢复一次右键播放完整四式。其他优化保留；请保存场景后试玩。", fighter);
    }
}
