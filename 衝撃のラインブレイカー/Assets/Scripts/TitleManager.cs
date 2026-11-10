using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    // GameSceneへ移行する関数
    public void StartGame()
    {
        SceneManager.LoadScene("GameScene");
    }

    // ゲームを終了する関数
    public void QuitGame()
    {
        UnityEngine.Debug.Log("ゲームを終了します。");

        // ★ どちらのApplicationか明確にするため「UnityEngine.」を追加しました
        UnityEngine.Application.Quit();

        // Unityエディター上でテストプレイしている際にも終了動作を確認できるようにするため
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}