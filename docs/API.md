# Hearthstone Hungary API (az alkalmazáshoz)

Az alkalmazás (3.0.0-tól) ezt az API-t hívja. A címek a `src/HearthstoneHungary/Core/HshuConfig.cs` fájlban
vannak; ha valamelyik megváltozik, ott írd át, és adj ki új verziót.

| Mi | Cím |
|---|---|
| API végpont | `https://hearthstonehungary.hu/api/app/collection.php` |
| Token létrehozása / visszavonása | `https://hearthstonehungary.hu/app` (belépve a profil Gyűjtemény fülére visz: `/profile/<név>/collection#app`) |

## Token

- A felhasználó a profilja Gyűjtemény fülén hoz létre tokent, és bemásolja az alkalmazásba. Jelszó soha nem kerül az alkalmazásba.
- Generálás: `bin2hex(random_bytes(32))` (64 karakter). Az alkalmazás csak `A-Z a-z 0-9 _ - .` karaktereket és 20–200 hosszt fogad el.
- Az adatbázisba csak a hash kerül (`hash('sha256', $token)`), a token csak a létrehozás után egyszer látszik.
- Felhasználónként egy aktív token van: új token kérésekor a régi azonnal érvényét veszti. A „Leválasztás” gomb visszavonja.
- A Gyűjtemény fülön látszik az utolsó feltöltés ideje, a lapok száma, a BattleTag és az alkalmazás verziója.
- Tábla: `jsweb_app_tokens` (a fő adatbázisban, a gyűjtemény mellett).

### Miért nem `Authorization` fejléc?

A token az **`X-HSHU-Token`** fejlécben érkezik. PHP-ban: `$_SERVER['HTTP_X_HSHU_TOKEN']`.
Az `Authorization: Bearer ...` fejlécet az Apache + `proxy_fcgi` (PHP-FPM) alapból nem adja át a PHP-nak,
egy saját fejléc viszont gond nélkül átmegy.

## Kérések

Minden válasz JSON (`Content-Type: application/json; charset=utf-8`), és mindig van benne `ok` (true/false).
A `message` mezőt (magyarul) az alkalmazás kiírja a felhasználónak.

### GET – token ellenőrzése

Az alkalmazás akkor hívja, amikor a felhasználó bemásolja a tokent („Összekapcsolás” gomb).

```
GET /api/app/collection.php
X-HSHU-Token: 3f9a...
```

```json
{ "ok": true, "user": "PHOENIX" }
```

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
- `card_id`: a játék kártyaazonosítója. Az oldal a `jsweb_cards.image` oszlop alapján keresi meg a lap dbfId-ját;
  ha egy azonosító többször szerepel, a gyűjthető sor nyer. Az oldalon még nem ismert lapok (új kiegészítő a kártya import előtt)
  kimaradnak; a válasz `unknown` mezője ezek számát adja. Az alkalmazás változatlan gyűjteményt is feltölt naponta egyszer,
  így ezek a kártya import utáni napon felkerülnek.
- A „gold” oszlopba az arany + gyémánt + aláírt lapok összege kerül (ahogy a CSV importnál is).
- `battletag`: több Battle.net fióknál az utolsó feltöltés nyer (felhasználónként egy gyűjtemény van).
- `hash`: a tartalom SHA-256 lenyomata (az alkalmazás ebből látja, hogy változott-e; az oldal csak eltárolja).
- `format`: ha később változik a formátum, ez nő. Ismeretlen formátumra vagy 3.0.0 alatti `app_version`-re 426 a válasz.

Sikeres válasz:

```json
{ "ok": true, "user": "PHOENIX", "message": "Gyűjtemény frissítve (4012 lap).", "cards": 4012, "unknown": 0 }
```

### Hibakódok

| HTTP | Mikor | Mit csinál az alkalmazás |
|---|---|---|
| 200 + `"ok": true` | siker | elmenti, hogy ez a gyűjtemény fent van |
| 400 | hibás JSON / hibás adat | kiírja a `message`-et |
| 401 | nincs token, rossz vagy visszavont token | **leválasztja magát** (törli a tokent), és új tokent kér |
| 413 | túl nagy kérés | kiírja a `message`-et |
| 426 | túl régi alkalmazás vagy ismeretlen `format` | kiírja a `message`-et (pl. „Frissítsd az alkalmazást”) |
| 429 | túl sűrű feltöltés (tokenenként percenként 1) | kiírja a `message`-et; automatikus módban 5 percig nem próbálkozik |
| 5xx | szerverhiba | kiírja; automatikus módban 5 percig nem próbálkozik |

## Szerver oldal

A hearthstonehungary.hu kódjában:

- `api/app/collection.php`: a végpont.
- `function/app_function.php`: tokenek (létrehozás, visszavonás, ellenőrzés), kártyaazonosító → dbfId, a gyűjtemény mentése.
- `modules/app_box.php`: a token kezelése a profil Gyűjtemény fülén. `modules/app.php`: a `/app` oldal vendégeknek.

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
