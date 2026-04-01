using UnityEngine;

public class GroundCheck : MonoBehaviour
{
    private PlayerMovement player;

    void Start()
    {
        player = GetComponentInParent<PlayerMovement>();
    }

    void OnTriggerEnter2D(Collider2D col)
    {
        if (col.CompareTag("Ground"))
        {
            player.SetGrounded(true);
        }
    }

    void OnTriggerExit2D(Collider2D col)
    {
        if (col.CompareTag("Ground"))
        {
            player.SetGrounded(false);
        }
    }
}