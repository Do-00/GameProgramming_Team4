using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMove : NetworkBehaviour
{
    [Header("이동 설정")]
    public float speed = 10f;
    public float jumpHeight = 5f;
    public float dashForce = 15f; // 대시 힘

    private Rigidbody rb;
    private Vector3 moveInput;
    private bool isDashing = false; // 지금 대시 상태 확인 변수

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
    }

    void Update()
    {
        if (!IsOwner) return; // 소유 캐릭터만 조종

        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        moveInput = new Vector3(moveX, 0f, moveZ).normalized;

        // 점프
        if (Input.GetKeyDown(KeyCode.Space))
        {
            rb.AddForce(Vector3.up * jumpHeight, ForceMode.Impulse);
        }

        // 대시 (왼쪽 Shift키)
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            // 바라보는 방향(이동 방향)으로 순간적인 힘을 가함
            // 만약 가만히 서있다면 캐릭터가 바라보는 앞쪽으로 대시
            Vector3 dashDirection = moveInput != Vector3.zero ? moveInput : transform.forward;
            rb.AddForce(dashDirection * dashForce, ForceMode.Impulse);

            isDashing = true;
            Invoke("ResetDash", 0.5f); // 0.5초 뒤에 대시 상태 해제 (유니티 내장 타이머 함수)
        }
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;
        rb.MovePosition(rb.position + moveInput * speed * Time.fixedDeltaTime);
    }

    void ResetDash()
    {
        isDashing = false;
    }

    // 여기서부터가 멀티플레이 상호작용이에용
    void OnCollisionEnter(Collision collision)
    {
        if (!IsOwner) return; // 사용자가 부딪힌 것만 판정

        // 내가 대시 중이고 && 부딪힌 상대가 'Player' 태그를 가졌다면
        if (isDashing && collision.gameObject.CompareTag("Player"))
        {
            // 상대방의 네트워크 신분증(ID)을 빼앗아옴
            ulong targetId = collision.gameObject.GetComponent<NetworkObject>().NetworkObjectId;

            // 튕겨나갈 방향 계산 (내 위치에서 팀원 위치로 향하는 방향)
            Vector3 pushDirection = (collision.transform.position - transform.position).normalized;
            // 위쪽으로도 살짝 뜨게 만들기
            pushDirection.y = 0.5f;

            // 서버에게 저 ID 가진 녀석 좀 이 방향으로 빵 차버려달라고 요청 (RPC 호출)
            PushPlayerServerRpc(targetId, pushDirection);
        }
    }

    // [ServerRpc]가 붙은 함수는 클라이언트가 실행해도 무조건 '서버 컴퓨터'에서 동작
    // 서버에게 부탁하려면 함수 이름 끝에 무조건 ServerRpc를 붙여야 하는 규칙이 있다네요
    [ServerRpc]
    void PushPlayerServerRpc(ulong targetNetworkId, Vector3 direction)
    {
        // 서버의 명부에서 해당 ID를 가진 플레이어를 찾음
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkId, out NetworkObject targetObject))
        {
            Rigidbody targetRb = targetObject.GetComponent<Rigidbody>();
            if (targetRb != null)
            {
                // 서버가 그 플레이어에게 물리적인 힘을 가해서 튕겨냄
                // 서버에서 밀면 NetworkTransform이 알아서 모두의 화면에 날아가는 걸 보여줌
                targetRb.AddForce(direction * 20f, ForceMode.Impulse);
            }
        }
    }
}