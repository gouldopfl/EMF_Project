# EMF Constitution

## Mission

The Evidence Management Framework (EMF) is a domain-independent platform for acquiring, processing, organizing, validating, preserving, and presenting evidence.

EMF provides the framework for managing evidence. Domain-specific meaning, terminology, rules, and interpretation belong in pluggable models.

## Article I — Domain Independence

The EMF core shall remain independent of any single industry, institution, data source, or evidence domain.

Industry-specific knowledge shall be implemented through replaceable models and adapters.

Core documentation, code, interfaces, examples, and terminology shall use domain-neutral language except where a specific model is being documented.

## Article II — Project Memory

The repository is the authoritative memory of the project.

Architectural decisions, terminology, governance, design rationale, implementation guidance, and significant changes shall be recorded in version-controlled documentation.

No essential project knowledge shall depend solely on an individual’s memory or a temporary communication channel.

## Article III — Documentation and Implementation

Significant architectural or governance decisions shall be documented before or alongside implementation.

Documentation and code shall evolve together so that the repository accurately reflects the current design.

## Article IV — Evidence Integrity

Original source evidence shall be preserved unchanged.

Laboratory processing shall use read-only access whenever possible and shall produce new derived artifacts rather than altering source evidence.

Every derived artifact shall retain sufficient provenance to identify its source and processing history.

## Article V — Stewardship

EMF is maintained through stewardship rather than ownership.

Current and future contributors are responsible for preserving the integrity, transparency, portability, continuity, and domain independence of the framework.

Knowledge shall be prepared so that responsibility can be passed safely to future contributors.

## Article VI — Git Discipline

Each completed documentation task, architectural milestone, or coherent implementation shall be committed to Git with a clear and descriptive message.

The Git history is part of the permanent record of EMF’s evolution.
