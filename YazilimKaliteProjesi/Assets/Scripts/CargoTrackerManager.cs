using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Her bir kargo kutusunun oyun içinde bulunabileceği durumları tanımlayan basit enum.
/// </summary>
public enum CargoState
{
    Rafta,
    Elde,
    Yerde,
    AracaYuklendi,
    MusteriyeTeslimEdildi
}

/// <summary>
/// Depodaki ve teslimattaki tüm kargoların durumunu anlık olarak takip eden merkezi yönetici sınıf.
/// Sahnedeki toplam kargo sayısını ve hangi durumda kaç adet kargo olduğunu otomatik hesaplar,
/// isteğe bağlı olarak ekrandaki UI Text (TextMeshPro veya standart Text) üzerinde ve konsolda gösterir.
/// </summary>
public class CargoTrackerManager : MonoBehaviour
{
    // Merkezi erişim için Singleton referansı
    private static CargoTrackerManager instance;
    public static CargoTrackerManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<CargoTrackerManager>();
            }
            return instance;
        }
    }

    [Header("Kargo Algılama Ayarı")]
    [Tooltip("Sahnedeki kargo kutularını başlangıçta bulmak için kullanılan Tag adı.")]
    [SerializeField] private string boxTag = "Box";

    [Header("Arayüz (UI) Referansları (İsteğe Bağlı)")]
    [Tooltip("Durum bilgisini ekranda göstermek için TextMeshPro (TextMeshProUGUI) bileşeni.")]
    [SerializeField] private TMP_Text statusTmpText;

    [Tooltip("Durum bilgisini ekranda göstermek için standart Unity UI.Text bileşeni.")]
    [SerializeField] private Text statusLegacyText;

    [Header("Konsol Çıktı Ayarı")]
    [Tooltip("Her durum değişiminde güncel kargo tablosunu Debug.Log ile konsola yazdırır.")]
    [SerializeField] private bool logToConsoleOnUpdate = true;

    [Header("Anlık Kargo Sayıları (Salt Okunur / Debug)")]
    [Tooltip("Sahnedeki toplam kargo sayısı.")]
    [SerializeField] private int totalCargoCount = 0;

    [Tooltip("Şu an raflarda (ShelfSlot) dizili olan kargo sayısı.")]
    [SerializeField] private int raftaCount = 0;

    [Tooltip("Şu an oyuncunun elinde taşıdığı kargo sayısı.")]
    [SerializeField] private int eldeCount = 0;

    [Tooltip("Şu an yerde serbest duran kargo sayısı.")]
    [SerializeField] private int yerdeCount = 0;

    [Tooltip("Kargo aracına yüklenmiş kargo sayısı.")]
    [SerializeField] private int aracaYuklendiCount = 0;

    [Tooltip("Müşteriye teslim edilmiş kargo sayısı.")]
    [SerializeField] private int teslimEdildiCount = 0;

    // Her kargo GameObject referansına göre anlık durumunu tutan sözlük.
    private readonly Dictionary<GameObject, CargoState> cargoStateMap = new Dictionary<GameObject, CargoState>();

    // Dışarıdan okunabilir özellikler (Properties)
    public int TotalCargoCount => totalCargoCount;
    public int RaftaCount => raftaCount;
    public int EldeCount => eldeCount;
    public int YerdeCount => yerdeCount;
    public int AracaYuklendiCount => aracaYuklendiCount;
    public int TeslimEdildiCount => teslimEdildiCount;

    /// <summary>
    /// Singleton örneğini başlatır (initialize eder).
    /// </summary>
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    /// <summary>
    /// Oyun başladığında sahnedeki tüm kargoları tarar ve başlangıç durumlarını kaydeder.
    /// </summary>
    private void Start()
    {
        ScanAllCargosInScene();
    }

    /// <summary>
    /// Sahnedeki tüm raf slotlarını ve kargo kutularını otomatik olarak bulup başlangıç durumlarını belirler.
    /// </summary>
    public void ScanAllCargosInScene()
    {
        // 1. Önce raflarda (ShelfSlot) bulunan kutuları "Rafta" olarak kaydet
        ShelfSlot[] allSlots = FindObjectsByType<ShelfSlot>();
        foreach (ShelfSlot slot in allSlots)
        {
            if (slot != null && slot.IsOccupied && slot.CurrentBox != null)
            {
                cargoStateMap[slot.CurrentBox] = CargoState.Rafta;
            }
        }

        // 2. Sahnedeki tüm Rigidbody'li kargo kutularını kontrol et
        Rigidbody[] allBodies = FindObjectsByType<Rigidbody>();
        foreach (Rigidbody rb in allBodies)
        {
            if (rb == null) continue;

            GameObject obj = rb.gameObject;
            bool isCargoBox = HasTagSafe(obj, boxTag) || obj.name.StartsWith("Box");
            if (!isCargoBox) continue;

            // Eğer henüz kaydedilmediyse, bir rafın altında mı yoksa yerde mi kontrol et
            if (!cargoStateMap.ContainsKey(obj))
            {
                ShelfSlot parentSlot = obj.GetComponentInParent<ShelfSlot>();
                if (parentSlot != null)
                {
                    cargoStateMap[obj] = CargoState.Rafta;
                }
                else
                {
                    cargoStateMap[obj] = CargoState.Yerde;
                }
            }
        }

        // Sayıları hesapla ve ekrana/konsola yansıt
        RecalculateCounts();
        UpdateDisplay();
    }

    /// <summary>
    /// Belirtilen kargo kutusunun durumunu günceller, sayaçları yeniden hesaplar ve UI/Konsol bilgisini yeniler.
    /// </summary>
    /// <param name="cargoBox">Durumu değişen kargo objesi.</param>
    /// <param name="newState">Kargonun yeni durumu (Rafta, Elde, Yerde, AracaYuklendi, MusteriyeTeslimEdildi).</param>
    public void SetCargoState(GameObject cargoBox, CargoState newState)
    {
        if (cargoBox == null) return;

        cargoStateMap[cargoBox] = newState;

        RecalculateCounts();
        UpdateDisplay();
    }

    /// <summary>
    /// Sözlükteki tüm kargoları tarayarak hangi durumda kaç adet kargo olduğunu sıfırdan hesaplar.
    /// </summary>
    private void RecalculateCounts()
    {
        raftaCount = 0;
        eldeCount = 0;
        yerdeCount = 0;
        aracaYuklendiCount = 0;
        teslimEdildiCount = 0;

        foreach (CargoState state in cargoStateMap.Values)
        {
            switch (state)
            {
                case CargoState.Rafta:
                    raftaCount++;
                    break;
                case CargoState.Elde:
                    eldeCount++;
                    break;
                case CargoState.Yerde:
                    yerdeCount++;
                    break;
                case CargoState.AracaYuklendi:
                    aracaYuklendiCount++;
                    break;
                case CargoState.MusteriyeTeslimEdildi:
                    teslimEdildiCount++;
                    break;
            }
        }

        totalCargoCount = cargoStateMap.Count;
    }

    /// <summary>
    /// Güncel kargo durum metnini alt alta oluşturur; UI Text atanmışsa ekranda gösterir, atanmamışsa veya konsol logu açıksa Debug.Log ile yazdırır.
    /// </summary>
    private void UpdateDisplay()
    {
        string summaryText =
            $"Toplam Kargo: {totalCargoCount}\n" +
            $"Rafta: {raftaCount}\n" +
            $"Elde: {eldeCount}\n" +
            $"Yerde: {yerdeCount}\n" +
            $"Araca Yüklendi: {aracaYuklendiCount}\n" +
            $"Teslim Edildi: {teslimEdildiCount}";

        bool hasUiAssigned = false;

        // TextMeshProUGUI atanmışsa metni güncelle
        if (statusTmpText != null)
        {
            statusTmpText.text = summaryText;
            hasUiAssigned = true;
        }

        // Standart UI.Text atanmışsa metni güncelle
        if (statusLegacyText != null)
        {
            statusLegacyText.text = summaryText;
            hasUiAssigned = true;
        }

        // UI atanmamışsa bile hata vermeden konsola yazdır (veya logToConsoleOnUpdate aktifse her zaman yazdır)
        if (!hasUiAssigned || logToConsoleOnUpdate)
        {
            Debug.Log($"[ÜRÜN TAKİP SİSTEMİ]\n{summaryText}");
        }
    }

    /// <summary>
    /// Tag tanımlı olmasa bile hata fırlatmadan güvenli Tag kontrolü yapar.
    /// </summary>
    private bool HasTagSafe(GameObject obj, string tagToCheck)
    {
        if (obj == null || string.IsNullOrEmpty(tagToCheck)) return false;
        return string.Equals(obj.tag, tagToCheck, System.StringComparison.Ordinal);
    }
}