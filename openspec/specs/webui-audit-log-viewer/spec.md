# webui-audit-log-viewer Specification

## Purpose

Defines the audit log viewer for tenant administrators: filtering the tenant's audit entries by user, timeframe, action, and outcome, reading them in sequence order, and showing the integrity state of the hash chain.

## Requirements

### Requirement: The audit log viewer is limited to tenant administrators
The audit log viewer SHALL be available only to users with the tenant administrator role. The viewer SHALL display only the entries the server returns for the caller's tenant.

#### Scenario: Administrator opens the viewer
- **WHEN** a tenant administrator opens the audit log
- **THEN** the most recent entries of the tenant are listed

#### Scenario: Non-administrator
- **WHEN** a user without the tenant administrator role opens the audit log
- **THEN** the page is not available

### Requirement: Entries can be filtered by several criteria at once
The viewer SHALL offer these filters: timeframe (a start and an end in local time, or the presets last 24 hours, last 7 days, and last 30 days), acting user (chosen from the tenant's members), action type, and outcome (any, succeeded, or denied). Filters SHALL combine with AND. Each filter change SHALL update the URL query so that a filtered view can be shared.

#### Scenario: Combined filters
- **WHEN** the administrator filters by user, the action "secret value read", and the outcome "denied"
- **THEN** only entries matching all three criteria are listed

#### Scenario: Timeframe preset
- **WHEN** the administrator selects "last 7 days"
- **THEN** only entries from the last seven days are listed

#### Scenario: Shared filter
- **WHEN** the administrator opens a link that contains the filter values
- **THEN** the viewer shows the same filters and results

### Requirement: Entries are shown in sequence order
Entries SHALL be listed in ascending sequence order by default. The administrator MAY switch to descending order, which changes only the display order. Each entry SHALL show its sequence number, its timestamp in UTC and in local time, the acting user, the action, the outcome, and its target identifiers.

#### Scenario: Default order
- **WHEN** the viewer loads entries
- **THEN** they are listed in ascending sequence order

#### Scenario: Descending order
- **WHEN** the administrator selects descending order
- **THEN** the same entries are listed from the highest sequence number down

### Requirement: Outcomes are distinguishable without color alone
Denied outcomes SHALL be shown with a text label and an icon in addition to any color.

#### Scenario: Denied entry
- **WHEN** an entry has the outcome denied
- **THEN** the row shows the text "Denied" and an icon, not only a color

### Requirement: Entries recorded by the data layer are labeled
An entry whose acting user is the empty identifier SHALL be shown with the label "Data layer" and MUST NOT be shown as an unknown user.

#### Scenario: Tampering attempt entry
- **WHEN** an entry recorded by the data layer is listed
- **THEN** its actor is shown as "Data layer"

### Requirement: The viewer pages through long results
The viewer SHALL load entries in pages in sequence order and SHALL load the next page when the administrator reaches the end of the loaded entries or selects "Load more". Loading MUST NOT drop or duplicate entries.

#### Scenario: Next page loaded
- **WHEN** the administrator reaches the end of the first page
- **THEN** the next page is loaded and its entries follow the previous page in sequence order

### Requirement: Hash chain integrity can be verified on demand
The viewer SHALL provide a "Verify integrity" action that asks the server to verify the tenant's audit chain. The result SHALL show either that the chain is intact with the number of entries verified, or that it is altered with the sequence number of the first altered entry. Until verification runs, the integrity status SHALL be shown as "Not verified".

#### Scenario: Intact chain
- **WHEN** the administrator runs verification on an intact chain
- **THEN** the viewer shows "Intact" with the number of verified entries

#### Scenario: Altered chain
- **WHEN** the administrator runs verification and the server reports an altered entry
- **THEN** the viewer shows "Altered" and the sequence number of the first altered entry

#### Scenario: Not verified
- **WHEN** the viewer loads without a verification run
- **THEN** the integrity status shows "Not verified"

### Requirement: Each entry shows its chain link status
Each loaded entry SHALL show whether its hash link to the previous entry is consistent with the entries the viewer holds, using the hash values returned by the server. An entry whose link does not match SHALL be marked "Link broken" with its sequence number.

#### Scenario: Link consistent
- **WHEN** an entry's previous hash equals the hash of the preceding loaded entry
- **THEN** the entry shows "Link verified"

#### Scenario: Link broken
- **WHEN** an entry's previous hash does not equal the hash of the preceding loaded entry
- **THEN** the entry shows "Link broken" and the integrity status is marked "Altered"

### Requirement: Audit entries never display secret values
The viewer SHALL display only the metadata and identifiers that the server returns for each entry. The viewer MUST NOT request or display secret values, file content, or passwords.

#### Scenario: Entry displayed
- **WHEN** an entry for a secret value read is shown
- **THEN** only its identifiers, action, outcome, and time are displayed
