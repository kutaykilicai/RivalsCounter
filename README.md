# Rivals Counter

Marvel Rivals maçında veya seçim ekranında ekran görüntüsünden takımları okuyan, karşı takıma göre counter öneren küçük bir Windows yardımcısı (C#, WPF, .NET 8, Türkçe arayüz).

Oyuna bağlanmaz, belleğini veya dosyalarını okumaz, enjeksiyon yapmaz. Sadece senin verdiğin ekran görüntüsü ve herkese açık istatistiklerle çalışır.

## Nasıl kullanılır

1. Uygulamayı aç (masaüstündeki kısayol).
2. Maçta veya seçimde **Ctrl+Shift+S** ile ekranı yakala, ya da görüntüyü **Ctrl+V** ile yapıştır / sürükle-bırak.
3. Algılanan düşman / takım / sen chip olarak gelir; yanlışsa arama kutusundan düzelt.
4. Solda counter önerileri (kısa neden + tier + team-up), sağda rol bazlı güncel meta (S/A).
5. Uyarılar: rol dengesi, aktif team-up, harita ipucu (görüntüde harita varsa), senin kahramanın bu kompozisyona uygun mu.

**Verileri güncelle** düğmesi rivalsmeta.com eşleşmelerini ve marvelrivals.gg tier listesini indirir (`%LOCALAPPDATA%\RivalsCounter\heroes.json`).

## Kısayollar

| Kısayol | İşlev |
|---|---|
| Ctrl+Shift+S | Ekranı yakala ve analiz et |
| Ctrl+V | Panodaki görüntüyü analiz et |
| Ctrl+Shift+R | Pencereyi göster / gizle |
| Ctrl+N | Yeni maç |
| F1 / F2 / F3 | Düşman / Takım / Ben ekleme modu |

API anahtarı: `%LOCALAPPDATA%\RivalsCounter\config.json` içinde `openRouterApiKey` veya `geminiApiKey` (repoya konmaz).

## Kurulum

Windows 10/11. Kaynaktan:

```powershell
dotnet publish -c Release -o publish
publish\RivalsCounter.exe
```

İkonlar: `Icons\<id>.png` veya `%LOCALAPPDATA%\RivalsCounter\Icons`.

## Veri / patch

Yerleşik veri Season 10.5 (2026-11-05). Tier: marvelrivals.gg Grandmaster+ (4 Kas 2026). Eşleşme: rivalsmeta.com. 5 Kasım 20261105 yaması denge değişikliği içermiyor.

## Uyarı

Resmi değildir. Marvel / NetEase ile bağı yoktur. MIT lisanslı kod.
