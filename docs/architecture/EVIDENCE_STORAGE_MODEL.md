# Evidence Storage Model

## Purpose

The Evidence Storage Model defines how EMF persists evidence objects while keeping
the core evidence framework independent from any storage technology.

The storage layer must support:

- Artifacts
- Provenance
- Content fingerprints
- Relationships between artifacts
- Extensible metadata

## Design Principles

1. The EMF.Core domain model does not depend on storage technology.
2. Storage implementations satisfy IEvidenceRepository.
3. Evidence relationships are first-class objects.
4. Metadata must support future domains without schema redesign.
5. Audit history and provenance must be preserved.

## Logical Model

### Artifact

Represents a discovered or generated evidence object.

Attributes:

- ArtifactId
- Name
- ArtifactType
- CreatedUtc
- ContentFingerprint
- Metadata

### Provenance

Records where an artifact came from and how it was recorded.

Attributes:

- ArtifactId
- Source
- RecordedBy
- Properties

### Relationship

Represents a directed connection between artifacts.

Attributes:

- SourceArtifactId
- TargetArtifactId
- RelationshipType
- CreatedUtc
- Properties

Examples:

- Contains
- DerivedFrom
- GeneratedFrom
- Supersedes
- References

## Repository Abstraction

The storage boundary is defined by:

IEvidenceRepository

Implementations may include:

- SQLite
- Cloud databases
- File-backed stores
- Other storage providers

## Initial SQLite Direction

The first production repository should preserve the logical model:

- Artifacts table
- Provenance table
- Relationships table
- Flexible metadata/property tables

The schema should follow the evidence model, not define it.

