@echo off
rem Example for run() in QuickHasteBot, started by the snippet "19 - Number via batch file" next to it.
rem Waits 2 seconds and then prints an order number: customer-random number.
rem Usage: "19 - Number via batch file.bat" customer
rem Without a customer: a message on the error output and exit code 2.
if "%~1"=="" (
  echo No customer given 1>&2
  exit /b 2
)
ping -n 3 127.0.0.1 >nul
echo %~1-%RANDOM%
exit /b 0