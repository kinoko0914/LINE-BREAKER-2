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

    [Header("ワイヤーアクション設定")]
    public float grappleBoostSpeed = 45f; // 飛び出すときの猛烈な初速
    public float searchRadius = 45f;        // 前方サーチの範囲
    public float maxGrappleTime = 0.7f;   // ワイヤーが自動で切れるまでの時間（秒）
    private bool isGrappling = false;
    private Vector3 grappleVelocity;      // ワイヤー専用の速度
    private float grappleTimer = 0f;      // ワイヤー発動からの経過時間

    private Vector3 leftGrappleTarget;
    private Vector3 rightGrappleTarget;
    private bool hasLeftTarget = false;
    private bool hasRightTarget = false;

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

        // --- カメラの周回操作 ---
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

        // --- 【ワイヤーアクション：視点中心 ±90度の範囲で自動サーチ＆発動】 ---
        if (Input.GetMouseButtonDown(1)) // 右クリック
        {
            Collider[] hitColliders = Physics.OverlapSphere(transform.position, searchRadius);

            Transform bestLeftTarget = null;
            Transform bestRightTarget = null;
            Vector3 bestLeftPoint = Vector3.zero;
            Vector3 bestRightPoint = Vector3.zero;

            float maxLeftScore = -999f;
            float maxRightScore = -999f;

            Vector3 searchCenterDir = playerCamera != null ? playerCamera.forward : transform.forward;

            foreach (var col in hitColliders)
            {
                if (col.transform == transform || col.transform.IsChildOf(transform)) continue;
                if (!col.CompareTag("GrappleTarget")) continue;

                Vector3 targetPoint = col.ClosestPoint(transform.position);
                Vector3 directionToTarget = (targetPoint - transform.position).normalized;

                float viewDot = Vector3.Dot(searchCenterDir, directionToTarget);

                if (viewDot >= 0.0f)
                {
                    float distance = Vector3.Distance(transform.position, targetPoint);
                    float score = viewDot * 20f - (distance * 0.1f);

                    Vector3 localPos = playerCamera.InverseTransformPoint(targetPoint);

                    if (localPos.x < 0) // カメラから見て左側
                    {
                        if (score > maxLeftScore)
                        {
                            maxLeftScore = score;
                            bestLeftTarget = col.transform;
                            bestLeftPoint = targetPoint;
                        }
                    }
                    else // カメラから見て右側
                    {
                        if (score > maxRightScore)
                        {
                            maxRightScore = score;
                            bestRightTarget = col.transform;
                            bestRightPoint = targetPoint;
                        }
                    }
                }
            }

            hasLeftTarget = (bestLeftTarget != null);
            hasRightTarget = (bestRightTarget != null);

            if (hasLeftTarget && !hasRightTarget) { rightGrappleTarget = bestLeftPoint; hasRightTarget = true; }
            if (!hasLeftTarget && hasRightTarget) { leftGrappleTarget = bestRightPoint; hasLeftTarget = true; }

            if (hasLeftTarget) leftGrappleTarget = bestLeftPoint;
            if (hasRightTarget) rightGrappleTarget = bestRightPoint;

            if (hasLeftTarget || hasRightTarget)
            {
                isGrappling = true;
                grappleTimer = 0f;

                if (isGrounded)
                {
                    controller.Move(Vector3.up * 0.3f);
                }

                // ★修正：ワイヤーがどこに刺さっていようと、初速が進む方向は「カメラ（視点）が向いている方向」！
                Vector3 boostDir = playerCamera != null ? playerCamera.forward : transform.forward;
                boostDir.y = 0.3f; // 少し上向きの浮遊感
                boostDir.Normalize();

                grappleVelocity = boostDir * grappleBoostSpeed;
            }
        }

        // --- 【ワイヤー中の挙動】 ---
        if (isGrappling)
        {
            grappleTimer += Time.deltaTime;

            bool shouldEnd = Input.GetMouseButtonUp(1) || (grappleTimer >= maxGrappleTime);

            if (shouldEnd)
            {
                isGrappling = false;
                velocity = grappleVelocity;
            }
            else
            {
                // 腰の左右からワイヤーを綺麗に伸ばす（あくまで見た目の飾り・演出）
                Vector3 leftHip = transform.position + Vector3.up * 1.0f - transform.right * 0.4f;
                Vector3 rightHip = transform.position + Vector3.up * 1.0f + transform.right * 0.4f;

                if (hasLeftTarget && leftLineRenderer != null)
                {
                    leftLineRenderer.positionCount = 2;
                    leftLineRenderer.SetPosition(0, leftHip);
                    leftLineRenderer.SetPosition(1, leftGrappleTarget);
                }
                else if (leftLineRenderer != null) { leftLineRenderer.positionCount = 0; }

                if (hasRightTarget && rightLineRenderer != null)
                {
                    rightLineRenderer.positionCount = 2;
                    rightLineRenderer.SetPosition(0, rightHip);
                    rightLineRenderer.SetPosition(1, rightGrappleTarget);
                }
                else if (rightLineRenderer != null) { rightLineRenderer.positionCount = 0; }

                // ★修正：ワイヤー中も進む方向は常にカメラの視点方向！
                Vector3 camForward = playerCamera != null ? playerCamera.forward : transform.forward;
                camForward.y = 0;
                camForward.Normalize();

                grappleVelocity.y += (gravity * 0.15f) * Time.deltaTime;
                grappleVelocity = (camForward * grappleBoostSpeed) + (Vector3.up * grappleVelocity.y);

                controller.Move(grappleVelocity * Time.deltaTime);

                // キャラクターの向きもカメラの視点方向に合わせる
                if (camForward.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(camForward);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 0.2f);
                }

                return;
            }
        }

        // ワイヤー中でないときは線を消す
        if (leftLineRenderer != null) leftLineRenderer.positionCount = 0;
        if (rightLineRenderer != null) rightLineRenderer.positionCount = 0;

        // --- 【通常移動 / ワイヤー終了後の空中制御（エアコントロール）】 ---
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