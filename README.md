# Hearthstone Hungary

Windows alkalmazás, ami a Hearthstone mellett fut, és feltölti a gyűjteményedet a
[Hearthstone Hungary](https://hearthstonehungary.hu) profilodba.

*Windows app that runs next to Hearthstone and uploads your collection to your
hearthstonehungary.hu profile. Primarily for Hungarian players.*

> A korábbi Hearthstone Deck Tracker plugin megszűnt: a Deck Tracker 2026. november 30-tól nem tölt be pluginokat.
> Az alkalmazáshoz nem kell a Deck Tracker.

## Telepítés

1. Töltsd le a telepítőt: **[HearthstoneHungary-Setup.exe](https://github.com/Phoenixy/Hearthstone_Card_Export/releases/latest/download/HearthstoneHungary-Setup.exe)**
2. Futtasd a fájlt, és kövesd a telepítő lépéseit (nem kér rendszergazdai jogot, a `%LocalAppData%\HearthstoneHungary` mappába települ, és a Start menüben is megjelenik).
   Az első indításkor a Windows figyelmeztethet („A Windows megvédte a számítógépet”), mert a program nincs digitálisan aláírva:
   *További információ → Futtatás mindenképp*.

*(Hordozható, telepítés nélküli változat is elérhető a Release-eknél: `HearthstoneHungary.zip`.)*

## Használat

1. Az alkalmazásban kattints az **„Összekötés a HSHU profiloddal”** gombra.
2. A böngészőben megnyíló oldalon (ha kell, jelentkezz be) ellenőrizd, hogy ugyanaz a kód látszik, mint az alkalmazásban, és kattints az **Összekötés** gombra.
3. Kész. Amíg fut a Hearthstone, az alkalmazás 2 percenként kiolvassa a gyűjteményt, és ha változott
   (csomagbontás, craftolás, új lapok), feltölti. A „Feltöltés most” gombbal kézzel is feltöltheted.

Az ablak bezárásakor a program a tálcán fut tovább; kilépni a tálcaikon menüjéből lehet. Alapból a Windows-szal együtt indul
(a tálcára), ez kikapcsolható. Egy profilhoz legfeljebb 5 gép köthető össze (pl. asztali gép és laptop); ezeket a profilod Gyűjtemény fülén
látod, és bármikor leválaszthatod. Az eszköz tokenje titkosítva, csak a te Windows felhasználódnak olvashatóan van elmentve. Beállítások és napló: `%AppData%\HearthstoneHungary`.

## Automatikus frissítés

Az alkalmazás 6 óránként megnézi, van-e új kiadás ebben a repóban (GitHub Releases). Ha van, letölti, ellenőrzi
(ez az alkalmazás-e, jó-e a verziószám, fut-e 64 biten), és kicseréli a fájlokat. A régieket `.old` végűre nevezi át,
mert a futó programot nem lehet felülírni. Az új verzió az újraindítás után fut: ha épp nem fut a Hearthstone és az ablak
nincs nyitva, magától újraindul. Kikapcsolható az ablakban.

---

## Fejlesztés

Felépítés:
- `src/HearthstoneHungary`: az alkalmazás (WPF, .NET Framework 4.8, AnyCPU).
- `external/unityspy`: a Hearthstone memóriaolvasó forrása (Firestone, MIT), változtatás nélkül. Lásd [external/unityspy/SOURCE.md](external/unityspy/SOURCE.md).
- `src/UnitySpy`, `src/UnitySpy.HearthstoneLib`: a memóriaolvasó projektfájljai.
- [docs/API.md](docs/API.md): a hearthstonehungary.hu API leírása.

Fordítás: a GitHub Actions minden pull requestnél lefordítja, a kész `HearthstoneHungary.zip` a futás oldalán,
az *Artifacts* résznél letölthető. Helyben (Windows, .NET SDK vagy Visual Studio 2022):

    dotnet build src\HearthstoneHungary\HearthstoneHungary.csproj

Fontos:
- **AnyCPU, Prefer32Bit nélkül.** A Hearthstone 64 bites, a memóriáját csak 64 bites folyamat tudja olvasni.
- Ha egy Hearthstone frissítés után nem olvasható a gyűjtemény, előbb a memóriaolvasót kell frissíteni:
  `powershell -ExecutionPolicy Bypass -File tools\update-unityspy.ps1` (a Firestone általában napokon belül javítja).
- A HSHU címek a `src/HearthstoneHungary/Core/HshuConfig.cs`-ben vannak.

## Új verzió kiadása

1. Emeld a verziót a `src/HearthstoneHungary/Properties/AssemblyInfo.cs`-ben (pl. `3.0.1.0`).
2. Írj egy `v3.0.1 - ...` bejegyzést a `CHANGELOG.md` elejére.
3. Commit + push a master ágra (vagy pull request, majd merge).

A GitHub Actions lefordítja, és létrehozza a `v3.0.1` Release-t a `HearthstoneHungary.zip`-pel és a CHANGELOG bejegyzéssel.
A felhasználóknál az alkalmazás 6 órán belül letölti.
