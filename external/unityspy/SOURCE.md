# UnitySpy (Hearthstone memóriaolvasó)

A Hearthstone gyűjteményét ezzel a könyvtárral olvassuk ki. A Firestone (Zero-to-Heroes) tartja karban,
MIT licenccel (lásd LICENSE). A forrást változtatás nélkül másoltuk be; csak a `.cs` fájlok kellenek,
a projektfájlok a `src/UnitySpy` és `src/UnitySpy.HearthstoneLib` mappában vannak.

- Forrás: https://github.com/Zero-to-Heroes/unity-spy-.net4.5
- Commit: 92b2c611b569472c97a233bdb71debba4a5f5f98 (2026-11-10)

Frissítés (ha egy Hearthstone frissítés után nem olvasható a gyűjtemény):

    powershell -ExecutionPolicy Bypass -File tools\update-unityspy.ps1
