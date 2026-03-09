#Requires -Version 7.0

<#
.SYNOPSIS
Downloads and compresses images from Azure/apiops open issues to local repository.

.DESCRIPTION
This script extracts images from open issues in the Azure/apiops repository, 
compresses them, and saves them locally using their original filenames.
You can then commit and push these files manually.

.PARAMETER JsonFile
Path to JSON file containing issue data (default: azure-apiops-issues.json)

.PARAMETER OutputFolder
Local folder to store downloaded images (default: assets/images/migrated)

.PARAMETER MaxIssues
Maximum number of issues to process (default: 0 = all)
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$JsonFile = "azure-apiops-issues.json",
    
    [Parameter(Mandatory = $false)]
    [string]$OutputFolder = "assets/images/migrated",
    
    [Parameter(Mandatory = $false)]
    [int]$MaxIssues = 0
)

# Set strict mode and error handling
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "🖼️  GitHub Issue Image Downloader" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Validate inputs
if (-not (Test-Path $JsonFile)) {
    Write-Error "❌ JSON file not found: $JsonFile"
    exit 1
}

# Create output folder if it doesn't exist
if (-not (Test-Path $OutputFolder)) {
    Write-Host "📁 Creating output folder: $OutputFolder" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
}

# Load JSON data and filter for open issues only
Write-Host "📖 Loading issue data..." -ForegroundColor Yellow
$jsonData = Get-Content -Path $JsonFile -Raw | ConvertFrom-Json
$openIssues = $jsonData.issues | Where-Object { $_.state -eq "open" }

Write-Host "✅ Found $($openIssues.Count) open issues to process" -ForegroundColor Green

# Limit issues if specified
$issuesToProcess = if ($MaxIssues -gt 0) { 
    $openIssues | Select-Object -First $MaxIssues 
} else { 
    $openIssues 
}

Write-Host "🎯 Processing $($issuesToProcess.Count) issues" -ForegroundColor Yellow

function Compress-And-Save-Image {
    param(
        [string]$ImageUrl,
        [string]$OutputPath
    )
    
    try {
        # Extract original filename from URL
        $originalFileName = ($ImageUrl -split '/')[-1]
        $outputFile = Join-Path $OutputPath "$originalFileName.jpg"
        
        # Skip if already downloaded
        if (Test-Path $outputFile) {
            Write-Host "        ⏭️  Already exists: $originalFileName.jpg" -ForegroundColor Gray
            return $outputFile
        }
        
        Write-Host "        📥 Downloading: $originalFileName..." -ForegroundColor Cyan
        
        # Download the image
        $response = Invoke-WebRequest -Uri $ImageUrl -UseBasicParsing
        $imageBytes = $response.Content
        
        # Check original size
        $originalSizeKB = ($imageBytes.Length / 1024)
        Write-Host "        📊 Original size: $($originalSizeKB.ToString('F1')) KB" -ForegroundColor Gray
        
        # Compress the image using .NET
        Add-Type -AssemblyName System.Drawing
        
        # Create image from bytes
        $memoryStream = New-Object System.IO.MemoryStream
        $memoryStream.Write($imageBytes, 0, $imageBytes.Length)
        $memoryStream.Position = 0
        $originalImage = [System.Drawing.Image]::FromStream($memoryStream)
        
        # Calculate new dimensions (max 800x600, maintain aspect ratio)
        $maxWidth = [Math]::Min(800, $originalImage.Width)
        $maxHeight = [Math]::Min(600, $originalImage.Height)
        
        $scaleX = $maxWidth / $originalImage.Width
        $scaleY = $maxHeight / $originalImage.Height
        $scale = [Math]::Min($scaleX, $scaleY)
        
        if ($scale -ge 1) {
            $newWidth = $originalImage.Width
            $newHeight = $originalImage.Height
        } else {
            $newWidth = [int][Math]::Round($originalImage.Width * $scale)
            $newHeight = [int][Math]::Round($originalImage.Height * $scale)
        }
        
        # Ensure minimum size
        if ($newWidth -lt 1) { $newWidth = 1 }
        if ($newHeight -lt 1) { $newHeight = 1 }
        
        Write-Host "        📐 Resizing from $($originalImage.Width)x$($originalImage.Height) to ${newWidth}x${newHeight}" -ForegroundColor Cyan
        
        # Create new bitmap
        $newBitmap = New-Object System.Drawing.Bitmap($newWidth, $newHeight)
        $graphics = [System.Drawing.Graphics]::FromImage($newBitmap)
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        
        # Draw resized image
        $graphics.DrawImage($originalImage, 0, 0, $newWidth, $newHeight)
        
        # Save as JPEG with compression
        $jpegEncoder = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq "image/jpeg" }
        $encoderParams = New-Object System.Drawing.Imaging.EncoderParameters(1)
        $encoderParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, 75L)
        
        # Save to file
        $newBitmap.Save($outputFile, $jpegEncoder, $encoderParams)
        
        # Get final size
        $finalSizeKB = ((Get-Item $outputFile).Length / 1024)
        Write-Host "        ✅ Saved: $originalFileName.jpg ($($finalSizeKB.ToString('F1')) KB)" -ForegroundColor Green
        
        # Clean up
        $graphics.Dispose()
        $newBitmap.Dispose()
        $originalImage.Dispose()
        $memoryStream.Dispose()
        
        return $outputFile
        
    }
    catch {
        Write-Host "        ❌ Failed to process image: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

# Process all open issues
$totalImages = 0
$downloadedImages = 0
$processedUrls = @{}

Write-Host ""
Write-Host "🚀 Processing open issues..." -ForegroundColor Yellow

foreach ($issue in $issuesToProcess) {
    Write-Host ""
    Write-Host "🔍 Issue #$($issue.originalIssueNumber): $($issue.title)" -ForegroundColor White
    
    # Find images in issue body
    $allText = $issue.body
    if ($issue.comments) {
        $allText += "`n" + $issue.comments.Body
    }
    
    # Find all GitHub user-attachments URLs
    $imageUrls = @([regex]::Matches($allText, 'https://github\.com/user-attachments/assets/[a-f0-9\-]+') | 
                 ForEach-Object { $_.Value } | 
                 Sort-Object -Unique)
    
    if ($imageUrls.Count -eq 0) {
        Write-Host "   📄 No images found" -ForegroundColor Gray
        continue
    }
    
    Write-Host "   🖼️  Found $($imageUrls.Count) unique images" -ForegroundColor Yellow
    $totalImages += $imageUrls.Count
    
    foreach ($imageUrl in $imageUrls) {
        # Skip if already processed (same image in multiple issues)
        if ($processedUrls.ContainsKey($imageUrl)) {
            Write-Host "        ♻️  Already processed: $($imageUrl.Split('/')[-1])" -ForegroundColor Gray
            continue
        }
        
        $savedFile = Compress-And-Save-Image -ImageUrl $imageUrl -OutputPath $OutputFolder
        if ($savedFile) {
            $downloadedImages++
            $processedUrls[$imageUrl] = $savedFile
        }
        
        # Small delay to be nice to GitHub
        Start-Sleep -Milliseconds 300
    }
}

Write-Host ""
Write-Host "🎉 Download Summary" -ForegroundColor Green
Write-Host "===================" -ForegroundColor Green
Write-Host "📊 Total unique images found: $totalImages" -ForegroundColor White
Write-Host "✅ Successfully downloaded: $downloadedImages" -ForegroundColor Green
Write-Host "📁 Saved to: $OutputFolder" -ForegroundColor Cyan
Write-Host ""
Write-Host "📋 Next steps:" -ForegroundColor Yellow
Write-Host "   1. Review the downloaded images in: $OutputFolder" -ForegroundColor White
Write-Host "   2. Commit and push the images to your repository" -ForegroundColor White
Write-Host "   3. Run the issue creation script to reference the uploaded images" -ForegroundColor White