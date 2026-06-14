# Orate for Windows

Push-to-talk voice transcription for Windows. Hold a key, speak, release — Orate transcribes
your speech with an LLM (cleaning up filler words, fixing grammar, formatting lists, handling
dictated punctuation) and pastes the polished text wherever your cursor is.

This is a standalone Windows port of the macOS Orate app. It shares **no code** with the macOS
or Linux builds — it only talks to the same optional Orate Cloud backend.

## Status

**Phase 1 (MVP):** the core loop works end to end — global push-to-talk → record → animated
overlay → transcribe (Orate Cloud / Google AI / Vertex) → paste at cursor, plus a tray icon
and a settings screen (provider, API key, key binding, custom instructions).

Planned next: recording history, vocabulary screen, onboarding, sound feedback, auto-update,
and UI polish.

## Tech

- **C# / WPF on .NET 8** — native, with **NAudio** as the only third-party runtime dependency.
  The global keyboard hook, synthetic paste, tray icon, and secret storage all use Win32
  P/Invoke or the in-box WinForms `NotifyIcon`.
- **Audio:** 16 kHz mono captured with NAudio, encoded to **FLAC** using the Media Foundation
  FLAC encoder built into Windows 10/11 — so the existing Cloudflare Worker needs no changes.

## Build & run

Requires **Windows 10 (1809+) or Windows 11** and the **.NET 8 SDK**. (WPF can only be built
and run on Windows.)

```powershell
# From the repo root
dotnet build Orate.sln -c Debug
dotnet run --project Orate
```

Or open `Orate.sln` in Visual Studio 2022 (17.8+) and press F5.

## First-time setup

1. Launch Orate. The main window opens and a tray icon appears.
2. Go to **Settings**:
   - Pick a **Provider** (Orate Cloud, Google AI Studio, or Vertex AI).
   - Paste the matching **API key** and click **Save Key**. (Vertex also needs a Project ID
     and Region.)
   - Set your **push-to-talk key** (default: **Right Alt**) — click the button, then press
     the key you want.
3. Put your cursor in any text field (e.g. Notepad), hold the key, speak, and release. The
   cleaned transcript is pasted at the cursor. Press **Esc** while transcribing to cancel.

Closing the window hides Orate to the tray; use **Quit** from the tray menu to exit.

## Where things live

| Concern | File |
|---|---|
| App entry + pipeline orchestration (tray, hook, recorder, overlay) | `Orate/App.xaml.cs` |
| Global push-to-talk (low-level keyboard hook) | `Orate/Services/GlobalHotkey.cs` |
| Mic capture + FLAC encode + level metering | `Orate/Services/AudioRecorder.cs` |
| Transcription (3 providers) | `Orate/Services/TranscriptionService.cs` |
| Clipboard + Ctrl+V paste | `Orate/Services/TextInserter.cs` |
| Floating animated overlay pill | `Orate/Overlay/OverlayWindow.xaml(.cs)` |
| API keys (Credential Manager) | `Orate/Services/CredentialStore.cs` |
| Settings (JSON in `%APPDATA%\Orate`) | `Orate/Services/SettingsStore.cs` |
| Win32 P/Invoke | `Orate/Interop/NativeMethods.cs` |
| Window UI | `Orate/MainWindow.xaml`, `Orate/Views/*` |

## Storage locations

- Settings: `%APPDATA%\Orate\settings.json`
- API keys: Windows Credential Manager (Generic credentials, target `Orate:*`)

## Known limitations (Phase 1)

- The push-to-talk key is **not swallowed**, so holding Right Alt still behaves as Alt in the
  focused app. A future option will let you suppress it.
- No custom app icon yet — the tray uses the default Windows application icon.
- Paste targets the focused window via Ctrl+V; it can't paste into apps running **elevated**
  unless Orate is also elevated (Windows UIPI).

## Packaging (later)

Distribution will use **Velopack** to produce a double-click `Setup.exe` and an auto-update
feed. Without a code-signing certificate, Windows SmartScreen will warn on first run.
