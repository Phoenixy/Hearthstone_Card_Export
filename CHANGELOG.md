v3.1.0.0 - 2026/11/24
- Az app mostantól telepíthető.
v3.0.2.0 - 2026/11/23
- Háttéer csere, plugin helyett alkalmazás. 
v3.0.0 - 2026/11/23
- ÚJDONSÁG: A Hearthstone Hungary mostantól önálló Windows-alkalmazásként működik, nem pedig a Hearthstone Deck Tracker bővítményeként. Mivel a Deck Tracker 2026. november 30-án megszünteti a bővítmények támogatását, az új alkalmazás már egyáltalán nem igényli a Deck Tracker használatát.
- Az alkalmazás a rendszertálcán fut a Hearthstone mellett, közvetlenül a játékból olvassa ki a gyűjteményedet (a Firestone nyílt forráskódú UnitySpy könyvtárát használva), és minden változás esetén feltölti azt a Hearthstone Hungary profilodba.
- A fiókodat egyszer kell összekapcsolnod a profilodban (a „Gyűjtemény” fülön vagy a hearthstonehungary.hu/app oldalon) található token segítségével. A token titkosítva tárolódik, és kizárólag a te Windows-felhasználói fiókod számára érhető el.
- Az alkalmazás (opcionálisan) a Windows-szal együtt indul, automatikusan frissül a GitHub Releases felületéről, és naplófájlokat vezet a `%AppData%\HearthstoneHungary` mappában.
- A CSV-exportálás lehetősége megszűnt, mivel az alkalmazás közvetlenül végzi a feltöltést. A weboldalon található CSV-feltöltő funkció továbbra is elfogadja a korábban exportált fájlokat.

v2.0.0 - never released (Hearthstone Deck Tracker dropped plugin support)
 - NEW: Automatic upload of your collection to your Hearthstone Hungary profile. Link the plugin once with a token from your HSHU profile; after that it uploads by itself whenever your collection changes (packs, crafting, new cards). There is also an "Upload now" button.
 - NEW: The plugin updates itself from GitHub Releases. Installing v2.0.0 is the last manual reinstall.
 - FIX: Works with the 64-bit Hearthstone Deck Tracker (v1.53.0 and newer). v1.2.2 was built for x86 only and could not be loaded there.
 - The collection is now read through Deck Tracker's own collection helper instead of calling HearthMirror directly, so Hearthstone/HearthMirror changes are much less likely to break the plugin.
 - CSV export still works the same way (same columns), without the CsvHelper dependency. The plugin is now a single DLL.
 - The plugin window has been redesigned. Settings are stored in %AppData%\HearthstoneDeckTracker\HSHU_Gyujtemeny (no longer in the Collection Tracker folder).
 - Fixed the plugin settings button in Deck Tracker (Options > Tracker > Plugins), which threw an error.
 - Developer: SDK-style project, HDT libraries are referenced from lib\ (tools/copy-hdt-libs.ps1), release script (tools/release.ps1), build output removed from the repository.

v1.2.2 - 2023/09/29
 - Reflection.GetCollection() has been changed to Reflection.Client.GetCollection(), so it works with the latest Deck Tracker versions (v1.22 and newer).
 - HearthMirror.dll and HearthDb.dll updated to latest versions.
 - Newtonsoft.Json, System.Memory, System.Runtime.CompilerServices.Unsafe and System.Reflection.DispatchProxy updated to latest stable versions.

v1.2.1 - 2023/08/05
 - Updated Microsoft.Bcl.AsyncInterfaces to 7.0.0 (from 5.0.0)!
 - Added Signature card support!

v1.2.0 - 2021/04/07
 - Updated HearthMirror.dll to work with the latest Deck Tracker changes (1.14.4 from April 3)!
 - Updated code to work with the new Deck Tracker changes!
 - Added Diamond card support!

v1.1.1 - 2021/04/01
 - Export button has been updated in design, and it's been relocated as well to find it easier!
 - The app now defaults to x86 CPU systems, so it's consistent with the Deck Tracker!
 - Updated CsVHelper to 26.1.0 (from 15.0.5)!
 - Updated System.Threading.Tasks.Extensions to 4.5.4 (from 4.5.2)!
 - Updated Microsoft.Bcl.AsyncInterfaces to 5.0.0 (from 1.1.1)!

v1.1.0 - 2020/05/23

- Updated .NET Framework to 4.8!
- Updated CsVHelper to 15.0.5!
- Added Microsoft.Bcl.AsyncInterfaces (it is now a required dependency for CsvHelper)!
- Updated System.Threading.Tasks.Extensions to 4.5.2!
- Modified some text to make it easier to understand!
- Other small changes.

v1.0.3 - 2020/12/21

- Updated design for the plugin!
