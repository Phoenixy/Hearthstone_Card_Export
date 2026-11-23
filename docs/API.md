# HSHU plugin API (szerver oldal)

Ez a leírás a hearthstonehungary.hu oldalra kerülő PHP részhez készült. A plugin 2.0.0-s verziója
ezt az API-t hívja. A címek a `exportalas/Internal/HshuConfig.cs` fájlban vannak; ha máshova teszed
a végpontot vagy a token oldalt, ott írd át, és adj ki új plugin verziót.

| Mi | Alapértelmezett cím |
|---|---|
| API végpont | `https://hearthstonehungary.hu/api/plugin/collection.php` |
| Token létrehozása / visszavonása (profil oldal) | `https://hearthstonehungary.hu/profile/plugin` |

## Token

- A felhasználó a profiljában hoz létre tokent, és bemásolja a pluginba. Jelszó soha nem kerül a pluginba.
- Generálás: `bin2hex(random_bytes(32))` (64 karakter). A plugin csak `A-Z a-z 0-9 _ - .` karaktereket és 20–200 hosszt fogad el.
- Az adatbázisba csak a hash kerüljön: `hash('sha256', $token)`. A tokent egyszer mutasd meg a létrehozás után.
- Legyen visszavonható (a profil oldalon), és látszódjon, mikor volt utoljára használva.
- A token létrehozó/visszavonó űrlapra kell CSRF védelem, mint a többi profil űrlapra.

Javasolt tábla:

```sql
CREATE TABLE hs_plugin_tokens (
  id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  user_id INT UNSIGNED NOT NULL,
  token_hash CHAR(64) NOT NULL,
  created_at DATETIME NOT NULL,
  last_used_at DATETIME NULL,
  last_upload_at DATETIME NULL,
  revoked_at DATETIME NULL,
  UNIQUE KEY token_hash (token_hash),
  KEY user_id (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_hungarian_ci;
```

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
- `battletag`: ha valaki több Battle.net fiókkal játszik, mindegyik fiók külön feltöltést küld.
  Döntsd el, hogy ilyenkor az utolsó feltöltés nyer (legegyszerűbb), vagy fiókonként tárolod.
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

## PHP váz

Procedurális mysqli, prepared statementekkel. A `// TODO` részeket igazítsd a meglévő táblákhoz
(felhasználók, kártyák, gyűjtemény); a gyűjtemény mentését a mostani CSV-importáló kódból érdemes átvenni.

```php
<?php
// /api/plugin/collection.php – HDT gyűjtemény plugin API (format 1). Leírás: docs/API.md a plugin repóban.
declare(strict_types=1);

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');

require __DIR__ . '/../../sql.php'; // TODO: a saját útvonalad ($conn, $conn_user)

const MIN_PLUGIN_VERSION = '2.0.0';
const MAX_BODY_BYTES = 1048576; // 1 MB, egy teljes gyűjtemény kb. 250 KB
const UPLOAD_INTERVAL_SEC = 60;

function api_out(int $status, array $data): never
{
    http_response_code($status);
    echo json_encode($data, JSON_UNESCAPED_UNICODE);
    exit;
}

function api_error(int $status, string $error, string $message): never
{
    api_out($status, ['ok' => false, 'error' => $error, 'message' => $message]);
}

// --- 1. Token ---------------------------------------------------------------
$token = $_SERVER['HTTP_X_HSHU_TOKEN'] ?? '';
if (!preg_match('/^[A-Za-z0-9_.-]{20,200}$/', $token)) {
    api_error(401, 'invalid_token', 'Hiányzó vagy hibás token.');
}
$token_hash = hash('sha256', $token);

// TODO: users tábla és oszlopnevek
$stmt = mysqli_prepare($conn_user,
    'SELECT t.id, t.user_id, t.last_upload_at, u.username
       FROM hs_plugin_tokens t
       JOIN users u ON u.id = t.user_id
      WHERE t.token_hash = ? AND t.revoked_at IS NULL');
mysqli_stmt_bind_param($stmt, 's', $token_hash);
mysqli_stmt_execute($stmt);
$row = mysqli_fetch_assoc(mysqli_stmt_get_result($stmt));
mysqli_stmt_close($stmt);
if (!$row) {
    api_error(401, 'invalid_token', 'A token érvénytelen vagy vissza lett vonva.');
}
$token_id = (int)$row['id'];
$user_id = (int)$row['user_id'];

$stmt = mysqli_prepare($conn_user, 'UPDATE hs_plugin_tokens SET last_used_at = NOW() WHERE id = ?');
mysqli_stmt_bind_param($stmt, 'i', $token_id);
mysqli_stmt_execute($stmt);
mysqli_stmt_close($stmt);

// --- 2. GET: token ellenőrzés -------------------------------------------------
if ($_SERVER['REQUEST_METHOD'] === 'GET') {
    api_out(200, ['ok' => true, 'user' => $row['username']]);
}
if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    api_error(405, 'method_not_allowed', 'Csak GET és POST kérés engedélyezett.');
}

// --- 3. POST: korlát, beolvasás, ellenőrzés ----------------------------------
if ($row['last_upload_at'] !== null && time() - strtotime($row['last_upload_at']) < UPLOAD_INTERVAL_SEC) {
    api_error(429, 'rate_limited', 'Túl sűrű feltöltés, próbáld újra egy perc múlva.');
}

$raw = file_get_contents('php://input', false, null, 0, MAX_BODY_BYTES + 1);
if ($raw === false || strlen($raw) > MAX_BODY_BYTES) {
    api_error(413, 'too_large', 'Túl nagy kérés.');
}
try {
    $body = json_decode($raw, true, 16, JSON_THROW_ON_ERROR);
} catch (JsonException $e) {
    api_error(400, 'bad_json', 'Hibás JSON.');
}

if (($body['format'] ?? null) !== 1
    || version_compare((string)($body['plugin_version'] ?? '0'), MIN_PLUGIN_VERSION, '<')) {
    api_error(426, 'plugin_outdated', 'A plugin túl régi. Frissítsd a legújabb verzióra!');
}
if (!isset($body['cards']) || !is_array($body['cards']) || count($body['cards']) > 20000) {
    api_error(400, 'bad_cards', 'Hibás kártyalista.');
}

$cards = []; // dbf_id => [normal, golden_osszes]
foreach ($body['cards'] as $c) {
    $dbf = $c['dbf_id'] ?? null;
    $counts = [$c['normal'] ?? 0, $c['golden'] ?? 0, $c['diamond'] ?? 0, $c['signature'] ?? 0];
    if (!is_int($dbf) || $dbf <= 0) {
        api_error(400, 'bad_cards', 'Hibás kártya azonosító.');
    }
    foreach ($counts as $n) {
        if (!is_int($n) || $n < 0 || $n > 99) {
            api_error(400, 'bad_cards', 'Hibás darabszám.');
        }
    }
    $cards[$dbf] = [$counts[0], $counts[1] + $counts[2] + $counts[3]];
}

// Csak az oldalon ismert lapokat mentjük (az új kiegészítő lapjai a kártya import után jelennek meg).
// TODO: jsweb_cards oszlopnév a dbfId-hoz
$known = [];
$res = mysqli_query($conn, 'SELECT dbf_id FROM jsweb_cards');
while ($r = mysqli_fetch_row($res)) {
    $known[(int)$r[0]] = true;
}
$cards = array_intersect_key($cards, $known);

// --- 4. Mentés: a régi gyűjtemény lecserélése --------------------------------
// TODO: a gyűjtemény tábla neve/oszlopai (ugyanaz, amibe a CSV import ír)
mysqli_begin_transaction($conn_user);
try {
    $stmt = mysqli_prepare($conn_user, 'DELETE FROM user_collection WHERE user_id = ?');
    mysqli_stmt_bind_param($stmt, 'i', $user_id);
    mysqli_stmt_execute($stmt);
    mysqli_stmt_close($stmt);

    $stmt = mysqli_prepare($conn_user, 'INSERT INTO user_collection (user_id, dbf_id, normal, golden) VALUES (?, ?, ?, ?)');
    foreach ($cards as $dbf => [$normal, $golden]) {
        mysqli_stmt_bind_param($stmt, 'iiii', $user_id, $dbf, $normal, $golden);
        mysqli_stmt_execute($stmt);
    }
    mysqli_stmt_close($stmt);

    $stmt = mysqli_prepare($conn_user, 'UPDATE hs_plugin_tokens SET last_upload_at = NOW() WHERE id = ?');
    mysqli_stmt_bind_param($stmt, 'i', $token_id);
    mysqli_stmt_execute($stmt);
    mysqli_stmt_close($stmt);

    mysqli_commit($conn_user);
} catch (mysqli_sql_exception $e) {
    mysqli_rollback($conn_user);
    error_log('[plugin collection] ' . $e->getMessage());
    api_error(500, 'server_error', 'Nem sikerült menteni a gyűjteményt, próbáld később.');
}

$total = 0;
foreach ($cards as [$normal, $golden]) {
    $total += $normal + $golden;
}
api_out(200, ['ok' => true, 'user' => $row['username'], 'message' => "Gyűjtemény frissítve ($total lap)."]);
```

## Kipróbálás curl-lel

```bash
# token ellenőrzés
curl -i -H "X-HSHU-Token: IDE_A_TOKENT" https://hearthstonehungary.hu/api/plugin/collection.php

# feltöltés
curl -i -X POST -H "X-HSHU-Token: IDE_A_TOKENT" -H "Content-Type: application/json" \
  --data '{"format":1,"plugin_version":"2.0.0","battletag":"Teszt#1234","hash":"x","total_cards":2,"cards":[{"dbf_id":315,"normal":2,"golden":0,"diamond":0,"signature":0}]}' \
  https://hearthstonehungary.hu/api/plugin/collection.php
```
