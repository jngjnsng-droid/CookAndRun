using UnityEngine;
using CookAndRun.Progression;

public class PlayerMovement : MonoBehaviour
{
    // ★ 수정: 걷기 속도와 달리기 속도를 분리
    public float walkSpeed = 5f;
    public float runSpeed = 10f; // ★ 추가: 달리기 속도
    private float currentSpeed; // 현재 적용되는 속도

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    
    private float moveX;
    private float lastMoveX = 0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        var flow = GameFlowController.Instance;
        if (flow != null && flow.HasRun && !flow.CanAcceptActions)
        {
            moveX = 0f;
            if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            if (animator != null)
            {
                animator.SetBool("isWalking", false);
                animator.SetFloat("SpeedY", 0f);
            }
            return;
        }
        // 1. 좌우 입력 받기
        moveX = Input.GetAxisRaw("Horizontal");

        // ★ 추가: 달리기 키(Left Shift) 입력 감지 및 속도 결정
        if (Input.GetKey(KeyCode.LeftShift) && Mathf.Abs(moveX) > 0.01f)
        {
            currentSpeed = runSpeed; // Shift 키를 누르고 움직이면 달리기 속도 적용
        }
        else
        {
            currentSpeed = walkSpeed; // 그 외에는 걷기 속도 적용
        }

        // 2. 입력에 따른 좌우 반전 (Flip) 처리 및 애니메이션 초기화
        if (moveX > 0)
        {
            if (lastMoveX < 0) 
            {
                spriteRenderer.flipX = false; 
                animator.Play("Player_Walk", -1, 0f); // 걷기로 재시작 (달리기로 가기 전 단계)
            }
            else
            {
                spriteRenderer.flipX = false;
            }
        }
        else if (moveX < 0)
        {
            if (lastMoveX > 0)
            {
                spriteRenderer.flipX = true; 
                animator.Play("Player_Walk", -1, 0f); 
            }
            else
            {
                spriteRenderer.flipX = true;
            }
        }

        // 3. 이동 여부에 따른 애니메이션 전환 (isWalking Bool값)
        bool isMoving = Mathf.Abs(moveX) > 0.01f;
        animator.SetBool("isWalking", isMoving);

        // ★ 추가: 애니메이터에 속도 값 전달 (SpeedY Float값)
        if (isMoving)
        {
            // 이동 중일 때 속도에 따라 값을 0.5(걷기) 또는 1.0(달리기)으로 보냄
            float speedValue = (currentSpeed == runSpeed) ? 1.0f : 0.5f;
            animator.SetFloat("SpeedY", speedValue);
        }
        else
        {
            animator.SetFloat("SpeedY", 0f); // 멈췄을 땐 0
        }

        // 이번 프레임의 방향을 저장
        lastMoveX = moveX; 
    }

    void FixedUpdate()
    {
        // ★ 수정: 부드러운 좌우 이동 처리 (currentSpeed 사용)
        rb.linearVelocity = new Vector2(moveX * currentSpeed, rb.linearVelocity.y);
    }
}
