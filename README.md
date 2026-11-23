# Hearthstone Hungary – gyűjtemény plugin

Hearthstone Deck Tracker (HDT) plugin, ami feltölti a Hearthstone gyűjteményedet a
[Hearthstone Hungary](https://hearthstonehungary.hu) profilodba, vagy CSV fájlba exportálja.

*Hearthstone Deck Tracker plugin that uploads your Hearthstone collection to your
hearthstonehungary.hu profile (or exports it to CSV). Primarily for Hungarian players.*

## Telepítés

1. Telepítsd a [Hearthstone Deck Trackert](https://hsreplay.net/downloads/).
2. Töltsd le a plugint: **[exportalas.zip](https://github.com/Phoenixy/Hearthstone_Card_Export/releases/latest/download/exportalas.zip)**
3. HDT: *Options → Tracker → Plugins*, és húzd rá a zip fájlt (vagy csomagold ki a `%AppData%\HearthstoneDeckTracker\Plugins\exportalas` mappába).
4. Kapcsold be a „Gyűjtemény exportálás” plugint.

Ezt csak egyszer kell megcsinálni: a 2.0.0-s verziótól a plugin magától frissül.
Ha a régi (1.x) verzió fent van, előtte töröld a `Plugins\exportalas` mappát.

## Használat

A HDT *Plugins* menüjében nyisd meg a „Gyűjtemény exportálás” ablakot.

**Automatikus feltöltés (ajánlott)**

1. A Hearthstone Hungary profilodban hozz létre egy plugin tokent.
2. Másold be a pluginba, és kattints az „Összekapcsolás” gombra.
3. Kész. Ha változik a gyűjteményed (csomagbontás, craftolás, új lapok), a plugin magától feltölti.
   A „Feltöltés most” gombbal bármikor kézzel is feltöltheted.

A token titkosítva, csak a te Windows felhasználódnak olvashatóan van elmentve. A profilodban bármikor visszavonhatod.

**Exportálás CSV fájlba**

Ha nem szeretnéd összekapcsolni a profilodat: indítsd el a Hearthstone-t, kattints az
„Exportálás CSV fájlba” gombra, és a kapott fájlt töltsd fel a Hearthstone Hungary-n a profilod gyűjtemény oldalán.

## Automatikus frissítés

A plugin 6 óránként megnézi, van-e új kiadás ebben a repóban (GitHub Releases). Ha van, letölti,
ellenőrzi (ez a plugin-e, jó-e a verziószám, betölthető-e a 64 bites HDT-ben), és a helyére teszi.
A HDT következő indításakor már az új verzió fut. Kikapcsolható a plugin ablakában.

---

## Fejlesztés

Kell hozzá: Visual Studio 2022 (.NET asztali fejlesztés) vagy a .NET SDK, és egy telepített HDT.

1. A HDT DLL-jeit másold a `lib\` mappába (ez nincs a repóban):
   ```
   powershell -ExecutionPolicy Bypass -File tools\copy-hdt-libs.ps1
   ```
2. Nyisd meg az `exportalas.sln`-t, vagy parancssorból: `dotnet build exportalas\export.csproj`
3. Kipróbáláshoz: `dotnet build exportalas\export.csproj -p:DeployToHdt=true` a HDT plugin mappájába másolja a DLL-t
   (utána indítsd újra a HDT-t).

Fontos:
- A plugin **AnyCPU**-ra fordul. A HDT 1.53.0 óta 64 bites, egy x86-os DLL be sem töltődik.
- A gyűjteményt a HDT `CollectionHelpers` osztályán keresztül olvassuk, nem közvetlenül a HearthMirrorral,
  így a Hearthstone változásait a HDT fejlesztői követik le.
- A HSHU címek az `exportalas/Internal/HshuConfig.cs`-ben vannak. A szerver oldali API leírása: [docs/API.md](docs/API.md).

## Új verzió kiadása

1. Írd át a verziót az `exportalas/Properties/AssemblyInfo.cs`-ben (pl. `2.0.1.0`), és írd be a `CHANGELOG.md`-be.
2. Commit + push.
3. Futtasd: `powershell -ExecutionPolicy Bypass -File tools\release.ps1`

   Ez Release módban fordít, elkészíti a `dist\exportalas.zip`-et, és létrehozza a GitHub Release-t
   (`v2.0.1` tag, a csatolt zippel, a CHANGELOG bejegyzéssel). Ehhez kell a [GitHub CLI](https://cli.github.com/) (`gh auth login`).

   Kézzel is mehet: `dotnet build exportalas\export.csproj -c Release`, majd a GitHubon *Releases → Draft a new release*,
   tag: `v2.0.1`, és csatold a `dist\exportalas.zip`-et.

A tagnek egyeznie kell az AssemblyInfo verziójával, és a zipben `exportalas.dll`-nek kell lennie,
különben a felhasználóknál az automatikus frissítés elutasítja.
