<p align="center">
  <img src="docs/banner.png" width="100%" alt="Battle City, Yu-Gi-Oh! Online: Phase 1's duels, back online. Bring your own copy of the game.">
</p>

<p align="center">
  <b>It's time to duel!</b> Yu-Gi-Oh! Online Phase 1 went offline long ago.<br>
  Battle City, a fan-made server, brings its online play back, and this launcher takes your copy of
  the game there.
</p>

<p align="center">
  <img src="docs/badges/server.png" height="34" alt="Server: battle-city.net">
  <img src="docs/badges/windows.png" height="34" alt="Runs on Windows 10 and 11">
  <img src="docs/badges/client.png" height="34" alt="Plays the February 2006 client">
  <a href="LICENSE"><img src="docs/badges/license.png" height="34" alt="License: GPL-3.0"></a>
</p>

<p align="center">
  <img src="docs/screenshots/select-pack.jpg" width="49%" alt="Choosing a starter deck: Yugi's, Kaiba's or Joey's">
  <img src="docs/screenshots/won-a-card.jpg" width="49%" alt="Opening the starter deck, with Dark Magician on top">
</p>

<p align="center">
  <a href="https://github.com/I-TD-I9/Battle-City-Launcher/releases/latest"><img src="docs/download.png" height="65" alt="Download for Windows"></a>
</p>

## Getting started

1. You need Windows 10 or 11, 64-bit, and Yu-Gi-Oh! ONLINE, the February 2006 version
   (`Ver.060110.00`), unpacked anywhere. The launcher never changes your game folder.
2. [Download the launcher](https://github.com/I-TD-I9/Battle-City-Launcher/releases/latest) and run
   it. If Windows says it doesn't recognise the app, click **More info**, then **Run anyway**.
3. Click **Change…** and pick your game folder.
4. Make your account under **Account**. You'll get a code by email to finish it.
5. Click **Play** and log in with your new account.

<p align="center">
  <img src="docs/launcher.gif" width="720" alt="The launcher with the game folder picked, then an account made with a code from an email, then Play">
</p>

## Good to know

- Your UserID is case-sensitive.
- One account per email address. Forgot your password? Set a new one under **Account**.

<p align="center"><b>See you in Battle City!</b></p>

## Building it yourself

With the .NET 10 SDK on Windows:

```
dotnet publish src/BattleCity.Launcher -c Release -o publish
```

## License

GPL-3.0. Yu-Gi-Oh! is a trademark of its owners; this project isn't affiliated with or endorsed
by Konami.
