# ADR-032: Security Alert Detection Boundary

**Status:** Accepted  
**Date:** 2026-08-17

## Context

EMF records authorization, integrity, recovery, and provider events, but an
audit record alone does not notify responders. Production deployments require
warnings for suspicious patterns without coupling the framework to one email,
messaging, monitoring, or SIEM product.

Authentication failures occur before EMF receives an authenticated principal
and therefore remain the responsibility of the configured identity provider.

## Decision

EMF separates security-event detection from notification delivery.

A detector evaluates verified audit evidence against deployment-approved
thresholds and produces structured security alerts. An alert sink delivers
those alerts through deployment-specific monitoring infrastructure.

The core framework does not send email, SMS, or chat messages directly.

Authentication-risk alerts come from the identity provider. EMF alerts cover
authenticated authorization denials, protected-operation failures, audit
integrity failures, recovery anomalies, and missing required telemetry.

## Consequences

Alert thresholds, evaluation windows, destinations, owners, and escalation
times remain deployment configuration and require approval.

Alert records must not contain passwords, tokens, MFA codes, encryption keys,
or unrestricted protected facts. Delivery failure is itself a monitoring
event.

A single low-risk denial may be recorded without paging responders. Repeated,
coordinated, privileged, integrity-related, or high-risk events can trigger
immediate escalation according to approved policy.

## Verification

Automated tests must verify:

- threshold breaches create alerts
- events below threshold do not create alerts
- tampered audit evidence prevents trusted alert evaluation
- alert delivery failures do not become silent successes
- sensitive authentication material is never required by alert contracts

## Follow-up Work

- define alert models and sink contracts
- implement verified audit threshold evaluation
- add Azure Monitor or SIEM delivery in a deployment adapter
- approve production thresholds, recipients, and escalation procedures

## References

- `../INCIDENT_RESPONSE_AND_MONITORING.md`
- `ADR-028-tamper-evident-security-audit-storage.md`
- `ADR-030-workflow-activity-claim-recovery.md`
