#!/bin/bash
# usage: d.sh <mode> <pattern> [method] [dll-basename default Game]
M="/c/XboxGames/Cities- Skylines II - PC Edition/Content/Cities2_Data/Managed"
DLL="${4:-Game}"
"/c/soft/cityskyline/tools/ildump/bin/ildump.exe" "$1" "$M/$DLL.dll" "$2" ${3:+"$3"}
