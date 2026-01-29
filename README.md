# GridFractAL MCP C# SDK (Enterprise Fork)

> **Enterprise fork of the official [Model Context Protocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)** with distributed session management, Redis session storage, and production-ready enhancements.

[![Upstream](https://img.shields.io/badge/upstream-modelcontextprotocol%2Fcsharp--sdk-blue)](https://github.com/modelcontextprotocol/csharp-sdk)
[![Fork](https://img.shields.io/badge/fork-GridFractAL%2Fmcp--csharp--sdk-green)](https://github.com/GridFractAL/mcp-csharp-sdk)

---

## Fork Information

| Property | Value |
|----------|-------|
| **Upstream Repository** | https://github.com/modelcontextprotocol/csharp-sdk |
| **Fork Organization** | [GridFractAL](https://github.com/GridFractAL) |
| **Fork Purpose** | Enterprise session management, distributed deployments |
| **Base Version** | Latest upstream main branch |
| **License** | Apache 2.0 (same as upstream) |

---

## Enterprise Enhancements

This fork extends the official SDK with production-ready features:

### 1. Distributed Session Storage (`ISessionStore`)

Pluggable session storage abstraction enabling:
- **InMemorySessionStore** - Default, backward compatible
- **RedisSessionStore** - Production distributed sessions
- **DistributedSessionStore** - Works with any `IDistributedCache` backend

### 2. Unsealed Core Classes

- `StatefulSessionManager` - Now `public` for enterprise customization
- `StreamableHttpSession` - Now `public` with exposed `UserId` property

### 3. OAuth Session Upgrade Flow

Supports MCP OAuth pattern where anonymous sessions upgrade to authenticated:
```
Discovery (anonymous) → Tool call (requires auth) → OAuth → Session upgrade
```

### 4. Session Persistence & Recovery

- Sessions survive server restarts (with Redis/distributed store)
- Multi-instance deployments share session state
- Server affinity tracking for load-balanced scenarios

---

## Packages

This SDK consists of three main packages:

| Package | Description |
|---------|-------------|
| **ModelContextProtocol** | Main package with hosting and DI extensions |
| **ModelContextProtocol.AspNetCore** | HTTP-based MCP servers + Enterprise extensions |
| **ModelContextProtocol.Core** | Low-level client/server APIs, minimal dependencies |

> [!NOTE]
> This project is in preview; breaking changes can be introduced without prior notice.

---

## About MCP

The Model Context Protocol (MCP) is an open protocol that standardizes how applications provide context to Large Language Models (LLMs). It enables secure integration between LLMs and various data sources and tools.

For more information about MCP:

- [Official Documentation](https://modelcontextprotocol.io/)
- [Protocol Specification](https://modelcontextprotocol.io/specification/)
- [GitHub Organization](https://github.com/modelcontextprotocol)

---

## Quick Start

### Basic Server (Same as Upstream)

```csharp
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

[McpServerToolType]
public static class EchoTool
{
    [McpServerTool, Description("Echoes the message back to the client.")]
    public static string Echo(string message) => $"hello {message}";
}
```

### With Redis Session Storage (Enterprise)

```csharp
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using ModelContextProtocol.AspNetCore.Enterprise;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer()
    .WithStreamableHttpServerTransport();

// Enterprise: Add Redis session storage
builder.Services.AddMcpRedisSessionStore(options =>
{
    options.Configuration = "localhost:6379";
    options.KeyPrefix = "mcp:session:";
    options.DefaultTtl = TimeSpan.FromHours(2);
});

var app = builder.Build();
app.MapMcp();
await app.RunAsync();
```

### Client Example

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "Everything",
    Command = "npx",
    Arguments = ["-y", "@modelcontextprotocol/server-everything"],
});

var client = await McpClient.CreateAsync(clientTransport);

// Print the list of tools available from the server.
foreach (var tool in await client.ListToolsAsync())
{
    Console.WriteLine($"{tool.Name} ({tool.Description})");
}

// Execute a tool
var result = await client.CallToolAsync(
    "echo",
    new Dictionary<string, object?>() { ["message"] = "Hello MCP!" },
    cancellationToken: CancellationToken.None);

Console.WriteLine(result.Content.OfType<TextContentBlock>().First().Text);
```

---

## Upstream Synchronization

This fork maintains compatibility with upstream. To sync:

```bash
git remote add upstream https://github.com/modelcontextprotocol/csharp-sdk
git fetch upstream
git merge upstream/main
# Resolve conflicts (primarily in StatefulSessionManager.cs, StreamableHttpSession.cs)
```

---

## Differences from Upstream

| Feature | Upstream | This Fork |
|---------|----------|-----------|
| Session Storage | In-memory only | Pluggable (Memory, Redis, IDistributedCache) |
| Session Persistence | ❌ Lost on restart | ✅ Survives restart |
| Multi-Instance | ❌ Not supported | ✅ Shared session state |
| Core Class Access | `internal sealed` | `public` (extensible) |
| OAuth Flow | Basic | Enhanced session upgrade |
| CI/CD | GitHub Actions | Integrated with GridFractAL monorepo |

---

## Attribution

- **Original Work**: Microsoft Corporation and contributors  
- **Enterprise Extensions**: GridFractAL Organization
- **Based On**: [mcpdotnet](https://github.com/PederHP/mcpdotnet) by Peder Holdgaard Pedersen

---

## License

This project is licensed under the [Apache License 2.0](LICENSE), same as the upstream repository.
