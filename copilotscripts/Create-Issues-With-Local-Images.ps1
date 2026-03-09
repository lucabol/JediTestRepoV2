#Requires -Version 7.0

<#
.SYNOPSIS
Creates GitHub issues from source project data with local image references.

.DESCRIPTION
This script creates GitHub issues in the private repository using the extracted
source project issue data. It assumes images have already been downloaded and 
committed to the repository.

.PARAMETER GitHubToken
GitHub Personal Access Token for API authentication

.PARAMETER JsonFile
Path to JSON file containing issue data (default: azure-apiops-issues.json)

.PARAMETER MaxIssues
Maximum number of issues to create (default: 5, 0 = all)

.PARAMETER StartFromIssue
Issue number to start processing from (default: 1, used for batch processing)

.PARAMETER RepoOwner
Repository owner (default: nicolehaugen)

.PARAMETER RepoName
Repository name (default: JediTestRepo)

.PARAMETER ImageFolder
Folder path in repository where images are stored (default: assets/images/migrated)
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$GitHubToken,
    
    [Parameter(Mandatory = $false)]
    [string]$JsonFile = "azure-apiops-issues-final.json",
    
    [Parameter(Mandatory = $false)]
    [int]$MaxIssues = 5,
    
    [Parameter(Mandatory = $false)]
    [int]$StartFromIssue = 1,
    
    [Parameter(Mandatory = $false)]
    [string]$RepoOwner = "nicolehaugen",
    
    [Parameter(Mandatory = $false)]
    [string]$RepoName = "JediTestRepo",
    
    [Parameter(Mandatory = $false)]
    [string]$ImageFolder = "assets/images/migrated"
)

# Set strict mode and error handling
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "🚀 GitHub Issue Creator with Local Images" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Validate inputs
if (-not (Test-Path $JsonFile)) {
    Write-Error "❌ JSON file not found: $JsonFile"
    exit 1
}

# Setup GitHub API headers
$headers = @{
    'Accept' = 'application/vnd.github.v3+json'
    'Authorization' = "token $GitHubToken"
    'User-Agent' = 'PowerShell-Issue-Migration'
}

# Load JSON data and filter for open issues only
Write-Host "📖 Loading issue data..." -ForegroundColor Yellow
$jsonData = Get-Content -Path $JsonFile -Raw | ConvertFrom-Json
$openIssues = $jsonData.issues | Where-Object { $_.state -eq "open" }

Write-Host "✅ Found $($openIssues.Count) open issues" -ForegroundColor Green

# Skip issues that have already been processed
$issuesToProcess = if ($MaxIssues -gt 0) { 
    $openIssues | Select-Object -Skip ($StartFromIssue - 1) | Select-Object -First $MaxIssues 
} else { 
    $openIssues | Select-Object -Skip ($StartFromIssue - 1)
}

Write-Host "🎯 Processing $($issuesToProcess.Count) issues (starting from issue #$StartFromIssue)" -ForegroundColor Yellow
if ($StartFromIssue -gt 1) {
    Write-Host "   Skipping first $($StartFromIssue - 1) issues (already processed)" -ForegroundColor Cyan
}
Write-Host ""

function Clean-TextOfReferences {
    param(
        [string]$Text
    )
    
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $Text
    }
    
    # Remove all GitHub references and Azure/apiops mentions
    $cleanedText = $Text
    
    # Remove Azure/apiops references
    $cleanedText = $cleanedText -replace 'Azure/apiops', 'this project'
    $cleanedText = $cleanedText -replace 'azure/apiops', 'this project'
    $cleanedText = $cleanedText -replace 'AZURE/APIOPS', 'this project'
    
    # Remove GitHub URLs completely
    $cleanedText = $cleanedText -replace 'https://github\.com/[^)\s\]]+', '[reference removed]'
    $cleanedText = $cleanedText -replace 'http://github\.com/[^)\s\]]+', '[reference removed]'
    
    # Remove GitHub @mentions
    $cleanedText = $cleanedText -replace '@[a-zA-Z0-9\-_]+', '[user]'
    
    # Remove GitHub issue references like #123
    $cleanedText = $cleanedText -replace '#\d+', '[issue reference]'
    
    # Remove "closed issues" links and similar repository-specific references
    $cleanedText = $cleanedText -replace '\[closed issues\]\([^)]+\)', 'previously closed issues'
    $cleanedText = $cleanedText -replace '\[open issues\]\([^)]+\)', 'open issues'
    
    # Clean up any remaining markdown links that might reference GitHub
    $cleanedText = $cleanedText -replace '\[([^\]]+)\]\([^)]*github[^)]*\)', '$1'
    
    # Remove any remaining references to "repository", "repo", "PR", "Pull Request"
    $cleanedText = $cleanedText -replace '\brepository\b', 'project'
    $cleanedText = $cleanedText -replace '\brepo\b', 'project'
    $cleanedText = $cleanedText -replace '\bPR\b', 'contribution'
    $cleanedText = $cleanedText -replace '\bPull Request\b', 'contribution'
    
    # Clean up multiple spaces and empty reference markers
    $cleanedText = $cleanedText -replace '\[reference removed\]\s*', ''
    $cleanedText = $cleanedText -replace '\s+', ' '
    $cleanedText = $cleanedText.Trim()
    
    return $cleanedText
}

function Process-ImagesInText {
    param(
        [string]$Text,
        [string]$RepoOwner,
        [string]$RepoName,
        [string]$ImageFolder
    )
    
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $Text
    }

    # Process GitHub user-attachments URLs and replace with relative repository references
    $result = [regex]::Replace($Text, 'https://github\.com/user-attachments/assets/([a-f0-9\-]+)', {
        param($match)
        
        $assetId = $match.Groups[1].Value
        $localImagePath = "../blob/main/$ImageFolder/$assetId.jpg?raw=true"
        
        # Check if this is in markdown image format
        $beforeMatch = $Text.Substring(0, $match.Index)
        $afterMatch = $Text.Substring($match.Index + $match.Length)
        
        if ($beforeMatch -match '!\[[^\]]*\]\($' -and $afterMatch -match '^\)') {
            # It's a markdown image, just replace the URL
            return $localImagePath
        }
        elseif ($beforeMatch -match '<img[^>]*src="$' -and $afterMatch -match '^"[^>]*>') {
            # It's an HTML img tag, just replace the URL
            return $localImagePath
        }
        else {
            # Convert to markdown image format
            return "![Image]($localImagePath)"
        }
    })

    return $result
}function Build-IssueBody {
    param(
        [object]$Issue,
        [string]$RepoOwner,
        [string]$RepoName,
        [string]$ImageFolder
    )
    
    $body = @()
    
    # NO migration notice - completely anonymous
    
    # Process main issue body with image replacements
    if (-not [string]::IsNullOrWhiteSpace($Issue.body)) {
        $processedBody = Process-ImagesInText -Text $Issue.body -RepoOwner $RepoOwner -RepoName $RepoName -ImageFolder $ImageFolder
        $body += $processedBody
    }
    
    # Add cleaned comments (they already have references redacted from extraction)
    if ($Issue.comments -and $Issue.comments.Count -gt 0) {
        $body += ""
        $body += "---"
        $body += ""
        $body += "## Comments from Original Issue"
        $body += ""
        
        for ($i = 0; $i -lt $Issue.comments.Count; $i++) {
            $comment = $Issue.comments[$i]
            $body += "**Comment $($i + 1)** _(by $($comment.Author) on $($comment.CreatedAt))_"
            $body += ""
            
            # Process comment body for images too
            $processedCommentBody = Process-ImagesInText -Text $comment.Body -RepoOwner $RepoOwner -RepoName $RepoName -ImageFolder $ImageFolder
            $body += $processedCommentBody
            $body += ""
            
            # Add separator between comments (but not after the last one)
            if ($i -lt ($Issue.comments.Count - 1)) {
                $body += "---"
                $body += ""
            }
        }
    }
    
    return ($body -join "`n")
}

function Create-GitHubIssue {
    param(
        [string]$Title,
        [string]$Body,
        [string[]]$Labels,
        [hashtable]$Headers,
        [string]$RepoOwner,
        [string]$RepoName
    )
    
    $issueData = @{
        title = $Title
        body = $Body
        labels = $Labels
    }
    
    $jsonBody = $issueData | ConvertTo-Json -Depth 10 -Compress
    $uri = "https://api.github.com/repos/$RepoOwner/$RepoName/issues"
    
    try {
        $response = Invoke-RestMethod -Uri $uri -Method POST -Headers $Headers -Body $jsonBody -ContentType 'application/json'
        return $response
    }
    catch {
        Write-Error "Failed to create issue: $($_.Exception.Message)"
        if ($_.Exception.Response) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            Write-Error "Response: $responseBody"
        }
        throw
    }
}

# Process each issue
$created = 0
$failed = 0

foreach ($issue in $issuesToProcess) {
    try {
        Write-Host "🔄 Processing Issue #$($issue.originalIssueNumber): $($issue.title)" -ForegroundColor White
        
        # Build the issue body with local image references
        $issueBody = Build-IssueBody -Issue $issue -RepoOwner $RepoOwner -RepoName $RepoName -ImageFolder $ImageFolder
        
        # Create labels from original issue (NO migration labels)
        $labels = @()
        if ($issue.labels -and $issue.labels.Count -gt 0) {
            foreach ($label in $issue.labels) {
                if ($label -is [string]) {
                    $labels += $label
                } else {
                    $labels += $label.name
                }
            }
        }
        
        # Create the issue
        Write-Host "   📝 Creating issue..." -ForegroundColor Cyan
        $newIssue = Create-GitHubIssue -Title $issue.title -Body $issueBody -Labels $labels -Headers $headers -RepoOwner $RepoOwner -RepoName $RepoName
        
        Write-Host "   ✅ Created issue #$($newIssue.number): $($newIssue.html_url)" -ForegroundColor Green
        $created++
        
        # Brief pause between issues
        Start-Sleep -Seconds 1
        
    }
    catch {
        Write-Host "   ❌ Failed to create issue #$($issue.originalIssueNumber): $($_.Exception.Message)" -ForegroundColor Red
        $failed++
        
        # Continue processing other issues
        continue
    }
}

Write-Host ""
Write-Host "🎉 Migration Summary" -ForegroundColor Green
Write-Host "===================" -ForegroundColor Green
Write-Host "✅ Successfully created: $created issues" -ForegroundColor Green
if ($failed -gt 0) {
    Write-Host "❌ Failed to create: $failed issues" -ForegroundColor Red
}
Write-Host ""
Write-Host "🔗 Repository: https://github.com/$RepoOwner/$RepoName/issues" -ForegroundColor Cyan