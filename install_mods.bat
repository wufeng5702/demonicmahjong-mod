@echo off
rem 一键安装/卸载 mod（PowerShell 5.1+）；交互式选择；传给 ps1 的参数原样透传
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_mods.ps1" %*