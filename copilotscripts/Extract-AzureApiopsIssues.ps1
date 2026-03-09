#Requires -Version 7.0

<#
.SYNOPSIS
Extracts all open issues from source repository and saves to JSON file.

.DESCRIPTION
This script fetches all open issues from the source public repository,
including all comments and metadata, processes them for migration, anonymizes
usernames, and exports everything to a JSON file for later processing.

.PARAMETER GitHubToken
Your GitHub Personal Access Token (only needs public repo access).

.PARAMETER OutputFile
Path to output JSON file (default: azure-apiops-issues.json)

.PARAMETER IncludeComments
If specified, includes full comment history in the JSON (default: true)

.EXAMPLE
.\Extract-AzureApiopsIssues.ps1 -GitHubToken "ghp_your_token_here"

.EXAMPLE
.\Extract-AzureApiopsIssues.ps1 -GitHubToken "ghp_your_token_here" -OutputFile "my-issues.json"
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$GitHubToken = "",
    
    [Parameter(Mandatory = $false)]
    [string]$OutputFile = "azure-apiops-issues.json",
    
    [Parameter(Mandatory = $false)]
    [bool]$IncludeComments = $true
)

# Set strict mode and error handling
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Hardcoded source repository (anonymized references)
$sourceOwner = "Azure"
$sourceRepo = "apiops"
$apiBase = "https://api.github.com"
$sourceUrl = "$apiBase/repos/$sourceOwner/$sourceRepo"

# Headers for GitHub API
$headers = @{
    "Accept" = "application/vnd.github.v3+json"
    "User-Agent" = "PowerShell-Azure-Apiops-Extractor"
}

# Add authorization header if token is provided
if ($GitHubToken -and $GitHubToken.Trim() -ne "") {
    $headers["Authorization"] = "Bearer $GitHubToken"
    Write-Host "🔑 Using provided GitHub token for authentication" -ForegroundColor Green
} else {
    Write-Host "🌐 Accessing public repository without authentication" -ForegroundColor Cyan
    Write-Host "   (Rate limited to 60 requests per hour)" -ForegroundColor Yellow
}

function Write-Banner {
    param($Text)
    Write-Host ""
    Write-Host "=" * 70 -ForegroundColor Blue
    Write-Host " $Text" -ForegroundColor Blue
    Write-Host "=" * 70 -ForegroundColor Blue
    Write-Host ""
}

function Test-SourceAccess {
    try {
        Write-Host "🔍 Testing access to $sourceOwner/$sourceRepo..." -ForegroundColor Yellow
        $response = Invoke-RestMethod -Uri $sourceUrl -Headers $headers -Method Get
        Write-Host "✅ Successfully connected to: $($response.full_name)" -ForegroundColor Green
        Write-Host "   - Description: $($response.description)" -ForegroundColor Cyan
        Write-Host "   - Open Issues: $($response.open_issues_count)" -ForegroundColor Cyan
        Write-Host "   - Language: $($response.language)" -ForegroundColor Cyan
        return $response
    }
    catch {
        Write-Error "❌ Failed to access $sourceOwner/$sourceRepo : $($_.Exception.Message)"
        return $null
    }
}

function Get-AllIssueComments {
    param($IssueNumber)
    
    try {
        $commentsUrl = "$sourceUrl/issues/$IssueNumber/comments"
        $allComments = @()
        $page = 1
        
        do {
            $comments = Invoke-RestMethod -Uri "${commentsUrl}?page=$page&per_page=100" -Headers $headers -Method Get
            $allComments += $comments
            $page++
        } while ($comments.Count -eq 100)
        
        return $allComments
    }
    catch {
        Write-Warning "⚠️  Could not fetch comments for issue #$IssueNumber : $($_.Exception.Message)"
        return @()
    }
}

function Anonymize-Username {
    param($Username)
    
    # Create consistent anonymized names
    $botNames = @("microsoft-github-operations", "microsoftopensource", "github-actions")
    
    if ($botNames -contains $Username.ToLower()) {
        return "Bot"
    }
    
    # For human users, create generic names based on hash for consistency
    $hash = [System.Security.Cryptography.MD5]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Username))
    $hashString = [System.BitConverter]::ToString($hash).Replace("-", "").Substring(0, 6)
    return "User$hashString"
}

function Clean-GitHubReferences {
    param([string]$Text)
    
    if ([string]::IsNullOrEmpty($Text)) {
        return $Text
    }
    
    # Simple find-replace approach to preserve formatting while removing references
    $cleanText = $Text
    
    # Replace direct GitHub repository references
    $cleanText = $cleanText -replace 'https://github\.com/Azure/apiops[^\s\)\]]*', '[repository link redacted]'
    $cleanText = $cleanText -replace 'github\.com/Azure/apiops[^\s\)\]]*', '[repository link redacted]'
    $cleanText = $cleanText -replace 'Azure/apiops', '[source project]'
    
    # Replace GitHub issue/PR references
    $cleanText = $cleanText -replace '#(\d+)', '[issue #$1 redacted]'
    $cleanText = $cleanText -replace '(?:closes?|fixes?|resolves?)\s+#\d+', '[closes issue - redacted]'
    
    # Replace GitHub issue query URLs
    $cleanText = $cleanText -replace 'https://github\.com/[^/]+/[^/]+/issues\?[^\s\)\]]*', '[issues page redacted]'
    
    # Replace GitHub commit references
    $cleanText = $cleanText -replace 'https://github\.com/[^/]+/[^/]+/commit/[a-f0-9]{7,40}', '[commit redacted]'
    $cleanText = $cleanText -replace '\b[a-f0-9]{7,40}\b(?=.*commit)', '[commit hash redacted]'
    
    # Replace @ mentions
    $cleanText = $cleanText -replace '@[a-zA-Z0-9_-]+', '[user redacted]'
    
    # Replace common migration language
    $cleanText = $cleanText -replace 'migrated from [^\s\.\!]*', 'imported'
    $cleanText = $cleanText -replace 'originally reported in [^\s\.\!]*', 'previously reported'
    $cleanText = $cleanText -replace 'transferred from [^\s\.\!]*', 'moved'
    
    # Replace specific problematic phrases while preserving context
    $cleanText = $cleanText -replace 'make sure you take a look at the \[closed issues\]\([^)]+\)', 'please review existing documentation'
    $cleanText = $cleanText -replace 'open source project', 'this project'
    
    return $cleanText
}

function Get-PrimaryLabel {
    param($Title, $Body, $Labels)
    
    $titleLower = $Title.ToLower()
    $bodyLower = if ($Body) { $Body.ToLower() } else { "" }
    
    # Check for bug indicators
    if ($titleLower -match "\[bug\]|bug:|error|fail|broken|issue|problem" -or 
        $bodyLower -match "bug|error|fail|broken|not working|doesn't work") {
        return "bug"
    }
    
    # Check for enhancement/feature indicators
    if ($titleLower -match "\[feature\]|\[enhancement\]|feature:|enhancement:|support" -or
        $bodyLower -match "feature|enhancement|improve|add|support|would like") {
        return "enhancement"
    }
    
    # Check existing labels
    foreach ($label in $Labels) {
        if ($label.name -match "^(bug|enhancement|feature)$") {
            return $label.name
        }
    }
    
    # Default to question
    return "question"
}

function Format-CommentsForJson {
    param($Comments)
    
    if (-not $Comments -or $Comments.Count -eq 0) {
        return @()
    }
    
    $formattedComments = @()
    foreach ($comment in $Comments) {
        $cleanBody = Clean-GitHubReferences -Text $comment.body
        $formattedComments += @{
            Author = Anonymize-Username $comment.user.login
            CreatedAt = $comment.created_at
            UpdatedAt = $comment.updated_at
            Body = $cleanBody
        }
    }
    
    return $formattedComments
}

function Export-IssuesToJson {
    param($Issues, $FilePath)
    
    Write-Host "📊 Preparing JSON export..." -ForegroundColor Yellow
    
    # Create JSON data structure
    $exportData = @{
        metadata = @{
            extractedAt = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
            totalIssues = $Issues.Count
            extractorVersion = "2.0"
        }
        issues = @()
    }
    
    foreach ($issue in $Issues) {
        $cleanBody = Clean-GitHubReferences -Text $issue.body
        $cleanTitle = Clean-GitHubReferences -Text $issue.title
        
        # Get and format comments for this issue
        $issueComments = if ($IncludeComments) { Format-CommentsForJson (Get-AllIssueComments -IssueNumber $issue.number) } else { @() }
        
        # Combine original body with all comments into a single body field
        $combinedBody = $cleanBody
        if ($issueComments.Count -gt 0) {
            $combinedBody += "`n`n---`n`n## Comments from Original Issue`n`n"
            for ($i = 0; $i -lt $issueComments.Count; $i++) {
                $comment = $issueComments[$i]
                $combinedBody += "**Comment $($i + 1)** _(by $($comment.Author) on $($comment.CreatedAt))_`n`n"
                $combinedBody += $comment.Body + "`n`n"
                if ($i -lt ($issueComments.Count - 1)) {
                    $combinedBody += "---`n`n"
                }
            }
        }
        
        $issueData = @{
            originalIssueNumber = $issue.number
            title = $cleanTitle
            cleanTitle = $cleanTitle -replace '^\[.*?\]\s*', ''
            state = $issue.state
            createdAt = $issue.created_at
            updatedAt = $issue.updated_at
            author = Anonymize-Username $issue.user.login
            primaryLabel = Get-PrimaryLabel -Title $cleanTitle -Body $cleanBody -Labels $issue.labels
            labels = @()  # No migration labels - keep anonymous
            commentCount = $issue.comments
            body = if ($combinedBody) { $combinedBody } else { "" }
            comments = $issueComments  # Keep separate comments for reference
            bodyPreview = if ($cleanBody) { 
                $preview = $cleanBody -replace '[`*#\[\]()]', '' -replace '\r\n|\n', ' ' -replace '\s+', ' '
                $preview.Substring(0, [Math]::Min(150, $preview.Length)).Trim() + "..."
            } else { "" }
            migrationStatus = @{
                status = "Pending"
                targetIssueNumber = $null
                targetUrl = ""
                notes = ""
                migratedAt = $null
            }
        }
        $exportData.issues += $issueData
    }
    
    # Export to JSON with pretty formatting
    $jsonContent = $exportData | ConvertTo-Json -Depth 10
    $jsonContent | Set-Content -Path $FilePath -Encoding UTF8
    
    Write-Host "✅ Exported $($exportData.issues.Count) issues to: $FilePath" -ForegroundColor Green
    
    # Display summary
    Write-Host ""
    Write-Host "📋 Export Summary:" -ForegroundColor Blue
    $labelCounts = $exportData.issues | Group-Object primaryLabel | Sort-Object Count -Descending
    foreach ($group in $labelCounts) {
        Write-Host "   $($group.Name): $($group.Count) issues" -ForegroundColor Cyan
    }
    
    # Show file size
    $fileInfo = Get-Item $FilePath
    $fileSizeMB = [Math]::Round($fileInfo.Length / 1MB, 2)
    Write-Host "   File size: $fileSizeMB MB" -ForegroundColor Cyan
}

# Main execution
try {
    Write-Banner "SOURCE PROJECT ISSUE EXTRACTOR"
    
    Write-Host "Configuration:" -ForegroundColor Cyan
    Write-Host "  Source: [source project]" -ForegroundColor Gray
    Write-Host "  Output: $OutputFile" -ForegroundColor Gray
    Write-Host "  Include Comments: $IncludeComments" -ForegroundColor Gray
    Write-Host ""
    
    # Test source repository access
    $sourceRepo = Test-SourceAccess
    if (-not $sourceRepo) { 
        exit 1 
    }
    
    Write-Host ""
    Write-Host "🔄 Fetching all open issues..." -ForegroundColor Yellow
    
    # Fetch all open issues
    $allIssues = @()
    $page = 1
    
    do {
        Write-Host "   📄 Fetching page $page..." -ForegroundColor Gray
        $issues = Invoke-RestMethod -Uri "$sourceUrl/issues?state=open&page=$page&per_page=100" -Headers $headers -Method Get
        
        # Filter out pull requests (they appear in issues endpoint)
        $issuesOnly = $issues | Where-Object { -not ($_.PSObject.Properties.Name -contains 'pull_request' -and $_.pull_request) }
        $allIssues += $issuesOnly
        
        Write-Host "      Found $($issuesOnly.Count) issues (filtered out $($issues.Count - $issuesOnly.Count) PRs)" -ForegroundColor Gray
        $page++
        
        # Small delay to be nice to API
        Start-Sleep -Milliseconds 200
        
    } while ($issues.Count -eq 100)
    
    Write-Host ""
    Write-Host "✅ Retrieved $($allIssues.Count) open issues" -ForegroundColor Green
    
    if ($IncludeComments) {
        Write-Host "🗨️  Fetching comments for all issues..." -ForegroundColor Yellow
        Write-Host "   (This may take a few minutes...)" -ForegroundColor Gray
    }
    
    # Export to JSON
    Export-IssuesToJson -Issues $allIssues -FilePath $OutputFile
    
    Write-Host ""
    Write-Host "🎉 Extraction completed successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "📋 Next Steps:" -ForegroundColor Yellow
    Write-Host "   1. Review the JSON file: $OutputFile" -ForegroundColor Cyan
    Write-Host "   2. Edit any issues you want to modify" -ForegroundColor Cyan
    Write-Host "   3. Run the creation script:" -ForegroundColor Cyan
    Write-Host "      .\Create-IssuesFromJson.ps1 -GitHubToken 'token' -JsonFile '$OutputFile'" -ForegroundColor White
    
} catch {
    Write-Error "💥 Script failed: $($_.Exception.Message)"
    Write-Error "   Stack trace: $($_.ScriptStackTrace)"
    exit 1
}