using UnityEngine;

public class VRLedgeDetector : MonoBehaviour
{
    [Header("References")]
    public Transform player;

    public Transform leftHand;
    public Transform rightHand;

    public Transform leftPalm;
    public Transform rightPalm;

    [Header("Detection Settings")]
    public LayerMask raycastLayerMask;
    public float shortRayLength = 0.1f;
    public float ledgeDetectionLength = 0.1f;
    public float minimumLedgeHeight = 0.5f;
    public float palmDownThreshold = 0.6f;

    [Header("Surface Validation")]
    public float surfaceCheckSize = 0.1f;
    public float maxSurfaceHeightDifference = 0.05f;
    public float maxSurfaceNormalAngle = 10f;

    [Header("Climb Points")]
    public GameObject climbPointPrefab;

    [Header("Debug")]
    public bool debugDrawRays = true;

    [Header("Left Hand Debug")]
    public bool isLeftPalmPointingDown;
    public bool leftAllRaysHit;
    public bool leftSurfaceFlat;
    public bool leftSurfaceNormalValid;
    public bool leftHeightValid;
    public bool leftHasDrop;
    public bool leftIsValidLedge;

    [Header("Right Hand Debug")]
    public bool isRightPalmPointingDown;
    public bool rightAllRaysHit;
    public bool rightSurfaceFlat;
    public bool rightSurfaceNormalValid;
    public bool rightHeightValid;
    public bool rightHasDrop;
    public bool rightIsValidLedge;

    private GameObject leftClimbPoint;
    private GameObject rightClimbPoint;

    void Start()
    {
        leftClimbPoint = Instantiate(climbPointPrefab);
        rightClimbPoint = Instantiate(climbPointPrefab);

        leftClimbPoint.SetActive(false);
        rightClimbPoint.SetActive(false);
    }

    void Update()
    {
        ProcessHand(
            leftHand, leftPalm, leftClimbPoint,
            ref isLeftPalmPointingDown,
            ref leftAllRaysHit,
            ref leftSurfaceFlat,
            ref leftSurfaceNormalValid,
            ref leftHeightValid,
            ref leftHasDrop,
            ref leftIsValidLedge
        );

        ProcessHand(
            rightHand, rightPalm, rightClimbPoint,
            ref isRightPalmPointingDown,
            ref rightAllRaysHit,
            ref rightSurfaceFlat,
            ref rightSurfaceNormalValid,
            ref rightHeightValid,
            ref rightHasDrop,
            ref rightIsValidLedge
        );
    }

    void ProcessHand(
        Transform hand,
        Transform palm,
        GameObject climbPoint,
        ref bool palmDown,
        ref bool allRaysHit,
        ref bool surfaceFlat,
        ref bool surfaceNormalValid,
        ref bool heightValid,
        ref bool hasDrop,
        ref bool isValidLedge
    )
    {
        // Reset debug
        palmDown = false;
        allRaysHit = false;
        surfaceFlat = false;
        surfaceNormalValid = false;
        heightValid = false;
        hasDrop = false;
        isValidLedge = false;

        if (hand == null || palm == null) return;

        // 1. Palm check
        float alignment = Vector3.Dot(palm.forward, Vector3.down);
        palmDown = alignment >= palmDownThreshold;

        if (!palmDown)
        {
            climbPoint.SetActive(false);
            return;
        }

        float halfSize = surfaceCheckSize * 0.5f;

        Vector3[] localOffsets = new Vector3[]
        {
            new Vector3(-halfSize, 0, -halfSize),
            new Vector3(-halfSize, 0,  halfSize),
            new Vector3( halfSize, 0, -halfSize),
            new Vector3( halfSize, 0,  halfSize),
        };

        RaycastHit[] hits = new RaycastHit[4];

        // 2. Raycasts
        for (int i = 0; i < 4; i++)
        {
            Vector3 worldOffset =
                hand.right * localOffsets[i].x +
                hand.forward * localOffsets[i].z;

            Vector3 origin = hand.position + worldOffset;

            bool hit = Physics.Raycast(origin, Vector3.down, out hits[i], shortRayLength, raycastLayerMask);

            if (debugDrawRays)
            {
                Debug.DrawRay(origin, Vector3.down * shortRayLength, hit ? Color.green : Color.red);
            }

            if (!hit)
            {
                climbPoint.SetActive(false);
                return;
            }
        }

        allRaysHit = true;

        // 3. Height consistency
        float minY = hits[0].point.y;
        float maxY = hits[0].point.y;

        for (int i = 1; i < 4; i++)
        {
            float y = hits[i].point.y;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        surfaceFlat = (maxY - minY) <= maxSurfaceHeightDifference;

        if (!surfaceFlat)
        {
            climbPoint.SetActive(false);
            return;
        }

        // 4. Normal consistency
        Vector3 baseNormal = hits[0].normal;
        surfaceNormalValid = true;

        for (int i = 1; i < 4; i++)
        {
            float angle = Vector3.Angle(baseNormal, hits[i].normal);
            if (angle > maxSurfaceNormalAngle)
            {
                surfaceNormalValid = false;
                break;
            }
        }

        if (!surfaceNormalValid)
        {
            climbPoint.SetActive(false);
            return;
        }

        // 5. Average point
        Vector3 avgPoint = Vector3.zero;
        for (int i = 0; i < 4; i++)
            avgPoint += hits[i].point;

        avgPoint /= 4f;

        // 6. Height vs player
        heightValid = avgPoint.y >= player.position.y + minimumLedgeHeight;

        if (!heightValid)
        {
            climbPoint.SetActive(false);
            return;
        }

        // 7. Drop check
        Vector3 secondOrigin = avgPoint + Vector3.down * ledgeDetectionLength;

        bool hitBelow = Physics.Raycast(secondOrigin, Vector3.down, minimumLedgeHeight, raycastLayerMask);

        if (debugDrawRays)
        {
            Debug.DrawRay(secondOrigin, Vector3.down * minimumLedgeHeight, hitBelow ? Color.red : Color.blue);
        }

        hasDrop = !hitBelow;

        if (!hasDrop)
        {
            climbPoint.SetActive(false);
            return;
        }

        // 8. Final valid ledge
        isValidLedge = true;

        climbPoint.transform.position = avgPoint;
        climbPoint.SetActive(true);
    }
}