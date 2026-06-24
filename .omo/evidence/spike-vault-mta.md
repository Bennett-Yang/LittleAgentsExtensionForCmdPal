# Vault MTA Smoke Evidence

Command:

```powershell
& "LittleAgentsExtension\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\LittleAgentsExtension.exe" --smoke-vault; "EXITCODE=$LASTEXITCODE"
```

Result:

```text
EXITCODE=
Vault smoke result written to C:\Users\benne\AppData\Local\Temp\LittleAgentsExtension\LittleAgents\spike-vault-mta.log: OK
```

Log path: `C:\Users\benne\AppData\Local\Temp\LittleAgentsExtension\LittleAgents\spike-vault-mta.log`

Log content:

```text
OK
```

The executable is a WinExe, so PowerShell did not populate `$LASTEXITCODE`; the process returned to the shell and the smoke log contained `OK`.

Verifier rerun for T45 blocker fixes produced the same result: the process returned to the shell and `C:\Users\benne\AppData\Local\Temp\LittleAgentsExtension\LittleAgents\spike-vault-mta.log` contained `OK`.
