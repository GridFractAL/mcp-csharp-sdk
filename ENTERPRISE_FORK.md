# Enterprise Fork Documentation

## Fork Overview

| Property | Value |
|----------|-------|
| **Fork Organization** | [GridFractAL](https://github.com/GridFractAL) |
| **Fork Repository** | https://github.com/GridFractAL/mcp-csharp-sdk |
| **Upstream Repository** | https://github.com/modelcontextprotocol/csharp-sdk |
| **Fork Date** | 2026-01-29 |
| **Base Commit** | Latest main branch |
| **Primary Branch** | `main` (enterprise enhancements) |

---

## Purpose

This fork extends the official MCP C# SDK with enterprise features required for production deployments:

1. **Distributed Session Management** - Sessions persist across restarts and scale horizontally
2. **Pluggable Storage** - Redis, IDistributedCache, or custom backends
3. **Extensible Architecture** - Unsealed core classes for enterprise customization
4. **OAuth Flow Support** - Anonymous-to-authenticated session upgrades

---

## Enterprise Enhancements

### Phase 1: Session Storage Abstraction

| File | Purpose | Status |
|------|---------|--------|
| `src/ModelContextProtocol.AspNetCore/Enterprise/ISessionStore.cs` | Session storage interface | 🔲 Pending |
| `src/ModelContextProtocol.AspNetCore/Enterprise/SessionMetadata.cs` | Session metadata record | 🔲 Pending |
| `src/ModelContextProtocol.AspNetCore/Enterprise/InMemorySessionStore.cs` | Default in-memory implementation | 🔲 Pending |
| `src/ModelContextProtocol.AspNetCore/Enterprise/RedisSessionStore.cs` | Redis distributed sessions | 🔲 Pending |
| `src/ModelContextProtocol.AspNetCore/Enterprise/DistributedSessionStore.cs` | IDistributedCache backend | 🔲 Pending |
| `src/ModelContextProtocol.AspNetCore/Enterprise/SessionStoreExtensions.cs` | DI registration helpers | 🔲 Pending |

### Phase 2: Core Class Modifications

| File | Change | Status |
|------|--------|--------|
| `src/ModelContextProtocol.AspNetCore/StatefulSessionManager.cs` | Unseal, add ISessionStore integration | 🔲 Pending |
| `src/ModelContextProtocol.AspNetCore/StreamableHttpSession.cs` | Unseal, expose UserId property | 🔲 Pending |
| `src/ModelContextProtocol.AspNetCore/StreamableHttpSession.cs` | OAuth session upgrade (HasSameUserId) | 🔲 Pending |

### Phase 3: Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `StackExchange.Redis` | 2.8.* | Redis session storage |

---

## Removed from Upstream

The following files/features from upstream have been removed as they're managed by the parent GridFractAL monorepo:

| Removed | Reason |
|---------|--------|
| `.github/workflows/*` | CI/CD managed by GridFractAL monorepo |
| `.github/dependabot.yml` | Dependency management at monorepo level |
| `.github/copilot-instructions.md` | Not needed |
| `.devcontainer/` | Dev environment managed at monorepo level |
| `Makefile` | Build managed by GridFractAL |
| `CONTRIBUTING.md` | Contributing guidelines at monorepo level |
| `SECURITY.md` | Security policy at monorepo level |

---

## Upstream Synchronization

### Adding Upstream Remote

```bash
git remote add upstream https://github.com/modelcontextprotocol/csharp-sdk
git fetch upstream
```

### Merging Upstream Changes

```bash
git checkout main
git fetch upstream
git merge upstream/main
# Resolve conflicts in:
#   - StatefulSessionManager.cs
#   - StreamableHttpSession.cs
#   - Directory.Packages.props (if Redis dependency conflicts)
```

### Expected Conflict Files

| File | Reason |
|------|--------|
| `StatefulSessionManager.cs` | Unsealed + ISessionStore integration |
| `StreamableHttpSession.cs` | Unsealed + UserId exposure + OAuth flow |
| `Directory.Packages.props` | Added Redis dependency |
| `README.md` | Fork documentation |

---

## Implementation Reference

For detailed implementation code, see:
- [ENTERPRISE_REVIEW.md](../../docs/plan/mcp/ENTERPRISE_REVIEW.md) - Full implementation guide
- [ENTERPRISE_REVIEW ADDON.md](../../docs/plan/mcp/ENTERPRISE_REVIEW%20ADDON.md) - OAuth flow enhancement

---

## License

Apache License 2.0 (same as upstream)

**Original Copyright**: Microsoft Corporation and contributors  
**Fork Extensions**: GridFractAL Organization
