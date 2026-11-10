using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    private CharacterController controller;
    private LineRenderer leftLineRenderer;
    private LineRenderer rightLineRenderer;
    private Vector3 velocity; // 慣性・重力・落下用の速度

    [Header("移動・ダッシュ設定")]
    public float moveSpeed = 6f;
    public float dashMultiplier = 2f;
    public float jumpHeight = 2f;
    public float gravity = -20f;

    [Header("カメラ・視点設定 (キャラ中心の周回カメラ)")]
    public Transform playerCamera;
    public float mouseSensitivity = 2.0f;
    public float cameraDistance = 4.0f;
    public float cameraHeight = 1.5f;
    private float cameraYaw = 0f;
    private float cameraPitch = 20f;

    [Header("立体機動・ワイヤーアクション設定")]
    public float searchRadius = 50f;          // 通常ワイヤーのサーチ範囲
    public float napeSearchRadius = 15f;    // 独立したうなじロックオン範囲
    public float grappleDelay = 0.5f;       // ワイヤーが伸びるまでのタメ時間（0.5秒）
    public float grappleBoostSpeed = 45f;   // 通常ワイヤーの猛烈な初速
    public float swingSpeed = 15f;          // うなじスイング中のスピード
    public float chargeSpeed = 45f;         // うなじへの突撃スピード
    public float attackRange = 1.5f;        // うなじを切り裂いて倒せる攻撃範囲
    public float maxGrappleDuration = 3.0f; // ワイヤーを刺していられる最大限界時間（秒）

    // --- 状態管理 ---
    private enum ActionState { Normal, WaitingNapeGrapple, Swinging, Charging, WaitingNormalGrapple, NormalGrappling }
    private ActionState currentState = ActionState.Normal;

    private Transform lockedNapeTarget = null; // ロックオンしている巨人の「うなじ」
    private Vector3 currentGrappleTargetPos;
    private float grappleDelayTimer = 0f;
    private float grappleActiveTimer = 0f;     // ワイヤーが刺さってからの経過時間タイマー
    private Vector3 grappleDirection;          // 刺さった瞬間の視点方向ロック

    // 通常ワイヤー用および接続中管理の変数
    private Transform currentGrappleTarget = null; // 現在ワイヤーが刺さっているターゲット本体
    private bool currentTargetIsNape = false;      // 現在刺さっているターゲットがうなじかどうか
    private Vector3 normalGrappleTarget;
    private Vector3 normalGrappleVelocity;
    private float normalDelayTimer = 0f;

    private bool isGrounded;

    void Start()
    {
        controller = GetComponent<CharacterController>();

        LineRenderer[] lines = GetComponents<LineRenderer>();
        if (lines.Length == 0)
        {
            leftLineRenderer = gameObject.AddComponent<LineRenderer>();
            rightLineRenderer = gameObject.AddComponent<LineRenderer>();
        }
        else if (lines.Length == 1)
        {
            leftLineRenderer = lines[0];
            rightLineRenderer = gameObject.AddComponent<LineRenderer>();
        }
        else
        {
            leftLineRenderer = lines[0];
            rightLineRenderer = lines[1];
        }

        SetupLineRenderer(leftLineRenderer);
        SetupLineRenderer(rightLineRenderer);

        CheckCamera();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void CheckCamera()
    {
        if (playerCamera == null && Camera.main != null)
        {
            playerCamera = Camera.main.transform;
        }
    }

    void SetupLineRenderer(LineRenderer lr)
    {
        if (lr == null) return;
        lr.positionCount = 0;
        lr.startWidth = 0.06f;
        lr.endWidth = 0.06f;
    }

    void LateUpdate()
    {
        CheckCamera();
        if (playerCamera == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        cameraYaw += mouseX;
        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, -10f, 75f);

        Quaternion rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0);
        Vector3 targetPosition = transform.position + Vector3.up * cameraHeight - (rotation * Vector3.forward * cameraDistance);

        playerCamera.position = targetPosition;
        playerCamera.LookAt(transform.position + Vector3.up * cameraHeight);
    }

    void Update()
    {
        CheckCamera();
        if (playerCamera == null) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        isGrounded = controller.isGrounded;

        // --- 【1. ターゲットロックオン (Qキー または マウス中クリック)】 ---
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetMouseButtonDown(2))
        {
            if (currentState == ActionState.NormalGrappling && currentTargetIsNape && currentGrappleTarget != null)
            {
                lockedNapeTarget = currentGrappleTarget;
                NapePoint nape = lockedNapeTarget.GetComponent<NapePoint>();
                if (nape != null) nape.SetHighlight(true);

                currentState = ActionState.Swinging;
                grappleActiveTimer = 0f;
                UnityEngine.Debug.Log("うなじにワイヤー接続中：そのままスイング（攻撃態勢）に移行！");
            }
            else
            {
                FindAndLockNapeTarget();
            }
        }

        // --- 【ステート別の処理】 ---
        switch (currentState)
        {
            case ActionState.Normal:
                HandleNormalMovement();

                if (Input.GetMouseButtonDown(1))
                {
                    if (lockedNapeTarget != null)
                    {
                        currentState = ActionState.WaitingNapeGrapple;
                        grappleDelayTimer = 0f;
                        currentGrappleTargetPos = lockedNapeTarget.position;
                    }
                    else
                    {
                        TryStartNormalGrapple();
                    }
                }
                break;

            case ActionState.WaitingNapeGrapple:
                HandleNormalMovement();
                HandleNapeDelayAndDraw();
                break;

            case ActionState.Swinging:
                HandleSwinging();
                break;

            case ActionState.Charging:
                HandleCharging();
                break;

            case ActionState.WaitingNormalGrapple:
                HandleNormalMovement();
                HandleNormalDelayAndDraw();
                break;

            case ActionState.NormalGrappling:
                HandleNormalGrappling();
                break;
        }
    }

    bool HasTagInParents(Transform objTransform, string tagName)
    {
        Transform current = objTransform;
        while (current != null)
        {
            if (current.CompareTag(tagName))
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    // --- うなじを探してロックオンし、赤く光らせる ---
    void FindAndLockNapeTarget()
    {
        if (lockedNapeTarget != null)
        {
            NapePoint oldNape = lockedNapeTarget.GetComponent<NapePoint>();
            if (oldNape != null) oldNape.SetHighlight(false);
        }

        Collider[] hitColliders = Physics.OverlapSphere(transform.position, napeSearchRadius);
        Transform bestTarget = null;
        float maxScore = -999f;

        Vector3 searchCenterDir = playerCamera != null ? playerCamera.forward : transform.forward;

        foreach (var col in hitColliders)
        {
            if (!col.CompareTag("NapeTarget") && col.GetComponent<NapePoint>() == null) continue;

            Vector3 targetPoint = col.ClosestPoint(playerCamera.position);
            Vector3 dirToTarget = (targetPoint - transform.position).normalized;
            float viewDot = Vector3.Dot(searchCenterDir, dirToTarget);

            if (viewDot >= 0.3f)
            {
                float distance = Vector3.Distance(transform.position, targetPoint);
                float score = viewDot * 30f - distance;

                if (score > maxScore)
                {
                    maxScore = score;
                    bestTarget = col.transform;
                }
            }
        }

        lockedNapeTarget = bestTarget;
        if (lockedNapeTarget != null)
        {
            NapePoint newNape = lockedNapeTarget.GetComponent<NapePoint>();
            if (newNape != null) newNape.SetHighlight(true);
            UnityEngine.Debug.Log("巨人のうなじをロックオンしました！（赤色ハイライト）");
        }
        else
        {
            UnityEngine.Debug.Log("ロックオンできるうなじが見つかりませんでした。");
        }
    }

    // --- ★改良: カメラの視線からスフィアキャストを飛ばし、木の上部に設定したコライダーの「当たった正確な位置（hit.point）」に刺す ---
    void TryStartNormalGrapple()
    {
        Transform bestTarget = null;
        Vector3 bestPoint = Vector3.zero;
        float minDistance = float.MaxValue;
        bool found = false;

        // カメラの向きから太めのレイ（SphereCast）を飛ばして、プレイヤーが見ている場所にあるGrappleTargetを正確に捉える
        Ray ray = new Ray(playerCamera.position, playerCamera.forward);
        RaycastHit[] hits = Physics.SphereCastAll(ray, 1.0f, searchRadius);

        foreach (var hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            bool isGrappleTarget = hit.collider.CompareTag("GrappleTarget") || HasTagInParents(hit.transform, "GrappleTarget");
            if (!isGrappleTarget) continue;

            if (hit.distance < minDistance)
            {
                minDistance = hit.distance;
                bestTarget = hit.transform;
                bestPoint = hit.point; // ★コライダーの下側ではなく、実際に光線が当たった正確な位置（上部）を採用！
                found = true;
            }
        }

        // 万が一直接当たらなかった場合のフォールバック（周囲のGrappleTargetを探索）
        if (!found)
        {
            Collider[] hitColliders = Physics.OverlapSphere(transform.position, searchRadius);
            float maxScore = -999f;
            Vector3 searchCenterDir = playerCamera != null ? playerCamera.forward : transform.forward;

            foreach (var col in hitColliders)
            {
                if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

                bool isGrappleTarget = col.CompareTag("GrappleTarget") || HasTagInParents(col.transform, "GrappleTarget");
                if (!isGrappleTarget) continue;

                // コライダーの上部や中心を優先するため、ClosestPointではなくBoundsの中心や上側を考慮
                Vector3 targetPoint = col.bounds.center;
                Vector3 dirToTarget = (targetPoint - transform.position).normalized;
                float viewDot = Vector3.Dot(searchCenterDir, dirToTarget);

                if (viewDot >= 0.0f)
                {
                    float distance = Vector3.Distance(transform.position, targetPoint);
                    float score = viewDot * 20f - distance;

                    if (score > maxScore)
                    {
                        maxScore = score;
                        bestTarget = col.transform;
                        bestPoint = col.bounds.center; // コライダーの中心（上部）に刺す
                    }
                }
            }
        }

        if (bestTarget != null)
        {
            currentGrappleTarget = bestTarget;
            currentTargetIsNape = bestTarget.CompareTag("NapeTarget") || bestTarget.GetComponent<NapePoint>() != null;

            normalGrappleTarget = bestPoint;
            currentState = ActionState.WaitingNormalGrapple;
            normalDelayTimer = 0f;
        }
    }

    // --- 通常ワイヤーが0.5秒かけて伸びていく処理（タメ演出） ---
    void HandleNormalDelayAndDraw()
    {
        if (currentGrappleTarget == null)
        {
            CancelGrapple();
            return;
        }

        // ターゲットが動くものであれば追従、固定オブジェクトならその位置
        normalGrappleTarget = currentGrappleTarget.position;

        normalDelayTimer += Time.deltaTime;
        float progress = Mathf.Clamp01(normalDelayTimer / grappleDelay);

        Vector3 leftHip = transform.position + Vector3.up * 1.0f - transform.right * 0.4f;
        Vector3 rightHip = transform.position + Vector3.up * 1.0f + transform.right * 0.4f;
        Vector3 currentLeftPoint = Vector3.Lerp(leftHip, normalGrappleTarget, progress);
        Vector3 currentRightPoint = Vector3.Lerp(rightHip, normalGrappleTarget, progress);

        UpdateLineRenderers(leftHip, currentLeftPoint, rightHip, currentRightPoint);

        if (Input.GetMouseButtonUp(1))
        {
            CancelGrapple();
            return;
        }

        // 0.5秒経過してワイヤーが刺さった瞬間
        if (normalDelayTimer >= grappleDelay)
        {
            currentState = ActionState.NormalGrappling;
            grappleActiveTimer = 0f;

            Vector3 anchorPoint = normalGrappleTarget;
            float heightDiff = anchorPoint.y - transform.position.y;

            Vector3 camHeading = playerCamera != null ? playerCamera.forward : transform.forward;
            camHeading.y = 0;
            camHeading.Normalize();
            grappleDirection = camHeading;

            Vector3 boostDir = grappleDirection;
            if (heightDiff > 0)
            {
                boostDir.y = Mathf.Clamp(heightDiff * 0.05f, 0.1f, 0.3f);
            }
            else
            {
                boostDir.y = Mathf.Clamp(heightDiff * 0.05f, -0.2f, 0.05f);
            }
            boostDir.Normalize();

            normalGrappleVelocity = boostDir * grappleBoostSpeed;
        }
    }

    // --- 通常ワイヤー中の挙動 ---
    void HandleNormalGrappling()
    {
        if (currentGrappleTarget == null)
        {
            CancelGrapple();
            return;
        }

        normalGrappleTarget = currentGrappleTarget.position;
        grappleActiveTimer += Time.deltaTime;

        bool shouldEnd = Input.GetMouseButtonUp(1) || (grappleActiveTimer >= maxGrappleDuration) || (isGrounded && grappleActiveTimer > 0.1f);

        if (shouldEnd)
        {
            CancelGrapple();
            velocity = normalGrappleVelocity;
        }
        else
        {
            Vector3 leftHip = transform.position + Vector3.up * 1.0f - transform.right * 0.4f;
            Vector3 rightHip = transform.position + Vector3.up * 1.0f + transform.right * 0.4f;
            UpdateLineRenderers(leftHip, normalGrappleTarget, rightHip, normalGrappleTarget);

            normalGrappleVelocity.y += (gravity * 0.15f) * Time.deltaTime;
            controller.Move(normalGrappleVelocity * Time.deltaTime);

            if (grappleDirection.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(grappleDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 0.2f);
            }
        }
    }

    // --- うなじ用：ワイヤーが0.5秒かけて伸びていく処理 ---
    void HandleNapeDelayAndDraw()
    {
        if (lockedNapeTarget == null)
        {
            CancelGrapple();
            return;
        }

        currentGrappleTargetPos = lockedNapeTarget.position;
        grappleDelayTimer += Time.deltaTime;
        float progress = Mathf.Clamp01(grappleDelayTimer / grappleDelay);

        Vector3 leftHip = transform.position + Vector3.up * 1.0f - transform.right * 0.4f;
        Vector3 rightHip = transform.position + Vector3.up * 1.0f + transform.right * 0.4f;
        Vector3 currentLeftPoint = Vector3.Lerp(leftHip, currentGrappleTargetPos, progress);
        Vector3 currentRightPoint = Vector3.Lerp(rightHip, currentGrappleTargetPos, progress);

        UpdateLineRenderers(leftHip, currentLeftPoint, rightHip, currentRightPoint);

        if (Input.GetMouseButtonUp(1))
        {
            CancelGrapple();
            return;
        }

        if (grappleDelayTimer >= grappleDelay)
        {
            currentState = ActionState.Swinging;
            grappleActiveTimer = 0f;

            Vector3 camHeading = playerCamera != null ? playerCamera.forward : transform.forward;
            camHeading.y = 0;
            camHeading.Normalize();
            grappleDirection = camHeading;
        }
    }

    // --- 周回スイング中の処理 ---
    void HandleSwinging()
    {
        Transform targetToCheck = lockedNapeTarget != null ? lockedNapeTarget : currentGrappleTarget;

        grappleActiveTimer += Time.deltaTime;

        if (targetToCheck == null || Input.GetMouseButtonUp(1) || grappleActiveTimer >= maxGrappleDuration)
        {
            CancelGrapple();
            return;
        }

        currentGrappleTargetPos = targetToCheck.position;

        Vector3 leftHip = transform.position + Vector3.up * 1.0f - transform.right * 0.4f;
        Vector3 rightHip = transform.position + Vector3.up * 1.0f + transform.right * 0.4f;
        UpdateLineRenderers(leftHip, currentGrappleTargetPos, rightHip, currentGrappleTargetPos);

        Vector3 toAnchor = currentGrappleTargetPos - transform.position;

        Vector3 swingForce = Quaternion.Euler(0, 90, 0) * toAnchor.normalized * swingSpeed;
        velocity = swingForce + (grappleDirection * moveSpeed);
        velocity.y += gravity * 0.2f * Time.deltaTime;

        controller.Move(velocity * Time.deltaTime);

        if (grappleDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(grappleDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 0.2f);
        }

        if (Input.GetMouseButtonDown(0))
        {
            currentState = ActionState.Charging;
        }
    }

    // --- うなじへの突撃処理 ---
    void HandleCharging()
    {
        Transform targetToCheck = lockedNapeTarget != null ? lockedNapeTarget : currentGrappleTarget;

        if (targetToCheck == null)
        {
            CancelGrapple();
            return;
        }

        currentGrappleTargetPos = targetToCheck.position;

        Vector3 leftHip = transform.position + Vector3.up * 1.0f - transform.right * 0.4f;
        Vector3 rightHip = transform.position + Vector3.up * 1.0f + transform.right * 0.4f;
        UpdateLineRenderers(leftHip, currentGrappleTargetPos, rightHip, currentGrappleTargetPos);

        Vector3 dirToTarget = (currentGrappleTargetPos - transform.position).normalized;
        velocity = dirToTarget * chargeSpeed;
        controller.Move(velocity * Time.deltaTime);

        float distanceToTarget = Vector3.Distance(transform.position, currentGrappleTargetPos);

        if (Input.GetMouseButtonDown(0))
        {
            if (distanceToTarget <= attackRange)
            {
                ExecuteAttack();
            }
            else
            {
                UnityEngine.Debug.Log("まだ遠すぎてうなじに届かない！（攻撃範囲外）");
            }
        }
    }

    // --- 攻撃（討伐判定）実行 ---
    void ExecuteAttack()
    {
        UnityEngine.Debug.Log("うなじを切り裂いた！ 巨人討伐！");

        Transform targetToAttack = lockedNapeTarget != null ? lockedNapeTarget : currentGrappleTarget;
        if (targetToAttack != null)
        {
            NapePoint nape = targetToAttack.GetComponent<NapePoint>();
            if (nape != null)
            {
                nape.OnHitByGrapple();
            }
        }

        CancelGrapple();
    }

    void CancelGrapple()
    {
        if (lockedNapeTarget != null)
        {
            NapePoint nape = lockedNapeTarget.GetComponent<NapePoint>();
            if (nape != null) nape.SetHighlight(false);
        }

        currentState = ActionState.Normal;
        lockedNapeTarget = null;
        currentGrappleTarget = null;
        currentTargetIsNape = false;
        if (leftLineRenderer != null) leftLineRenderer.positionCount = 0;
        if (rightLineRenderer != null) rightLineRenderer.positionCount = 0;
    }

    void UpdateLineRenderers(Vector3 lStart, Vector3 lEnd, Vector3 rStart, Vector3 rEnd)
    {
        if (leftLineRenderer != null)
        {
            leftLineRenderer.positionCount = 2;
            leftLineRenderer.SetPosition(0, lStart);
            leftLineRenderer.SetPosition(1, lEnd);
        }
        if (rightLineRenderer != null)
        {
            rightLineRenderer.positionCount = 2;
            rightLineRenderer.SetPosition(0, rStart);
            rightLineRenderer.SetPosition(1, rEnd);
        }
    }

    // --- 通常時の移動・落下処理 ---
    void HandleNormalMovement()
    {
        if (isGrounded)
        {
            velocity.y = -2f;

            Vector3 horizontalInertia = new Vector3(velocity.x, 0, velocity.z);
            horizontalInertia = Vector3.Lerp(horizontalInertia, Vector3.zero, 10f * Time.deltaTime);
            velocity.x = horizontalInertia.x;
            velocity.z = horizontalInertia.z;

            float x = Input.GetAxis("Horizontal");
            float z = Input.GetAxis("Vertical");

            Vector3 forward = playerCamera.forward;
            Vector3 right = playerCamera.right;
            forward.y = 0;
            right.y = 0;
            forward.Normalize();
            right.Normalize();

            Vector3 moveInput = (forward * z + right * x);

            if (moveInput.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveInput);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 0.2f);
            }

            float currentSpeed = moveSpeed;
            if (Input.GetKey(KeyCode.LeftShift))
            {
                currentSpeed *= dashMultiplier;
            }

            controller.Move(moveInput.normalized * currentSpeed * Time.deltaTime);

            if (GetButtonDownOrJump())
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;

            float x = Input.GetAxis("Horizontal");
            float z = Input.GetAxis("Vertical");

            Vector3 forward = playerCamera.forward;
            Vector3 right = playerCamera.right;
            forward.y = 0;
            right.y = 0;
            forward.Normalize();
            right.Normalize();

            Vector3 airInput = (forward * z + right * x);

            Vector3 horizontalVel = new Vector3(velocity.x, 0, velocity.z);
            horizontalVel *= 0.99f;

            if (airInput.sqrMagnitude > 0.01f)
            {
                horizontalVel += airInput * 18f * Time.deltaTime;
            }

            velocity = new Vector3(horizontalVel.x, velocity.y, horizontalVel.z);
        }

        controller.Move(velocity * Time.deltaTime);
    }

    bool GetButtonDownOrJump()
    {
        try { return Input.GetButtonDown("Jump"); }
        catch { return Input.GetKeyDown(KeyCode.Space); }
    }
}