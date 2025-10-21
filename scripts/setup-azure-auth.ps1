# Azure Setup Script for TwinFx Azure AI Agents
# This script helps setup Azure authentication for AgentCodeInt functionality

Write-Host "?? TwinFx Azure AI Agents Setup Script" -ForegroundColor Cyan
Write-Host "=" * 50 -ForegroundColor Cyan

# Function to check if Azure CLI is installed
function Test-AzureCLI {
    try {
        $azVersion = az --version 2>$null
        if ($azVersion) {
            Write-Host "? Azure CLI is installed" -ForegroundColor Green
            return $true
        }
    }
    catch {
        Write-Host "? Azure CLI not found" -ForegroundColor Red
        return $false
    }
    return $false
}

# Function to check current Azure login status
function Test-AzureLogin {
    try {
        $account = az account show 2>$null | ConvertFrom-Json
        if ($account) {
            Write-Host "? Already logged in to Azure" -ForegroundColor Green
            Write-Host "   Account: $($account.user.name)" -ForegroundColor Gray
            Write-Host "   Subscription: $($account.name)" -ForegroundColor Gray
            return $true
        }
    }
    catch {
        Write-Host "? Not logged in to Azure" -ForegroundColor Red
        return $false
    }
    return $false
}

# Function to perform Azure login
function Invoke-AzureLogin {
    Write-Host "?? Starting Azure login process..." -ForegroundColor Yellow
    
    try {
        # Try regular login first
        Write-Host "Please complete the login in your browser..." -ForegroundColor Yellow
        az login --only-show-errors
        
        # Verify login worked
        $account = az account show 2>$null | ConvertFrom-Json
        if ($account) {
            Write-Host "? Azure login successful!" -ForegroundColor Green
            Write-Host "   Account: $($account.user.name)" -ForegroundColor Gray
            Write-Host "   Subscription: $($account.name)" -ForegroundColor Gray
            return $true
        }
        else {
            Write-Host "? Login verification failed" -ForegroundColor Red
            return $false
        }
    }
    catch {
        Write-Host "? Azure login failed: $_" -ForegroundColor Red
        
        # Try device code login as fallback
        Write-Host "?? Trying device code login..." -ForegroundColor Yellow
        try {
            az login --use-device-code --only-show-errors
            $account = az account show 2>$null | ConvertFrom-Json
            if ($account) {
                Write-Host "? Device code login successful!" -ForegroundColor Green
                return $true
            }
        }
        catch {
            Write-Host "? Device code login also failed: $_" -ForegroundColor Red
            return $false
        }
    }
    return $false
}

# Function to test AI Agents access
function Test-AIAgentsAccess {
    Write-Host "?? Testing Azure AI services access..." -ForegroundColor Yellow
    
    try {
        # Test getting an access token for Cognitive Services
        $token = az account get-access-token --resource https://cognitiveservices.azure.com/ 2>$null | ConvertFrom-Json
        if ($token -and $token.accessToken) {
            Write-Host "? Azure AI services access verified" -ForegroundColor Green
            return $true
        }
        else {
            Write-Host "? Could not get Azure AI services access token" -ForegroundColor Red
            return $false
        }
    }
    catch {
        Write-Host "? Azure AI services access test failed: $_" -ForegroundColor Red
        return $false
    }
}

# Function to check local.settings.json
function Test-LocalSettings {
    $settingsPath = "local.settings.json"
    $testSettingsPath = "../TwinFxTests/local.settings.json"
    
    $foundPath = $null
    if (Test-Path $settingsPath) {
        $foundPath = $settingsPath
    }
    elseif (Test-Path $testSettingsPath) {
        $foundPath = $testSettingsPath
    }
    
    if ($foundPath) {
        Write-Host "? Found configuration file: $foundPath" -ForegroundColor Green
        
        try {
            $settings = Get-Content $foundPath -Raw | ConvertFrom-Json
            
            # Check for PROJECT_CONNECTION_STRING
            $projectConnection = $settings.Values.PROJECT_CONNECTION_STRING
            if ($projectConnection) {
                Write-Host "? PROJECT_CONNECTION_STRING found in configuration" -ForegroundColor Green
                return $true
            }
            else {
                Write-Host "?? PROJECT_CONNECTION_STRING not found in configuration" -ForegroundColor Yellow
                Write-Host "   This is needed for Azure AI Agents functionality" -ForegroundColor Gray
                
                # Check for OpenAI settings as fallback
                $openaiEndpoint = $settings.Values.'AzureOpenAI:Endpoint' -or $settings.'AzureOpenAI'.'Endpoint'
                if ($openaiEndpoint) {
                    Write-Host "? Azure OpenAI settings found (fallback available)" -ForegroundColor Green
                }
                else {
                    Write-Host "? No Azure OpenAI settings found either" -ForegroundColor Red
                }
            }
        }
        catch {
            Write-Host "? Could not parse configuration file: $_" -ForegroundColor Red
        }
    }
    else {
        Write-Host "? No local.settings.json found" -ForegroundColor Red
        Write-Host "   Please create local.settings.json with your Azure configuration" -ForegroundColor Gray
    }
    
    return $false
}

# Main execution
Write-Host ""
Write-Host "?? Checking current setup..." -ForegroundColor Cyan

# Check Azure CLI
$cliInstalled = Test-AzureCLI
if (-not $cliInstalled) {
    Write-Host ""
    Write-Host "?? Please install Azure CLI first:" -ForegroundColor Yellow
    Write-Host "   https://docs.microsoft.com/en-us/cli/azure/install-azure-cli" -ForegroundColor Gray
    Write-Host ""
    exit 1
}

# Check current login status
Write-Host ""
$loggedIn = Test-AzureLogin

# Login if needed
if (-not $loggedIn) {
    Write-Host ""
    $loginSuccess = Invoke-AzureLogin
    if (-not $loginSuccess) {
        Write-Host ""
        Write-Host "? Azure login failed. Please try manual login:" -ForegroundColor Red
        Write-Host "   az login" -ForegroundColor Gray
        Write-Host ""
        exit 1
    }
}

# Test AI services access
Write-Host ""
$aiAccess = Test-AIAgentsAccess

# Check configuration
Write-Host ""
$configOK = Test-LocalSettings

# Summary
Write-Host ""
Write-Host "?? Setup Summary:" -ForegroundColor Cyan
Write-Host "   Azure CLI: $(if ($cliInstalled) { '?' } else { '?' })" -ForegroundColor $(if ($cliInstalled) { 'Green' } else { 'Red' })
Write-Host "   Azure Login: $(if ($loggedIn -or $loginSuccess) { '?' } else { '?' })" -ForegroundColor $(if ($loggedIn -or $loginSuccess) { 'Green' } else { 'Red' })
Write-Host "   AI Services Access: $(if ($aiAccess) { '?' } else { '?' })" -ForegroundColor $(if ($aiAccess) { 'Green' } else { 'Red' })
Write-Host "   Configuration: $(if ($configOK) { '?' } else { '??' })" -ForegroundColor $(if ($configOK) { 'Green' } else { 'Yellow' })

Write-Host ""
if ($cliInstalled -and ($loggedIn -or $loginSuccess) -and $aiAccess) {
    Write-Host "?? Azure authentication is ready for AgentCodeInt!" -ForegroundColor Green
    Write-Host ""
    Write-Host "You can now use the CSV analysis features with Azure AI Agents." -ForegroundColor Gray
    Write-Host "If you still encounter issues, you may need to set up PROJECT_CONNECTION_STRING" -ForegroundColor Gray
    Write-Host "in your local.settings.json file. See docs/azure-ai-agents-setup.md for details." -ForegroundColor Gray
}
else {
    Write-Host "?? Setup incomplete. Please address the issues above." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "For detailed instructions, see: docs/azure-ai-agents-setup.md" -ForegroundColor Gray
}

Write-Host ""