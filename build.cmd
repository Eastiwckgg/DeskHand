@echo off
setlocal
cd /d "%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "WPF=%FW%\WPF"
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /out:DeskHand.exe ^
  /win32icon:src\app.ico /win32manifest:src\app.manifest ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xaml.dll ^
  /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:"%WPF%\UIAutomationClient.dll" /r:"%WPF%\UIAutomationTypes.dll" ^
  /resource:src\Settings.xaml,DeskHand.Settings.xaml ^
  /resource:src\app.ico,DeskHand.app.ico ^
  /resource:src\tray-off.ico,DeskHand.tray-off.ico ^
  /resource:src\logo.png,DeskHand.logo.png ^
  /resource:src\dict\ru.txt.gz,DeskHand.dict.ru.txt.gz /resource:src\dict\en.txt.gz,DeskHand.dict.en.txt.gz ^
  /resource:src\hints\hint-brackets.gif,DeskHand.hint-brackets.gif /resource:src\hints\hint-caps-lock.gif,DeskHand.hint-caps-lock.gif /resource:src\hints\hint-double-space.gif,DeskHand.hint-double-space.gif /resource:src\hints\hint-layout.gif,DeskHand.hint-layout.gif /resource:src\hints\hint-punctuation.gif,DeskHand.hint-punctuation.gif /resource:src\hints\hint-shortcuts.gif,DeskHand.hint-shortcuts.gif ^
  src\*.cs || exit /b 1
echo Done: DeskHand.exe
