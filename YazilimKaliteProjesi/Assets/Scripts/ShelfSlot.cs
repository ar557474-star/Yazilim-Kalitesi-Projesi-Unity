using UnityEngine;

/// <summary>
/// Rafların (Shelf) her bir katına veya bölmesine yerleştirilen Trigger alanı.
/// Kutuların rafa nizami oturmasını, raftan geri alınmasını ve yeşil yerleştirme önizlemesini (Highlight) yönetir.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ShelfSlot : MonoBehaviour
{
    [Header("Slot Noktası ve Görsel Önizleme (Highlight)")]
    [Tooltip("Kargonun raf üzerinde tam olarak oturacağı Transform noktası (Boş bırakılırsa bu objenin kendi Transform'u kullanılır).")]
    [SerializeField] private Transform slotPoint;

    [Tooltip("Oyuncu elinde kutuyla yaklaştığında açılacak yarı şeffaf yeşil önizleme objesi (Cube / Quad).")]
    [SerializeField] private GameObject highlightVisual;

    [Header("Slot Durumu (Salt Okunur / Başlangıç Ataması)")]
    [Tooltip("Bu raf bölmesinde şu an kargo var mı?")]
    [SerializeField] private bool isOccupied = false;

    [Tooltip("Şu an bu slotta duran kargo kutusu (Debug / Başlangıç ataması için).")]
    [SerializeField] private GameObject currentBox;

    // Dolu mu kontrolü (Hem bool hem de referans doğrulaması yapar)
    public bool IsOccupied => isOccupied && currentBox != null;

    // Slotta duran mevcut kargo kutusu referansı
    public GameObject CurrentBox => currentBox;

    // Kargonun yerleşeceği hedef nokta
    public Transform SlotPoint => slotPoint != null ? slotPoint : transform;

    /// <summary>
    /// Bileşen ilk eklendiğinde Collider'ı otomatik olarak Trigger yapar.
    /// </summary>
    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    /// <summary>
    /// Oyun başladığında slotun kendi görünümünü gizler, yeşil önizlemeyi kapatır ve başlangıç kutusu varsa yerleştirir.
    /// </summary>
    private void Awake()
    {
        if (slotPoint == null)
        {
            slotPoint = transform;
        }

        // 1. Trigger Collider kontrolü
        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[{name}] ShelfSlot üzerindeki Collider 'Is Trigger' olarak işaretlenmemiş! Otomatik aktif edildi.");
            col.isTrigger = true;
        }

        // 2. Slot objesinin kendisinde bir MeshRenderer varsa oyun içinde görünmez yap (Trigger çalışmaya devam eder)
        if (highlightVisual != gameObject)
        {
            MeshRenderer ownRenderer = GetComponent<MeshRenderer>();
            if (ownRenderer != null)
            {
                ownRenderer.enabled = false;
            }
        }

        // 3. Yeşil önizleme göstergesini oyun başında kapalı (SetActive(false)) duruma getir
        SetHighlight(false);

        // 4. Eğer sahne başında slota bir kutu atanmışsa onu nizami oturt
        if (currentBox != null)
        {
            PlaceBox(currentBox);
        }
        else
        {
            isOccupied = false;
        }
    }

    /// <summary>
    /// Yeşil yerleştirme önizlemesini (highlightVisual) açar veya kapatır.
    /// Slot doluysa güvenlik amacıyla önizleme her zaman kapalı tutulur.
    /// </summary>
    /// <param name="isActive">Önizleme aktif edilsin mi?</param>
    public void SetHighlight(bool isActive)
    {
        // Slot doluysa yeşil önizleme kesinlikle gösterilmez
        bool shouldShow = isActive && !IsOccupied;

        if (highlightVisual != null)
        {
            // Eğer kullanıcı yanlışlıkla Slot objesinin kendisini atadıysa sadece MeshRenderer'ı aç/kapat (Collider kapanmasın)
            if (highlightVisual == gameObject)
            {
                MeshRenderer ownRenderer = GetComponent<MeshRenderer>();
                if (ownRenderer != null)
                {
                    ownRenderer.enabled = shouldShow;
                }
            }
            else if (highlightVisual.activeSelf != shouldShow)
            {
                highlightVisual.SetActive(shouldShow);
            }
        }
    }

    /// <summary>
    /// Kargo kutusunu bu raf slotuna nizami olarak yerleştirir ve yeşil önizlemeyi kapatır.
    /// </summary>
    /// <param name="box">Yerleştirilecek kutu objesi.</param>
    /// <returns>Yerleştirme başarılıysa true döner.</returns>
    public bool PlaceBox(GameObject box)
    {
        if (box == null || IsOccupied) return false;

        currentBox = box;
        isOccupied = true;

        // Slot dolduğu anda yeşil önizlemeyi otomatik kapat
        SetHighlight(false);

        // 1. Kutunun fiziğini kinematik yap (raftan düşmemesi ve kaymaması için)
        Rigidbody rb = box.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // 2. Kutunun Collider'ını aktif tut (OverlapSphere ile tekrar algılanabilmesi için)
        Collider col = box.GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = true;
        }

        // 3. Kutuyu slotPoint'e bağla ve local pozisyon/rotasyonunu sıfırla
        Transform targetPoint = SlotPoint;
        box.transform.SetParent(targetPoint);
        box.transform.localPosition = Vector3.zero;
        box.transform.localRotation = Quaternion.identity;

        // 4. Ürün Takip Sistemine kargonun rafa konulduğunu bildir
        if (CargoTrackerManager.Instance != null)
        {
            CargoTrackerManager.Instance.SetCargoState(box, CargoState.Rafta);
        }

        Debug.Log($"[RAFA KONULDU] '{box.name}', '{name}' raf bölmesine yerleştirildi.");
        return true;
    }

    /// <summary>
    /// Raftaki kutuyu geri alır ve slotu boş (isOccupied = false) durumuna getirir.
    /// </summary>
    /// <returns>Slottaki kutu objesini döner.</returns>
    public GameObject RemoveBox()
    {
        if (!IsOccupied) return null;

        GameObject boxToReturn = currentBox;
        ClearSlot();

        Debug.Log($"[RAFTAN ALINDI] '{boxToReturn.name}', '{name}' raf bölmesinden alındı.");
        return boxToReturn;
    }

    /// <summary>
    /// Kutu doğrudan dışarıdan alındığında slot durumunu sıfırlar.
    /// </summary>
    public void ClearSlot()
    {
        currentBox = null;
        isOccupied = false;
    }

    /// <summary>
    /// Scene penceresinde raf slotunun konumunu ve dolu/boş durumunu görselleştirir.
    /// </summary>
    private void OnDrawGizmos()
    {
        Transform point = slotPoint != null ? slotPoint : transform;
        Gizmos.color = (isOccupied && currentBox != null) ? new Color(1f, 0.3f, 0.3f, 0.8f) : new Color(0.2f, 1f, 0.4f, 0.8f);
        Gizmos.matrix = Matrix4x4.TRS(point.position, point.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.45f, 0.45f, 0.45f));
    }
}
