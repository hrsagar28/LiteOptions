LiteOptions 2.0
A small, local stand-in for Logi Options+ for the Pebble Mouse 2 M350s and the K380 keyboard.

------------------------------------------------------------
INSTALL OR UPDATE
------------------------------------------------------------
Download the installer from github.com/hrsagar28/LiteOptions/releases:
  - LiteOptions-ARM64.exe for Surface Laptop and other Snapdragon PCs
  - LiteOptions-x64.exe for Intel or AMD PCs
Double-click it and click Install / Update.
  - It installs for your Windows account only, so no administrator prompt.
  - It goes into %LOCALAPPDATA%\Programs\LiteOptions, gets a Start menu entry and is listed
    under Settings > Apps > Installed apps.
  - If an older version is running, it is closed and replaced. Your settings are kept.
  - The downloaded file isn't needed afterwards; you can delete it.

Future updates work the same way: download the new installer, double-click it, click Update.

If Windows SmartScreen says "Windows protected your PC": click "More info" > "Run anyway".
This happens because the app isn't code-signed. It isn't a virus warning.
If you see "Smart App Control blocked this app": Smart App Control only runs signed apps.
The only way past it is to turn Smart App Control off (Windows Security > App & browser
control). Decide before you do it; on some Windows 11 builds it can't be turned back on.

Make sure Logi Options+ is uninstalled. Running both makes them fight over the devices.

------------------------------------------------------------
UNINSTALL
------------------------------------------------------------
Either:
  - Settings > Apps > Installed apps > LiteOptions > Uninstall, or
  - Open LiteOptions > Settings > Uninstall.
This removes the app, its settings, the Start menu entry and the startup entry. If you had
turned on "Reverse scroll direction", it switches that back too (Windows asks for permission).

------------------------------------------------------------
WHAT IT DOES
------------------------------------------------------------
Mouse (Pebble Mouse 2 M350s)
  - Middle button: normal click, one action of your choice, or gestures. For gestures, hold the
    wheel down, nudge the mouse up, down, left or right, then let go.
  - Sensitivity (DPI) 400-4000. It is stored on the mouse and set again whenever the mouse
    reconnects.
  - Windows pointer speed, pointer precision and scroll distance.
  - Reverse scroll direction for this mouse only.
  - Battery level and a low-battery warning.

Keyboard (K380)
  - Top row: media keys first, or F1-F12 first. Set again whenever the keyboard reconnects.
  - Remap any key or key combination to a shortcut, an app, file, folder or website, typed text,
    media control, or a Windows action.

Per-app settings: use "Applies to" at the top right to add an app (e.g. excel.exe) and give the
middle button or keys a different job while that app is in front.

Ctrl + Esc switches the K380's top row between media keys and F1-F12 at any time
(the K380 has no Fn-lock of its own). You can change or remove that shortcut under Key remapping.

On-screen pop-ups: a small note at the bottom of the screen when you switch the top row,
change DPI or use a gesture. Turn them off under Settings if you prefer.

The tray icon shows a small battery gauge for whichever device is lowest: green, amber below
30%, red when low. Hover over it for details.

Click the dots on the mouse or keyboard picture to jump to those settings.

The settings window (new in 2.0) uses Windows 11's own controls: Mica background, the
Settings-app sidebar, menus and dialogs. It follows light or dark mode and stays sharp on every
screen. It only runs while it's open; close it and its memory is freed. The part that does the
work stays in the tray. If the new window ever can't start, the classic window opens instead
(also reachable from Settings > Troubleshooting). The tray icon menu has quick toggles for F1-F12 first and Pause remapping.
Settings > Run setup repeats the three first-run choices.

------------------------------------------------------------
GOOD TO KNOW
------------------------------------------------------------
- Key remaps also apply to the laptop's own keyboard. Windows can't tell the two apart for this.
- The K380's Easy-Switch keys (F1-F3), the Fn key and the mouse's channel button work inside the
  devices themselves and can't be changed by any app.
- Firmware updates only come through Logi Options+. They are rare and optional. If you ever want
  one, install Options+, let it update, then uninstall it. The firmware stays on the device.
- No account, no internet access, no telemetry, no background service. It runs natively on ARM64.
- Sizes: the tray part uses about 25 MB of memory. The settings window adds about 60 MB only
  while it's open. Installed size is about 130 MB, mostly Microsoft's WinUI and .NET files that
  the settings window carries so nothing else needs installing.

------------------------------------------------------------
IF SOMETHING DOESN'T WORK
------------------------------------------------------------
Open LiteOptions > Settings > Troubleshooting > Copy, and paste the text to Claude.
