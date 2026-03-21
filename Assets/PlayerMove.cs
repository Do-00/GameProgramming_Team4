using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMove : MonoBehaviour
{
    private Rigidbody rb; // 변수명을 rb로 변경 (가독성)
    public float speed = 10f;
    public float jumpHeight = 3f;
    public float dash = 5f; // 'g'를 'f'로 수정

    private Vector3 dir = Vector3.zero;

    void Start()
    {
        // Rigidbody 가져오기
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        // 입력 받기
        dir.x = Input.GetAxisRaw("Horizontal"); // 즉각적인 반응 위해 GetAxisRaw 사용
        dir.z = Input.GetAxisRaw("Vertical");

        // 회전 처리
        if (dir != Vector3.zero)
        {
            transform.forward = dir;
        }
    }

    void FixedUpdate()
    {
        // 물리 이동
        Move();
    }

    void Move()
    {
        // 수평 이동 방향 계산
        Vector3 moveDir = dir.normalized * speed;

        // Y축 속도는 유지 (점프/중력 자연스럽게)
        rb.linearVelocity = new Vector3(moveDir.x, rb.linearVelocity.y, moveDir.z);
    }
}