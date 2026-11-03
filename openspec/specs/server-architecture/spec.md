# server-architecture Specification

## Purpose
Defines how the Custode server is organized so that features can be added independently while shared concerns such as persistence and data protection stay consistent across the system.

## Requirements
### Requirement: Feature slices own their behavior
The server SHALL organize each feature as a self-contained vertical slice that owns its request handling, validation, and business logic end to end. A slice MUST NOT depend on the internals of another slice.

#### Scenario: Feature is added as a new slice
- **WHEN** a new feature is added to the server
- **THEN** its handling, validation, and logic are contained in a single slice without modifying the internals of existing slices

#### Scenario: Slice needs data from another feature
- **WHEN** a slice requires behavior that belongs to another feature
- **THEN** it accesses that behavior only through a published contract, not through the other slice's internal types

### Requirement: Shared infrastructure is common to all slices
The server SHALL provide persistence and data protection as shared infrastructure that all slices use through the same configuration. Slices MUST NOT configure their own database context or data protection key ring.

#### Scenario: Slice persists data
- **WHEN** a slice stores or reads data
- **THEN** it uses the shared persistence infrastructure

#### Scenario: Slice requires data protection
- **WHEN** a slice needs to protect or unprotect a value
- **THEN** it uses the shared data protection infrastructure with the same key ring as every other slice

### Requirement: Dependencies flow inward to shared infrastructure
Feature slices SHALL depend on shared infrastructure abstractions. Shared infrastructure MUST NOT depend on any feature slice.

#### Scenario: Shared infrastructure references a feature
- **WHEN** shared infrastructure is built or analyzed for dependencies
- **THEN** no reference to any feature slice exists within it

### Requirement: Server composition is explicit
Each slice SHALL register its own endpoints and services through a single entry point, so that the set of features served by the server is visible in one place at startup.

#### Scenario: Server starts
- **WHEN** the server starts
- **THEN** every registered slice's endpoints are available and the list of slices is determined by the composition entry point alone
