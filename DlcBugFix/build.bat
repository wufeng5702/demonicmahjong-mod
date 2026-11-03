@echo off
call "%~dp0..\tools\modbat\build.bat" DlcBugFix %*
exit /b %ERRORLEVEL%
