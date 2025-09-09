# ?? Configuration Security Guide

## ?? **IMPORTANT SECURITY NOTICE**

**NEVER commit sensitive credentials to the repository!**

## ?? **Configuration Files Structure**

### ? **Safe for Repository:**
- `settings.json` - Contains sample/template configuration
- `appsettings.json` - Base application settings
- `host.json` - Azure Functions host configuration

### ? **NEVER Commit (Protected by .gitignore):**
- `local.settings.json` - Contains actual credentials for local development
- `appsettings.Development.json` - Development environment secrets
- `.env` files - Environment variables with secrets

## ??? **Setup for Development**

### 1. **Copy Template:**
```bash
cp settings.json local.settings.json
```

### 2. **Update Local Settings:**
Edit `local.settings.json` with your actual credentials:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AZURE_STORAGE_ACCOUNT_NAME": "your-actual-storage-name",
    "AZURE_STORAGE_ACCOUNT_KEY": "your-actual-storage-key",
    "COSMOS_ENDPOINT": "https://your-cosmos.documents.azure.com:443/",
    "COSMOS_KEY": "your-actual-cosmos-key",
    "AzureOpenAI:ApiKey": "your-actual-openai-key"
  }
}
```

## ??? **Production Deployment**

### **Azure App Service:**
Use Application Settings instead of config files:
```bash
az webapp config appsettings set --name your-app --resource-group your-rg --settings COSMOS_KEY="your-key"
```

### **Azure Key Vault:**
Store secrets in Key Vault and reference them:
```json
{
  "COSMOS_KEY": "@Microsoft.KeyVault(VaultName=your-vault;SecretName=cosmos-key)"
}
```

## ?? **Security Checklist**

- [ ] `local.settings.json` is in .gitignore ?
- [ ] No credentials in `settings.json` ?
- [ ] No credentials in source code ?
- [ ] Production uses Azure App Settings or Key Vault ?

## ?? **If Credentials Were Accidentally Committed:**

1. **Immediately rotate/revoke the exposed credentials**
2. **Remove from Git history:**
   ```bash
   git filter-branch --force --index-filter 'git rm --cached --ignore-unmatch local.settings.json' --prune-empty --tag-name-filter cat -- --all
   ```
3. **Update .gitignore and commit the fix**

## ?? **Need Help?**

If you accidentally committed credentials:
1. Stop what you're doing
2. Rotate the compromised credentials immediately
3. Contact the security team
4. Follow the credential removal procedure above