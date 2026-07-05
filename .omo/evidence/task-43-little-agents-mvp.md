# Little Agents Manual QA Checklist

1. F5 Deploy the `LittleAgentsExtension` project from Visual Studio.
2. Open Command Palette and run `Reload Command Palette Extension`.
3. Confirm `Little Agents` appears in Command Palette top-level results.
4. Add a provider with a valid OpenAI-compatible base URL and API key, for example base URL `https://api.openai.com/v1`.
5. Verify secret hygiene by running `Get-ChildItem "$env:LOCALAPPDATA\Packages\*\LocalState\LittleAgents" -Recurse -File | Select-String -Pattern "<canary key>"` and confirm it returns 0 matches.
6. Add an agent that references the provider and a model.
7. Run an agent whose user template does not contain `{input}` and confirm the first response chunk is visible within 1 second of the click.
8. Run an agent whose user template contains `{input}`, confirm the input form appears, submit text, and confirm streaming output appears.
9. Cancel a run mid-stream and confirm the body ends with `_(stopped)_` within 1 second of clicking Stop.
10. Use Reply on a completed run and confirm the follow-up is part of the same `ChatRunPage` multi-turn session.
11. While agent A is streaming, invoke agent B whose template contains `{input}`; as soon as B's input form appears and before submitting it, confirm A stops within 1 second, then submit B and confirm B streams.
12. Use Copy result and confirm the clipboard contains the most recent assistant turn; use Copy transcript and confirm the clipboard contains the full markdown body.
13. Edit an agent, reload Command Palette, and confirm the edited agent persists.
14. Try deleting a provider that agents reference and confirm deletion is blocked with a D-ORPHAN toast listing the referencing agent names.
15. Close and reopen Command Palette, then confirm agents and providers persist while ephemeral run history does not persist.
16. Run `dotnet publish LittleAgentsExtension/LittleAgentsExtension.csproj -c Release -r win-x64 -p:Platform=x64 2>&1 | Select-String "error"` and confirm it returns 0 matches.
