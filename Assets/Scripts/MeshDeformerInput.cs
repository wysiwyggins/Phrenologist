using UnityEngine;

public class MeshDeformerInput : MonoBehaviour
{
    public float force = 1f;
    public float impulse = 10f;

    void HandleInput()
    {
        Ray inputRay = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(inputRay, out RaycastHit hit))
        {
            MeshDeformer deformer = hit.collider.GetComponent<MeshDeformer>();
            if (deformer)
            {
                // Punch in the direction the camera is looking through the cursor
                deformer.AddDent(hit.point, inputRay.direction, force);

                // Knock the head at the impact point so off-center hits make it rock
                Rigidbody body = FindDynamicBody(deformer.transform);
                if (body)
                {
                    body.AddForceAtPosition(inputRay.direction * impulse, hit.point, ForceMode.Impulse);
                }
            }
        }
    }

    // The model's own Rigidbody is kinematic (for its click collider), so look up the hierarchy for the one gravity moves
    Rigidbody FindDynamicBody(Transform start)
    {
        foreach (Rigidbody body in start.GetComponentsInParent<Rigidbody>())
        {
            if (!body.isKinematic)
            {
                return body;
            }
        }
        return null;
    }

    // Update is called once per frame
    void Update()
    {
        // One dent per click
        if (Input.GetMouseButtonDown(0))
        {
            HandleInput();
        }
    }
}
