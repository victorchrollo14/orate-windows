# SignPath setup

Release signing goes through [SignPath Foundation](https://signpath.org). The release workflow
(`.github/workflows/build.yml`) submits two signing requests per release, and each needs a manual
approval in SignPath:

1. **`app`**: `Orate.exe` and `Orate.dll`, before Velopack packs them.
2. **`installer`**: `Orate-win-Setup.exe`, after packing.

## One-time setup (after SignPath accepts the project)

1. In the SignPath project, create two artifact configurations with the slugs **`app`** and
   **`installer`**. Paste in the XML from `artifact-configurations/app.xml` and
   `artifact-configurations/installer.xml`.
2. Link the project to the predefined **GitHub.com** trusted build system.
3. Create a CI user with submitter permission on the signing policy, and generate an API token.
4. In GitHub, go to **Settings → Secrets and variables → Actions** and add:
   - secret `SIGNPATH_API_TOKEN`: the CI user's API token;
   - variable `SIGNPATH_ORGANIZATION_ID`: your SignPath organization ID;
   - variable `SIGNPATH_PROJECT_SLUG`: the project slug, e.g. `orate-windows`;
   - variable `SIGNPATH_SIGNING_POLICY_SLUG`: e.g. `release-signing`.

Signing switches on as soon as `SIGNPATH_ORGANIZATION_ID` is set. Until then, releases are
published unsigned, exactly as before.
