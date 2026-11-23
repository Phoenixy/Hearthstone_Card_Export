# HSHU plugin API (szerver oldal)

A plugin 2.0.0-s verziója ezt az API-t hívja. A címek a `exportalas/Internal/HshuConfig.cs` fájlban
vannak; ha valamelyik megváltozik, ott írd át, és adj ki új plugin verziót.

| Mi | Cím |
|---|---|
| API végpont | `https://hearthstonehungary.hu/api/plugin/collection.php` |
| Token létrehozása / visszavonása | `https://hearthstonehungary.hu/plugin` (belépve a profil Gyűjtemény fülére visz: `/profile/<név>/collection#plugin`) |

## Token

- A felhasználó a profilja Gyűjtemény fülén hoz létre tokent, és bemásolja a pluginba. Jelszó soha nem kerül a pluginba.
- Generálás: `bin2hex(random_bytes(32))` (64 karakter). A plugin csak `A-Z a-z 0-9 _ - .` karaktereket és 20–200 hosszt fogad el.
- Az adatbázisba csak a hash kerül (`hash('sha256', $token)`), a token csak a létrehozás után egyszer látszik.
- Felhasználónként egy aktív token van: új token kérésekor a régi azonnal érvényét veszti. A „Leválasztás” gomb visszavonja.
- A Gyűjtemény fülön látszik az utolsó feltöltés ideje, a lapok száma, a BattleTag és a plugin verziója.
- Tábla: `jsweb_plugin_tokens` (a fő adatbázisban, a gyűjtemény mellett).

### Miért nem `Authorization` fejléc?

A token az **`X-HSHU-Token`** fejlécben érkezik. PHP-ban: `$_SERVER['HTTP_X_HSHU_TOKEN']`.
Az `Authorization: Bearer ...` fejlécet az Apache + `proxy_fcgi` (PHP-FPM) alapból nem adja át a PHP-nak,
egy saját fejléc viszont gond nélkül átmegy.

## Kérések

Minden válasz JSON (`Content-Type: application/json; charset=utf-8`), és mindig van benne `ok` (true/false).
A `message` mezőt (magyarul) a plugin kiírja a felhasználónak, ezért hibánál érdemes érthető mondatot küldeni.

### GET – token ellenőrzése

A plugin akkor hívja, amikor a felhasználó bemásolja a tokent („Összekapcsolás” gomb).

```
GET /api/plugin/collection.php
X-HSHU-Token: 3f9a...
```

Válasz, ha jó a token:

```json
{ "ok": true, "user": "PHOENIX" }
```

A `user` a HSHU felhasználónév, a plugin kiírja: „Összekapcsolva: PHOENIX”.

### POST – gyűjtemény feltöltése

```
POST /api/plugin/collection.php
X-HSHU-Token: 3f9a...
Content-Type: application/json; charset=utf-8
User-Agent: HSHU-Gyujtemeny-Plugin/2.0.0
```

```json
{
  "format": 1,
  "plugin_version": "2.0.0",
  "hdt_version": "1.59.8.0",
  "battletag": "PHOENIX#2123",
  "hash": "9b1c…(64 hexa karakter)",
  "total_cards": 4012,
  "cards": [
    { "dbf_id": 315, "normal": 2, "golden": 1, "diamond": 0, "signature": 0 },
    { "dbf_id": 1004, "normal": 0, "golden": 0, "diamond": 1, "signature": 0 }
  ]
}
```

- **A `cards` a teljes gyűjtemény.** Csak a gyűjthető lapok vannak benne, amikből legalább 1 darab van.
  Ami nincs a listában, abból 0 van, tehát a mentés a régi gyűjtemény **lecserélése**, nem hozzáadás.
- A CSV-ben eddig a „Golden lapok” oszlop az arany + gyémánt + aláírt (signature) lapok összege volt.
  Ha a mostani tábla is így tárolja, mentsd a `golden + diamond + signature` összeget.
- A próba (trial) lapok nincsenek benne.
- `battletag`: több Battle.net fióknál az utolsó feltöltés nyer (felhasználónként egy gyűjtemény van).
- `hash`: a tartalom SHA-256 lenyomata. Ha megegyezik az előzővel, a mentés kihagyható.
- `format`: ha később változik a formátum, ez nő. Ismeretlen formátumra válaszolj 426-tal.

Sikeres válasz:

```json
{ "ok": true, "user": "PHOENIX", "message": "Gyűjtemény frissítve (4012 lap)." }
```

### Hibakódok

| HTTP | Mikor | Mit csinál a plugin |
|---|---|---|
| 200 + `"ok": true` | siker | elmenti, hogy ez a gyűjtemény fent van |
| 400 | hibás JSON / hibás adat | kiírja a `message`-et |
| 401 | nincs token, rossz vagy visszavont token | **leválasztja magát** (törli a tokent), és új tokent kér |
| 413 | túl nagy kérés | kiírja a `message`-et |
| 426 | túl régi plugin vagy ismeretlen `format` | kiírja a `message`-et (pl. „Frissítsd a plugint”) |
| 429 | túl sűrű feltöltés | kiírja a `message`-et; automatikus módban 5 percig nem próbálkozik |
| 5xx | szerverhiba | kiírja; automatikus módban 5 percig nem próbálkozik |

A plugin magától csak akkor tölt fel, ha változott a gyűjtemény, ezért normál használatnál
óránként legfeljebb néhány kérés jön felhasználónként. Egy „percenként 1 feltöltés tokenenként”
korlát bőven elég.

## Szerver oldal

A hearthstonehungary.hu kódjában:

- `api/plugin/collection.php`: a végpont.
- `function/plugin_function.php`: tokenek (létrehozás, visszavonás, ellenőrzés) és a gyűjtemény mentése.
- `modules/plugin_box.php`: a token kezelése a profil Gyűjtemény fülén. `modules/plugin.php`: a `/plugin` oldal vendégeknek.

A mentés ugyanúgy működik, mint a CSV import: a `jsweb_register_collection` sorában a `normal` / `gold`
a lap maximumáig (Legendary 1, más 2) tárolja a darabszámot, az `all_normal` / `all_gold` pedig azokat a lapokat,
amikből ennél több van. A „gold” az arany + gyémánt + aláírt lapok összege. Az oldalon még nem ismert lapok
(új kiegészítő a kártya import előtt) is elmentődnek, így a következő kártya import után magától megjelennek.

Korlátok: a kérés legfeljebb 2 MB és 20 000 lap lehet, a darabszám 0–999, tokenenként percenként 1 feltöltés.

## Kipróbálás curl-lel

```bash
# token ellenőrzés
curl -i -H "X-HSHU-Token: IDE_A_TOKENT" https://hearthstonehungary.hu/api/plugin/collection.php

# feltöltés
curl -i -X POST -H "X-HSHU-Token: IDE_A_TOKENT" -H "Content-Type: application/json" \
  --data '{"format":1,"plugin_version":"2.0.0","battletag":"Teszt#1234","hash":"x","total_cards":2,"cards":[{"dbf_id":315,"normal":2,"golden":0,"diamond":0,"signature":0}]}' \
  https://hearthstonehungary.hu/api/plugin/collection.php
```
