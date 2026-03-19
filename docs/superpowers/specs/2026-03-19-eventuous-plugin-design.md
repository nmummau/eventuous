# Eventuous Claude Code Plugin — Design Spec

**Date:** 2026-03-19
**Status:** Draft

## Goal

Create a Claude Code plugin that packages Eventuous skills and an expert agent so that developers building event-sourced .NET applications with Eventuous can install it and get contextual guidance directly in their IDE.

## Plugin Location

`plugin/` directory at the repository root. The existing `skills/` directory content moves into `plugin/skills/`.

## Installation

Users install from the GitHub repository:

```bash
claude plugin add https://github.com/Eventuous/eventuous --path plugin
```

## Plugin Manifest

`plugin/.claude-plugin/plugin.json`:

```json
{
  "name": "eventuous",
  "version": "1.0.0",
  "description": "Skills and agents for building event-sourced .NET applications with Eventuous",
  "author": {
    "name": "Eventuous",
    "url": "https://eventuous.dev"
  },
  "homepage": "https://eventuous.dev",
  "repository": "https://github.com/Eventuous/eventuous",
  "license": "Apache-2.0",
  "keywords": ["event-sourcing", "ddd", "dotnet", "cqrs", "eventuous"]
}
```

Versioning is manual — updated by hand when releasing.

## Directory Structure

```
plugin/
├── .claude-plugin/
│   └── plugin.json
├── README.md
├── agents/
│   └── eventuous-expert.md
└── skills/
    ├── eventuous/
    │   └── SKILL.md
    ├── eventuous-postgres/
    │   └── SKILL.md
    ├── eventuous-kurrentdb/
    │   └── SKILL.md
    ├── eventuous-mongodb/
    │   └── SKILL.md
    ├── eventuous-rabbitmq/
    │   └── SKILL.md
    ├── eventuous-kafka/
    │   └── SKILL.md
    ├── eventuous-sqlserver/
    │   └── SKILL.md
    ├── eventuous-google-pubsub/
    │   └── SKILL.md
    ├── eventuous-azure-servicebus/
    │   └── SKILL.md
    └── eventuous-gateway/
        └── SKILL.md
```

## Skills

### Source Migration

The 10 existing markdown files in `/skills/` move into the plugin structure:

| Source file | Target |
|---|---|
| `skills/eventuous.md` | `plugin/skills/eventuous/SKILL.md` |
| `skills/eventuous-postgres.md` | `plugin/skills/eventuous-postgres/SKILL.md` |
| `skills/eventuous-kurrentdb.md` | `plugin/skills/eventuous-kurrentdb/SKILL.md` |
| `skills/eventuous-mongodb.md` | `plugin/skills/eventuous-mongodb/SKILL.md` |
| `skills/eventuous-rabbitmq.md` | `plugin/skills/eventuous-rabbitmq/SKILL.md` |
| `skills/eventuous-kafka.md` | `plugin/skills/eventuous-kafka/SKILL.md` |
| `skills/eventuous-sqlserver.md` | `plugin/skills/eventuous-sqlserver/SKILL.md` |
| `skills/eventuous-google-pubsub.md` | `plugin/skills/eventuous-google-pubsub/SKILL.md` |
| `skills/eventuous-azure-servicebus.md` | `plugin/skills/eventuous-azure-servicebus/SKILL.md` |
| `skills/eventuous-gateway.md` | `plugin/skills/eventuous-gateway/SKILL.md` |

The original `/skills/` directory is removed after migration.

### Frontmatter Format

Each `SKILL.md` gets YAML frontmatter with `name` and `description`. The description controls when Claude Code activates the skill.

Pattern: `"Use when [trigger context]. Covers [specific topics]."`

#### Core Skill

```yaml
---
name: eventuous
description: "Use when building event-sourced .NET applications with Eventuous. Covers domain modeling (aggregates, state, events), command services (aggregate-based and functional), event stores, subscriptions, producers, type mapping, serialization, and diagnostics. Defaults to KurrentDB as the recommended event store unless the user specifies otherwise."
---
```

The core skill explicitly recommends KurrentDB as the default event store.

#### Integration Skills

```yaml
# eventuous-postgres
---
name: eventuous-postgres
description: "Use when configuring or implementing PostgreSQL integration with Eventuous. Covers PostgreSQL event store, subscriptions (all-stream and per-stream), checkpoint storage, and projections."
---

# eventuous-kurrentdb
---
name: eventuous-kurrentdb
description: "Use when configuring or implementing KurrentDB (EventStoreDB) integration with Eventuous. Covers KurrentDB event store, subscriptions (all-stream, per-stream, persistent), producer, and registration."
---

# eventuous-mongodb
---
name: eventuous-mongodb
description: "Use when configuring or implementing MongoDB integration with Eventuous. Covers MongoDB checkpoint storage and projections (typed and untyped)."
---

# eventuous-rabbitmq
---
name: eventuous-rabbitmq
description: "Use when configuring or implementing RabbitMQ integration with Eventuous. Covers RabbitMQ producer, subscriptions, exchange/queue configuration, and connection setup."
---

# eventuous-kafka
---
name: eventuous-kafka
description: "Use when configuring or implementing Kafka integration with Eventuous. Covers Kafka producer and subscription configuration."
---

# eventuous-sqlserver
---
name: eventuous-sqlserver
description: "Use when configuring or implementing SQL Server integration with Eventuous. Covers SQL Server event store, subscriptions, and checkpoint storage."
---

# eventuous-google-pubsub
---
name: eventuous-google-pubsub
description: "Use when configuring or implementing Google Cloud Pub/Sub integration with Eventuous. Covers Pub/Sub producer and subscription configuration."
---

# eventuous-azure-servicebus
---
name: eventuous-azure-servicebus
description: "Use when configuring or implementing Azure Service Bus integration with Eventuous. Covers Azure Service Bus producer and subscription configuration."
---

# eventuous-gateway
---
name: eventuous-gateway
description: "Use when implementing cross-context event routing with Eventuous Gateway. Covers gateway configuration, subscription-to-producer bridging, and event transformations."
---
```

## Agent: Eventuous Expert

`plugin/agents/eventuous-expert.md`

### Frontmatter

```yaml
---
name: eventuous-expert
description: Use this agent when the task requires deep Eventuous knowledge spanning multiple concerns — designing event-sourced systems, implementing aggregates/services/subscriptions, configuring infrastructure integrations, debugging Eventuous issues, or choosing between approaches. Examples:

<example>
Context: User is starting a new event-sourced service with Eventuous
user: "I need to build an order management system with PostgreSQL for the event store and MongoDB for read models"
assistant: "I'll use the eventuous-expert agent to help design and implement this system."
<commentary>
Task spans domain modeling, PostgreSQL event store, and MongoDB projections — needs cross-cutting Eventuous expertise.
</commentary>
</example>

<example>
Context: User is debugging an Eventuous subscription issue
user: "My subscription keeps replaying events from the beginning instead of from the checkpoint"
assistant: "I'll use the eventuous-expert agent to diagnose the checkpoint issue."
<commentary>
Debugging Eventuous subscription behavior requires deep knowledge of checkpoint management and subscription lifecycle.
</commentary>
</example>

<example>
Context: User is choosing between Eventuous approaches
user: "Should I use aggregate-based or functional command services for my use case?"
assistant: "I'll use the eventuous-expert agent to help evaluate the trade-offs."
<commentary>
Architectural decision about Eventuous patterns requires opinionated guidance.
</commentary>
</example>

model: inherit
color: cyan
tools: ["Read", "Grep", "Glob", "Bash", "Write", "Edit"]
---
```

### System Prompt

The agent body defines its role and behavior:

- **Role**: Eventuous specialist helping design, implement, and debug event-sourced .NET applications
- **Opinionated defaults**: Recommends KurrentDB as the default event store, prefers functional command services for simple cases, uses `IEventReader`/`IEventWriter` extension methods (not deprecated `IAggregateStore`)
- **Knowledge base**: All plugin skills are available to the agent automatically (same plugin); the system prompt directs it to read relevant skill files from `${CLAUDE_PLUGIN_ROOT}/skills/` when it needs reference material
- **Process**: Understand the user's context, identify which Eventuous concerns are involved, reference the relevant skills, provide concrete code examples following Eventuous conventions

## Skill Body Content

The existing markdown content in `/skills/*.md` is copied verbatim into each `SKILL.md` — no body modifications needed. The only change is prepending YAML frontmatter (`name` and `description`) to each file.

## CLAUDE.md

No changes to `CLAUDE.md`. The plugin is a separate install path for external users. Contributors working in this repo already have the skills content available via the project's existing documentation.

## Versioning

Plugin version in `plugin.json` is independent of the NuGet library version. Update it manually when the plugin content changes materially (new skills, significant content updates, agent changes). Minor fixes don't require a bump.

## Migration Notes

The only reference to the old `/skills/` path outside the spec is a `wc` permission in `.claude/settings.local.json` (local dev convenience). This will be updated or removed during migration. No CI, documentation, or shared configuration references the old path.

## Verification

After implementation, verify by:
1. Install the plugin locally: `claude plugin add --path ./plugin`
2. Start a new session and confirm all 10 skills appear in the skill list
3. Ask an Eventuous question (e.g., "how do I set up a PostgreSQL event store with Eventuous?") and confirm the relevant skill activates
4. Ask a cross-cutting question and confirm the eventuous-expert agent is dispatched

## README

`plugin/README.md` documents:
- What the plugin provides (10 skills + 1 agent)
- Installation command
- List of available skills with brief descriptions
- How skills activate (contextually, based on what you're working on)
