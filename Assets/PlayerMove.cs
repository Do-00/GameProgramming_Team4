using UnityEngine;
using Unity.Netcode;


[RequireComponent(typeof(Rigidbody))]

public class PlayerMove : NetworkBehaviour
{
    [Header("이동 설정")]
    public float speed = 10f;
    public float jumpHeight = 3f;
    // Dash는 상호작용할 때 쓸 거라 지금 이동 로직에선 일단 보류함
    public float dash = 5f;

    private Rigidbody rb;
    private Vector3 moveInput;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        //물리 힘에 의해 오뚝이처럼 쓰러지는 걸 방지
        rb.freezeRotation = true;
    }

    void Update()
    {
        //이 캐릭터의 주인이 내가 아니면(IsOwner가 false면), 키보드 입력 무시
        if (!IsOwner) return;

        //Input System (Old)
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");

        //대각선 속도 보정 (.normalized)
        moveInput = new Vector3(moveX, 0f, moveZ).normalized;

        //점프 (스페이스바)
        if (Input.GetKeyDown(KeyCode.Space))
        {
            rb.AddForce(Vector3.up * jumpHeight, ForceMode.Impulse);
        }
    }

    void FixedUpdate()
    {
        //물리 이동 본인 것만 처리
        if (!IsOwner) return;

        //현재 위치에서 입력받은 방향으로 스무스하게 이동
        rb.MovePosition(rb.position + moveInput * speed * Time.fixedDeltaTime);
    }
}