# Workspace Rules - Jellyfin Media Cleaner Plugin

## Jellyfin Server Environment Details
- **Server Address:** `http://jellyfin.home:8096` (`192.168.1.160`)
- **Server Hostname:** `jellyfin.home` / `192.168.1.160`
- **SSH Access:** `ssh root@192.168.1.160` (passwordless root SSH access enabled)
- **Plugin Directory:** `/var/lib/jellyfin/plugins/Media Cleaner_3.3.0.101109/`
- **Jellyfin API Token:** `f21addb76b10477497a089c1915823cb` (Header: `X-Emby-Token: f21addb76b10477497a089c1915823cb`)

## Build & Deployment Procedure
When building and deploying changes to the MediaCleaner plugin:

1. **Build Binaries (Release Mode):**
   ```bash
   dotnet build MediaCleaner/MediaCleaner.csproj -c Release /p:RollForward=LatestMajor
   ```

2. **Deploy to Jellyfin Server:**
   ```bash
   scp -o StrictHostKeyChecking=no \
     /home/mehhossein/projects/jellyfin-plugin-media-cleaner/MediaCleaner/bin/Release/net9.0/MediaCleaner.dll \
     /home/mehhossein/projects/jellyfin-plugin-media-cleaner/MediaCleaner/bin/Release/net9.0/MediaCleaner.Core.dll \
     root@192.168.1.160:"/var/lib/jellyfin/plugins/Media Cleaner_3.3.0.101109/"
   ```

3. **Set Ownership & Restart Jellyfin Service:**
   ```bash
   ssh -o StrictHostKeyChecking=no root@192.168.1.160 \
     "chown -R jellyfin:jellyfin '/var/lib/jellyfin/plugins/Media Cleaner_3.3.0.101109' && systemctl restart jellyfin"
   ```

4. **Verify Live Status:**
   Query `http://jellyfin.home:8096/MediaCleaner/Report` with `X-Emby-Token: f21addb76b10477497a089c1915823cb` to verify dry-run cleanup plans and audit decisions.
