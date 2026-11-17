param(
    [string]$Repository = 'poikilometis/WindowDock',
    [string]$GhConfigDirectory = '',
    [switch]$Release
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($GhConfigDirectory) { $env:GH_CONFIG_DIR = $GhConfigDirectory }
$env:GIT_TERMINAL_PROMPT = '0'
$stateDirectory = Join-Path $root 'artifacts\github-publish'
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
function Save-State($State) {
    [IO.File]::WriteAllText((Join-Path $stateDirectory 'status.json'), ($State | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}
function Assert-Exit([string]$Message) { if ($LASTEXITCODE -ne 0) { throw $Message } }
function Api([string]$Endpoint, [string]$Method = 'GET', $Body = $null) {
    if ($null -eq $Body) { $result = & gh api $Endpoint --method $Method }
    else { $result = ($Body | ConvertTo-Json -Depth 20 -Compress) | & gh api $Endpoint --method $Method --input - }
    Assert-Exit "GitHub API failed: $Method $Endpoint"
    if ($result) { return ($result -join "`n" | ConvertFrom-Json) }
}
function Wait-Workflow([string]$Workflow, [string]$Sha, [string]$Event, [string]$Branch = '') {
    $deadline = (Get-Date).AddMinutes(20)
    $previous = ''
    while ((Get-Date) -lt $deadline) {
        $response = Api "repos/$Repository/actions/workflows/$Workflow/runs?head_sha=$Sha&event=$Event&per_page=20"
        $run = $response.workflow_runs | Where-Object { $_.head_sha -eq $Sha -and (-not $Branch -or $_.head_branch -eq $Branch) } | Sort-Object created_at -Descending | Select-Object -First 1
        if ($run) {
            Save-State @{ workflow=$Workflow; status=$run.status; conclusion=$run.conclusion; run_id=$run.id; url=$run.html_url; sha=$Sha }
            $state = "$($run.status) $($run.conclusion)"
            if ($state -ne $previous) { Write-Host "$Workflow : $state $($run.html_url)"; $previous = $state }
            if ($run.status -eq 'completed') {
                if ($run.conclusion -ne 'success') {
                    $failedLog = & gh run view $run.id --repo $Repository --log-failed 2>&1
                    $failedLog | Out-File -LiteralPath (Join-Path $stateDirectory 'failed-ci.log') -Encoding utf8
                    $failedLog | Write-Host
                    throw "$Workflow failed. Fix CI before releasing; no release tag is created on a failed initial CI run."
                }
                return $run
            }
        }
        Start-Sleep -Seconds 10
    }
    throw "Timed out waiting for $Workflow. Check the repository Actions tab."
}
$login = & gh api user --jq .login
Assert-Exit 'GitHub CLI is not authenticated; run gh auth login --scopes workflow first.'
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid repository name.' }
$owner = $Repository.Split('/')[0]
if ($login -ne $owner) { throw "Expected account $owner; active account is $login." }
$sha = (& git -C $root rev-parse HEAD).Trim(); Assert-Exit 'Commit the reviewed source first.'
if (& git -C $root status --porcelain) { throw 'Commit or save local changes before publication.' }
$remote = (& git -C $root remote get-url origin).Trim(); Assert-Exit 'Configure origin before publication.'
if ($remote -ne "https://github.com/$Repository.git") { throw 'Origin does not match the requested repository.' }
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$tag = "v$version"
& gh repo view $Repository --json nameWithOwner --jq .nameWithOwner 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    & gh repo create $Repository --public --description 'Group existing terminals and desktop apps into compact, tabbed workspaces.'
    Assert-Exit 'Could not create the public repository.'
}
$repo = Api "repos/$Repository"
if ($repo.private) { throw 'The existing repository is private; this script never changes visibility implicitly.' }
Api "repos/$Repository/actions/permissions" 'PUT' @{ enabled=$true; allowed_actions='selected' } | Out-Null
Api "repos/$Repository/actions/permissions/selected-actions" 'PUT' @{ github_owned_allowed=$true; verified_allowed=$false; patterns_allowed=@() } | Out-Null
Api "repos/$Repository/actions/permissions/workflow" 'PUT' @{ default_workflow_permissions='read'; can_approve_pull_request_reviews=$false } | Out-Null
& git -C $root -c credential.helper= -c 'credential.helper=!gh auth git-credential' push --set-upstream origin main
Assert-Exit 'Push failed. Remote history was not overwritten.'
Api "repos/$Repository" 'PATCH' @{ description='Group existing terminals and desktop apps into compact, tabbed workspaces.'; default_branch='main'; has_issues=$true; has_projects=$false; has_wiki=$false; has_discussions=$true; allow_squash_merge=$true; allow_merge_commit=$false; allow_rebase_merge=$false; delete_branch_on_merge=$true; allow_auto_merge=$true } | Out-Null
Api "repos/$Repository/topics" 'PUT' @{ names=@('windows','window-manager','desktop','avalonia','dotnet','terminal','ai-agents') } | Out-Null
Api "repos/$Repository/vulnerability-alerts" 'PUT' | Out-Null
Api "repos/$Repository/automated-security-fixes" 'PUT' | Out-Null
Api "repos/$Repository/private-vulnerability-reporting" 'PUT' | Out-Null
Api "repos/$Repository" 'PATCH' @{ security_and_analysis=@{ secret_scanning=@{ status='enabled' }; secret_scanning_push_protection=@{ status='enabled' } } } | Out-Null
$ci = Wait-Workflow 'ci.yml' $sha 'push' 'main'
$checkRuns = Api "repos/$Repository/commits/$sha/check-runs"
$contexts = @($checkRuns.check_runs | Where-Object { $_.name.EndsWith('Required checks') } | Select-Object -ExpandProperty name -Unique)
if ($contexts.Count -ne 1) { throw 'Could not identify the stable required CI check.' }
Api "repos/$Repository/branches/main/protection" 'PUT' @{ required_status_checks=@{ strict=$true; contexts=$contexts }; enforce_admins=$false; required_pull_request_reviews=@{ dismiss_stale_reviews=$true; require_code_owner_reviews=$false; required_approving_review_count=0 }; restrictions=$null; required_conversation_resolution=$true; allow_force_pushes=$false; allow_deletions=$false } | Out-Null
Api "repos/$Repository/environments/github-release" 'PUT' @{ deployment_branch_policy=@{ protected_branches=$false; custom_branch_policies=$true } } | Out-Null
$policies = Api "repos/$Repository/environments/github-release/deployment-branch-policies"
if (-not ($policies.branch_policies | Where-Object { $_.name -eq 'v*' -and $_.type -eq 'tag' })) {
    Api "repos/$Repository/environments/github-release/deployment-branch-policies" 'POST' @{ name='v*'; type='tag' } | Out-Null
}
$tagRules = @{ name='Immutable version tags'; target='tag'; enforcement='active'; conditions=@{ ref_name=@{ include=@('refs/tags/v*'); exclude=@() } }; rules=@(@{ type='deletion' },@{ type='non_fast_forward' }); bypass_actors=@() }
$existingRules = Api "repos/$Repository/rulesets"
$existingTagRule = $existingRules | Where-Object name -eq $tagRules.name | Select-Object -First 1
if ($existingTagRule) { Api "repos/$Repository/rulesets/$($existingTagRule.id)" 'PUT' $tagRules | Out-Null }
else { Api "repos/$Repository/rulesets" 'POST' $tagRules | Out-Null }
if ($Release) {
    $existingTag = & git -C $root rev-parse --verify "refs/tags/$tag^{}" 2>$null
    if ($LASTEXITCODE -eq 0) { if ($existingTag.Trim() -ne $sha) { throw 'The existing version tag points at another commit.' } }
    else { & git -C $root tag -a $tag -m "WindowDock $version"; Assert-Exit 'Tag creation failed.' }
    & git -C $root -c credential.helper= -c 'credential.helper=!gh auth git-credential' push origin "refs/tags/$tag"
    Assert-Exit 'Tag push failed; an existing remote tag was not overwritten.'
    $releaseRun = Wait-Workflow 'release.yml' $sha 'push' $tag
    $releaseInfo = & gh release view $tag --repo $Repository --json url,isDraft,assets
    Assert-Exit 'The release workflow finished but no release was found.'
    $releaseInfo = $releaseInfo -join "`n" | ConvertFrom-Json
    if ($releaseInfo.isDraft -or $releaseInfo.assets.Count -lt 4) { throw 'The release is missing public downloadable assets.' }
    Save-State @{ status='published'; version=$version; sha=$sha; url=$releaseInfo.url; assets=$releaseInfo.assets.Count }
    Write-Host "Published: $($releaseInfo.url)"
}
Write-Host "Configured https://github.com/$Repository"