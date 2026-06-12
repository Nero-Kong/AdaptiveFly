using UnityEngine;
using WaveHarmonic.Crest;

[DisallowMultipleComponent]
public class SubmarineWaterImpactFx : MonoBehaviour
{
    [SerializeField] Rigidbody targetBody;
    [SerializeField] GameObject waveImpactPrefab;
    [SerializeField] GameObject bubbleImpactPrefab;
    [SerializeField] Vector3 samplePointLocalOffset = new(0f, 0f, 0f);
    [SerializeField] float entrySpeedThreshold = 1.5f;
    [SerializeField] float exitSpeedThreshold = 1.0f;
    [SerializeField] float cooldownSeconds = 0.35f;
    [SerializeField] float waveImpactScale = 1.25f;
    [SerializeField] float bubbleScale = 2.5f;
    [SerializeField] float waveImpactLifetime = 2.0f;
    [SerializeField] float bubbleLifetime = 2.5f;
    [SerializeField] float bubbleDepthOffset = 1.5f;

    bool wasUnderwater;
    float lastTriggerTime = float.NegativeInfinity;

    void Awake()
    {
        if (targetBody == null)
        {
            TryGetComponent(out targetBody);
        }
    }

    void Start()
    {
        var water = WaterRenderer.Instance;
        if (water == null)
        {
            return;
        }

        wasUnderwater = SamplePointWorld().y <= water.SeaLevel;
    }

    void FixedUpdate()
    {
        var water = WaterRenderer.Instance;
        if (water == null || targetBody == null)
        {
            return;
        }

        var point = SamplePointWorld();
        var waterLevel = water.SeaLevel;
        var isUnderwater = point.y <= waterLevel;

        if (isUnderwater != wasUnderwater)
        {
            var verticalSpeed = targetBody.linearVelocity.y;

            if (Time.time - lastTriggerTime >= cooldownSeconds)
            {
                if (!wasUnderwater && isUnderwater && verticalSpeed <= -entrySpeedThreshold)
                {
                    SpawnImpact(point, waterLevel, waveImpactScale, true);
                    lastTriggerTime = Time.time;
                }
                else if (wasUnderwater && !isUnderwater && verticalSpeed >= exitSpeedThreshold)
                {
                    SpawnImpact(point, waterLevel, waveImpactScale * 0.7f, false);
                    lastTriggerTime = Time.time;
                }
            }

            wasUnderwater = isUnderwater;
        }
    }

    Vector3 SamplePointWorld()
    {
        return transform.TransformPoint(samplePointLocalOffset);
    }

    void SpawnImpact(Vector3 point, float waterLevel, float waveScale, bool spawnBubbles)
    {
        var impactPosition = new Vector3(point.x, waterLevel, point.z);

        if (waveImpactPrefab != null)
        {
            var wave = Instantiate(waveImpactPrefab, impactPosition, waveImpactPrefab.transform.rotation);
            wave.transform.localScale *= waveScale;
            Destroy(wave, waveImpactLifetime);
        }

        if (spawnBubbles && bubbleImpactPrefab != null)
        {
            var bubblePosition = impactPosition + Vector3.down * bubbleDepthOffset;
            var bubbles = Instantiate(bubbleImpactPrefab, bubblePosition, bubbleImpactPrefab.transform.rotation);
            bubbles.transform.localScale *= bubbleScale;
            Destroy(bubbles, bubbleLifetime);
        }
    }
}
