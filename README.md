<p align="center">
  <img src="docs/launcher.gif" width="720" alt="Making an account in the Battle City Launcher, then starting the game">
</p>

# Battle City Launcher

**It's time to duel!** Yu-Gi-Oh! Online Phase 1 went offline long ago. Battle City, a fan-made
server, brings its online play back, and this launcher takes your copy of the game there.

## Draw Phase: what you need

- Windows 10 or 11, 64-bit
- Yu-Gi-Oh! ONLINE, the February 2006 version (`Ver.060110.00`), unpacked anywhere

## Main Phase: getting set up

1. Download `BattleCityLauncher.exe` from the
   [latest release](https://github.com/I-TD-I9/Battle-City-Launcher/releases/latest) and run it.
2. If Windows says it doesn't recognise the app, click **More info**, then **Run anyway**.
3. Click **Change…** and pick your Yu-Gi-Oh! ONLINE folder.
4. Make your account under **Account**. You'll get a code by email to finish it.

## Battle Phase: duel

Click **Play** and log in with your new account.

## End Phase: good to know

- Your UserID is case-sensitive.
- One account per email address. Forgot your password? Set a new one under **Account**.
- Your game folder is never changed.

See you in Battle City!

## Building it yourself

With the .NET 10 SDK on Windows:

```
dotnet publish src/BattleCity.Launcher -c Release -o publish
```

## License

GPL-3.0. Yu-Gi-Oh! is a trademark of its owners; this project isn't affiliated with or endorsed
by Konami.
