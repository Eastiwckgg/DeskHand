@echo off
setlocal
cd /d "%~dp0.."
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist tests\bin mkdir tests\bin
"%FW%\csc.exe" /nologo /codepage:65001 /out:tests\bin\EngineTests.exe ^
  /resource:src\dict\ru.txt.gz,DeskHand.dict.ru.txt.gz /resource:src\dict\en.txt.gz,DeskHand.dict.en.txt.gz ^
  tests\EngineTests.cs src\Engine.cs src\Config.cs src\Dictionaries.cs src\Hotkey.cs || exit /b 1
chcp 65001 >nul
tests\bin\EngineTests.exe
