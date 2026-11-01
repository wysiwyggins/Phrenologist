using UnityEngine;

// GameController adds this to the head's Rigidbody so collisions with any of the head's colliders reach it
public class HeadImpact : MonoBehaviour
{
    public GameController controller;

    void OnCollisionEnter(Collision collision)
    {
        if (controller) {
            controller.OnHeadCollision(collision);
        }
    }
}
