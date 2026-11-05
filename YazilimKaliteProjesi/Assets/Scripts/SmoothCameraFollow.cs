using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 3. Şahıs (Top-Down / Isometric / Free Orbit) Kamera Takip Sistemi.
/// Mouse ile karakter etrafında sağa-sola serbest dönüş (Yaw), sınırlı dikey açı (Pitch)
/// ve Scroll tekerleği ile yakınlaştırma/uzaklaştırma (Zoom) destekler.
/// </summary>
public class SmoothCameraFollow : MonoBehaviour
{
    [Header("Takip Hedefi (Target)")]
    [Tooltip("Kameranın takip edeceği Player Transform referansı.")]
    [SerializeField] private Transform target;

    [Tooltip("Kameranın karakter üzerinde odaklanacağı dikey nokta ofseti (göğüs/baş hizası).")]
    [SerializeField] private float lookAtHeightOffset = 0.8f;

    [Header("Mesafe ve Yükseklik Ayarları (Distance & Height)")]
    [Tooltip("Kameranın karaktere olan varsayılan yatay mesafesi.")]
    [SerializeField, Range(1.5f, 15f)] private float distance = 5.0f;

    [Tooltip("Kameranın karakterden varsayılan yüksekliği.")]
    [SerializeField, Range(1f, 12f)] private float height = 3.0f;

    [Header("Dönüş Kontrolleri (Mouse Orbit)")]
    [Tooltip("Mouse dönüş hassasiyeti (Sağa/Sola ve Yukarı/Aşağı bakış hızı).")]
    [SerializeField, Range(0.5f, 15f)] private float rotationSpeed = 3.5f;

    [Tooltip("True ise sadece Mouse Sağ Tık basılı tutulduğunda döner; False ise mouse hareketiyle sürekli döner.")]
    [SerializeField] private bool requireRightClick = true;

    [Tooltip("True ise mouse dikey hareketiyle (Y ekseni) dikey açıyı da (minPitch-maxPitch arasında) değiştirir.")]
    [SerializeField] private bool allowVerticalTilt = true;

    [Tooltip("Minimum dikey bakış açısı (Kameranın zeminin altına girmesini engeller).")]
    [SerializeField, Range(10f, 45f)] private float minPitch = 20f;

    [Tooltip("Maksimum dikey bakış açısı (Kuşbakışı sınırı).")]
    [SerializeField, Range(45f, 80f)] private float maxPitch = 60f;

    [Header("Yumuşatma Ayarları (Smoothing)")]
    [Tooltip("Kamera pozisyon takibinin yumuşaklık süresi (Düşük değer = daha hızlı tepki).")]
    [SerializeField, Range(0.01f, 0.5f)] private float smoothTime = 0.12f;

    [Tooltip("Kamera dönüşünün yumuşama hızı.")]
    [SerializeField, Range(5f, 30f)] private float rotationSmoothSpeed = 15f;

    [Header("Zoom Ayarları (Mouse ScrollWheel)")]
    [Tooltip("Mouse tekerleği ile yakınlaştırma/uzaklaştırma aktif olsun mu?")]
    [SerializeField] private bool enableScrollZoom = true;

    [Tooltip("Scroll tekerleği ile yakınlaştırma hızı.")]
    [SerializeField, Range(1f, 15f)] private float zoomSpeed = 4f;

    [Tooltip("En fazla ne kadar yakınlaşabilir?")]
    [SerializeField, Range(1.5f, 6f)] private float minDistance = 2.5f;

    [Tooltip("En fazla ne kadar uzaklaşabilir?")]
    [SerializeField, Range(5f, 20f)] private float maxDistance = 10.0f;

    private float currentYaw = 0f;
    private float currentPitch = 30f;
    private float basePitch = 30f;
    private Vector3 currentVelocity = Vector3.zero;

    private void Start()
    {
        // Target Inspector'dan atanmadıysa sahnedeki Player'ı otomatik bul
        if (target == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                target = playerObj.transform;
            }
            else
            {
                PlayerMovement playerMovement = FindAnyObjectByType<PlayerMovement>();
                if (playerMovement != null)
                {
                    target = playerMovement.transform;
                }
            }
        }

        // Başlangıç açılarını mevcut kamera açısından ve mesafe/yükseklik oranından al
        currentYaw = transform.eulerAngles.y;
        basePitch = Mathf.Clamp(Mathf.Atan2(height, distance) * Mathf.Rad2Deg, minPitch, maxPitch);
        currentPitch = basePitch;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        // İlk karede kamerayı doğrudan hedef konuma yerleştir
        if (target != null)
        {
            Vector3 initialPos = CalculateDesiredPosition();
            transform.position = initialPos;

            Vector3 lookPoint = target.position + Vector3.up * lookAtHeightOffset;
            transform.rotation = Quaternion.LookRotation(lookPoint - transform.position);
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        HandleZoomInput();
        HandleRotationInput();

        // 1. Hedef kamera pozisyonunu hesapla ve SmoothDamp ile sarsıntısız takip et
        Vector3 desiredPosition = CalculateDesiredPosition();
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref currentVelocity,
            smoothTime
        );

        // 2. Kameranın karaktere bakış açısını yumuşakça güncelle
        Vector3 lookTarget = target.position + Vector3.up * lookAtHeightOffset;
        Vector3 directionToTarget = lookTarget - transform.position;
        if (directionToTarget.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSmoothSpeed * Time.deltaTime
            );
        }
    }

    /// <summary>
    /// Mouse sağa/sola (Yaw) ve yukarı/aşağı (Pitch) dönüş girdilerini işler.
    /// </summary>
    private void HandleRotationInput()
    {
        bool canRotate = !requireRightClick || IsRightClickHeld();
        if (!canRotate) return;

        Vector2 mouseDelta = GetMouseDelta();

        // Yatayda (Yaw) karakter etrafında 360 derece serbest dönüş
        currentYaw += mouseDelta.x * rotationSpeed;

        // Dikeyde (Pitch) zeminin altına girmemesi için minPitch - maxPitch arasında sınırlı açı
        if (allowVerticalTilt)
        {
            currentPitch -= mouseDelta.y * rotationSpeed;
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
        }
    }

    /// <summary>
    /// Mouse Scroll tekerleği ile kamera mesafesini (distance) yakınlaştırıp uzaklaştırır.
    /// </summary>
    private void HandleZoomInput()
    {
        if (!enableScrollZoom) return;

        float scrollDelta = GetScrollDelta();
        if (Mathf.Abs(scrollDelta) > 0.0001f)
        {
            distance = Mathf.Clamp(distance - scrollDelta * zoomSpeed, minDistance, maxDistance);
        }
    }

    /// <summary>
    /// Yatay dönüş (currentYaw), dikey açı (currentPitch), mesafe (distance) ve yükseklik (height)
    /// değerlerine göre kameranın bulunması gereken dünya pozisyonunu hesaplar.
    /// </summary>
    private Vector3 CalculateDesiredPosition()
    {
        Quaternion yawRotation = Quaternion.Euler(0f, currentYaw, 0f);
        Vector3 horizontalOffset = -(yawRotation * Vector3.forward * distance);

        // Dikey yüksekliği hem Height parametresine hem de Pitch sınırlarına (20°-60°) göre hesapla
        float effectiveHeight = height;
        if (allowVerticalTilt)
        {
            float baseTan = Mathf.Tan(Mathf.Max(10f, basePitch) * Mathf.Deg2Rad);
            float currentTan = Mathf.Tan(currentPitch * Mathf.Deg2Rad);
            effectiveHeight = height * (currentTan / baseTan);
        }

        // Kameranın hiçbir durumda zeminin altına düşmemesi için minimum yükseklik koruması
        float minAllowedHeight = Mathf.Sin(minPitch * Mathf.Deg2Rad) * distance;
        effectiveHeight = Mathf.Max(effectiveHeight, minAllowedHeight);

        return target.position + horizontalOffset + (Vector3.up * effectiveHeight);
    }

    /// <summary>
    /// Hem New Input System hem de Legacy Input Manager ile uyumlu Mouse X/Y hareketi okuma.
    /// </summary>
    private Vector2 GetMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            // New Input System piksel deltasını klasik Input.GetAxis ölçeğine (0.1f) normalize et
            return Mouse.current.delta.ReadValue() * 0.1f;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#else
        return Vector2.zero;
#endif
    }

    /// <summary>
    /// Hem New Input System hem de Legacy Input Manager ile uyumlu Sağ Tık kontrolü.
    /// </summary>
    private bool IsRightClickHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            return Mouse.current.rightButton.isPressed;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(1);
#else
        return false;
#endif
    }

    /// <summary>
    /// Hem New Input System hem de Legacy Input Manager ile uyumlu Mouse Scroll okuma.
    /// </summary>
    private float GetScrollDelta()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            float rawScroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(rawScroll) > 0.01f)
            {
                return Mathf.Sign(rawScroll) * 0.25f;
            }
            return 0f;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetAxis("Mouse ScrollWheel");
#else
        return 0f;
#endif
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
