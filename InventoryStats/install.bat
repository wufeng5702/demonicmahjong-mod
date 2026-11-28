@echo off
setlocal
call "%~dp0..\tools\modbat\install.bat" InventoryStats %*
exit /b %errorlevel%
