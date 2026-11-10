using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody))]
public class GiantController : MonoBehaviour
{
    [Header("基本設定")]
    public Transform player;
    public float rotationSpeed = 1.5f; // ゆっくり振り向くことでデカさを演出
    public float customGravity = -30f; // 強めの重力を加えて浮遊感を消す

    private Rigidbody rb;
    private bool isDead = false;

    // --- シーン内の巨人総数を管理する静的変数 ---
    public static int totalGiantCount = 0;

    void OnEnable()
    {
        totalGiantCount++;
    }

    void OnDisable()
    {
        if (!isDead)
        {
            totalGiantCount = Mathf.Max(0, totalGiantCount - 1);
        }
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        // Rigidbody の質量を自動で重く設定
        if (rb != null)
        {
            rb.mass = 2000f;
            rb.freezeRotation = true; // 回転を固定して転倒を防ぐ
        }

        if (player == null && GameObject.FindWithTag("Player") != null)
        {
            player = GameObject.FindWithTag("Player").transform;
        }
    }

    void FixedUpdate()
    {
        if (isDead) return;

        // 独自の重力を加えてドッシリ地面に接地させる
        if (rb != null && !rb.isKinematic)
        {
            rb.AddForce(Vector3.up * customGravity, ForceMode.Acceleration);
        }

        // プレイヤーの方向へゆっくりと回転（重量感のある振り向き）
        if (player != null)
        {
            Vector3 direction = (player.position - transform.position);
            direction.y = 0;
            if (direction.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
            }
        }
    }

    // 巨人討伐時の処理
    public void Die()
    {
        if (isDead) return;
        isDead = true;

        UnityEngine.Debug.Log("巨人を討伐しました！");

        // 生存数を減らす
        totalGiantCount = Mathf.Max(0, totalGiantCount - 1);
        UnityEngine.Debug.Log("残り巨人数: " + totalGiantCount);

        // ★すべての巨人が倒されたら、1.5秒後に ClearScene へ遷移
        if (totalGiantCount <= 0)
        {
            UnityEngine.Debug.Log("すべての巨人を討伐しました！ClearSceneへ移行します。");
            Invoke("LoadClearScene", 1.5f);
        }

        // 倒れた時は物理挙動を解禁
        if (rb != null)
        {
            rb.freezeRotation = false;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (var rend in renderers)
        {
            rend.material.color = Color.red;
        }

        Destroy(gameObject, 2.0f);
    }

    void LoadClearScene()
    {
        SceneManager.LoadScene("ClearScene");
    }
}