# Compensation

## Entity
CompensationRecord

## Rules
- Immutable history
- Fully audited
- HR Administrators always have company-wide compensation access
- Employees may view their own salary when `DisplaySalaryOnEmployeeProfile` is enabled
- Managers may view salary for their complete subordinate hierarchy when `DisplaySalaryOnEmployeeProfile` is enabled
- Company Administrator and Recruiter roles do not grant compensation access

## Events
- salary_changed
- compensation_adjusted
