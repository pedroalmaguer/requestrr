using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.Locale;

namespace Requestrr.WebApi.RequestrrBot.ChatClients.Discord
{
    public enum DeletionMediaType
    {
        Movie, TvShow
    }

    /// <summary>
    /// Sends deletion requests to the members of the configured approval roles and tracks their answers.
    /// Pending requests only live in memory, a restart cancels them instead of risking an unintended deletion.
    /// </summary>
    public class DiscordDeletionApprovals
    {
        private static readonly TimeSpan RequestLifetime = TimeSpan.FromHours(24);

        //Guards the duplicate check, the settlement of a request and its list of requesters and messages
        private static readonly object _sync = new object();

        private readonly ConcurrentDictionary<string, PendingDeletion> _pendingDeletions = new ConcurrentDictionary<string, PendingDeletion>();
        private readonly ILogger _logger;

        public DiscordDeletionApprovals(ILogger logger)
        {
            _logger = logger;
        }

        public async Task RequestApprovalAsync(ComponentInteractionCreateEventArgs e, DiscordClient client, DiscordSettings settings, DeletionMediaType mediaType, string mediaId, string title, DiscordEmbed embed)
        {
            RemoveExpiredRequests();

            var requester = new DeletionRequester { UserId = e.User.Id, ChannelId = e.Channel.Id };
            PendingDeletion pending;

            //The duplicate check and the insert must be atomic, otherwise two requests for the same media both DM every approver
            lock (_sync)
            {
                pending = _pendingDeletions.Values.FirstOrDefault(x => x.MediaType == mediaType && x.MediaId == mediaId && !IsExpired(x));

                if (pending != null)
                {
                    //Track the extra requester so they are told the outcome as well
                    if (!pending.Requesters.Any(x => x.UserId == requester.UserId))
                    {
                        pending.Requesters.Add(requester);
                    }

                    pending = null;
                }
                else
                {
                    pending = new PendingDeletion
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        MediaType = mediaType,
                        MediaId = mediaId,
                        Title = title,
                        GuildId = e.Guild.Id,
                        CreatedAt = DateTime.UtcNow,
                    };

                    pending.Requesters.Add(requester);
                    _pendingDeletions[pending.Id] = pending;
                }
            }

            //Tell the requester before contacting approvers, so an early answer is never overwritten by this message
            await EditRequesterMessageAsync(e, FormatMessage(Language.Current.DiscordCommandDeleteApprovalSent, title));

            if (pending == null)
            {
                return;
            }

            try
            {
                await SendApprovalRequestsAsync(e, settings, pending, embed);
            }
            catch
            {
                //Never leave a request behind that no approver received, it would block this media until it expires
                RemovePendingIfUnanswered(pending);
                throw;
            }

            bool noApprovers;

            lock (_sync)
            {
                noApprovers = !pending.Messages.Any() && !pending.Settled;
            }

            if (noApprovers)
            {
                RemovePendingIfUnanswered(pending);
                await EditRequesterMessageAsync(e, Language.Current.DiscordCommandDeleteApprovalNoApprovers);
                await NotifyRequestersAsync(client, pending, Language.Current.DiscordCommandDeleteApprovalNoApprovers, e.User.Id);
                return;
            }

            _logger.LogWarning($"User {e.User.Username} ({e.User.Id}) requested the deletion of {mediaType} {mediaId}");
        }

        private async Task SendApprovalRequestsAsync(ComponentInteractionCreateEventArgs e, DiscordSettings settings, PendingDeletion pending, DiscordEmbed embed)
        {
            var approvers = (await e.Guild.GetAllMembersAsync())
                .Where(x => !x.IsBot && x.Id != e.User.Id && HasApprovalRole(x, settings))
                .ToArray();

            var content = FormatMessage(Language.Current.DiscordCommandDeleteApprovalRequest, pending.Title, e.User.Username);

            foreach (var approver in approvers)
            {
                lock (_sync)
                {
                    if (pending.Settled)
                    {
                        return;
                    }
                }

                try
                {
                    var builder = new DiscordMessageBuilder().WithContent(content).AddEmbed(embed).AddComponents(CreateButtons(pending.Id, false));
                    var message = await approver.SendMessageAsync(builder);
                    string outcome;

                    lock (_sync)
                    {
                        pending.Messages.Add(message);
                        outcome = pending.Outcome;
                    }

                    //The request was answered while this message was being sent, so it missed the update of the other approvers
                    if (outcome != null)
                    {
                        await DisableApprovalMessageAsync(pending, message, outcome);
                    }
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, $"Could not send deletion approval request to user {approver.Id}: " + ex.Message);
                }
            }
        }

        public async Task HandleAnswerAsync(ComponentInteractionCreateEventArgs e, DiscordClient client, DiscordSettings settings, Func<DeletionMediaType, string, DiscordInteraction, Task<bool>> deleteMedia)
        {
            RemoveExpiredRequests();

            var id = e.Id.Split("/").Last();
            var approved = e.Id.StartsWith("DAA", StringComparison.InvariantCultureIgnoreCase);

            if (!_pendingDeletions.TryGetValue(id, out var pending) || IsExpired(pending))
            {
                _pendingDeletions.TryRemove(id, out _);
                await EditAnswerMessageAsync(e, id, Language.Current.DiscordCommandDeleteApprovalExpired);
                return;
            }

            //Roles can change after the request was sent, so validate against the current ones
            var isApprover = await IsApproverAsync(client, pending.GuildId, e.User.Id, settings);

            if (isApprover == null)
            {
                //The roles could not be checked, leave the request untouched so the approver can try again
                await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbeds(e.Message.Embeds).AddComponents(CreateButtons(id, false)).WithContent(e.Message.Content));
                return;
            }

            if (IsRequester(pending, e.User.Id) || !isApprover.Value)
            {
                await EditAnswerMessageAsync(e, id, Language.Current.DiscordCommandDeleteApprovalNotApprover);
                return;
            }

            //Only the first answer wins
            lock (_sync)
            {
                if (!_pendingDeletions.TryRemove(id, out _))
                {
                    pending = null;
                }
                else
                {
                    pending.Settled = true;
                }
            }

            if (pending == null)
            {
                await EditAnswerMessageAsync(e, id, Language.Current.DiscordCommandDeleteApprovalExpired);
                return;
            }

            var result = Language.Current.DiscordCommandDeleteApprovalDenied;

            if (approved)
            {
                _logger.LogWarning($"User {e.User.Username} ({e.User.Id}) approved the deletion of {pending.MediaType} {pending.MediaId} requested by {string.Join(", ", pending.Requesters.Select(x => x.UserId))}");
                result = await TryDeleteAsync(e, pending, deleteMedia) ? Language.Current.DiscordCommandDeleteApprovalApproved : Language.Current.DiscordCommandDeleteApprovalFailed;
            }
            else
            {
                _logger.LogWarning($"User {e.User.Username} ({e.User.Id}) denied the deletion of {pending.MediaType} {pending.MediaId} requested by {string.Join(", ", pending.Requesters.Select(x => x.UserId))}");
            }

            var content = FormatMessage(result, pending.Title, e.User.Username);
            DiscordMessage[] otherMessages;

            lock (_sync)
            {
                pending.Outcome = content;
                otherMessages = pending.Messages.Where(x => x.Id != e.Message.Id).ToArray();
            }

            await EditAnswerMessageAsync(e, id, content);

            foreach (var message in otherMessages)
            {
                await DisableApprovalMessageAsync(pending, message, content);
            }

            await NotifyRequestersAsync(client, pending, content);
        }

        private async Task<bool> TryDeleteAsync(ComponentInteractionCreateEventArgs e, PendingDeletion pending, Func<DeletionMediaType, string, DiscordInteraction, Task<bool>> deleteMedia)
        {
            try
            {
                return await deleteMedia(pending.MediaType, pending.MediaId, e.Interaction);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, $"Error while deleting {pending.MediaType} {pending.MediaId}: " + ex.Message);
                return false;
            }
        }

        //Returns null when the roles could not be checked
        private async Task<bool?> IsApproverAsync(DiscordClient client, ulong guildId, ulong userId, DiscordSettings settings)
        {
            try
            {
                var guild = await client.GetGuildAsync(guildId);
                return HasApprovalRole(await guild.GetMemberAsync(userId), settings);
            }
            catch (NotFoundException)
            {
                return false;
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, $"Could not check the approval roles of user {userId}: " + ex.Message);
                return null;
            }
        }

        private static bool IsRequester(PendingDeletion pending, ulong userId)
        {
            lock (_sync)
            {
                return pending.Requesters.Any(x => x.UserId == userId);
            }
        }

        private static bool HasApprovalRole(DiscordMember member, DiscordSettings settings)
        {
            return member.Roles.Any(x => settings.DeleteApprovalRoles.Contains(x.Id.ToString()));
        }

        private async Task DisableApprovalMessageAsync(PendingDeletion pending, DiscordMessage message, string content)
        {
            try
            {
                await message.ModifyAsync(new DiscordMessageBuilder().WithContent(content).AddEmbeds(message.Embeds).AddComponents(CreateButtons(pending.Id, true)));
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Could not update deletion approval message: " + ex.Message);
            }
        }

        private async Task NotifyRequestersAsync(DiscordClient client, PendingDeletion pending, string content, ulong? skipUserId = null)
        {
            DeletionRequester[] requesters;

            lock (_sync)
            {
                requesters = pending.Requesters.Where(x => x.UserId != skipUserId).ToArray();
            }

            foreach (var channelRequesters in requesters.GroupBy(x => x.ChannelId))
            {
                try
                {
                    var channel = await client.GetChannelAsync(channelRequesters.Key);
                    await channel.SendMessageAsync($"{string.Join(" ", channelRequesters.Select(x => $"<@{x.UserId}>"))} {content}");
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, "Could not notify requester of deletion answer: " + ex.Message);
                }
            }
        }

        private static Task EditRequesterMessageAsync(ComponentInteractionCreateEventArgs e, string content)
        {
            var requestedButton = new DiscordButtonComponent(ButtonStyle.Secondary, "0/1/0", Language.Current.DiscordCommandDeleteButton, true);

            return e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbeds(e.Message.Embeds).AddComponents(requestedButton).WithContent(content));
        }

        private static Task EditAnswerMessageAsync(ComponentInteractionCreateEventArgs e, string id, string content)
        {
            return e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbeds(e.Message.Embeds).AddComponents(CreateButtons(id, true)).WithContent(content));
        }

        private static DiscordComponent[] CreateButtons(string id, bool disabled)
        {
            return new DiscordComponent[]
            {
                new DiscordButtonComponent(ButtonStyle.Danger, $"DAA/{id}", Language.Current.DiscordCommandDeleteApprovalApproveButton, disabled),
                new DiscordButtonComponent(ButtonStyle.Secondary, $"DAD/{id}", Language.Current.DiscordCommandDeleteApprovalDenyButton, disabled),
            };
        }

        private static string FormatMessage(string message, string title, string username = "")
        {
            return message.Replace(LanguageTokens.DeletionTitle, title).Replace(LanguageTokens.DeletionUsername, username);
        }

        private void RemoveExpiredRequests()
        {
            lock (_sync)
            {
                foreach (var pending in _pendingDeletions.Values.Where(IsExpired))
                {
                    _pendingDeletions.TryRemove(pending.Id, out _);
                }
            }
        }

        private void RemovePendingIfUnanswered(PendingDeletion pending)
        {
            lock (_sync)
            {
                if (!pending.Settled)
                {
                    _pendingDeletions.TryRemove(pending.Id, out _);
                }
            }
        }

        private static bool IsExpired(PendingDeletion pending)
        {
            return DateTime.UtcNow - pending.CreatedAt >= RequestLifetime;
        }

        private class PendingDeletion
        {
            public string Id { get; set; }
            public DeletionMediaType MediaType { get; set; }
            public string MediaId { get; set; }
            public string Title { get; set; }
            public ulong GuildId { get; set; }
            public DateTime CreatedAt { get; set; }
            public bool Settled { get; set; }
            public string Outcome { get; set; }
            public List<DeletionRequester> Requesters { get; } = new List<DeletionRequester>();
            public List<DiscordMessage> Messages { get; } = new List<DiscordMessage>();
        }

        private class DeletionRequester
        {
            public ulong UserId { get; set; }
            public ulong ChannelId { get; set; }
        }
    }
}
