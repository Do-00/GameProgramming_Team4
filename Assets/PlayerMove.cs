using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMove : NetworkBehaviour
{
    [Header("이동 설정")]
    public float speed = 10f;
    public float jumpHeight = 7f;
    public float dashForce = 40f; // 대시 초기 폭발력

    private Rigidbody rb;
    private Vector3 moveInput;
    private bool isDashing = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
    }

    void Update()
    {
        if (!IsOwner) return;

        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        moveInput = new Vector3(moveX, 0f, moveZ).normalized;

        // 이동 방향으로 회전
        if (moveInput != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveInput), Time.deltaTime * 10f);
        }

        // 점프 (Y축 속도가 0에 가까울 때 = 땅에 있을 때만)
        if (Input.GetKeyDown(KeyCode.Space) && Mathf.Abs(rb.linearVelocity.y) < 0.1f)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(Vector3.up * jumpHeight, ForceMode.Impulse);
        }

        // 대시 (Shift)
        if (Input.GetKeyDown(KeyCode.LeftShift) && !isDashing)
        {
            isDashing = true;

            // 누르는 순간 앞으로 강하게 나감
            rb.AddForce(transform.forward * dashForce, ForceMode.Impulse);

            // 0.5초 후에 대시 상태 해제
            Invoke("ResetDash", 0.7f);
        }
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;

        // 우리가 방향키로 가고 싶은 목표 속도
        Vector3 targetVelocity = moveInput * speed;

        // 현재 내 캐릭터의 속도 (여기서 Y축 중력은 0으로 빼둬서 절대 건드리지 않음!)
        Vector3 currentVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // 목표 속도가 되려면 '얼마나 힘을 더 줘야 하는지' 계산
        Vector3 velocityChange = targetVelocity - currentVelocity;

        if (isDashing)
        {
            // 대시 중: 브레이크(속도 강제 고정)를 걸지 않음
            // 대신, 방향키(moveInput)를 누르면 그 방향으로 살짝 힘을 줘서 방향을 틀 수 있게 해줌.
            if (moveInput != Vector3.zero)
            {
                rb.AddForce(moveInput * (speed * 0.5f), ForceMode.Acceleration);
            }
        }
        else
        {
            // 평소: 계산한 힘을 즉각적으로 적용해서 딱 원하는 속도만큼만 걷게 함.
            // Y축은 건드리지 않았으니 중력은 유니티 엔진이 알아서 처리
            rb.AddForce(velocityChange, ForceMode.VelocityChange);
        }
    }

    void ResetDash()
    {
        isDashing = false;
    }

    // --- 상호작용 (팀원 밀치기) ---
    void OnCollisionEnter(Collision collision)
    {
        if (!IsOwner) return;

        if (isDashing && collision.gameObject.CompareTag("Player"))
        {
            ulong targetId = collision.gameObject.GetComponent<NetworkObject>().NetworkObjectId;
            Vector3 pushDirection = transform.forward + new Vector3(0, 0.5f, 0);
            PushPlayerServerRpc(targetId, pushDirection.normalized);
        }
    }

    [ServerRpc]
    void PushPlayerServerRpc(ulong targetNetworkId, Vector3 direction)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkId, out NetworkObject targetObject))
        {
            Rigidbody targetRb = targetObject.GetComponent<Rigidbody>();
            if (targetRb != null)
            {
                targetRb.AddForce(direction * 25f, ForceMode.Impulse);
            }
        }
    }
}