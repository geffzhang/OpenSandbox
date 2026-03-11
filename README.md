# OpenSandbox

<p align="center">
  <a href="./README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <a href="https://github.com/AIDotNet/OpenSandbox/stargazers">
    <img alt="GitHub stars" src="https://img.shields.io/github/stars/AIDotNet/OpenSandbox?style=social">
  </a>
  <a href="./LICENSE">
    <img alt="License" src="https://img.shields.io/badge/license-see%20LICENSE-blue">
  </a>
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8.0+-512BD4">
  <img alt="Docker" src="https://img.shields.io/badge/runtime-Docker-2496ED">
</p>

OpenSandbox is a lightweight sandbox service built with `.NET 8` and `Docker`. It provides a unified HTTP API and a .NET SDK for creating, managing, and accessing short-lived isolated runtime environments.

## Overview

This repository currently includes:
- `OpenSandbox.Server`: ASP.NET Core Minimal API server
- `OpenSandbox.Runtime.Docker`: Docker CLI-based sandbox runtime
- `OpenSandbox.Store.FileSystem`: JSON file-based metadata store
- `OpenSandbox.Sdk`: multi-target .NET SDK
- `OpenSandbox.Sample`: minimal SDK sample
- `OpenSandbox.Sdk.Tests`: basic SDK tests

OpenSandbox is suitable for scenarios such as:
- ephemeral code execution environments
- remote tool containers and task runners
- browser-accessible terminal sessions over WebSocket
- higher-level platforms that need a unified sandbox management API over Docker

## Features

### Server API

The current server implementation exposes these core endpoints:
- `GET /health`
- `GET /v1/ping`
- `GET /v1/sandboxes`
- `POST /v1/sandboxes`
- `GET /v1/sandboxes/{id}`
- `GET /v1/sandboxes/{id}/stats`
- `POST /v1/sandboxes/{id}/exec`
- `DELETE /v1/sandboxes/{id}`
- `POST /v1/sandboxes/{id}/pause`
- `POST /v1/sandboxes/{id}/resume`
- `POST /v1/sandboxes/{id}/renew-expiration`
- `GET /v1/sandboxes/{id}/endpoints/{port}`
- `/v1/sandboxes/{id}/proxy/{port}/...`
- `/v1/sandboxes/{id}/terminal/ws`

### .NET SDK

The SDK currently supports:
- API key and bearer token authentication
- listing, creating, retrieving, pausing, resuming, renewing, and deleting sandboxes
- usage stats and in-sandbox command execution
- terminal WebSocket URI generation and connection
- `IServiceCollection` registration extensions

### Runtime and Storage

Default implementations included in this repository:
- runtime: manages containers through the local `docker` command
- store: persists sandbox metadata in a local JSON file

## Architecture

The solution is split into clear layers:
- `src/OpenSandbox.Abstractions`: contracts, models, and core abstractions such as `ISandboxRuntime` and `ISandboxStore`
- `src/OpenSandbox.Runtime.Docker`: Docker-based runtime implementation
- `src/OpenSandbox.Store.FileSystem`: file-based persistence implementation
- `src/OpenSandbox.Server`: HTTP and WebSocket server
- `sdks/dotnet/OpenSandbox.Sdk`: .NET client SDK
- `samples/dotnet/OpenSandbox.Sample`: sample usage
- `tests/OpenSandbox.Sdk.Tests`: SDK tests

The server depends on abstractions and composes the runtime and storage through dependency injection, which makes it possible to replace the underlying runtime or store without changing the API layer.

## Quick Start

### Requirements

- `.NET SDK 8.0+`
- `Docker`
- Windows, Linux, or macOS with a working `docker` command available locally

### Run the server

```bash
dotnet run --project src/OpenSandbox.Server
```

The server reads configuration from:
- `src/OpenSandbox.Server/appsettings.json`
- `src/OpenSandbox.Server/appsettings.Development.json`

After startup, you can access:
- OpenAPI document: `http://localhost:5000/openapi/v1.json`
- health check: `http://localhost:5000/health`

> Use the actual listening address printed by ASP.NET Core if it differs from the examples above.

## Configuration

### `OpenSandbox`

| Key | Default | Description |
| --- | --- | --- |
| `Tokens` | `['dev-sandbox-key']` | Allowed API keys or bearer tokens |
| `CleanupIntervalSeconds` | `30` | Interval for cleaning up expired sandboxes |
| `ProxyUpstreamHost` | `127.0.0.1` | Upstream host used by the server-side proxy |
| `EndpointHost` | `null` | Optional host override for returned endpoint URLs |
| `Proxy.ActivityTimeoutMinutes` | `10` | Activity timeout for proxied requests |

### `DockerRuntime`

| Key | Default | Description |
| --- | --- | --- |
| `DockerCommand` | `docker` | Docker CLI command name or absolute path |
| `PublishedPorts` | `80, 443, 3000, 4173, 5000, 5050, 5173, 8000, 8080, 8081, 8787, 18789` | Ports pre-published to the host when creating containers |

### `FileSystemStore`

| Key | Default | Description |
| --- | --- | --- |
| `StorePath` | `data/sandboxes.json` | Path of the sandbox metadata store file |

## Authentication

Requests under `/v1/*` currently support two authentication modes.

### API key

Use either header:
- `OPEN-SANDBOX-API-KEY`
- `OPEN_SANDBOX_API_KEY`

### Bearer token

Use the header:
- `Authorization: Bearer <token>`

The server validates the provided value against `OpenSandbox:Tokens`.

## API Examples

### Create a sandbox

```bash
curl -X POST http://localhost:5000/v1/sandboxes \
  -H "Content-Type: application/json" \
  -H "OPEN-SANDBOX-API-KEY: dev-sandbox-key" \
  -d '{
    "image": { "uri": "nginx:latest" },
    "timeout": 600,
    "metadata": { "project": "demo" },
    "resourceLimits": { "cpu": "500m", "memory": "512Mi" }
  }'
```

### Get an endpoint

```bash
curl "http://localhost:5000/v1/sandboxes/<id>/endpoints/80?useServerProxy=true" \
  -H "OPEN-SANDBOX-API-KEY: dev-sandbox-key"
```

### Execute a command

```bash
curl -X POST http://localhost:5000/v1/sandboxes/<id>/exec \
  -H "Content-Type: application/json" \
  -H "OPEN-SANDBOX-API-KEY: dev-sandbox-key" \
  -d '{ "command": "echo hello" }'
```

## .NET SDK Example

### Direct client usage

```csharp
using OpenSandbox.Sdk;
using OpenSandbox.Sdk.Models;

var client = new OpenSandboxClient(
    new HttpClient(),
    new OpenSandboxClientOptions
    {
        BaseUrl = "http://localhost:5000",
        AuthenticationMode = OpenSandboxAuthenticationMode.ApiKey,
        ApiKey = "dev-sandbox-key"
    });

var created = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Image = new SandboxImageReference { Uri = "nginx:latest" },
    Timeout = 600
});

var endpoint = await client.GetSandboxEndpointAsync(created.Id, 80, useServerProxy: true);
Console.WriteLine(endpoint?.Url);
```

### DI registration

```csharp
using OpenSandbox.Sdk;
using OpenSandbox.Sdk.Extensions;

services.AddOpenSandboxSdk(options =>
{
    options.BaseUrl = "http://localhost:5000";
    options.AuthenticationMode = OpenSandboxAuthenticationMode.ApiKey;
    options.ApiKey = "dev-sandbox-key";
});
```

## Limitations

The repository is already usable for local development and prototyping, but it is still a minimal implementation. Current boundaries include:
- the default runtime depends on the local Docker CLI, not the Docker Engine SDK
- the default store is a single JSON file and is not designed for high-concurrency or distributed deployment
- port exposure is based on a predefined allowlist, not arbitrary dynamic port publishing
- security controls such as image policy, isolation hardening, and network restrictions are still basic
- automated tests currently focus on the SDK layer more than the server and runtime layers

For a production-grade managed sandbox platform, you will likely want to add:
- stronger authentication and tenant isolation
- audit logs and operation tracing
- database-backed persistence
- finer-grained quota and network controls
- broader automated test coverage for the server and runtime

## Development

### Build

```bash
dotnet build OpenSandbox.slnx
```

### Test

```bash
dotnet test OpenSandbox.slnx
```

### Run the sample

```bash
dotnet run --project samples/dotnet/OpenSandbox.Sample
```

Useful environment variables for the sample:
- `OPEN_SANDBOX_BASE_URL`
- `OPEN_SANDBOX_API_KEY`
- `OPEN_SANDBOX_BEARER_TOKEN`
- `OPEN_SANDBOX_SAMPLE_MODE`

## Open Source Checklist

If you want to turn this repository into a more polished open source project, the next recommended additions are:
- `CONTRIBUTING.md`
- `SECURITY.md`
- package publishing and versioning guidance
- CI workflows for build, test, formatting, and release
- richer API docs and end-to-end examples

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=AIDotNet/OpenSandbox&type=Date)](https://star-history.com/#AIDotNet/OpenSandbox&Date)

## License

This repository includes a `LICENSE` file. Make sure the license content matches your intended public release policy.