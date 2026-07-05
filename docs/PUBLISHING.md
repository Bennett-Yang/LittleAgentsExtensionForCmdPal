# Publishing LittleAgents

This guide covers the supported publishing paths for the LittleAgents MSIX package. Use placeholders while drafting commands. Don't record real certificate thumbprints, publisher subjects, Partner Center account names, tenant IDs, secrets, or maintainer identities in this file.

## 1. WinGet Path

The preferred WinGet path is an Inno Setup `.exe` installer that publishes through GitHub Releases and then `microsoft/winget-pkgs`.

Approved first-release metadata:

| Field | Value |
| --- | --- |
| PackageIdentifier | `BennettYang.LittleAgents` |
| PackageName | `Little Agents` |
| Publisher | `Bennett Yang` |
| Version | `0.1.0.0` |
| License | `MIT` |
| PackageUrl | `https://github.com/Bennett-Yang/LittleAgentsExtensionForCmdPal` |
| PublisherUrl | `https://github.com/Bennett-Yang` |
| PublisherSupportUrl | `https://github.com/Bennett-Yang/LittleAgentsExtensionForCmdPal/issues` |
| PrivacyUrl | omitted |

Build local installers:

```powershell
cd LittleAgentsExtension
.\build-exe.ps1 -Version "0.1.0.0"
```

The public WinGet manifest cannot be finalized until the installer files are uploaded to immutable public HTTPS URLs. The locale manifest must include:

```yaml
Tags:
- windows-commandpalette-extension
```

Only add a Windows App SDK runtime dependency if the project later takes a direct `Microsoft.WindowsAppSDK` dependency:

```yaml
Dependencies:
  PackageDependencies:
  - PackageIdentifier: Microsoft.WindowsAppRuntime.1.8
```

Do not submit a WinGet PR without explicit approval.

## 2. Self-Signed Dev Cert Path

For local development signing, create a self-signed code-signing certificate with a placeholder common name:

```powershell
New-SelfSignedCertificate -Type Custom -Subject "CN=<your-name>" -KeyUsage DigitalSignature -FriendlyName "LittleAgents Dev" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}")
```

After creating the certificate, update `Package.appxmanifest` so `<Identity Publisher="..."/>` matches the certificate CN exactly. For example, if the certificate subject is `CN=<your-name>`, the manifest publisher value must be `CN=<your-name>`.

Sign the built MSIX with the certificate thumbprint placeholder:

```powershell
signtool sign /fd SHA256 /sha1 <thumbprint> <msix>
```

## 3. Microsoft Store Path

Follow the PowerToys Command Palette publish guidance for Store distribution: https://learn.microsoft.com/en-us/windows/powertoys/command-palette/publish-extension-store

Use Partner Center to reserve the extension name, upload the signed MSIX package, and fill the required metadata. Keep package names, publisher fields, screenshots, descriptions, and Store listing details aligned with the reserved Partner Center app record.

## 4. Sparse Package and CI Signing Reference

For advanced sparse-cert handling or CI signing patterns, see the PowerToys package identity script: https://github.com/microsoft/PowerToys/blob/main/src/PackageIdentity/BuildSparsePackage.ps1

Treat that script as a reference for sparse package certificate handling. Don't copy real certificate subjects, thumbprints, account names, or signing secrets from local machines or CI systems into this repository.
