#!/bin/bash

set -e

echo "Im building"
dotnet build

EXECUTABLE_PATH="./bin/Debug/net10.0/TrafficAnalyzer"

echo "Im granting permissions"
sudo setcap cap_net_raw,cap_net_admin=eip "$EXECUTABLE_PATH"

echo "Im running the traffic analyzer"
"$EXECUTABLE_PATH"