#!/usr/bin/env bash
# Launch the Dofus Slice game (needs the .NET 8 SDK installed).
cd "$(dirname "$0")"
exec dotnet run --project DofusSlice.Game -c Release
