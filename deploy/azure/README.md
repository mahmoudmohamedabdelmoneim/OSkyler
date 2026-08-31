# Azure judging deployment

This bundle deploys the existing Skyler Portal, API/background worker, SQLite data, and `mistral` Ollama model on one always-on Ubuntu VM.

The VM exposes only SSH, HTTP, and HTTPS. API port 5128, Portal port 5133, and Ollama port 11434 remain loopback-only. Caddy terminates public HTTPS and starts automatically after reboot. The API retains its current five-minute Outlook synchronization and model-analysis behavior.

Expected VM layout:

```text
/opt/skyler/current -> /opt/skyler/releases/<timestamp>
/opt/skyler/data/skyler.db
/home/skyler/.local/share/Skyler/Authentication/outlook-msal-cache.bin
/usr/share/ollama/.ollama/models
```

`install.sh` is idempotent for package installation and preserves an existing database and Outlook token cache across future application releases.
