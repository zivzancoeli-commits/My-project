using UnityEngine;

public class Enemy : MonoBehaviour
{
    public float speed = 2f;
    public float stompBounce = 10f;
    public LayerMask groundLayer;

    private Rigidbody2D rb;
    private BoxCollider2D box;
    private SpriteRenderer sprite;
    private float direction = -1f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        box = GetComponent<BoxCollider2D>();
        sprite = GetComponent<SpriteRenderer>();
    }

    void FixedUpdate()
    {
        rb.linearVelocity = new Vector2(direction * speed, rb.linearVelocity.y);
        Vector2 front = (Vector2)transform.position + new Vector2(direction * (box.size.x / 2f + 0.05f), 0f);
        // wall check
        bool wallAhead = Physics2D.Raycast(front, new Vector2(direction, 0f), 0.1f, groundLayer);
        // will it fall if it moves forward
        bool groundAhead = Physics2D.Raycast(front, Vector2.down, box.size.y / 2f + 0.2f, groundLayer);
        if (wallAhead || !groundAhead)
        {
            Turn();
        }
    }

    void Turn()
    {
        direction = -direction;
        sprite.flipX = direction > 0f;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Hazard"))
        {
            Turn();
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.GetComponent<Enemy>() != null)
        {
            Turn();
            return;
        }

        PlayerHealth player = collision.gameObject.GetComponent<PlayerHealth>();
        if (player == null) return;
        // checks if player stomps it
        bool stomped = collision.transform.position.y > transform.position.y + box.size.y * 0.5f;
        if (stomped)
        {
            Rigidbody2D playerRb = collision.rigidbody;
            playerRb.linearVelocity = new Vector2(playerRb.linearVelocity.x, stompBounce);
            Destroy(gameObject);
        }
        else
        {
            player.TakeDamage(1, transform.position);
        }
    }
}