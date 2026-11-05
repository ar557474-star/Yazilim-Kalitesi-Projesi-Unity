using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Oyuncunun kargo kutularını (Box) yerden veya raflardan (ShelfSlot) almasını, taşımasını,
/// boş raf slotlarına dizmesini, yere bırakmasını veya DeliveryZone (Müşteri / Kargo Aracı) alanlarına teslim etmesini yönetir.
/// Player objesine eklenir.
/// </summary>
public class CargoPickupDrop : MonoBehaviour
{
    [Header("Taşıma Noktası (Hold Point)")]
    [Tooltip("Karakterin önünde/elinde kargonun tutulacağı boş Transform objesi.")]
    [SerializeField] private Transform holdPoint;

    [Tooltip("Kutu yere bırakılırken karakterin ne kadar önüne bırakılacağı.")]
    [SerializeField] private Vector3 dropOffset = new Vector3(0f, 0.35f, 1.1f);

    [Header("Menzil ve Katman Kontrolleri (OverlapSphere)")]
    [Tooltip("Kutu alma, raf ve teslimat alanı algılama menzili (yarıçap).")]
    [SerializeField, Range(0.5f, 5f)] private float interactionRange = 1.8f;

    [Tooltip("Kargo kutularının bulunduğu Fizik Katmanı (LayerMask).")]
    [SerializeField] private LayerMask boxLayer = ~0;

    [Tooltip("Raf slotlarının (ShelfSlot) bulunduğu Fizik Katmanı (LayerMask).")]
    [SerializeField] private LayerMask shelfLayer = ~0;

    [Tooltip("Teslimat / Yükleme alanlarının bulunduğu Fizik Katmanı (LayerMask).")]
    [SerializeField] private LayerMask deliveryZoneLayer = ~0;

    [Header("Etiket (Tag) Ayarları")]
    [Tooltip("Kargo kutusu objelerinin Tag adı.")]
    [SerializeField] private string boxTag = "Box";

    [Tooltip("Müşteri teslim alanı Tag adı (DeliveryZone bileşeni yoksa fallback olarak kullanılır).")]
    [SerializeField] private string customerZoneTag = "CustomerZone";

    [Tooltip("Kargo aracı yükleme alanı Tag adları (DeliveryZone bileşeni yoksa fallback olarak kullanılır).")]
    [SerializeField] private string truckZoneTag = "TruckZone";
    [SerializeField] private string deliveryVanTag = "DeliveryVan";

    [Header("Tuş Ayarı")]
    [Tooltip("Kargo alma / rafa koyma / bırakma / teslim etme etkileşim tuşu.")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Header("Skor ve Sayaçlar (Salt Okunur / Debug)")]
    [SerializeField] private int deliveredToCustomerCount = 0;
    [SerializeField] private int loadedToTruckCount = 0;

    [Header("Arayüz (UI) Eventleri")]
    public UnityEvent<int> onCustomerDeliveryCountChanged;
    public UnityEvent<int> onTruckLoadCountChanged;

    // Şu an elde tutulan kutu referansı ve bileşenleri
    private GameObject heldBox;
    private Rigidbody heldBoxRb;
    private Collider heldBoxCollider;
    private Collider[] playerColliders;

    // Trigger üzerinden girilen aktif teslimat alanı
    private DeliveryZone activeTriggerZone;

    public bool IsHoldingBox => heldBox != null;
    public int DeliveredToCustomerCount => deliveredToCustomerCount;
    public int LoadedToTruckCount => loadedToTruckCount;

    private void Awake()
    {
        playerColliders = GetComponentsInChildren<Collider>();

        // Eğer holdPoint Inspector'dan atanmadıysa karakterin önünde otomatik bir HoldPoint oluştur
        if (holdPoint == null)
        {
            GameObject createdHoldPoint = new GameObject("HoldPoint");
            createdHoldPoint.transform.SetParent(transform);
            createdHoldPoint.transform.localPosition = new Vector3(0f, 0.8f, 0.75f);
            createdHoldPoint.transform.localRotation = Quaternion.identity;
            holdPoint = createdHoldPoint.transform;
            Debug.LogWarning("[CargoPickupDrop] 'holdPoint' atanmadığı için otomatik oluşturuldu.");
        }
    }

    private void Update()
    {
        if (IsInteractKeyPressed())
        {
            if (!IsHoldingBox)
            {
                TryPickupNearestBox();
            }
            else
            {
                TryPlaceDeliverOrDropBox();
            }
        }
    }

    /// <summary>
    /// Hem yeni Input System paketini hem de klasik Input Manager'ı destekleyen E tuşu kontrolü.
    /// </summary>
    private bool IsInteractKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(interactKey))
        {
            return true;
        }
#endif
        return false;
    }

    /// <summary>
    /// Karakterin eli boşken E tuşuna basıldığında:
    /// Menzildeki en yakın kutuyu (yerde veya dolu bir ShelfSlot üzerinde) bulur ve eline alır.
    /// </summary>
    private void TryPickupNearestBox()
    {
        // 1. Önce menzildeki tüm kutu collider'larını tara
        Collider[] hitColliders = Physics.OverlapSphere(
            transform.position,
            interactionRange,
            boxLayer,
            QueryTriggerInteraction.Ignore
        );

        GameObject nearestBox = null;
        float minDistanceSqr = float.MaxValue;

        foreach (Collider col in hitColliders)
        {
            if (col.transform.IsChildOf(transform)) continue;

            bool hasValidTag = HasTagSafe(col.gameObject, boxTag) || col.gameObject.name.StartsWith("Box");
            if (!hasValidTag) continue;

            if (col.attachedRigidbody == null && col.GetComponent<Rigidbody>() == null) continue;

            GameObject candidateBox = col.attachedRigidbody != null
                ? col.attachedRigidbody.gameObject
                : col.gameObject;

            float distSqr = (candidateBox.transform.position - transform.position).sqrMagnitude;
            if (distSqr < minDistanceSqr)
            {
                minDistanceSqr = distSqr;
                nearestBox = candidateBox;
            }
        }

        // 2. Ayrıca menzildeki dolu raf slotlarını (ShelfSlot) kontrol et
        // (Kutu collider'ı küçük veya rafın içindeyse doğrudan ShelfSlot trigger'ından da alınabilsin)
        ShelfSlot occupiedSlot = FindBestShelfSlot(requireOccupied: true);
        if (occupiedSlot != null && occupiedSlot.CurrentBox != null)
        {
            float slotDistSqr = (occupiedSlot.SlotPoint.position - transform.position).sqrMagnitude;
            if (slotDistSqr < minDistanceSqr)
            {
                nearestBox = occupiedSlot.CurrentBox;
            }
        }

        if (nearestBox != null)
        {
            // Eğer alınan kutu bir raf slotunun (ShelfSlot) içindeyse o slotu boşalt (isOccupied = false)
            ShelfSlot parentShelfSlot = nearestBox.GetComponentInParent<ShelfSlot>();
            if (parentShelfSlot != null)
            {
                parentShelfSlot.RemoveBox();
            }

            PickupBox(nearestBox);
        }
    }

    /// <summary>
    /// Seçilen kutuyu eldeki holdPoint'e sabitler, fiziğini kapatır ve çarpışmayı engeller.
    /// </summary>
    private void PickupBox(GameObject box)
    {
        heldBox = box;
        heldBoxRb = heldBox.GetComponent<Rigidbody>();
        heldBoxCollider = heldBox.GetComponent<Collider>();

        // 1. Fizik motoru etkisini devre dışı bırak
        if (heldBoxRb != null)
        {
            if (!heldBoxRb.isKinematic)
            {
                heldBoxRb.linearVelocity = Vector3.zero;
                heldBoxRb.angularVelocity = Vector3.zero;
            }
            heldBoxRb.isKinematic = true;
            heldBoxRb.useGravity = false;
        }

        // 2. Karakter ile kutu arasındaki collider çakışmalarını engelle
        if (heldBoxCollider != null)
        {
            SetIgnoreCollisionWithPlayer(heldBoxCollider, true);
            heldBoxCollider.enabled = false;
        }

        // 3. Kutuyu holdPoint pozisyonuna ve rotasyonuna bağla
        heldBox.transform.SetParent(holdPoint);
        heldBox.transform.localPosition = Vector3.zero;
        heldBox.transform.localRotation = Quaternion.identity;

        Debug.Log($"[KARGO ALINDI] '{heldBox.name}' alındı.");
    }

    /// <summary>
    /// Elde kutu varken E tuşuna basıldığında:
    /// 1) Menzilde ve karakterin önünde boş bir ShelfSlot (!isOccupied) varsa kutuyu rafa yerleştirir,
    /// 2) Yakında Müşteri Teslim Noktası veya Kargo Arabası varsa teslim eder / yükler,
    /// 3) Hiçbiri yoksa karakterin önüne yere düzgünce bırakır.
    /// </summary>
    private void TryPlaceDeliverOrDropBox()
    {
        if (heldBox == null) return;

        // 1. Öncelikle yakınlarda boş bir Raf Slotu (ShelfSlot) var mı kontrol et
        ShelfSlot emptyShelfSlot = FindBestShelfSlot(requireOccupied: false);
        if (emptyShelfSlot != null)
        {
            Collider boxCol = heldBoxCollider;
            GameObject boxToPlace = ReleaseHeldBoxReference();

            if (boxCol != null)
            {
                boxCol.enabled = true;
                SetIgnoreCollisionWithPlayer(boxCol, false);
            }

            emptyShelfSlot.PlaceBox(boxToPlace);
            return;
        }

        // 2. Teslimat veya Araç Yükleme Alanı (DeliveryZone) kontrolü
        DeliveryZone targetZone = FindNearbyDeliveryZone(out Transform fallbackZoneTransform, out bool isFallbackCustomer, out bool isFallbackTruck);

        if (targetZone != null)
        {
            GameObject boxToProcess = ReleaseHeldBoxReference();

            if (targetZone.Type == DeliveryZone.ZoneType.Customer)
            {
                targetZone.ProcessCargo(boxToProcess);
                deliveredToCustomerCount++;
                onCustomerDeliveryCountChanged?.Invoke(deliveredToCustomerCount);
                Debug.Log($"[SKOR] Toplam Müşteri Teslimatı: {deliveredToCustomerCount}");
                return;
            }
            else if (targetZone.Type == DeliveryZone.ZoneType.Truck)
            {
                targetZone.ProcessCargo(boxToProcess);
                loadedToTruckCount++;
                onTruckLoadCountChanged?.Invoke(loadedToTruckCount);
                Debug.Log($"[YÜKLEME] Toplam Araca Yüklenen Kargo: {loadedToTruckCount}");
                return;
            }
        }

        // Tag ile tanımlanmış fallback alan kontrolü
        if (fallbackZoneTransform != null)
        {
            GameObject boxToProcess = ReleaseHeldBoxReference();

            if (isFallbackCustomer)
            {
                deliveredToCustomerCount++;
                onCustomerDeliveryCountChanged?.Invoke(deliveredToCustomerCount);
                Debug.Log($"[MÜŞTERİ TESLİMATI] Kargo müşteriye teslim edildi! Toplam Teslimat: {deliveredToCustomerCount}");
                Destroy(boxToProcess);
                return;
            }
            else if (isFallbackTruck)
            {
                loadedToTruckCount++;
                onTruckLoadCountChanged?.Invoke(loadedToTruckCount);

                boxToProcess.transform.SetParent(fallbackZoneTransform);
                boxToProcess.transform.localPosition = new Vector3(0f, 0.45f * (loadedToTruckCount - 1), 0f);
                boxToProcess.transform.localRotation = Quaternion.identity;
                boxToProcess.tag = "Untagged";

                Debug.Log($"[ARAÇ YÜKLEME] Kargo araca sabitlendi! Toplam Yüklenen: {loadedToTruckCount}");
                return;
            }
        }

        // 3. Raf veya özel bir alan yoksa kutuyu karakterin önüne yere bırak
        DropBoxToGround();
    }

    /// <summary>
    /// Menzildeki (Physics.OverlapSphere) raf slotlarını tarar ve karakterin baktığı yöne / konumuna
    /// en yakın uygun ShelfSlot'u döndürür.
    /// </summary>
    /// <param name="requireOccupied">True ise dolu slotları, False ise boş slotları arar.</param>
    private ShelfSlot FindBestShelfSlot(bool requireOccupied)
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            interactionRange,
            shelfLayer,
            QueryTriggerInteraction.Collide
        );

        ShelfSlot bestSlot = null;
        float bestScore = float.MaxValue;

        foreach (Collider col in hits)
        {
            ShelfSlot slot = col.GetComponent<ShelfSlot>();
            if (slot == null)
            {
                slot = col.GetComponentInParent<ShelfSlot>();
            }

            if (slot == null) continue;
            if (slot.IsOccupied != requireOccupied) continue;

            Vector3 toSlot = slot.SlotPoint.position - transform.position;
            Vector3 toSlotFlat = new Vector3(toSlot.x, 0f, toSlot.z);

            // Karakterin arkasında kalan slotları ele (sadece ön/yan cephedeki raf bölmelerini seç)
            if (toSlotFlat.sqrMagnitude > 0.01f)
            {
                float dot = Vector3.Dot(transform.forward, toSlotFlat.normalized);
                if (dot < -0.15f) continue;
            }

            float distSqr = toSlot.sqrMagnitude;
            if (distSqr < bestScore)
            {
                bestScore = distSqr;
                bestSlot = slot;
            }
        }

        return bestSlot;
    }

    /// <summary>
    /// Oyuncunun içinde bulunduğu veya menzilindeki (Physics.OverlapSphere) teslimat/yükleme alanını bulur.
    /// </summary>
    private DeliveryZone FindNearbyDeliveryZone(out Transform fallbackTransform, out bool isCustomer, out bool isTruck)
    {
        fallbackTransform = null;
        isCustomer = false;
        isTruck = false;

        if (activeTriggerZone != null)
        {
            return activeTriggerZone;
        }

        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            interactionRange,
            deliveryZoneLayer,
            QueryTriggerInteraction.Collide
        );

        foreach (Collider col in hits)
        {
            DeliveryZone zone = col.GetComponentInParent<DeliveryZone>();
            if (zone != null)
            {
                return zone;
            }

            if (HasTagSafe(col.gameObject, customerZoneTag))
            {
                fallbackTransform = col.transform;
                isCustomer = true;
                return null;
            }

            if (HasTagSafe(col.gameObject, truckZoneTag) || HasTagSafe(col.gameObject, deliveryVanTag))
            {
                fallbackTransform = col.transform;
                isTruck = true;
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Kutuyu karakterin önüne yere düzgünce bırakır ve fiziklerini tekrar aktif eder.
    /// </summary>
    private void DropBoxToGround()
    {
        GameObject boxToDrop = heldBox;
        Rigidbody rb = heldBoxRb;
        Collider col = heldBoxCollider;

        ReleaseHeldBoxReference();

        boxToDrop.transform.SetParent(null);
        Vector3 targetDropPos = transform.position + transform.TransformDirection(dropOffset);
        boxToDrop.transform.position = targetDropPos;
        boxToDrop.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        if (col != null)
        {
            col.enabled = true;
            SetIgnoreCollisionWithPlayer(col, false);
        }

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Debug.Log($"[KARGO BIRAKILDI] '{boxToDrop.name}' yere bırakıldı.");
    }

    private GameObject ReleaseHeldBoxReference()
    {
        GameObject box = heldBox;
        heldBox = null;
        heldBoxRb = null;
        heldBoxCollider = null;
        return box;
    }

    private void SetIgnoreCollisionWithPlayer(Collider boxCol, bool ignore)
    {
        if (playerColliders == null || boxCol == null) return;

        foreach (Collider playerCol in playerColliders)
        {
            if (playerCol != null && playerCol != boxCol)
            {
                Physics.IgnoreCollision(playerCol, boxCol, ignore);
            }
        }
    }

    private bool HasTagSafe(GameObject obj, string tagToCheck)
    {
        if (obj == null || string.IsNullOrEmpty(tagToCheck)) return false;
        return string.Equals(obj.tag, tagToCheck, System.StringComparison.Ordinal);
    }

    public void SetCurrentZone(DeliveryZone zone)
    {
        activeTriggerZone = zone;
    }

    public void ClearCurrentZone(DeliveryZone zone)
    {
        if (activeTriggerZone == zone)
        {
            activeTriggerZone = null;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRange);

        if (holdPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(holdPoint.position, 0.2f);
        }
    }
}
