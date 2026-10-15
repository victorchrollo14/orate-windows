# Orate for Windows — Privacy Policy

_Last updated: 15 October 2026_

Orate is a push-to-talk dictation app. This policy explains what data the Windows app handles,
where it goes, and how to control it. Orate has no accounts, no analytics, and no telemetry.

## What leaves your computer

Orate sends data over the network in only two situations, and both happen only when you act.

### 1. When you dictate

While you hold your push-to-talk key, Orate records audio from your microphone. When you
release the key, Orate sends the following to the transcription provider you selected in
**Settings**:

- the audio recording (FLAC);
- Orate's built-in transcription instructions, plus your **custom instructions** and your
  **vocabulary** words, if you have added any;
- the API key for that provider.

The provider returns the transcribed text. Nothing is recorded or sent unless you press the
push-to-talk key.

The available providers are:

| Provider | Where the data goes |
|---|---|
| **Orate Cloud** (default) | The Orate Cloud service (`orate-api.lavisht22.workers.dev`), run by the maintainers of the [Orate project](https://github.com/lavisht22/orate) on Cloudflare Workers. It forwards the request to Google's Gemini model on Vertex AI and returns the text. Orate Cloud keeps your API key's remaining word balance and a log of how many words each request used. It does not store your audio or your transcripts. |
| **Google AI Studio** | Sent directly to Google's Gemini API using your own API key, under [Google's terms](https://ai.google.dev/gemini-api/terms). |
| **Vertex AI** | Sent directly to Google Cloud Vertex AI in your own Google Cloud project, under your agreement with Google Cloud. |

The source code of the Orate Cloud service is public, so you can check what it does with requests
yourself (see `api/` in the [Orate repository](https://github.com/lavisht22/orate)).

### 2. When you check for updates

When you click **Check for Updates** (in Settings or the tray menu), Orate contacts GitHub
(`github.com`) to look for a newer release and, if you agree, downloads it. Orate never checks for
updates automatically. GitHub's handling of these requests is covered by the
[GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

## What stays on your computer

These files are stored only on your PC. They are never uploaded.

- **Recordings and transcripts:** `%APPDATA%\Orate\recordings`. You can clear all of them, or only
  older ones, with **Clear** on the **Home** page.
- **Settings**, including custom instructions and vocabulary: `%APPDATA%\Orate\settings.json`.
- **API keys:** Windows Credential Manager (entries named `Orate:*`).
- **Diagnostic log:** `%APPDATA%\Orate\orate.log`. It records technical events such as timings,
  sizes and errors, not the text of your transcripts.

To paste a transcript, Orate briefly places it on the clipboard, sends Ctrl+V, and then restores
what was on your clipboard before.

## Your choices

- **Don't send anything:** don't use the push-to-talk key. Orate makes no network requests unless
  you dictate or check for updates.
- **Choose where your audio goes:** switch providers at any time in **Settings**.
- **Delete local data:** clear your history from **Home**. Uninstalling Orate (from *Settings → Apps*)
  removes the app. To remove every trace, also delete `%APPDATA%\Orate` and the `Orate:*`
  entries in Windows Credential Manager.

## Changes and contact

If this policy changes, the new version will be published in this file, and its history is kept
in the repository. For questions, open an issue at
<https://github.com/victorchrollo14/orate-windows/issues>.
