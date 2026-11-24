# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

*Ingyenes kódaláírás: [SignPath.io](https://about.signpath.io), a tanúsítványt a [SignPath Foundation](https://signpath.org) adja.*

## What is signed / Mi kerül aláírásra

- `HearthstoneHungary.exe` (inside `HearthstoneHungary.zip`) and the installer `HearthstoneHungary-Setup.exe`,
  attached to the [GitHub Releases](https://github.com/Phoenixy/Hearthstone_Card_Export/releases) of this repository.
- Every signed file is built by GitHub Actions (`.github/workflows/build.yml`, GitHub-hosted runner) from the
  `master` branch of this public repository. Nothing built elsewhere is submitted for signing.
- Third-party libraries are not signed by this project: `UnitySpy.dll` / `UnitySpy.HearthstoneLib.dll`
  ([Firestone, MIT](https://github.com/Zero-to-Heroes/unity-spy-.net4.5)) are included unchanged, `Newtonsoft.Json.dll`
  is signed by its own publisher.
- The SignPath artifact configurations are kept in [`signing/`](signing/).

## Team roles / Szerepek

| Role | Members |
|---|---|
| Committers and reviewers | [Phoenixy](https://github.com/Phoenixy) |
| Approvers (every signing request is approved manually in SignPath) | [Phoenixy](https://github.com/Phoenixy) |

All team members use multi-factor authentication on GitHub and SignPath.

## Privacy policy / Adatvédelem

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it.

- **hearthstonehungary.hu:** only after the user links the app to their Hearthstone Hungary profile (*Összekötés a HSHU
  profiloddal*). It then uploads the card collection, the played games (mode, classes, result, deck code) and, when the
  user clicks it, a chosen deck. Each of these can be switched off in the app, and the device can be unlinked at any time
  in the app or on the website. The news notification reads the public list of the latest articles from the same site.
- **GitHub:** the automatic update checks the latest release of this repository (can be switched off in the app).
- The app reads the Hearthstone game's memory and log files on the local computer only; it does not send them anywhere
  else.

---

*Az alkalmazás csak akkor küld adatot a hearthstonehungary.hu-ra, ha a felhasználó összekötötte a profiljával (gyűjtemény,
lejátszott meccsek, a kiválasztott pakli), és a GitHubon keresi az új verziót (kikapcsolható). Más helyre nem küld adatot.*
