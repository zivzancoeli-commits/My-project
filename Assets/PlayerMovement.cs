using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float moveSpeed = 7f;        // Top side-to-side speed
    public float acceleration = 40f;    // How fast you speed up
    [Range(0f, 1f)]
    public float friction = 0.25f;      // Share of speed lost each update when you let go
    public float updatesPerSecond = 30f;
    public float jumpForce = 12f;

    private Rigidbody2D rb;
    private bool isGrounded;
    private float moveInput;
    private float timer;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        moveInput = Input.GetAxisRaw("Horizontal");

        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        }
    }

    void FixedUpdate()
    {
        timer += Time.fixedDeltaTime;
        float step = 1f / updatesPerSecond;

        while (timer >= step)
        {
            timer -= step;
            float x = rb.linearVelocity.x;

            if (Mathf.Abs(moveInput) > 0.01f)
            {
                // Speed up toward top speed
                x = Mathf.MoveTowards(x, moveInput * moveSpeed, acceleration * step);
            }
            else
            {
                // Lose a share of current speed: fast = big slowdown, slow = small slowdown
                x -= x * friction;
                if (Mathf.Abs(x) < 0.05f) x = 0f;
            }

            // Only change side-to-side speed; keep Unity's falling/jumping speed
            rb.linearVelocity = new Vector2(x, rb.linearVelocity.y);
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        isGrounded = true;
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        isGrounded = false;
    }
}