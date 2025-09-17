# Branch Protection Rules Configuration
# This file documents the recommended branch protection settings for your repository
# These settings should be configured in your Git hosting platform (GitHub, Azure DevOps, etc.)

## Main Branch Protection Rules

### Required Settings:
1. **Restrict pushes to main branch**
   - Disable direct pushes to main branch
   - All changes must come through pull requests/merge requests

2. **Required Pull Request Reviews**
   - Require at least 1 approving review before merging
   - Dismiss stale reviews when new commits are pushed
   - Require review from CODEOWNERS

3. **Required Status Checks**
   - Require all CI/CD pipelines to pass before merging
   - Require branches to be up to date before merging

4. **Additional Restrictions**
   - Restrict force pushes
   - Restrict deletions of the main branch
   - Only allow merge commits (disable squash and rebase if desired)

### GitHub Configuration (if using GitHub):
```json
{
  "required_status_checks": {
    "strict": true,
    "contexts": ["continuous-integration"]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": {
    "required_approving_review_count": 1,
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": true,
    "require_last_push_approval": true
  },
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false
}
```

### Azure DevOps Configuration (if using Azure DevOps):
- Set up branch policies for main branch
- Require minimum number of reviewers: 1
- Check for linked work items: Optional
- Check for comment resolution: Required
- Limit merge types: As desired

## Protected File Patterns
The following files and directories have special protection via CODEOWNERS:
- CI/CD configurations (.github/, azure-pipelines.yml, etc.)
- Project files (*.sln, *.csproj)
- Security files (CODEOWNERS, LICENSE)
- Build configurations
