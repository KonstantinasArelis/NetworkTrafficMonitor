# Project Context: High-Performance Network Traffic Tracker

## Project Overview
Building a local network usage tracker from scratch. The application sniffs raw network packets at the OS interface level, parses the headers (Ethernet, IPv4, TCP/UDP), aggregates data volume per IP/Port, and stores it for UI presentation.

## Target Environment
* **OS:** Linux (Ubuntu/Debian based) with Docker present.
* **Target Interface:** Physical network card (e.g., `enp5s0`). Ignoring `docker0`, `lo`, and `any` to prevent double-counting traffic.

## Architectural Roadmap
* **Phase 1 (Current):** .NET 10 (C#) Backend + SQLite (WAL mode) + gRPC Server Streaming + Desktop UI (Avalonia).
* **Phase 2 (Future):** C++ backend + WebSockets + Web UI (React).

## Core Engineering Decisions & Constraints
1.  **Zero-Allocation Capture:** We are explicitly avoiding `.NET` Garbage Collection pauses. 
    * We do **not** use SharpPcap's `OnPacketArrival` event or the `RawCapture` class (which copies data via `.ToArray()`).
    * We use a dedicated background thread running a **Polling Loop** (`while(true)`) with `GetNextPacket(out PacketCapture capture)`.
    * We process data exclusively via `ReadOnlySpan<byte>` pointing directly to the unmanaged `libpcap` shared kernel ring buffer (`PACKET_MMAP`).
2.  **Manual Header Parsing:** We are taking the hardcore path, bypassing `PacketDotNet` for header parsing. We manually slice the `ReadOnlySpan<byte>` and use bitwise math and `BinaryPrimitives` to extract IPs, Protocols, and Ports to maximize performance and learning.
3.  **Linux Permissions Strategy:** We do **not** run the .NET app as root via `sudo dotnet run` (which breaks IDE file permissions). Instead, we compile the binary as a normal user and use `setcap` to grant `cap_net_raw` privileges to the executable.
4.  **Producer-Consumer Model:** To prevent dropping packets in the OS buffer, the capture loop must remain ultra-fast. Heavy lifting (aggregation, DB writes) must be offloaded to a background `.NET Channel<T>` (not Kafka/RabbitMQ due to serialization overhead).

## Current Codebase State

### 1. `run.sh` (The Build & Execution Script)
Automates building and applying Linux capabilities to avoid IDE permission lockouts.
```bash
#!/bin/bash
set -e
echo "🔨 Building the .NET project..."
dotnet build

EXECUTABLE_PATH="./bin/Debug/net10.0/TrafficAnalyzer" # Update if name changes

echo "🔐 Granting network capture capabilities to the binary..."
sudo setcap cap_net_raw,cap_net_admin=eip "$EXECUTABLE_PATH"

echo "🚀 Starting Traffic Analyzer (Running without sudo!)..."
echo "------------------------------------------------------"
"$EXECUTABLE_PATH"