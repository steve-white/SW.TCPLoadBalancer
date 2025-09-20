# SW.TCPLoadBalancer

- [SW.TCPLoadBalancer](#swtcploadbalancer)
- [Overview](#overview)
- [Configuration](#configuration)
- [Running](#running)
  - [Local](#local)
- [Logging](#logging)
- [Building](#building)
  - [Local](#local-1)
  - [Container](#container)


# Overview

This service provides a layer 4 TCP load balancer (LB) with health checking and automatic failover if a backend service instance goes offline.
Load balancing is connection based using a time-based algorithm.

Process:
- LB: Starts and reads configuration
- LB: Connects to configured backend services with watchdog connections
- LB: Listens on configured interface and port
- Client: Connects to load balancer
- LB: Looks up an active watchdog connection to a backend service and uses connection details from that to make a new connection to a backend service for that client
- LB: Proxies data bi-directionally between client and backend service until either side closes the connection

![Component Diagram](./doc/tcp-loadbalancer-component.png)

Notes:
- Client: Upon client disconnection, the relating connection to the backend service is closed
- Backend service: Upon backend disconnection, the load balancer connection closes the client connection
- LB: When a watchdog connection closes, the backend service is removed and not used for new client connections, until it comes back up.

TODO:
- Upon read/write failure of a client backend connection, it should be removed from the watchdog list.
- Unit tests
- Additional integration tests

# Configuration

Configuration is via `appsettings.json`

See `ServerOptions` section in the provided `appsettings.json` for examples of how to set backend connection details.

# Running

## Local

Run `SW.TCPLoadBalancer.Server.exe`

# Logging

The server process logs to: `./logs/*.txt`
Logs are rotated when they reach 5MiB, up to a maximum of 10 files.

# Building

## Local

.NET 8 is required.

```
dotnet build
```

## Container

TODO - fix

Builds will work on Linux or Windows (via cross-compilation) and will generate a single container
artifact which is self-contained, and can be deployed anywhere.

- Install Docker Desktop or Rancher Desktop (preferred)
- From the repo root:

```
docker build --no-cache -t sw-tcploadbalancer:latest .
docker run -d -p 3400:3400 --name sw-tcploadbalancer sw-tcploadbalancer:latest
```
