# Hearthstone Hungary API (az alkalmazáshoz)

Az alkalmazás (3.0.0-tól) ezt az API-t hívja. A címek a `src/HearthstoneHungary/Core/HshuConfig.cs` fájlban
vannak; ha valamelyik megváltozik, ott írd át, és adj ki új verziót.

| Mi | Cím |
|---|---|
| Gyűjtemény (feltöltés, token ellenőrzése) | `https://hearthstonehungary.hu/api/app/collection.php` |
| Összekötés, leválasztás | `https://hearthstonehungary.hu/api/app/link.php` |
| Pakli küldése az oldalra | `https://hearthstonehungary.hu/api/app/deck.php` |
| Legfrissebb hírek (értesítéshez) | `https://hearthstonehungary.hu/api/app/news.php` |
| Jóváhagyó oldal (a böngészőben) | `https://hearthstonehungary.hu/app/connect/<kód>` |
| Leírás / összekötött eszközök | `https://hearthstonehungary.hu/app` (belépve a profil Gyűjtemény fülére visz: `/profile/<név>/collection#app`) |

## Összekötés és eszközök

Az összekötés a TV-s és konzolos alkalmazásokból ismert „eszközkódos” módszerrel megy (mint az OAuth device flow),
jelszó és token másolása nélkül:

1. Az alkalmazás `start` kérést küld (`device_id`, gépnév, verzió). A szerver ad egy titkos `device_code`-ot
   (csak az alkalmazás ismeri) és egy rövid, felhasználónak szóló kódot (`KX7P-29QD`, 10 percig érvényes).
2. Az alkalmazás megnyitja a böngészőben a `verify_url`-t (`/app/connect/KX7P-29QD`), és kiírja a kódot.
3. A felhasználó (belépve) az oldalon látja a gép nevét és a kódot, és az **Összekötés** gombbal jóváhagyja.
   Az oldal figyelmeztet, hogy csak akkor hagyja jóvá, ha az alkalmazásban is ez a kód látszik (így egy idegen által
   küldött link nem köti össze más gépét a profiljával).
4. Az alkalmazás közben `interval` másodpercenként `poll` kérést küld; a jóváhagyás utáni első válaszban megkapja
   a tokent (egyszer: a következő lekérdezés már `expired`).

Eszközök:

- Minden eszköz (telepítés) saját tokent kap. A `device_id` az első indításkor létrejövő véletlen azonosító
  (`settings.xml`); ugyanennek az eszköznek az újbóli összekötése a régi tokenjét visszavonja, nem számít új eszköznek.
- Egy profilhoz legfeljebb **5** eszköz köthető. A profil Gyűjtemény fülén látszik a gép neve, az utolsó feltöltés,
  a lapok száma, a BattleTag és a verzió, és eszközönként leválaszthatók.
- Felhasználónként egy gyűjtemény van: ha az eszközök különböző Battle.net fiókot töltenek fel, mindig a legutóbbi
  feltöltés látszik (erre a Gyűjtemény fül figyelmeztet).

## Token

- Generálás: `bin2hex(random_bytes(32))` (64 karakter). Az alkalmazás csak `A-Z a-z 0-9 _ - .` karaktereket és 20–200 hosszt fogad el.
- Az adatbázisba csak a hash kerül (`hash('sha256', $token)`), a nyers tokent csak az alkalmazás kapja meg, egyszer.
- Táblák (a fő adatbázisban): `jsweb_app_tokens` (eszközönként egy sor, `device_id`, `device_name`),
  `jsweb_app_link_requests` (összekötési kérések; egy napnál régebbieket a következő `start` törli).

### Miért nem `Authorization` fejléc?

A token az **`X-HSHU-Token`** fejlécben érkezik. PHP-ban: `$_SERVER['HTTP_X_HSHU_TOKEN']`.
Az `Authorization: Bearer ...` fejlécet az Apache + `proxy_fcgi` (PHP-FPM) alapból nem adja át a PHP-nak,
egy saját fejléc viszont gond nélkül átmegy.

## Kérések

Minden válasz JSON (`Content-Type: application/json; charset=utf-8`), és mindig van benne `ok` (true/false).
A `message` mezőt (magyarul) az alkalmazás kiírja a felhasználónak.

### Összekötés (`/api/app/link.php`)

Mindegyik `POST`, JSON törzzsel.

**start**

```json
{ "action": "start", "device_id": "0123456789abcdef0123456789abcdef", "device_name": "ASZTALI-PC", "app_version": "3.2.0" }
```

```json
{ "ok": true, "device_code": "4afe…(64 hexa)", "user_code": "KX7P-29QD",
  "verify_url": "https://hearthstonehungary.hu/app/connect/KX7P-29QD", "interval": 3, "expires_in": 600 }
```

Egy IP 10 percen belül legfeljebb 10 összekötést indíthat (utána 429).

**poll**

```json
{ "action": "poll", "device_code": "4afe…" }
```

| HTTP | Válasz | Az alkalmazás |
|---|---|---|
| 200 | `{"ok":true,"status":"pending"}` | vár, és újra kérdez |
| 200 | `{"ok":true,"status":"approved","token":"…","user":"PHOENIX"}` | elmenti a tokent |
| 403 | `denied` (elutasította) | kiírja a `message`-et, vége |
| 409 | `limit` (már 5 eszköz) | kiírja a `message`-et, vége |
| 410 | `expired` (lejárt, vagy a tokent már kiadtuk) | kiírja a `message`-et, vége |
| 429, 5xx, hálózati hiba | | a következő körben újrapróbálja |

**unlink** (`X-HSHU-Token` fejléccel): ennek az eszköznek a tokenje érvényét veszti. Mindig `{"ok":true}`, akkor is,
ha a token már nem élt.

### GET – token ellenőrzése

Az alkalmazás indításkor, az ablak megnyitásakor (legfeljebb percenként) és óránként hívja, hogy a profilban
leválasztott eszközt akkor is észrevegye, ha épp nem tölt fel. 401-re leválasztja magát; hálózati / szerverhibánál nem csinál semmit.

```
GET /api/app/collection.php
X-HSHU-Token: 3f9a...
```

```json
{ "ok": true, "user": "PHOENIX" }
```

### Pakli küldése (`/api/app/deck.php`)

Az alkalmazás a játék `Decks.log` naplójából olvassa ki a paklik nevét és kódját (lásd `Core/DeckTracker.cs`), és így küldi az oldalra:

```
POST /api/app/deck.php
X-HSHU-Token: 3f9a...
```

```json
{ "action": "ticket", "mode": "instant", "deckstring": "AAECAf0EBu0F…", "name": "Pakli neve" }
```

```json
{ "ok": true, "url": "https://hearthstonehungary.hu/deck-import?ticket=…" }
```

- `mode`: `instant` = a pakli azonnal létrejön a megadott névvel; `builder` = a pakli készítő nyílik meg a lapokkal.
- Az alkalmazás a kapott címet megnyitja a böngészőben. A pakli importáló oldal a jegy alapján kitölti az űrlapot, és magától
  elindítja ugyanazt, amit a kézi „Létrehozás” / „Lapok összeállítása” gomb (így a kiforgott lapok cseréje, a tiltott lapok stb. kezelése közös).
- A jegy egyszer használható, 10 percig érvényes, és csak a tulajdonosának indul el magától (más fióknál vagy belépés nélkül az oldal
  csak üzenetet ír ki), így egy idegen link nem tud a felhasználó nevében paklit létrehozni. Óránként legfeljebb 30 jegy kérhető.
- Hibák: 400 `bad_deck` (nem pakli kód), 401 (leválasztott eszköz), 403 `no_access`, 429 `rate_limited`.

### Meccsek feltöltése (`/api/app/games.php`)

Az alkalmazás a játék `Power.log` naplójából olvassa ki a befejezett meccseket (lásd `Core/PowerLogParser.cs`, `Core/GameTracker.cs`),
a paklit pedig a `Decks.log`-ból (a meccs előtti utolsó „Finding Game With Deck” / „Starting Arena Game With Deck” blokk).

```
POST /api/app/games.php
X-HSHU-Token: 3f9a...
```

```json
{ "format": 1, "app_version": "3.5.0", "games": [ {
    "uid": "86f7…(40 hexa)", "played_at": "2026-11-24T15:13:34Z", "duration": 459,
    "game_type": "GT_RANKED", "format_type": "FT_STANDARD", "result": "loss", "first": false,
    "player_hero": "HERO_05", "opponent_hero": "HERO_01u",
    "deck_id": 2777523599, "deck_name": "Pakli neve", "deckstring": "AAECAR8E…", "build": 255790 } ] }
```

```json
{ "ok": true, "saved": 1, "duplicates": 0, "skipped": 0 }
```

- `uid`: a naplómappa neve + a meccs kezdésének időbélyege (SHA-1). Egy meccs felhasználónként csak egyszer kerül be (`duplicates`),
  ezért az alkalmazás újraküldi, amire nem kapott választ.
- `game_type` / `format_type`: ahogy a napló kiírja. Csak a `GT_RANKED`, a `GT_CASUAL` és az aréna (`…ARENA…`, az Underground külön mód)
  számít; a többi kimarad (`skipped`). Az alkalmazás ezeket el sem küldi.
- `result`: `win` / `loss` / `tie`. `first`: `true`, ha a játékos kezdett, `false`, ha érmével, `null`, ha nem derült ki.
- `player_hero` / `opponent_hero`: a hősök kártyaazonosítója a meccs elején; az oldal ebből számolja a kasztot (`HERO_05…` → Hunter).
- `deck_id`, `deck_name`, `deckstring`: a játékbeli pakli (hiányozhat). Az oldal a kód fő listájából lenyomatot számol
  (lapnevek + darabszámok), ezzel köthető a meccs az oldalra feltöltött paklihoz.
- Kérésenként legfeljebb 50 meccs, felhasználónként naponta 300. A 60 napnál régebbi vagy jövőbeli meccs hibás adat (400).
- Hibák: 400 `bad_games` (az alkalmazás nem küldi újra), 401 (leválasztott eszköz), 403 `no_access`, 426 (ismeretlen `format`), 429.
- Tárolás: `jsweb_app_games` tábla.

### Hírek (`/api/app/news.php`)

`GET`, token nélkül (nyilvános adat). A megjelenés ideje szerint a legújabb 5 cikk; a későbbi szerkesztés nem számít új hírnek.

```json
{ "ok": true, "items": [ { "id": 5599, "title": "Patch 37.0.3 hotfixek…", "url": "https://hearthstonehungary.hu/node/…", "date": "2026-11-17T22:00:00+01:00" } ] }
```

Az alkalmazás óránként kérdezi le (ha az értesítés be van kapcsolva). Az első alkalommal csak megjegyzi a legújabb
cikket, utána az annál újabbakról küld tálcaértesítést (3 napnál régebbi cikkről nem, pl. ha a gép sokáig ki volt kapcsolva).

### POST – gyűjtemény feltöltése (format 2)

```
POST /api/app/collection.php
X-HSHU-Token: 3f9a...
Content-Type: application/json; charset=utf-8
User-Agent: HearthstoneHungary/3.0.0
```

```json
{
  "format": 2,
  "app_version": "3.0.0",
  "battletag": "PHOENIX#2123",
  "hash": "9b1c…(64 hexa karakter)",
  "total_cards": 4012,
  "cards": [
    { "card_id": "CS2_029", "normal": 2, "golden": 1, "diamond": 0, "signature": 0 },
    { "card_id": "EX1_001", "normal": 0, "golden": 0, "diamond": 1, "signature": 0 }
  ]
}
```

- **A `cards` a teljes gyűjtemény**, a próba (trial) lapok nélkül. Ami nincs a listában, abból 0 van,
  tehát a mentés a régi gyűjtemény **lecserélése**, nem hozzáadás.
- `card_id`: a játék kártyaazonosítója. Az oldal a `jsweb_cards.image` oszlop alapján keresi meg a lap dbfId-ját.
  Csak az oldalon megjelenő lapok (`collectible_constructed = 1`, mint a gyűjtemény oldalon) mentődnek; ha egy azonosító
  többször szerepel, ez a sor nyer.
  - A játék a gyűjteményben a Classic formátum (`VAN_`) lapjait, a hősöket és a régi Core-változatokat is felsorolja:
    ezek kimaradnak, a darabszámuk a válasz `skipped` mezőjében van.
  - Az oldalon még nem ismert lapok (új kiegészítő a kártya import előtt) is kimaradnak, a számuk az `unknown` mezőben van.
    Az alkalmazás változatlan gyűjteményt is feltölt naponta egyszer, így ezek a kártya import utáni napon felkerülnek.
  - A `cards` mező a ténylegesen mentett darabszám; az alkalmazás ezt írja ki.
- A „gold” oszlopba az arany + gyémánt + aláírt lapok összege kerül (ahogy a CSV importnál is).
- `battletag`: több Battle.net fióknál az utolsó feltöltés nyer (felhasználónként egy gyűjtemény van).
- `hash`: a tartalom SHA-256 lenyomata (az alkalmazás ebből látja, hogy változott-e; az oldal csak eltárolja).
- `format`: ha később változik a formátum, ez nő. Ismeretlen formátumra vagy 3.0.0 alatti `app_version`-re 426 a válasz.

Sikeres válasz:

```json
{ "ok": true, "user": "PHOENIX", "message": "Gyűjtemény frissítve (2560 lap).", "cards": 2560, "unknown": 0, "skipped": 629 }
```

### Hibakódok

| HTTP | Mikor | Mit csinál az alkalmazás |
|---|---|---|
| 200 + `"ok": true` | siker | elmenti, hogy ez a gyűjtemény fent van |
| 400 | hibás JSON / hibás adat | kiírja a `message`-et |
| 401 | nincs token, rossz vagy visszavont (leválasztott) token | **leválasztja magát** (törli a tokent), és újra összeköthető |
| 413 | túl nagy kérés | kiírja a `message`-et |
| 426 | túl régi alkalmazás vagy ismeretlen `format` | kiírja a `message`-et (pl. „Frissítsd az alkalmazást”) |
| 429 | túl sűrű feltöltés (tokenenként percenként 1) | kiírja a `message`-et; automatikus módban 5 percig nem próbálkozik |
| 5xx | szerverhiba | kiírja; automatikus módban 5 percig nem próbálkozik |

## Szerver oldal

A hearthstonehungary.hu kódjában:

- `api/app/collection.php`: feltöltés. `api/app/link.php`: összekötés és leválasztás. `api/app/deck.php`: pakli küldése. `api/app/games.php`: meccsek. `api/app/news.php`: hírek.
- `function/app_function.php`: eszközök és tokenek, összekötési kérések, kártyaazonosító → dbfId, a gyűjtemény mentése.
- `modules/app_box.php`: az összekötött eszközök a profil Gyűjtemény fülén. `modules/app.php`: a `/app` leírás és a
  `/app/connect/<kód>` jóváhagyó oldal (a POST-ot a `modules/h_404.php` dolgozza fel, mielőtt bármi kiíródna).

A mentés ugyanúgy működik, mint a CSV import: a `jsweb_register_collection` sorában a `normal` / `gold`
a lap maximumáig (Legendary 1, más 2) tárolja a darabszámot, az `all_normal` / `all_gold` pedig azokat a lapokat,
amikből ennél több van.

Korlátok: a kérés legfeljebb 2 MB és 30 000 lap lehet, a darabszám 0–999.

## Kipróbálás curl-lel

```bash
# token ellenőrzés
curl -i -H "X-HSHU-Token: IDE_A_TOKENT" https://hearthstonehungary.hu/api/app/collection.php

# feltöltés
curl -i -X POST -H "X-HSHU-Token: IDE_A_TOKENT" -H "Content-Type: application/json" \
  --data '{"format":2,"app_version":"3.0.0","battletag":"Teszt#1234","cards":[{"card_id":"CS2_029","normal":2,"golden":0,"diamond":0,"signature":0}]}' \
  https://hearthstonehungary.hu/api/app/collection.php
```
