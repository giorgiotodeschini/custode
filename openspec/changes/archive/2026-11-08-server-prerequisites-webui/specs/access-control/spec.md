# Spec Delta

## ADDED Requirements

### Requirement: Administrators can read groups and their members
A tenant administrator SHALL be able to list the groups of the tenant with their member and grant counts, and the members of each group, through the access read endpoints. These reads are restricted to tenant administrators and are not audited.

#### Scenario: Groups listed with counts
- **WHEN** a tenant administrator lists the groups of the tenant
- **THEN** each group is returned with its name, member count, and grant count

#### Scenario: Members of a group listed
- **WHEN** a tenant administrator lists the members of a group of the tenant
- **THEN** each member is returned with its identifier and login

#### Scenario: Group of another tenant
- **WHEN** a tenant administrator lists the members of a group that belongs to another tenant
- **THEN** the response is not found

#### Scenario: Group read by a non-administrator
- **WHEN** a user without the tenant administrator role lists groups
- **THEN** the request is rejected as forbidden

### Requirement: Administrators can read folder grants with their effective permissions
A tenant administrator SHALL be able to read the grants of a folder through `GET /api/access/grants` with the query parameter `folderPath`, with each principal's sources and effective permission levels after inheritance is resolved.

#### Scenario: Inherited grant shown with its source
- **WHEN** a group holds Read on `/production` and `/production/aws` inherits permissions
- **THEN** the entry for `/production/aws` lists the group with a source of `/production` with Read, and the effective level Read

#### Scenario: Direct grant and inherited grant
- **WHEN** a group holds Read on `/production` and Write on `/production/aws`
- **THEN** the group has two sources and its effective levels are Read and Write on `/production/aws`

#### Scenario: Inheritance broken
- **WHEN** `/production/aws` has broken inheritance
- **THEN** the grants of `/production` do not appear as sources for `/production/aws`

#### Scenario: Entry contents
- **WHEN** a tenant administrator reads the grants of a folder
- **THEN** each entry contains the principal kind, identifier, and name, its sources with the holding folder path and levels, and its effective levels

#### Scenario: Unknown folder
- **WHEN** a tenant administrator requests the grants of a folder that does not exist
- **THEN** the response is not found

### Requirement: Administrators can read the inheritance status of folders
A tenant administrator SHALL be able to list every folder of the tenant through `GET /api/access/folders`, with each folder's path, its parent path, and whether it inherits permissions.

#### Scenario: Inheritance status listed
- **WHEN** a tenant administrator lists the folders of the tenant
- **THEN** each folder is returned with its path, parent path, and inheritance flag

### Requirement: Callers can read their own effective permissions on a folder
Every authenticated user SHALL be able to read their own effective permission levels on a folder through `GET /api/access/me/permissions` with the query parameter `folderPath`, computed from direct and group grants after inheritance is resolved.

#### Scenario: Caller reads permissions on a folder
- **WHEN** a user with Read and Create through a group reads their permissions on the folder
- **THEN** the response lists Read and Create

#### Scenario: Other principals not revealed
- **WHEN** a user reads their own permissions on a folder where another user holds grants
- **THEN** the response contains only the caller's levels

#### Scenario: No permission on the folder
- **WHEN** a user with no effective permission on a folder reads their permissions on it
- **THEN** the response is not found, the same as for a folder that does not exist
