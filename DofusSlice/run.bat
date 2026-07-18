@echo off
REM Launch the Dofus Slice game (needs the .NET 8 SDK installed).
cd /d "%~dp0"
dotnet run --project DofusSlice.Game -c Release
