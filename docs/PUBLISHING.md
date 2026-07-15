# Publishing Little Agents to Microsoft Store

Little Agents is distributed as an MSIX bundle so Windows registers its
`com.microsoft.commandpalette` app extension and Command Palette can discover it.

## Partner Center identity

The following values come from Partner Center under **Product management → Product identity** and must match exactly:

| Field | Value |
| --- | --- |
| Package/Identity/Name | `BennettYang.LittleAgentsExtensionforCommandPalette` |
| Package/Identity/Publisher | `CN=F5B40A0F-B50F-4878-8D4E-AE4A09317B53` |
| Package/Properties/PublisherDisplayName | `BennettYang` |
| Reserved product name | `Little Agents Extension for Command Palette` |
| Package version | `0.1.1.0` |

These values are configured in `Package.appxmanifest` and `LittleAgentsExtension.csproj`.

## Build the Store bundle

Prerequisites:

- .NET 9 SDK
- Windows 10/11 SDK with `makeappx.exe`
- Visual Studio MSIX tooling for IDE packaging commands

Run from the project directory:

```powershell
cd LittleAgentsExtension
.\build-store.ps1 -Version "0.1.1.0"
```

The script builds unsigned x64 and ARM64 MSIX packages and combines them into:

```text
LittleAgentsExtension_0.1.1.0_Bundle.msixbundle
```

Unsigned Store packages are expected: Microsoft Store signs the accepted package during ingestion. Do not replace the Partner Center publisher with a self-signed certificate subject for the Store submission build.

### Build with GitHub Actions

The `Build Microsoft Store MSIX bundle` workflow runs the tests, builds both
architectures through `build-store.ps1`, validates the required runtime files,
and uploads the bundle with its SHA-256 checksum.

Run it manually from **Actions**. Leave the version input blank to use
`AppxPackageVersion` from `LittleAgentsExtension.csproj`, or supply a four-part
MSIX version such as `0.1.2.0`.

Pushing a tag with the `store-v` prefix also runs the workflow and uses the tag
suffix as the package version:

```powershell
git tag store-v0.1.2.0
git push origin store-v0.1.2.0
```

Download the `LittleAgentsExtension-<version>-PartnerCenter` workflow artifact
and upload the `.msixbundle` inside it to Partner Center. The individual x64 and
ARM64 packages are already contained in the bundle.

## Validate before submission

Confirm that both architecture packages exist:

```powershell
Get-ChildItem AppPackages -Recurse -Filter *.msix
```

Confirm the bundle exists:

```powershell
Get-Item LittleAgentsExtension_0.1.1.0_Bundle.msixbundle
```

The package manifest must continue to declare both:

- `windows.comServer` for activation.
- `windows.appExtension` with the name `com.microsoft.commandpalette` for discovery.

## Submit in Partner Center

1. Open **Apps and games → Little Agents Extension for Command Palette**.
2. Open the submission and go to **Packages**.
3. Upload `LittleAgentsExtension_0.1.1.0_Bundle.msixbundle`.
4. In the English description, state that Little Agents integrates with Windows PowerToys Command Palette.
5. In **Supplemental info → Additional testing information**, tell certification that PowerToys with Command Palette is required and explain how to reload extensions.
6. Enter `https://github.com/Bennett-Yang/LittleAgentsExtensionForCmdPal/blob/main/PRIVACY.md` as the privacy policy URL after `PRIVACY.md` has been pushed to the public `main` branch.
7. Complete age ratings, properties, availability, screenshots, and privacy declarations, then submit for certification.

Reference: https://learn.microsoft.com/windows/powertoys/command-palette/publish-extension-store
