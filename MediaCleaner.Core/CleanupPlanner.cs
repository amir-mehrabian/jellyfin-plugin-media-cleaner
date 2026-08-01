using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaCleaner.Core;

public sealed class CleanupPlanner(IClock clock, IPathMatcher pathMatcher, IExtraFileProbe extraFileProbe)
{
    public CleanupPlan Plan(CleanupRequest request)
    {
        var enabledRules = request.Policy.Rules.Where(x => x.Enabled).ToList();
        if (enabledRules.Count == 0)
        {
            return CleanupPlan.Empty;
        }

        var now = clock.UtcNow;
        var auditEntries = new List<CleanupAuditEntry>();
        var matcher = new CleanupRuleMatcher(now, pathMatcher, request.Policy);
        var deleteMatches = new List<RuleMatch>();
        var protectMatches = new List<RuleMatch>();

        foreach (var rule in enabledRules)
        {
            foreach (var match in matcher.CollectRuleMatches(request, rule, auditEntries))
            {
                if (rule.Actions.Kind == CleanupRuleActionKind.Delete)
                {
                    deleteMatches.Add(match);
                }
                else if (rule.Actions.Kind == CleanupRuleActionKind.Protect)
                {
                    protectMatches.Add(match);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported rule action: {rule.Actions.Kind}");
                }
            }
        }

        var protectedIds = protectMatches.Select(x => x.Item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var protectedMatch in protectMatches)
        {
            CleanupAudit.AddItem(
                auditEntries,
                protectedMatch.Item,
                protectedMatch.Rule,
                CleanupAuditStage.Protection,
                CleanupAuditOutcome.Protected,
                $"protected by rule '{protectedMatch.Rule.Name}'");
        }

        var expandedProtectedIds = ExpandProtectedIds(protectedIds, request.Items);

        var decisions = BuildDeleteDecisions(deleteMatches, expandedProtectedIds, auditEntries)
            .OrderBy(x => CleanupRuleKinds.Priority(x.Kind))
            .ThenBy(x => x.Kind == ExpiredKind.Played ? FirstPlaybackLastPlayedDate(x.Playback) : x.Item.DateCreated)
            .ToList();

        var cascadePlanner = new DeletionCascadePlanner(extraFileProbe);
        IReadOnlyList<DeletionOperation> deletions;
        if (request.IsDryRun)
        {
            // Dry-run needs the deletion-cascade audit entries and counts, but it does not need
            // to retain every DeletionOperation object. On large not-played libraries this avoids
            // keeping tens of thousands of deletion records alive until the report is rendered.
            foreach (var _ in cascadePlanner.BuildDeletionOperations(decisions, request.Items, expandedProtectedIds, auditEntries))
            {
            }

            deletions = [];
        }
        else
        {
            deletions = cascadePlanner.BuildDeletionOperations(decisions, request.Items, expandedProtectedIds, auditEntries).ToList();
        }

        return new CleanupPlan(decisions, deletions, auditEntries);
    }

    public static IEnumerable<MediaUser> FilterUsers(
        IEnumerable<MediaUser> users,
        IReadOnlyCollection<string> selectedUserIds,
        UsersListMode mode) =>
        users.Where(user => selectedUserIds.Contains(user.Id) switch
        {
            true when mode == UsersListMode.Ignore => false,
            true when mode == UsersListMode.Acknowledge => true,
            false when mode == UsersListMode.Ignore => true,
            false when mode == UsersListMode.Acknowledge => false,
            _ => throw new NotSupportedException($"Unsupported users list mode: {mode}"),
        });

    private static DateTime? FirstPlaybackLastPlayedDate(IReadOnlyList<PlaybackState> playback)
    {
        return playback.Count == 0 ? null : playback[0].LastPlayedDate;
    }

    private static ISet<string> ExpandProtectedIds(ISet<string> protectedIds, IReadOnlyList<MediaItem> items)
    {
        var expanded = new HashSet<string>(protectedIds, StringComparer.OrdinalIgnoreCase);
        var byId = items.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var id in protectedIds)
        {
            if (byId.TryGetValue(id, out var item))
            {
                AddDescendantsToProtectedSet(item, byId, expanded);
            }
        }

        return expanded;
    }

    private static void AddDescendantsToProtectedSet(MediaItem item, IReadOnlyDictionary<string, MediaItem> byId, HashSet<string> expanded)
    {
        foreach (var seasonId in item.SeasonIds ?? [])
        {
            expanded.Add(seasonId);
            if (byId.TryGetValue(seasonId, out var season))
            {
                foreach (var episodeId in season.EpisodeIds ?? [])
                {
                    expanded.Add(episodeId);
                }
            }
        }

        foreach (var episodeId in item.EpisodeIds ?? [])
        {
            expanded.Add(episodeId);
        }
    }

    private static bool IsProtectedItem(MediaItem item, ISet<string> protectedIds)
    {
        if (protectedIds.Contains(item.Id))
        {
            return true;
        }

        if (item.SeasonId is not null && protectedIds.Contains(item.SeasonId))
        {
            return true;
        }

        if (item.SeriesId is not null && protectedIds.Contains(item.SeriesId))
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<CleanupDecision> BuildDeleteDecisions(
        IEnumerable<RuleMatch> deleteMatches,
        ISet<string> protectedIds,
        List<CleanupAuditEntry> auditEntries)
    {
        foreach (var group in deleteMatches.GroupBy(x => x.Item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            if (IsProtectedItem(first.Item, protectedIds))
            {
                foreach (var match in group)
                {
                    CleanupAudit.AddItem(
                        auditEntries,
                        match.Item,
                        match.Rule,
                        CleanupAuditStage.Protection,
                        CleanupAuditOutcome.Suppressed,
                        "delete suppressed because item is protected");
                }

                continue;
            }

            var selectedKind = group
                .Select(x => x.Kind)
                .OrderBy(CleanupRuleKinds.Priority)
                .First();
            var selectedItem = group
                .Where(x => x.Kind == selectedKind)
                .Select(x => x.Item)
                .First();
            var playback = group
                .SelectMany(x => x.Playback)
                .GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.OrderByDescending(y => y.LastPlayedDate).First())
                .ToList();
            var matchedRules = group
                .Select(x => x.Rule)
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Name)
                .ToList();
            var markUnplayedUsers = group
                .Where(x => x.Kind == ExpiredKind.Played && x.Rule.Actions.MarkAsUnplayed)
                .SelectMany(x => x.Playback.Select(y => y.UserId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            yield return CleanupDecisionFactory.Create(selectedItem, selectedKind, playback, markUnplayedUsers, matchedRules);
        }
    }
}
