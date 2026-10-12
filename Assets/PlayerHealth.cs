using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 3;
    public float invincibleTime = 1f;
    public float knockbackForce = 8f;
    public float fallLimit = -10f;
    public Image[] hearts;   // Drag your heart Images in here

    private int currentHealth;
    private float invincibleTimer;
    private Vector3 spawnPoint;
    private Rigidbody2D rb;
    private SpriteRenderer sprite;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
        spawnPoint = transform.position;
        currentHealth = maxHealth;
        UpdateHearts();
    }

    void Update()
    {
        if (invincibleTimer > 0)
        {
            invincibleTimer -= Time.deltaTime;
            sprite.enabled = Mathf.FloorToInt(invincibleTimer * 10) % 2 == 0;
        }
        else
        {
            sprite.enabled = true;
        }

        if (transform.position.y < fallLimit)
        {
            Respawn();
        }
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (other.CompareTag("Hazard"))
        {
            TakeDamage(1, other.transform.position);
        }
    }

    public void TakeDamage(int amount, Vector3 hitFrom)
    {
        if (invincibleTimer > 0) return;

        currentHealth -= amount;
        Debug.Log("Ouch! Health: " + currentHealth);
        UpdateHearts();

        if (currentHealth <= 0)
        {
            Respawn();
            return;
        }

        float direction = transform.position.x >= hitFrom.x ? 1f : -1f;
        rb.linearVelocity = new Vector2(direction * knockbackForce * 0.5f, knockbackForce);

        invincibleTimer = invincibleTime;
    }

    public bool Heal(int amount)
    {
        if (currentHealth >= maxHealth) return false;
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        UpdateHearts();
        return true;
    }

    void UpdateHearts()
    {
        for (int i = 0; i < hearts.Length; i++)
        {
            // Show a heart for each point of health you still have
            hearts[i].enabled = i < currentHealth;
        }
    }

    // Used by checkpoints: respawn here from now on
    public void SetSpawnPoint(Vector3 point)
    {
        spawnPoint = point;
    }

    // Used by the start flag: move the player there with full health
    public void TeleportTo(Vector3 point)
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        transform.position = point;
        spawnPoint = point;
        Respawn();
    }

    void Respawn()
    {
        rb.linearVelocity = Vector2.zero;
        rb.position = spawnPoint;
        currentHealth = maxHealth;
        invincibleTimer = 0;
        UpdateHearts();
        Debug.Log("Respawned with full health");
    }
}