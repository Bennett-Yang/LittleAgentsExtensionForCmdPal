# WinGet release checklist

Approved package metadata for the first public release:

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
| ReleaseNotes | `First public release of Little Agents for PowerToys Command Palette.` |

The first WinGet submission is intentionally not performed from this repository.
Create a public GitHub Release first, then generate manifests from the released installer URLs.

Required locale tag for Command Palette discovery:

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

Local-only workflow:

```powershell
cd LittleAgentsExtension
.\build-exe.ps1 -Version "0.1.0.0"
certutil -hashfile "bin\Release\installer\LittleAgentsExtension-Setup-0.1.0.0-x64.exe" SHA256
certutil -hashfile "bin\Release\installer\LittleAgentsExtension-Setup-0.1.0.0-arm64.exe" SHA256
```

After the `.exe` files are attached to a public GitHub Release, use the final HTTPS asset URLs with `wingetcreate new` or create a multi-file manifest under:

```text
manifests\b\BennettYang\LittleAgents\0.1.0.0\
```

Do not submit until these local checks pass:

```powershell
winget validate <manifest-directory>
winget install --manifest <manifest-directory>
```

Current local installer hashes:

| Architecture | Installer | SHA256 |
| --- | --- | --- |
| x64 | `LittleAgentsExtension-Setup-0.1.0.0-x64.exe` | `1eeececa78ba0d19232b8b0300b3955a44df16bfbdb00f40d82ef63b458c2b80` |
| arm64 | `LittleAgentsExtension-Setup-0.1.0.0-arm64.exe` | `582772c0614e17af57a634367ced36f7fa6c43091c746e38a05176caa9a26702` |
