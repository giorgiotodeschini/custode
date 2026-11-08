# Spec Delta

## Purpose
Defines the tenant administration area, where tenant administrators manage tenant members, groups, and tenant-wide security policies, so that the people and rules that govern a tenant can be maintained from the web UI.

## ADDED Requirements

### Requirement: The tenant administration area is limited to tenant administrators
The tenant administration area SHALL be available only to users with the tenant administrator role and SHALL contain the sections Members, Groups, and Security Policies.

#### Scenario: Administrator opens the area
- **WHEN** a tenant administrator opens Tenant Administration
- **THEN** the sections Members, Groups, and Security Policies are available

#### Scenario: Non-administrator
- **WHEN** a user without the tenant administrator role opens Tenant Administration
- **THEN** the page is not available

### Requirement: Members are listed with their status and roles
The Members section SHALL list the users of the tenant with their display name, login identifier, roles, group count, and status (active or disabled).

#### Scenario: Members listed
- **WHEN** an administrator opens Members
- **THEN** each user of the tenant is shown with the fields above

### Requirement: Members can be created, disabled, and re-enabled
A tenant administrator SHALL be able to create a member with a login identifier and an initial password that the member must change at first sign-in, disable a member, and re-enable a disabled member. A disabled member MUST NOT be able to sign in or renew a session.

#### Scenario: Member created
- **WHEN** the administrator creates a member with a unique login identifier
- **THEN** the member appears in the list with status active

#### Scenario: Member disabled
- **WHEN** the administrator disables a member who has an active session
- **THEN** the member's session ends on the next request and the member cannot sign in

### Requirement: Tenant administrator role can be granted and withdrawn
A tenant administrator SHALL be able to grant or withdraw the tenant administrator role of a member. The UI MUST NOT allow withdrawing the role from the last remaining tenant administrator of the tenant.

#### Scenario: Role granted
- **WHEN** the administrator grants the tenant administrator role to a member
- **THEN** the member's role list includes the tenant administrator role

#### Scenario: Last administrator protected
- **WHEN** the only remaining tenant administrator is selected to lose the role
- **THEN** the action is disabled and the page explains that at least one administrator is required

### Requirement: Groups are listed with membership and grant counts
The Groups section SHALL list the tenant's groups with the number of members and the number of folder grants for each group.

#### Scenario: Groups listed
- **WHEN** an administrator opens Groups
- **THEN** each group is shown with its member count and grant count

### Requirement: Groups can be created, renamed, and deleted
A tenant administrator SHALL be able to create, rename, and delete a group. A group name MUST be unique within the tenant. Deleting a group SHALL require confirmation that lists the number of members and grants that will be removed.

#### Scenario: Duplicate group name
- **WHEN** the administrator creates a group with the name of an existing group in the tenant
- **THEN** the form shows that the name is already used and no group is created

#### Scenario: Group deletion confirmed
- **WHEN** the administrator confirms the deletion of a group with two members
- **THEN** the group is removed and the confirmation had stated that two members would be removed

### Requirement: Group membership is managed from the group
The group detail SHALL allow adding and removing members from the tenant's users. Changes to membership SHALL take effect for the affected user on the next request, and the page SHALL state this.

#### Scenario: Member added to a group
- **WHEN** the administrator adds a tenant user to a group
- **THEN** the user appears among the group's members

#### Scenario: Member removed from a group
- **WHEN** the administrator removes a member from a group
- **THEN** the page states that the removed member loses the group's access on the next request

### Requirement: Security policies are tenant-wide and validated
The Security Policies section SHALL let a tenant administrator set the following values for the tenant: idle timeout (5 to 120 minutes, default 15), refresh token lifetime (1 to 30 days), and clipboard clear delay (10 to 120 seconds, default 30). Values outside these ranges MUST be rejected before saving.

#### Scenario: Valid policy saved
- **WHEN** the administrator sets an idle timeout of 20 minutes and saves
- **THEN** the policy is stored and applies to the tenant's users from their next session activity

#### Scenario: Out-of-range value
- **WHEN** the administrator sets an idle timeout of 2 minutes
- **THEN** the form rejects the value and nothing is saved

### Requirement: Policy changes show their effect before saving
Before a policy change is saved, the page SHALL summarize the effect on the tenant, including the sessions that end when the idle timeout or refresh token lifetime is shortened.

#### Scenario: Shorter idle timeout
- **WHEN** the administrator reduces the idle timeout from 30 to 10 minutes
- **THEN** the page states that users idle for more than 10 minutes will be signed out

### Requirement: Administrative changes are shown only after confirmation
Each member, role, group, membership, and policy change SHALL display the state confirmed by the server. A change refused by the server SHALL show the refusal reason and leave the displayed state unchanged.

#### Scenario: Refused change
- **WHEN** the server refuses a policy change
- **THEN** the page shows the reason and the policy values remain as before
