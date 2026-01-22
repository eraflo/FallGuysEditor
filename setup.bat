@echo off
echo Configuration du projet FallGuysEditor...

REM Configure Git pour utiliser nos hooks personnalises
git config core.hooksPath .githooks

REM Initialise les submodules
git submodule update --init --recursive

echo.
echo ✓ Configuration terminee !
echo Les submodules seront maintenant mis a jour automatiquement.
pause
