# OpenSandbox

<p align="center">
  <a href="./README.md">English</a>
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

OpenSandbox 是一个基于 `.NET 8` 与 `Docker` 的轻量级沙箱服务，提供统一的 HTTP API 与 .NET SDK，用于创建、管理和访问短生命周期的隔离运行环境。

## 项目概览

当前仓库包含：
- `OpenSandbox.Server`：ASP.NET Core Minimal API 服务端
- `OpenSandbox.Runtime.Docker`：基于 Docker CLI 的沙箱运行时
- `OpenSandbox.Store.FileSystem`：基于 JSON 文件的元数据存储
- `OpenSandbox.Sdk`：多目标框架 .NET SDK
- `OpenSandbox.Sample`：最小示例程序
- `OpenSandbox.Sdk.Tests`：基础 SDK 测试

适用场景包括：
- 临时代码执行环境
- 远程工具容器与任务运行容器
- 通过 WebSocket 暴露浏览器终端
- 需要统一 Docker 沙箱管理 API 的上层平台

## 功能

### 服务端 API

当前实现的核心接口：
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

当前 SDK 支持：
- API Key 与 Bearer Token 两种鉴权方式
- 沙箱列表、创建、查询、暂停、恢复、续期、删除
- 使用情况统计与命令执行
- 终端 WebSocket URI 构建与连接
- `IServiceCollection` 扩展注册

### 运行时与存储

仓库内默认实现：
- 运行时：通过本机 `docker` 命令管理容器
- 存储：通过本地 JSON 文件持久化沙箱元数据

## 架构

解决方案按职责分层：
- `src/OpenSandbox.Abstractions`：核心抽象、契约与模型，如 `ISandboxRuntime`、`ISandboxStore`
- `src/OpenSandbox.Runtime.Docker`：Docker 运行时实现
- `src/OpenSandbox.Store.FileSystem`：文件存储实现
- `src/OpenSandbox.Server`：HTTP / WebSocket 服务端
- `sdks/dotnet/OpenSandbox.Sdk`：.NET 客户端 SDK
- `samples/dotnet/OpenSandbox.Sample`：使用示例
- `tests/OpenSandbox.Sdk.Tests`：SDK 测试

服务端依赖抽象层，并通过依赖注入组合运行时与存储实现，因此可以在不修改 API 层的前提下替换底层实现。

## 快速开始

### 环境要求

- `.NET SDK 8.0+`
- `Docker`
- Windows、Linux 或 macOS，且本机可直接执行 `docker`

### 启动服务

```bash
dotnet run --project src/OpenSandbox.Server
```

服务默认读取：
- `src/OpenSandbox.Server/appsettings.json`
- `src/OpenSandbox.Server/appsettings.Development.json`

启动后可访问：
- OpenAPI 文档：`http://localhost:5000/openapi/v1.json`
- 健康检查：`http://localhost:5000/health`

> 如果实际监听地址不同，请以 ASP.NET Core 启动输出为准。

## 配置

### `OpenSandbox`

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `Tokens` | `['dev-sandbox-key']` | 允许的 API Key 或 Bearer Token |
| `CleanupIntervalSeconds` | `30` | 清理过期沙箱的后台轮询间隔 |
| `ProxyUpstreamHost` | `127.0.0.1` | 服务端代理访问上游宿主机地址 |
| `EndpointHost` | `null` | 返回 endpoint 时可覆盖主机名 |
| `Proxy.ActivityTimeoutMinutes` | `10` | 代理请求活动超时 |

### `DockerRuntime`

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `DockerCommand` | `docker` | Docker CLI 命令名或绝对路径 |
| `PublishedPorts` | `80, 443, 3000, 4173, 5000, 5050, 5173, 8000, 8080, 8081, 8787, 18789` | 创建容器时预映射到宿主机的端口列表 |

### `FileSystemStore`

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `StorePath` | `data/sandboxes.json` | 沙箱元数据存储文件路径 |

## 鉴权

`/v1/*` 路由当前支持两种认证模式。

### API Key

请求头可使用：
- `OPEN-SANDBOX-API-KEY`
- `OPEN_SANDBOX_API_KEY`

### Bearer Token

请求头：
- `Authorization: Bearer <token>`

服务端会使用 `OpenSandbox:Tokens` 中的值做校验。

## API 示例

### 创建沙箱

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

### 获取端点

```bash
curl "http://localhost:5000/v1/sandboxes/<id>/endpoints/80?useServerProxy=true" \
  -H "OPEN-SANDBOX-API-KEY: dev-sandbox-key"
```

### 执行命令

```bash
curl -X POST http://localhost:5000/v1/sandboxes/<id>/exec \
  -H "Content-Type: application/json" \
  -H "OPEN-SANDBOX-API-KEY: dev-sandbox-key" \
  -d '{ "command": "echo hello" }'
```

## .NET SDK 示例

### 直接实例化客户端

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

### 使用依赖注入注册 SDK

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

## 当前边界

这个仓库已经适合本地开发与原型验证，但整体仍是最小可用实现，主要边界包括：
- 默认运行时依赖本机 Docker CLI，而不是 Docker Engine SDK
- 默认存储是单 JSON 文件，不适合高并发或分布式部署
- 端口暴露依赖预设白名单，不是任意动态发布
- 镜像策略、隔离强化、网络限制等安全能力仍较基础
- 自动化测试当前主要覆盖 SDK 层，服务端与运行时层覆盖还不够

如果你的目标是生产级托管沙箱平台，建议优先补充：
- 更强的鉴权与租户隔离
- 审计日志与操作追踪
- 基于数据库的持久化方案
- 更细粒度的资源配额与网络控制
- 更完整的服务端与运行时自动化测试

## 开发

### 构建

```bash
dotnet build OpenSandbox.slnx
```

### 测试

```bash
dotnet test OpenSandbox.slnx
```

### 运行示例

```bash
dotnet run --project samples/dotnet/OpenSandbox.Sample
```

示例程序可使用这些环境变量：
- `OPEN_SANDBOX_BASE_URL`
- `OPEN_SANDBOX_API_KEY`
- `OPEN_SANDBOX_BEARER_TOKEN`
- `OPEN_SANDBOX_SAMPLE_MODE`

## 开源补充建议

如果你要把仓库打磨成更标准的开源项目，建议下一步增加：
- `CONTRIBUTING.md`
- `SECURITY.md`
- 包发布与版本管理说明
- 构建、测试、格式检查和发布的 CI 流程
- 更完整的 API 文档与端到端示例

## Star 历史

[![Star History Chart](https://api.star-history.com/svg?repos=AIDotNet/OpenSandbox&type=Date)](https://star-history.com/#AIDotNet/OpenSandbox&Date)

## 许可证

仓库已包含 `LICENSE` 文件。对外发布前，请确认许可证内容与你的发布策略一致。