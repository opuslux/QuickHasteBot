@echo off
rem Beispiel fuer run() in QuickHasteBot, gestartet vom Baustein "19 - Nummer per Batch-Datei" daneben.
rem Wartet 2 Sekunden und gibt dann eine Bestellnummer aus: Kunde-Zufallszahl.
rem Aufruf: "19 - Nummer per Batch-Datei.bat" Kunde
rem Ohne Kunde: Meldung auf der Fehlerausgabe und Rueckgabewert 2.
if "%~1"=="" (
  echo Kein Kunde angegeben 1>&2
  exit /b 2
)
ping -n 3 127.0.0.1 >nul
echo %~1-%RANDOM%
exit /b 0