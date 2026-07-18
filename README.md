
## Overview

Adds a new SeriesKeepKind option 'LatestWatched' that protects the most
recently-watched episode in a series from deletion, even when it matches
an active cleanup rule. This is designed to handle two real-world cases:

  1. The user has finished watching an episode and the cleanup rule would
     ordinarily delete it immediately (e.g. 'delete played episodes after
     0 days'). KeepLatestWatched ensures the last episode they watched is
     always retained.

  2. The user stopped playback while the credits were still rolling.
     Jellyfin marks the episode as 'played' when the user reaches the
     credit point, but keeps PlaybackPositionTicks non-zero until the
     playback session fully closes. This plugin maps that to IsWatching=true.
     The new exception explicitly protects any episode in that state, so it
     will never be deleted mid-session.

## How KeepLatestWatched differs from the existing 'Last' option

- 'Last' keeps the final episode in series index order (e.g. S02E10 of a
  10-episode season), regardless of whether it was ever watched.

- 'LatestWatched' keeps the episode most recently interacted with by any
  matching user, determined by LastPlayedDate. If any candidate episode is
  currently IsWatching (credits rolling / session open), that episode takes
  priority over the LastPlayedDate comparison.
