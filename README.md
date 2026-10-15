# Orate for Windows

Push-to-talk voice transcription for Windows. Hold a key, speak, release — Orate transcribes
your speech with an LLM (cleaning up filler words, fixing grammar, formatting lists, handling
dictated punctuation) and pastes the polished text wherever your cursor is.

This is a standalone Windows port of the macOS Orate app. It shares **no code** with the macOS
or Linux builds — it only talks to the same optional Orate Cloud backend.

## Status

The core loop works end to end: global push-to-talk → record → animated overlay → transcribe
(Orate Cloud / Google AI Studio / Vertex AI) → paste at the cursor. The app also has a tray icon,
transcript history, custom instructions, a vocabulary list, settings, and in-app updates.

## Tech

- **C# / WPF on .NET 8** — native, with **NAudio** (audio) and **Velopack** (installer and updates) as the only third-party runtime dependencies.
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

## Download

Get **`Orate-win-Setup.exe`** from the
[latest release](https://github.com/victorchrollo14/orate-windows/releases/latest) and run it.
Orate installs per-user (no admin rights needed) and adds Start menu and desktop shortcuts. To update, use
**Check for Updates** in Settings or the tray menu. To uninstall, use *Settings → Apps*.

Every push to `main` builds a new release with the **Windows Release** workflow
(`.github/workflows/build.yml`). It publishes a self-contained build, so nothing else needs to
be installed. The workflow packs the app with [Velopack](https://velopack.io) and publishes the
installer, a portable `.zip`, and the update packages as a GitHub Release.

## First-time setup

1. Launch Orate. On first launch it shows a short privacy notice, then the main window opens
   and a tray icon appears.
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

## Known limitations

- The push-to-talk key is **not swallowed**, so holding Right Alt still behaves as Alt in the
  focused app. A future option will let you suppress it.
- Paste targets the focused window via Ctrl+V; it can't paste into apps running **elevated**
  unless Orate is also elevated (Windows UIPI).

## Privacy

Orate records audio only while you hold the push-to-talk key. It sends the recording to the
transcription provider you choose, and it contacts GitHub only when you check for updates. It
has no analytics or telemetry. See [PRIVACY.md](PRIVACY.md) for the full policy.

## Code signing policy

Free code signing is provided by [SignPath.io](https://about.signpath.io), and the certificate is
issued by [SignPath Foundation](https://signpath.org).

Only binaries built from this repository's source code, by the GitHub Actions release workflow,
are signed. Each release signing request is approved by hand.

**Team roles**

- Committers and reviewers: [victorchrollo14](https://github.com/victorchrollo14)
- Approvers: [victorchrollo14](https://github.com/victorchrollo14)

**Privacy policy:** This program will not transfer any information to other networked systems
unless the user or the person installing or operating it specifically requests it. Recordings
are sent to the selected transcription provider only when you use push-to-talk. See
[PRIVACY.md](PRIVACY.md).

## License

[MIT](LICENSE)
