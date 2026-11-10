using UnityEngine;
using UnityEngine.SceneManagement;

public class ClearManager : MonoBehaviour
{
    void Start()
    {
        // ★ クリア画面が開いた瞬間にマウスポインターを表示＆ロック解除する
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // タイトルシーンへ戻る関数
    public void LoadTitle()
    {
        SceneManager.LoadScene("TitleScene");
    }

    // もう一度ゲームシーンへ移行する関数
    public void RetryGame()
    {
        SceneManager.LoadScene("GameScene");
    }
}