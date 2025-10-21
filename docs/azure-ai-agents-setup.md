# Azure AI Agents Setup Guide

This guide helps you set up Azure AI Agents for the AgentCodeInt functionality in TwinFx.

## ?? Current Issue

The `AgentCodeInt` class uses Azure AI Agents which requires either:
1. **Azure AI Project** with proper PROJECT_CONNECTION_STRING, OR
2. **Valid Azure authentication** through DefaultAzureCredential

## ?? Quick Fix Solutions

### Solution 1: Azure CLI Login (Recommended for Development)

```bash
# Install Azure CLI if not already installed
# https://docs.microsoft.com/en-us/cli/azure/install-azure-cli

# Login to Azure
az login

# Set your subscription (replace with your subscription ID)
az account set --subscription "your-subscription-id"

# Verify login
az account show
```

### Solution 2: Add PROJECT_CONNECTION_STRING to Configuration

Add this to your `local.settings.json`:

```json
{
  "Values": {
    "PROJECT_CONNECTION_STRING": "your-azure-ai-project-connection-string",
    // ... existing settings
  }
}
```

To get your PROJECT_CONNECTION_STRING:
1. Go to [Azure AI Studio](https://ai.azure.com)
2. Create or select an Azure AI Project
3. Go to **Settings** ? **Properties**
4. Copy the **Connection string**

### Solution 3: Use Environment Variables

Set these environment variables:

```bash
# Windows (PowerShell)
$env:AZURE_TENANT_ID="your-tenant-id"
$env:AZURE_CLIENT_ID="your-client-id"
$env:AZURE_CLIENT_SECRET="your-client-secret"

# Or use Azure CLI login
$env:AZURE_CLI_PATH="C:\Program Files (x86)\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"
```

## ?? Alternative: Create Azure AI Project

If you don't have an Azure AI Project:

1. **Go to [Azure AI Studio](https://ai.azure.com)**
2. **Create New Project**:
   - Name: `TwinFx-AI-Project`
   - Subscription: Your Azure subscription
   - Resource Group: Create new or use existing
   - Location: Same as your OpenAI resource

3. **Configure the project**:
   - Add your existing Azure OpenAI resource
   - Enable required APIs

4. **Get Connection String**:
   - Project Settings ? Properties
   - Copy the connection string
   - Add to `local.settings.json` as PROJECT_CONNECTION_STRING

## ?? Updated Configuration

Your complete `local.settings.json` should look like:

```json
{
  "Values": {
    "PROJECT_CONNECTION_STRING": "your-azure-ai-project-connection-string",
    
    "AzureOpenAI:Endpoint": "https://flatbitai.openai.azure.com/",
    "AzureOpenAI:ApiKey": "your-api-key",
    "AzureOpenAI:DeploymentName": "gpt4mini",
    
    // ... other settings
  }
}
```

## ?? Test the Setup

Run this PowerShell command to test:

```powershell
# Test Azure CLI authentication
az account get-access-token --resource https://cognitiveservices.azure.com/

# If successful, you should see an access token
```

## ?? Troubleshooting

### "DefaultAzureCredential failed to retrieve a token"

1. **Check Azure CLI login**:
   ```bash
   az account show
   az login --use-device-code  # Alternative login method
   ```

2. **Clear credentials cache**:
   ```bash
   az account clear
   az login
   ```

3. **Check Visual Studio authentication**:
   - Tools ? Options ? Azure Service Authentication
   - Sign out and sign back in

### "PROJECT_CONNECTION_STRING not found"

1. Verify the connection string is correct
2. Check it's in the right section (`Values` for Azure Functions)
3. Restart your application after adding the configuration

### Still having issues?

The AgentCodeInt has fallback mechanisms, but for full functionality you need proper Azure AI Agents setup. 

**Temporary workaround**: Use the other CSV analysis methods in the codebase that don't require Azure AI Agents until you can set up the proper authentication.

## ?? References

- [Azure AI Studio Documentation](https://docs.microsoft.com/azure/ai-studio/)
- [DefaultAzureCredential Documentation](https://docs.microsoft.com/azure/identity/credential-chains)
- [Azure CLI Authentication](https://docs.microsoft.com/cli/azure/authenticate-azure-cli)