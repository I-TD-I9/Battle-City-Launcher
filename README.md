# Battle City Launcher

Play Yu-Gi-Oh! Online Phase 1 again. This launcher connects your own copy of the game to Battle
City, an unofficial server that brings its online play back.

It doesn't include any game files, and it isn't affiliated with Konami.

## What you need

- Windows 10 or 11, 64-bit
- Yu-Gi-Oh! ONLINE installed, with its last update (June 2005)

## Getting started

1. Download `BattleCityLauncher.exe` from the
   [latest release](https://github.com/I-TD-I9/Battle-City-Launcher/releases/latest) and run it.
   There's nothing to install.
2. If Windows warns that it doesn't recognise the app, click **More info**, then **Run anyway**.
3. The launcher should find your game on its own. If it doesn't, click **Change…** and pick the
   folder you installed it in.
4. Make an account under **Create account**.
5. Click **Play** and log in with it.

A few things to know:

- Your name is case-sensitive when you log in.
- Pick a password you don't use anywhere else.
- There's no password reset yet.

Your installed game is never changed.

## Building it yourself

With the .NET 10 SDK on Windows:

```
dotnet publish src/BattleCity.Launcher -c Release -o publish
```

## License

GPL-3.0. Yu-Gi-Oh! is a trademark of its owners; this project isn't affiliated with or endorsed
by Konami.
