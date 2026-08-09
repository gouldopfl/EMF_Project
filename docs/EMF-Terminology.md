# EMF Terminology Dictionary

## Purpose

This document defines the authoritative terminology used throughout EMF.

The purpose is to provide a consistent vocabulary for Stewards, extension developers, maintainers, architects, documentation, and the EMF community.

Terminology should describe stable concepts rather than temporary implementation technologies.

---

## 1. Core Concepts

### Steward

A person who uses EMF to accomplish a mission through their domain expertise.

**Status:** Active

**Former term:** Shepherd

---

### Mission

The outcome or objective that a Steward is attempting to accomplish using EMF.

---

### Intent

The Steward's expressed description of what they want EMF to accomplish.

---

### Policy

A rule, constraint, or organizational requirement governing how EMF may accomplish an Intent or Mission.

---

## 2. Execution Concepts

### Workflow

A defined sequence or structure of Activities used to accomplish a Mission.

---

### Activity

A meaningful unit of workflow behavior.

An Activity should expose a simple, consistent contract and should not require extension developers to understand EMF's internal execution infrastructure.

---

### Work Unit

A discrete, independently executable portion of an Activity that can be assigned to a Worker and checkpointed independently.

---

### Worker

An execution resource responsible for processing a Work Unit.

A Worker may be implemented as a thread, process, container, VM, or other execution mechanism.

The implementation is intentionally not part of the definition.

---

### Workflow Execution Coordinator

The orchestration boundary responsible for initiating and coordinating the execution of a Workflow, establishing its execution context, and directing execution or recovery according to the current persisted execution state.

---

### Workflow Runner

The execution component responsible for executing the Activities of a Workflow in their defined order, using persisted checkpoints to determine execution state and record progress.

---

### Workflow Recovery Coordinator

The orchestration component responsible for coordinating recovery evaluation for an existing Workflow execution, obtaining the decision produced by the Workflow Recovery Policy, and recording the resulting recovery state.

---

### Workflow Recovery Policy

A policy that determines how EMF should respond to an interrupted or failed Workflow execution.

**Architectural relationship:**

> Recovery Policy decides. Recovery Coordinator records recovery state. Execution Coordinator directs execution. Workflow Runner executes.

---

## 3. Integration and Extension Concepts

### Connector

A boundary through which EMF communicates with an external system or service.

---

### Extension

A component that adds capability to EMF without requiring changes to the EMF core.

---

## 4. AI and Intelligence Concepts

### EISL

The EMF provider-independent intelligence/service boundary.

EISL allows EMF to request capabilities without coupling the platform to a particular AI or intelligence provider.

---

### AI Engine

A technology capable of providing one or more intelligence-related Capabilities.

An AI Engine is an implementation detail and is not the architectural identity of a Capability.

---

## 5. Architecture Principles

### Stable Vocabulary

EMF contracts should use consistent terminology across modules.

### Stable Behavior

Equivalent operations should behave consistently across EMF APIs and extensions.

### Technology Independence

Architectural concepts should not be defined by a particular cloud provider, programming language, AI provider, or infrastructure technology.

### Mission First

Technical execution exists to serve the Mission.

### Simplicity

Complexity should remain inside the platform whenever it can be hidden without reducing capability or control.

### Replaceability

Implementations should be replaceable without requiring changes to the Mission, Intent, or Steward experience.
EOFclear && cd ~/EMF_Project && cat > docs/EMF-Terminology.md <<'EOF'
# EMF Terminology Dictionary

## Purpose

This document defines the authoritative terminology used throughout EMF.

The purpose is to provide a consistent vocabulary for Stewards, extension developers, maintainers, architects, documentation, and the EMF community.

Terminology should describe stable concepts rather than temporary implementation technologies.

---

## 1. Core Concepts

### Steward

A person who uses EMF to accomplish a mission through their domain expertise.

**Status:** Active

**Former term:** Shepherd

---

### Mission

The outcome or objective that a Steward is attempting to accomplish using EMF.

---

### Intent

The Steward's expressed description of what they want EMF to accomplish.

---

### Policy

A rule, constraint, or organizational requirement governing how EMF may accomplish an Intent or Mission.

---

## 2. Execution Concepts

### Workflow

A defined sequence or structure of Activities used to accomplish a Mission.

---

### Activity

A meaningful unit of workflow behavior.

An Activity should expose a simple, consistent contract and should not require extension developers to understand EMF's internal execution infrastructure.

---

### Work Unit

A discrete, independently executable portion of an Activity that can be assigned to a Worker and checkpointed independently.

---

### Worker

An execution resource responsible for processing a Work Unit.

A Worker may be implemented as a thread, process, container, VM, or other execution mechanism.

The implementation is intentionally not part of the definition.

---

### Workflow Execution Coordinator

The orchestration boundary responsible for initiating and coordinating the execution of a Workflow, establishing its execution context, and directing execution or recovery according to the current persisted execution state.

---

### Workflow Runner

The execution component responsible for executing the Activities of a Workflow in their defined order, using persisted checkpoints to determine execution state and record progress.


**Architectural relationship:**

> Recovery Policy decides. Recovery Coordinator records recovery state. Execution Coordinator directs execution. Workflow Runner executes.

---

## 3. Integration and Extension Concepts

### Connector

A boundary through which EMF communicates with an external system or service.

---

### Extension

A component that adds capability to EMF without requiring changes to the EMF core.

---

## 4. AI and Intelligence Concepts

### EISL

The EMF provider-independent intelligence/service boundary.

EISL allows EMF to request capabilities without coupling the platform to a particular AI or intelligence provider.

---

### AI Engine

A technology capable of providing one or more intelligence-related Capabilities.

An AI Engine is an implementation detail and is not the architectural identity of a Capability.

---

## 5. Architecture Principles

### Stable Vocabulary

EMF contracts should use consistent terminology across modules.

### Stable Behavior

Equivalent operations should behave consistently across EMF APIs and extensions.

### Technology Independence

Architectural concepts should not be defined by a particular cloud provider, programming language, AI provider, or infrastructure technology.

### Mission First

Technical execution exists to serve the Mission.

### Simplicity

Complexity should remain inside the platform whenever it can be hidden without reducing capability or control.

### Replaceability

Implementations should be replaceable without requiring changes to the Mission, Intent, or Steward experience.
