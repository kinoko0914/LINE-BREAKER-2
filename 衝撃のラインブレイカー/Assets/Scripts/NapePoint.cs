using UnityEngine;

public class NapePoint : MonoBehaviour
{
    [Header("巨人本体への参照")]
    public GiantController giantController;

    private Renderer myRenderer;
    private Color originalColor;

    void Start()
    {
        myRenderer = GetComponent<Renderer>();
        if (myRenderer != null && myRenderer.material != null)
        {
            // TextMeshPro用 (_FaceColor)、標準 (_Color)、URP/HDRP (_BaseColor) のすべてに対応して元の色を保持
            if (myRenderer.material.HasProperty("_FaceColor"))
            {
                originalColor = myRenderer.material.GetColor("_FaceColor");
            }
            else if (myRenderer.material.HasProperty("_Color"))
            {
                originalColor = myRenderer.material.GetColor("_Color");
            }
            else if (myRenderer.material.HasProperty("_BaseColor"))
            {
                originalColor = myRenderer.material.GetColor("_BaseColor");
            }
            else
            {
                originalColor = Color.white;
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("NapePoint に Renderer または Material がアタッチされていません！", this);
        }
    }

    // ロックオンされたときに赤く光らせる、外れたら元に戻す
    public void SetHighlight(bool highlight)
    {
        if (myRenderer == null) myRenderer = GetComponent<Renderer>();
        if (myRenderer == null || myRenderer.material == null) return;

        Color targetColor = highlight ? Color.red : originalColor;

        // 各種シェーダーのプロパティに赤色を反映
        if (myRenderer.material.HasProperty("_FaceColor"))
        {
            myRenderer.material.SetColor("_FaceColor", targetColor);
        }
        if (myRenderer.material.HasProperty("_Color"))
        {
            myRenderer.material.SetColor("_Color", targetColor);
        }
        if (myRenderer.material.HasProperty("_BaseColor"))
        {
            myRenderer.material.SetColor("_BaseColor", targetColor);
        }

        UnityEngine.Debug.Log("うなじのハイライト切替: " + (highlight ? "赤く点灯" : "通常色に戻す"));
    }

    // 巨人討伐時の処理
    public void OnHitByGrapple()
    {
        if (giantController != null)
        {
            giantController.Die();
        }
    }
}