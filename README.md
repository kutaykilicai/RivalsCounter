# Rivals Counter

Marvel Rivals'ta kahraman seçimi sırasında yanda açık tuttuğun küçük bir counter önerici (C#, WPF, .NET 8, Türkçe arayüz). Düşman takımını ve kendi takımını girersin, rol bazında hangi kahramanın daha iyi eşleştiğini nedeniyle birlikte gösterir.

Oyuna bağlanmaz, oyunun belleğini ya da dosyalarını okumaz, girdiyi otomatikleştirmez. Sadece senin girdiğin seçimlerle ve herkese açık istatistiklerle hesap yapar.

## Nasıl çalışır

- Düşman takımı (6 kişi), kendi kahramanın ve takım arkadaşların seçilir. Zorlandığın düşmana sağ tıklarsan ağırlığı 3 katına çıkar.
- Öneriler role göre ayrılır: Öncü, Düellocu, Stratejist. Her önerinin yanında nedeni ve kaynağı yazar.
- Puan = Σ (düşmana karşı kazanma oranı − 50) × ağırlık + tier bonusu (S +1,5, A +0,8, C −0,8) + aktif team-up için +1. En az 1500 maçlık eşleşmeler hesaba katılır.
- Meta ekranı (F4): rol filtreli tier listesi, kazanma ve seçilme oranlarıyla.
- "Verileri güncelle" düğmesi güncel eşleşme verisini rivalsmeta.com'dan indirir ve `%LOCALAPPDATA%\RivalsCounter\heroes.json` dosyasına yazar.

## Kısayollar

F1/F2/F3 mod değiştirir (düşman, takımım, ben). İsim yazıp Enter ekler, Shift+Enter düşmana "zorlandığım" olarak ekler. Backspace son ekleneni siler, Ctrl+N yeni maç açar, F4 meta ekranına geçer. Ctrl+Shift+R pencereyi her yerden gösterir ya da gizler.

## Kahraman ikonları

Depoda ve hazır sürümde oyunun görselleri yoktur. Kahramanlar rol renginde, baş harfli kartlarla ve isimleriyle gösterilir. İkon görmek istersen kendi bulduğun PNG dosyalarını kahraman kimliğiyle (`1011.png` gibi, kimlikler `Data/heroes.json` içinde) `%LOCALAPPDATA%\RivalsCounter\Icons` klasörüne koy; uygulama açılışta bunları kullanır.

## Kurulum

Gerekenler: Windows 10 veya 11, [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

**Hazır sürüm:** [Releases](https://github.com/kutaykilicai/RivalsCounter/releases) sayfasından zip'i indir, bir klasöre aç ve `RivalsCounter.exe`'yi çalıştır.

**Kaynaktan derleme** (.NET 8 SDK gerekir):

```powershell
git clone https://github.com/kutaykilicai/RivalsCounter.git
cd RivalsCounter
dotnet publish -c Release -o app
app\RivalsCounter.exe
```

Veriyi baştan üretmek için `tools/build.py` (Python 3) kullanılabilir.

## Uyarı

Resmi değildir. Marvel, NetEase Games ya da Marvel Rivals ile bağı yoktur, onlar tarafından onaylanmamıştır. Marvel Rivals ve kahraman adları sahiplerinin ticari markalarıdır. İstatistikler rivalsmeta.com ve marvelrivals.gg'den alınır; doğrulukları o sitelere bağlıdır.

## Lisans

Kod MIT lisanslıdır, ayrıntı için `LICENSE` dosyasına bak. `Data/heroes.json` içindeki kahraman adları ve yetenek açıklamaları oyunun sahiplerine aittir.

## English

Rivals Counter is a small Windows helper (WPF, .NET 8, Turkish UI) that suggests counter picks during Marvel Rivals hero select. You enter the enemy team and your own team; it ranks heroes per role using public head-to-head win rates (rivalsmeta.com) and the current tier list. It does not hook into the game, read its memory or files, or automate input.

- **Install:** download the zip from Releases and run `RivalsCounter.exe` (needs the .NET 8 Desktop Runtime), or build with `dotnet publish -c Release -o app`.
- **Icons:** no game art is bundled. Heroes are shown as role-coloured initials; you can drop your own `<id>.png` files into `%LOCALAPPDATA%\RivalsCounter\Icons`.
- **Disclaimer:** unofficial; not affiliated with or endorsed by Marvel or NetEase Games.
- **License:** MIT for the code.
