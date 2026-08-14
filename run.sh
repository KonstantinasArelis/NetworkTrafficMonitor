#!/bin/bash

if [[ "$1" != "debug" && "$1" != "release" ]]; then
    echo "first paramater must be: release or debug"
    echo "Usage: ./run.sh [debug|release]"
    exit 1
fi

TARGET=$1
set -e

# Map lowercase input to proper .NET configuration casing
if [[ "$TARGET" == "debug" ]]; then
    TARGET="Debug"
else
    TARGET="Release"
fi

echo "Im building in $TARGET mode"
dotnet build -c "$TARGET"

if [[ "$TARGET" == "Debug" ]]; then
    EXECUTABLE_PATH="./bin/Debug/net10.0/TrafficAnalyzer"
fi

if [[ "$TARGET" == "Release" ]]; then
    # not sure if path is correct
    EXECUTABLE_PATH="./bin/Release/net10.0/TrafficAnalyzer"
fi

echo "Im granting permissions"
sudo setcap cap_net_raw,cap_net_admin=eip "$EXECUTABLE_PATH"

echo "Im running the traffic analyzer"
echo "Path of executable is: "
echo "$EXECUTABLE_PATH"

"$EXECUTABLE_PATH"