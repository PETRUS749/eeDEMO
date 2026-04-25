# AGENTS.md — eeDEMO (LattePanda IOTA + eeCLOUD)

## Purpose

This repository demonstrates a real-world **edge + cloud pattern** using:

- LattePanda IOTA (edge device)
- eeCLOUD (Data-as-a-Service backend)
- Blazor Server (admin dashboard)

The goal is clarity, simplicity, and reproducibility — not over-engineering.

---

## Architecture Overview

The solution is composed of three main projects:

/src
  ├── Shared   → shared models and contracts
  ├── Agent    → device-side logic (runs on LattePanda IOTA)
  └── Admin    → Blazor Server dashboard

---

## Key Concepts

### eeCLOUD

- Data is stored using "memories" (logical collections)
- No schema needs to be created upfront
- Data is written/read via API using memory names

Main logical memories used:

- devices
- desiredConfig
- reportedState
- telemetry

---

## Data Flow

Device Agent → writes telemetry & state → eeCLOUD  
Admin Dashboard → publishes config → eeCLOUD  
Device Agent → reads config → applies changes  

---

## Coding Guidelines

When modifying code:

- Keep code **simple and readable**
- Avoid unnecessary abstractions
- Prefer explicit logic over clever patterns
- Do not introduce new frameworks or dependencies unless necessary

---

## Agent (Device) Rules

- Runs as a background service / loop
- Must:
  - register device
  - fetch configuration periodically
  - apply configuration changes safely
  - push telemetry
  - update reported state

- Configuration flow:
  1. check device-specific config
  2. fallback to group config

---

## Admin Rules

- Must remain lightweight
- No heavy frontend frameworks
- Focus on:
  - device visibility
  - configuration publishing
  - telemetry visualization

---

## Security Rules

- NEVER commit API keys
- Use:
  - .NET User Secrets
  - Environment variables

---

## What NOT to do

- Do not redesign the architecture
- Do not introduce complex patterns
- Do not convert this into a generic framework
- Do not over-optimize prematurely

---

## When suggesting improvements

Focus on:

- reliability (retry, error handling)
- clarity
- production-readiness (without losing simplicity)

Avoid:

- academic suggestions
- unnecessary microservices
- over-engineering

---

## Expected Output from AI agents

When analyzing or modifying this repo:

- Explain reasoning clearly
- Provide concrete suggestions
- Reference actual code paths
- Avoid vague statements

---

## Context

This project is used for:
- a public tutorial series (LattePanda forum)
- demonstration of eeCLOUD capabilities
- developer onboarding example

Changes should respect this purpose.