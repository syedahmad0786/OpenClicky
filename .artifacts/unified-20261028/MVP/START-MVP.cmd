@echo off
set "MVP_EXE=D:\OpenClicky\.artifacts\unified-20261028\MVP\OpenClicky.App.exe"
if not exist "%MVP_EXE%" (
  echo The local MVP build was not found at %MVP_EXE%
  pause
  exit /b 1
)
start "" "%MVP_EXE%" --unified-mvp
