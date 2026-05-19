using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(LineRenderer), typeof(BoxCollider))]
public class SpiderWeb : NetworkBehaviour
{
    [Header("거미줄 설정")]
    [SerializeField] private float lifetime = 60f;
    [SerializeField] private float webThickness = 1f;

    [Header("은신 및 가시성 설정")]
    [SerializeField] private float visibleDistance = 16f;
    [SerializeField] private float fullyVisibleDistance = 8f; 
    [SerializeField] private float initialVisibleDuration = 2f;

    public NetworkVariable<Vector3> startPoint = new NetworkVariable<Vector3>();
    public NetworkVariable<Vector3> endPoint = new NetworkVariable<Vector3>();

    private MonsterSpiderAI ownerSpider;
    private LineRenderer lineRenderer;
    private BoxCollider boxCollider;
    private bool isInitialVisiblePeriod = true;

    private Color originalStartColor;
    private Color originalEndColor;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        boxCollider = GetComponent<BoxCollider>();

        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = false;
        lineRenderer.startWidth = webThickness;
        lineRenderer.endWidth = webThickness;
        boxCollider.isTrigger = true;

        originalStartColor = lineRenderer.startColor;
        originalEndColor = lineRenderer.endColor;

        lineRenderer.enabled = true;
    }

    public override void OnNetworkSpawn()
    {
        startPoint.OnValueChanged += (prev, curr) => UpdateWebShape();
        endPoint.OnValueChanged += (prev, curr) => UpdateWebShape();

        if (IsClient) UpdateWebShape();

        StartCoroutine(InitialVisibilityRoutine());
    }

    private IEnumerator InitialVisibilityRoutine()
    {
        yield return new WaitForSeconds(initialVisibleDuration);
        isInitialVisiblePeriod = false;
    }

    public void InitializeLine(MonsterSpiderAI spider, Vector3 start, Vector3 end)
    {
        ownerSpider = spider;
        startPoint.Value = start;
        endPoint.Value = end;

        float finalLifetime = lifetime <= 0 ? 60f : lifetime;
        Invoke(nameof(DespawnWeb), finalLifetime);

        UpdateWebShape();
    }

    private void UpdateWebShape()
    {
        float distance = Vector3.Distance(startPoint.Value, endPoint.Value);
        if (distance < 0.1f) return;

        transform.position = startPoint.Value;
        transform.LookAt(endPoint.Value);

        lineRenderer.SetPosition(0, Vector3.zero);
        lineRenderer.SetPosition(1, new Vector3(0, 0, distance));

        boxCollider.size = new Vector3(webThickness, webThickness, distance);
        boxCollider.center = new Vector3(0, 0, distance / 2f);
    }

    private void DespawnWeb()
    {
        if (IsServer && NetworkObject.IsSpawned) NetworkObject.Despawn(true);
    }

    private void Update()
    {
        if (isInitialVisiblePeriod)
        {
            SetWebAlpha(1f);
            return;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            NetworkObject localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
            if (localPlayer != null)
            {
                Vector3 closestPoint = boxCollider.ClosestPoint(localPlayer.transform.position);
                float distance = Vector3.Distance(closestPoint, localPlayer.transform.position);

                if (distance > visibleDistance)
                {
                    lineRenderer.enabled = false; 
                }
                else
                {
                    lineRenderer.enabled = true;

                    float alphaMultiplier = Mathf.InverseLerp(visibleDistance, fullyVisibleDistance, distance);

                    SetWebAlpha(alphaMultiplier);
                }
            }
        }
    }

    private void SetWebAlpha(float alphaMultiplier)
    {
        Color start = originalStartColor;
        start.a *= alphaMultiplier;
        lineRenderer.startColor = start;

        Color end = originalEndColor;
        end.a *= alphaMultiplier;
        lineRenderer.endColor = end;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Player") && ownerSpider != null)
        {
            ownerSpider.AlertByWeb(other.transform);
            DespawnWeb();
        }
    }
}