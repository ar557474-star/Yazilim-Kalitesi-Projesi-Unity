using System;
using UnityEngine;
using UnityEngine.Events;
///ahmed
/// <summary>
/// Müşteri teslimat noktası (Customer) veya Kargo Aracı yükleme alanı (Truck) bileşeni.
/// Trigger Collider içeren bölge objelerine eklenir.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DeliveryZone : MonoBehaviour
{
    public enum ZoneType
    {
        Customer,
        Truck
    }

    [Header("Alan Ayarları")]
    [Tooltip("Alanın tipi: Müşteri Teslim Noktası (Customer) veya Kargo Aracı (Truck).")]
    [SerializeField] private ZoneType zoneType = ZoneType.Customer;

    [Header("Araç Kasa Ayarları (Sadece Truck İçin)")]
    [Tooltip("Kargonun araç kasasında sabitleneceği referans nokta (boş bırakılırsa bu objenin Transform'u kullanılır).")]
    [SerializeField] private Transform truckCargoAnchor;

    [Tooltip("Araç kasasında kutuların dizileceği hazır slot noktaları (opsiyonel).")]
    [SerializeField] private Transform[] truckCargoSlots;

    [Tooltip("Slot atanmamışsa her yeni kutunun kasada üst üste/yan yana dizilme mesafesi.")]
    [SerializeField] private Vector3 stackOffset = new Vector3(0f, 0.45f, 0f);

    [Header("Arayüz (UI) ve Oyun Olayları (Events)")]
    [Tooltip("Kargo teslim edildiğinde veya yüklendiğinde tetiklenir (Parametre: Güncel toplam sayı).")]
    public UnityEvent<int> onCargoProcessed;

    [Tooltip("Müşteri veya araç işlem mesajını UI Text bileşenine aktarmak için tetiklenir.")]
    public UnityEvent<string> onStatusMessageChanged;

    // Kod üzerinden dinlenebilecek C# event'i (Alan Tipi, Güncel Sayı, Kutu Objesi)
    public event Action<ZoneType, int, GameObject> OnZoneActionCompleted;

    private int processedCount = 0;

    public ZoneType Type => zoneType;
    public int ProcessedCount => processedCount;

    private void Reset()
    {
        // Script eklendiğinde Collider'ı otomatik olarak Trigger yap
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[{name}] DeliveryZone üzerindeki Collider 'Is Trigger' olarak işaretlenmemiş! Otomatik olarak aktif edildi.");
            col.isTrigger = true;
        }
    }

    /// <summary>
    /// Oyuncu bu alana bir kutu teslim ettiğinde veya yüklediğinde CargoPickupDrop tarafından çağrılır.
    /// </summary>
    /// <param name="box">Teslim edilen / yüklenen kargo kutusu.</param>
    /// <returns>İşlem başarılıysa true döner.</returns>
    public bool ProcessCargo(GameObject box)
    {
        if (box == null) return false;

        processedCount++;

        if (zoneType == ZoneType.Customer)
        {
            string message = $"[MÜŞTERİ TESLİMATI] Kargo müşteriye teslim edildi! Bu noktadaki toplam teslimat: {processedCount}";
            Debug.Log(message);

            onCargoProcessed?.Invoke(processedCount);
            onStatusMessageChanged?.Invoke(message);
            OnZoneActionCompleted?.Invoke(zoneType, processedCount, box);

            // Kutuyu teslim edilmiş say ve yok et
            Destroy(box);
            return true;
        }
        else if (zoneType == ZoneType.Truck)
        {
            AttachBoxToTruck(box);

            string message = $"[ARAÇ YÜKLEME] Kargo araca yüklendi! Araçtaki toplam kargo: {processedCount}";
            Debug.Log(message);

            onCargoProcessed?.Invoke(processedCount);
            onStatusMessageChanged?.Invoke(message);
            OnZoneActionCompleted?.Invoke(zoneType, processedCount, box);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Kutuyu kargo aracının kasasına sabitler ve fiziğini kapatır.
    /// </summary>
    private void AttachBoxToTruck(GameObject box)
    {
        // Fizik ve çarpışmaları devre dışı bırak (araç içinde savrulmasını önler)
        Rigidbody rb = box.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
        }

        Collider boxCol = box.GetComponent<Collider>();
        if (boxCol != null)
        {
            boxCol.enabled = false;
        }

        // Tekrar yerden alınmasını engellemek için Tag ve Layer'ını sıfırla
        box.tag = "Untagged";
        box.layer = 0; // Default layer

        // Kasada sabitleneceği noktayı belirle
        int index = processedCount - 1;
        if (truckCargoSlots != null && index < truckCargoSlots.Length && truckCargoSlots[index] != null)
        {
            Transform slot = truckCargoSlots[index];
            box.transform.SetParent(slot);
            box.transform.localPosition = Vector3.zero;
            box.transform.localRotation = Quaternion.identity;
        }
        else
        {
            Transform parentAnchor = truckCargoAnchor != null ? truckCargoAnchor : transform;
            box.transform.SetParent(parentAnchor);
            box.transform.localPosition = stackOffset * index;
            box.transform.localRotation = Quaternion.identity;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        CargoPickupDrop playerCargo = other.GetComponentInParent<CargoPickupDrop>();
        if (playerCargo != null)
        {
            playerCargo.SetCurrentZone(this);

            if (playerCargo.IsHoldingBox)
            {
                string hint = zoneType == ZoneType.Customer
                    ? "Müşteri teslimat alanındasınız. Teslim etmek için [E] tuşuna basın."
                    : "Kargo aracı yükleme alanındasınız. Araca yüklemek için [E] tuşuna basın.";
                Debug.Log(hint);
                onStatusMessageChanged?.Invoke(hint);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        CargoPickupDrop playerCargo = other.GetComponentInParent<CargoPickupDrop>();
        if (playerCargo != null)
        {
            playerCargo.ClearCurrentZone(this);
        }
    }
}
