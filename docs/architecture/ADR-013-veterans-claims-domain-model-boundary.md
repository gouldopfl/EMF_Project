# ADR-013: Veterans Claims Domain Model Boundary

## Status

Accepted

## Context

ADR-012 established that domain-specific functionality belongs in Domain
Extensions and that the EMF platform remains domain-neutral.

The Veterans Claims Domain Extension is the first concrete Domain Extension.

The extension now requires a domain model that can represent veterans' claims
without duplicating or redefining EMF platform concepts such as evidence,
provenance, workflow execution, persistence, or integrity.

A veterans' claim may contain multiple separately adjudicated issues. Each
issue may involve a different claimed condition, service-connection theory,
evidence set, decision, disability evaluation, and effective date.

The domain model must therefore distinguish the overall claim submission from
the individual issues evaluated within that claim.

## Decision

The Veterans Claims Domain Extension will define veterans-specific concepts
within the extension boundary.

The initial domain model will distinguish at least the following concepts:

- Veteran
- Claim
- Claim Issue
- Claim Issue Type
- Submission
- Submission Type
- Claimed Condition
- Medical Condition
- Service Connection Theory
- Service Connection Basis
- Service Event
- Exposure
- Regulatory Authority
- Regulatory Provision
- Requirement
- VA Decision
- Issue Decision
- Disability Evaluation
- Effective Date

A Claim represents the veterans-benefits matter being pursued and may contain
one or more Claim Issues.

A Submission represents a procedural presentation of one or more Claim Issues
to VA.

A Claim may therefore be associated with multiple Submissions over time without
changing the identity of the underlying Claim Issues.

Submission Type identifies the procedural mechanism through which one or more
Claim Issues are presented, developed, or returned for review.

Submission Types may represent mechanisms including:

- Initial Claim
- Supplemental Claim
- Higher-Level Review
- Board Appeal
- other claim, submission, or decision-review mechanisms defined by
  veterans-benefits law or policy

Submission Type is an EMF domain abstraction and does not imply that all
represented mechanisms have the same legal or procedural character under
veterans-benefits law.

A Claim Issue represents an independently adjudicable matter within a Claim.

A Claim Issue may identify:

- a Claim Issue Type
- one or more Claimed Conditions
- one or more Service Connection Theories
- one or more Service Connection Bases
- one or more Exposures
- supporting or opposing evidence
- adjudicative findings
- a VA Decision
- a Disability Evaluation
- an Effective Date

Claim Issue is the primary veterans-domain unit for adjudication modeling.

Claim Issue Type identifies the substantive adjudicative question presented by
a Claim Issue and is distinct from both Submission Type and Service Connection
Theory.

Initial Claim Issue Types may include:

- Service Connection
- Increased Evaluation
- Effective Date
- other independently adjudicable veterans-benefits matters defined by law or
  policy

A Service Connection Claim Issue may be evaluated under one or more Service
Connection Theories, including direct service connection, secondary service
connection, aggravation, presumptive service connection, and other theories
defined by veterans-benefits law or policy.

An Increased Evaluation Claim Issue concerns the evaluation of an already
service-connected condition and therefore does not ordinarily require a new
Service Connection Theory.

The Veterans Claims Domain Extension shall not create a separate evidence,
provenance, persistence, integrity, or workflow infrastructure.

Evidence used by the Veterans Claims domain remains represented through EMF
platform evidence and Artifact concepts.

The Veterans Claims Domain Extension may classify, interpret, associate, and
evaluate platform evidence using veterans-specific terminology and rules.

Domain objects may reference platform identities or contracts where needed,
but platform projects shall not depend on Veterans Claims domain types.

## Veteran

A Veteran represents the person whose military service and veterans-benefits
matters are represented within the Veterans Claims Domain Extension.

A Veteran may be associated with one or more Claims, Claim Issues, Service
Events, Exposures, Medical Conditions, and other veterans-domain information.

Veteran is a domain identity and relationship concept. It does not replace
platform identity, authentication, authorization, user, person, or record
management concepts.

Military service history relevant to a Claim Issue may be represented through
domain relationships and supporting Evidence rather than embedded as
unstructured data within the Veteran object.

A Veteran's status, service history, or eligibility shall not be inferred
solely from the existence of a Veteran domain object. Such determinations must
remain traceable to applicable Evidence, Regulatory Provisions, Findings, or
official determinations where appropriate.

## Claim and Submission History

Claim Issue identity persists independently from the procedural Submissions
through which the issue is presented or reviewed.

A Claim Issue may participate in multiple Submissions over time.

A Submission may present one or more Claim Issues.

The association between a Claim Issue and a Submission represents a procedural
event in the history of the issue and does not create a new Claim Issue merely
because the issue is submitted, reviewed, appealed, or returned for further
development.

VA Decisions and their Issue Decisions form part of this longitudinal history.

An Issue Decision references the Claim Issue adjudicated by that decision and
may be associated with the Submission or review process that resulted in the
decision.

The Veterans Claims Domain Extension must preserve this history rather than
representing only the current state of a Claim Issue.

Historical Submissions, Issue Decisions, Disability Evaluations, Effective
Dates, Findings, and relevant Evidence relationships must remain traceable.

This longitudinal model allows EMF to distinguish the identity of an issue from
the procedural events and adjudicative outcomes that occur during the lifetime
of that issue.

## Regulatory Knowledge

Regulatory Authority represents an authoritative legal or regulatory source
that governs or informs a Claim Issue.

A Regulatory Authority may include statutes, regulations, Code of Federal
Regulations provisions, Diagnostic Codes, rating schedules, precedential
decisions, policy provisions, or other authoritative sources.

A Regulatory Authority is a structured domain concept and shall not be reduced
to an unstructured citation string.

A Regulatory Authority may contain or reference multiple Regulatory Provisions.

A Regulatory Provision represents a specific rule, subsection, criterion,
presumption, definition, exception, or cross-reference within a Regulatory
Authority.

A Regulatory Provision may represent:

- a Definition
- a Presumption
- a Qualifying Condition
- a Requirement
- a Rating Criterion
- an Exception
- a Cross-Reference

Regulatory knowledge is maintained independently from individual Claim Issues.
Claim Issues reference the Regulatory Provisions applicable to the issue.

A Requirement represents a condition, element, criterion, or determination
that must be established, considered, or applied when evaluating a Claim Issue.
A Requirement is derived from or associated with one or more applicable
Regulatory Provisions and is evaluated against available Evidence.

This structure allows regulatory information such as presumptive conditions to
be represented as reusable regulatory knowledge rather than hard-coded into
individual claims or conditions.

## Conditions

A Claimed Condition represents the condition, disability, disease, injury,
symptom pattern, or other health-related matter asserted or identified as the
subject of a Claim Issue.

A Claimed Condition represents the matter being claimed and does not itself
establish a medical diagnosis or service connection.

A Claimed Condition may be expressed using terminology supplied by the veteran,
VA, a representative, a medical professional, or another authorized source.

A Medical Condition represents a medically identified disease, disorder,
injury, diagnosis, or other health condition reflected in Evidence.

A Medical Condition is distinct from a Claimed Condition. A Claimed Condition
may exist before a definitive Medical Condition has been identified.

A Claimed Condition may be associated with zero, one, or multiple Medical
Conditions when supported by the available Evidence.

Medical Conditions may also exist in Evidence without being the subject of a
Claim Issue.

Neither Claimed Condition nor Medical Condition inherently means that the
condition is service connected.

Service connection, evaluation, and effective-date determinations are
represented through the appropriate Service Connection, Issue Decision,
Disability Evaluation, and Effective Date concepts.

Relationships between Claimed Conditions and Medical Conditions must remain
traceable to the Evidence or other domain information supporting that
association.

## Service Connection

Service Connection Theory represents the legal or adjudicative pathway through
which a claimed condition is asserted or evaluated as related to military
service or another service-connected condition.

The domain model may support theories including:

- direct service connection
- secondary service connection
- aggravation
- presumptive service connection
- other theories defined by veterans-benefits law or policy

The precise rule set for evaluating these theories is not defined by this ADR.

### Service Connection Basis

Service Connection Basis represents the specific factual or legal relationship
evaluated under a Service Connection Theory.

A Service Connection Basis identifies the relevant entities or circumstances
participating in that relationship without itself determining whether service
connection is established.

Depending on the Service Connection Theory, a Service Connection Basis may
identify relationships involving:

- a Claimed Condition
- a Service Event
- another service-connected condition
- an Exposure
- a preexisting condition
- an applicable Presumption
- other relationships recognized by veterans-benefits law or policy

Service Connection Basis is distinct from Evidence, Requirement, Finding,
Medical Opinion, and Regulatory Provision.

Evidence and Medical Opinions may support or contradict a Service Connection
Basis. Requirements define what must be established. Findings record
determinations concerning those Requirements.

A Claim Issue may contain multiple Service Connection Theories and multiple
Service Connection Bases.

### Aggravation

Aggravation relationships must preserve the distinction between aggravation of
a preexisting condition associated with military service and aggravation of a
non-service-connected condition by a service-connected condition.

These relationships may involve different Requirements, Regulatory Provisions,
Evidence, and Medical Opinions and shall not be treated as interchangeable.

The domain model must preserve this semantic distinction without requiring this
ADR to prescribe the eventual implementation representation.

### Service Event

A Service Event represents an event, injury, disease, duty, activity,
circumstance, or period of service that may be relevant to a Claim Issue or
Service Connection Basis.

A Service Event may identify information such as:

- the nature of the event or circumstance
- an approximate or known date or date range
- a location or duty assignment
- an occupational or operational activity
- involved units, organizations, or service environments
- related symptoms, injuries, diseases, or other consequences
- Evidence supporting, contradicting, or qualifying the event

A Service Event does not itself establish service connection.

Service Events remain traceable to EMF platform Evidence and Artifacts and may
be referenced by multiple Claim Issues or Service Connection Bases where
appropriate.

The domain model shall not require every Service Event to be represented as an
Exposure.

### Exposure

An Exposure represents contact with, presence in, or another legally or
medically relevant relationship to an environmental, occupational, chemical,
biological, radiological, physical, or other potentially harmful condition
during qualifying service or another relevant period.

An Exposure may identify information such as:

- the type or category of exposure
- the location of the exposure
- the date or date range
- the service duty, activity, or circumstance associated with the exposure
- the frequency, duration, or extent of exposure when known
- applicable Regulatory Provisions or Presumptions
- Evidence supporting, contradicting, or qualifying the exposure

An Exposure may be associated with one or more Service Events but remains a
distinct domain concept because exposure-specific Requirements, Presumptions,
and Regulatory Provisions may apply.

An Exposure does not itself establish that a Claimed Condition was caused or
aggravated by the exposure.

The Veterans Claims Domain Extension shall not hard-code individual exposure
programs, locations, substances, conflicts, or presumptive conditions into the
fundamental domain model. Those details belong in regulatory knowledge,
Policies, reference data, or other domain mechanisms that may evolve
independently from the core domain concepts.

## Evidence Classification

Evidence remains an EMF platform concept.

The Veterans Claims Domain Extension may associate veterans-specific
classifications, roles, and interpretations with platform Evidence without
creating a separate veterans-domain evidence subsystem.

Veterans-domain evidence classifications may identify Evidence as, for example:

- medical evidence
- service treatment records
- military personnel or service records
- lay evidence
- examinations
- medical opinions
- adjudicative or decision records
- other evidence classifications defined by veterans-benefits law, policy,
  or domain needs

Evidence classification describes the domain role or character of Evidence and
does not alter the identity, provenance, integrity, or storage semantics of the
underlying platform Evidence.

A single Evidence item may have multiple applicable classifications or roles.

Evidence classifications may be associated with Claim Issues, Requirements,
Service Events, Exposures, Medical Conditions, Findings, Medical Opinions, and
other veterans-domain concepts where appropriate.

Domain classification does not itself determine evidentiary weight,
credibility, competency, sufficiency, or legal effect. Those determinations
belong to applicable domain Policies, analysis, Findings, or official
adjudication.

## Evidence Development

An Evidence Gap identifies a Requirement for which available Evidence is
missing, insufficient, conflicting, or otherwise inadequate for the intended
analysis.

An Evidence Gap may identify supporting Evidence, contradictory Evidence,
missing Evidence, potentially useful Evidence, and whether a professional
opinion may be appropriate.

An Evidence Gap does not automatically mean that additional Evidence is
legally required.

An Evidence Development Plan identifies Evidence that may be useful in
addressing Requirements or Evidence Gaps.

The plan may identify Evidence as necessary, potentially useful, already
available, conflicting, or unnecessary or low value. Recommendations should
identify the Requirement and Regulatory Provision that make the Evidence
relevant.

AI may assist in identifying applicable Requirements, comparing them with
available Evidence, identifying Evidence Gaps, and preparing a proposed
Evidence Development Plan. AI assistance does not establish a medical or
legal conclusion.

## Medical Opinion

A Medical Opinion represents a professional medical opinion concerning a
medical question relevant to a Claim Issue.

A Medical Opinion may address diagnosis, causation, secondary service
connection, aggravation, severity, prognosis, or other medical questions.

A Medical Opinion is distinct from ordinary Evidence as a domain concept,
although its underlying source remains represented by EMF platform Evidence
and Artifacts.

A Claim Issue does not inherently require a Medical Opinion.

## Legal Analysis

Legal Analysis represents analysis of applicable legal authority as it relates
to a Claim Issue or Requirement.

Legal Analysis is distinct from Regulatory Authority, Regulatory Provision,
Evidence, Medical Opinion, Finding, and VA Decision.

AI-assisted Legal Analysis must remain distinguishable from authoritative
legal sources and professional legal judgment.

## Finding

A Finding represents a determination concerning a Requirement, fact,
condition, or other matter relevant to a Claim Issue.

A Finding must be traceable to the Evidence and applicable authority that
supports, contradicts, or qualifies it.

A Finding may be favorable, unfavorable, partially favorable, unresolved, or
disputed.

A Finding is distinct from a VA Decision. EMF-generated analytical Findings
must not be represented as official VA adjudicative determinations.

## Evidence Package

An Evidence Package is a curated, traceable collection of existing Evidence
assembled for a specific Claim Issue, purpose, or authorized reviewer.

An Evidence Package may be prepared for a veteran, VSO or accredited
representative, attorney, C&P examiner, medical professional, or other
authorized reviewer.

An Evidence Package does not create new Evidence. It references and presents
existing platform Evidence and source Artifacts.

An Evidence Package may contain EMF-generated organizational material such as
indexes, timelines, summaries, requirement-to-evidence mappings, evidence-gap
analysis, and source references. Such generated material must remain clearly
distinguishable from the underlying Evidence.

A C&P Examiner Evidence Package may organize relevant medical history,
diagnostic studies, imaging, laboratory results, treatment history,
medication history, examinations, Medical Opinions, lay Evidence, timelines,
and source citations.

The purpose is to make relevant Evidence accessible, organized, traceable,
and verifiable. EMF must not instruct a C&P examiner what medical conclusion
to reach. The examiner retains independent medical judgment.

## Decisions and Ratings

A VA Decision represents an official VA adjudicative action that may address
one or more Claim Issues.

An Issue Decision represents the issue-level adjudicative outcome contained
within or associated with a VA Decision.

A VA Decision may contain or reference multiple Issue Decisions, allowing
different Claim Issues addressed by the same VA Decision to have independent
outcomes.

An Issue Decision references the Claim Issue being adjudicated and may identify
an outcome such as granted, denied, deferred, partially granted, or another
disposition defined by veterans-benefits law or policy.

An Issue Decision may reference the Findings, Evidence, Regulatory Provisions,
and other domain information relevant to that adjudicative outcome.

A Disability Evaluation represents the percentage or other evaluation assigned
to a service-connected condition or issue.

A Disability Evaluation is historical domain information and shall not be
limited to representing only the current evaluation.

A zero-percent or other noncompensable evaluation remains a Disability
Evaluation.

An Issue Decision may establish or reference one or more Disability Evaluations.
This allows the domain model to represent staged evaluations or other decisions
in which different evaluations apply to different periods.

Changes such as increased evaluations, reduced evaluations, temporary
evaluations, restored evaluations, or other rating changes are represented
through the adjudicative and evaluation history rather than by creating a new
condition merely because its evaluation changes.

An Effective Date represents the date from which an awarded benefit,
evaluation, or other adjudicative determination becomes effective.

A Disability Evaluation may reference the Effective Date applicable to that
evaluation. Different Disability Evaluations associated with the same Claim
Issue may therefore have different Effective Dates.

Disability Evaluations and Effective Dates must remain traceable to the
appropriate Issue Decisions so that the evaluation history of a Claim Issue can
be reconstructed over time.

Decision logic, rating schedules, combined-rating calculations, and effective
date rules are domain Policies and are not fundamental EMF platform behavior.

## Platform Boundary

The intended separation is:

EMF Platform
    |
    +---- Evidence / Artifacts
    +---- Provenance
    +---- Relationships
    +---- Workflow
    +---- Persistence
    +---- Integrity
    |
    v
Veterans Claims Domain Extension
    |
    +---- Veteran
    +---- Claim
    |       +---- Claim Issue
    |       |       +---- Claim Issue Type
    |       |       +---- Claimed Condition
    |       |       +---- Medical Condition relationships
    |       |       +---- Service Connection Theory
    |       |       +---- Service Connection Basis
    |       |       +---- Service Event
    |       |       +---- Exposure
    |       +---- Submission
    |               +---- Submission Type
    |               +---- Claim Issue references
    +---- Regulatory Authority
    |       +---- Regulatory Provision
    |               +---- Requirement
    |               +---- Presumption
    |               +---- Rating Criterion
    |               +---- Exception
    +---- Evidence Classification / References
    +---- Medical Opinion
    +---- Legal Analysis
    +---- Evidence Gap
    +---- Evidence Development Plan
    +---- Evidence Package
    +---- Finding
    +---- VA Decision
            +---- Issue Decision
                    +---- Claim Issue reference
                    +---- Disability Evaluation
                            +---- Effective Date

The diagram illustrates conceptual ownership and major relationships rather than
prescribing the eventual implementation structure.

The Veterans Claims Domain Extension interprets, classifies, associates, and
organizes platform Evidence for veterans-benefits purposes but does not replace
platform evidence, provenance, relationship, workflow, persistence, or
integrity semantics.

Veterans-domain objects may reference platform Evidence and Artifacts while
preserving their platform identities and provenance.

## Consequences

Benefits:

- multiple Claim Issues within one Claim can be modeled independently
- Claim Issue identity can persist across multiple procedural Submissions
- Submission Type remains distinct from the substantive Claim Issue Type
- different Service Connection Theories and Bases can be associated with individual Claim Issues
- Service Events and Exposures can be represented without hard-coding specific programs or presumptions
- Claimed Conditions remain distinct from medically identified conditions
- veterans-specific evidence classifications can be applied without creating a duplicate evidence subsystem
- VA Decisions can contain independent Issue Decisions for individual Claim Issues
- Disability Evaluations and Effective Dates can be represented historically
- regulatory knowledge can evolve independently from individual claims
- platform Evidence remains reusable across domains
- veterans-specific terminology remains outside EMF.Core
- future veterans workflows can operate on stable domain concepts
- domain Policies can evolve independently from platform infrastructure

Tradeoffs:

- Claim, Claim Issue, and Submission must remain distinct concepts
- longitudinal history requires relationships among Submissions, Claim Issues, VA Decisions, and Issue Decisions
- domain objects must reference platform Evidence rather than own a separate evidence subsystem
- domain evidence classification must not alter platform evidence semantics
- service-connection analysis requires distinction among Theory, Basis, Requirements, Evidence, Medical Opinions, and Findings
- some veterans-benefits processes may require additional domain concepts as the model evolves
- detailed adjudication, rating, effective-date, and procedural rules require separate Policies and architectural decisions

## Architectural Principle

EMF owns evidence and process semantics.

The Veterans Claims Domain Extension owns veterans-specific interpretation,
classification, adjudication concepts, regulatory knowledge relationships, and
domain rules.

The platform owns the process and evidence infrastructure; the Veterans Claims
Domain Extension provides the veterans-benefits expertise applied to that
infrastructure.
