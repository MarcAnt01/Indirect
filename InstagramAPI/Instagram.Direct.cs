﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using InstagramAPI.Classes;
using InstagramAPI.Classes.Direct;
using InstagramAPI.Classes.Direct.ItemContent;
using InstagramAPI.Classes.Media;
using InstagramAPI.Classes.Responses;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HttpMethod = System.Net.Http.HttpMethod;
using HttpRequestMessage = Windows.Web.Http.HttpRequestMessage;
using HttpResponseMessage = Windows.Web.Http.HttpResponseMessage;

namespace InstagramAPI
{
    public partial class Instagram
    {
        public async Task<Result<InboxContainer>> GetInboxAsync(PaginationParameters paginationParameters)
        {
            ValidateLoggedIn();
            try
            {
                if (paginationParameters == null)
                    paginationParameters = PaginationParameters.MaxPagesToLoad(1);

                var inboxResult = await GetDirectInbox(paginationParameters.NextMaxId).ConfigureAwait(false);
                if (!inboxResult.IsSucceeded)
                    return inboxResult;
                var inbox = inboxResult.Value;
                foreach (var directThread in inboxResult.Value.Inbox.Threads)
                {
                    AddToUserRegistry(directThread.Users);
                }
                paginationParameters.NextMaxId = inbox.Inbox.OldestCursor;
                var pagesLoaded = 1;
                while (inbox.Inbox.HasOlder
                      && !string.IsNullOrEmpty(inbox.Inbox.OldestCursor)
                      && pagesLoaded < paginationParameters.MaximumPagesToLoad)
                {
                    var nextInbox = await GetDirectInbox(inbox.Inbox.OldestCursor).ConfigureAwait(false);

                    if (!nextInbox.IsSucceeded)
                        return Result<InboxContainer>.Fail(inbox, nextInbox.Message, nextInbox.Json);

                    inbox.Inbox.OldestCursor = paginationParameters.NextMaxId = nextInbox.Value.Inbox.OldestCursor;
                    inbox.Inbox.HasOlder = nextInbox.Value.Inbox.HasOlder;
                    inbox.Inbox.BlendedInboxEnabled = nextInbox.Value.Inbox.BlendedInboxEnabled;
                    inbox.Inbox.UnseenCount = nextInbox.Value.Inbox.UnseenCount;
                    inbox.Inbox.UnseenCountTs = nextInbox.Value.Inbox.UnseenCountTs;
                    inbox.Inbox.Threads.AddRange(nextInbox.Value.Inbox.Threads);
                    foreach (var directThread in nextInbox.Value.Inbox.Threads)
                    {
                        AddToUserRegistry(directThread.Users);
                    }
                    pagesLoaded++;
                }

                return Result<InboxContainer>.Success(inbox);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<InboxContainer>.Except(exception);
            }
        }

        private async Task<Result<InboxContainer>> GetDirectInbox(string maxId = null)
        {
            try
            {
                var directInboxUri = UriCreator.GetDirectInboxUri(maxId);
                var response = await _httpClient.GetAsync(directInboxUri);
                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogResponse(response);

                if (response.StatusCode != HttpStatusCode.Ok)
                    return Result<InboxContainer>.Fail(json, response.ReasonPhrase);
                var inbox = JsonConvert.DeserializeObject<InboxContainer>(json);
                return Result<InboxContainer>.Success(inbox);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<InboxContainer>.Except(exception);
            }
        }

        /// <summary>
        ///     Get direct inbox thread by its id asynchronously
        /// </summary>
        /// <param name="threadId">Thread id</param>
        /// <param name="paginationParameters">Pagination parameters: next id and max amount of pages to load</param>
        /// <returns>
        ///     <see cref="DirectThread" />
        /// </returns>
        public async Task<Result<DirectThread>> GetThreadAsync(string threadId, PaginationParameters paginationParameters)
        {
            ValidateLoggedIn();
            try
            {
                if (paginationParameters == null)
                    paginationParameters = PaginationParameters.MaxPagesToLoad(1);

                var thread = await GetDirectThread(threadId, paginationParameters.NextMaxId).ConfigureAwait(false);
                if (!thread.IsSucceeded)
                    return thread;

                var threadResponse = thread.Value;
                paginationParameters.NextMaxId = threadResponse.OldestCursor;
                var pagesLoaded = 1;

                while ((threadResponse.HasOlder ?? false)
                      && !string.IsNullOrEmpty(threadResponse.OldestCursor)
                      && pagesLoaded < paginationParameters.MaximumPagesToLoad)
                {
                    var nextThread = await GetDirectThread(threadId, threadResponse.OldestCursor).ConfigureAwait(false);

                    if (!nextThread.IsSucceeded)
                    {
                        threadResponse.Items.Reverse();
                        return Result<DirectThread>.Fail(threadResponse, nextThread.Message, nextThread.Json);
                    }

                    threadResponse.OldestCursor = paginationParameters.NextMaxId = nextThread.Value.OldestCursor;
                    threadResponse.HasOlder = nextThread.Value.HasOlder;
                    threadResponse.Canonical = nextThread.Value.Canonical;
                    threadResponse.ExpiringMediaReceiveCount = nextThread.Value.ExpiringMediaReceiveCount;
                    threadResponse.ExpiringMediaSendCount = nextThread.Value.ExpiringMediaSendCount;
                    threadResponse.HasNewer = nextThread.Value.HasNewer;
                    threadResponse.LastActivity = nextThread.Value.LastActivity;
                    threadResponse.LastSeenAt = nextThread.Value.LastSeenAt;
                    threadResponse.ReshareReceiveCount = nextThread.Value.ReshareReceiveCount;
                    threadResponse.ReshareSendCount = nextThread.Value.ReshareSendCount;
                    threadResponse.Status = nextThread.Value.Status;
                    threadResponse.Title = nextThread.Value.Title;
                    threadResponse.IsGroup = nextThread.Value.IsGroup;
                    threadResponse.IsSpam = nextThread.Value.IsSpam;
                    threadResponse.IsPin = nextThread.Value.IsPin;
                    threadResponse.Muted = nextThread.Value.Muted;
                    threadResponse.Pending = nextThread.Value.Pending;
                    threadResponse.Users = nextThread.Value.Users;
                    threadResponse.ValuedRequest = nextThread.Value.ValuedRequest;
                    threadResponse.VCMuted = nextThread.Value.VCMuted;
                    threadResponse.ViewerId = nextThread.Value.ViewerId;
                    threadResponse.Items.AddRange(nextThread.Value.Items);
                    pagesLoaded++;
                }

                //Reverse for Chat Order
                threadResponse.Items.Reverse();

                return Result<DirectThread>.Success(threadResponse);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<DirectThread>.Except(exception);
            }
        }

        private async Task<Result<DirectThread>> GetDirectThread(string threadId, string maxId = null)
        {
            try
            {
                var directInboxUri = UriCreator.GetDirectInboxThreadUri(threadId, maxId);
                var response = await _httpClient.GetAsync(directInboxUri);
                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogResponse(response);

                if (response.StatusCode != HttpStatusCode.Ok)
                    return Result<DirectThread>.Fail(json, response.ReasonPhrase);

                var statusResponse = JObject.Parse(json);
                if (statusResponse["status"].ToObject<string>() != "ok")
                    Result<DirectThread>.Fail(json);
                var thread = statusResponse["thread"].ToObject<DirectThread>();

                return Result<DirectThread>.Success(thread);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<DirectThread>.Except(exception);
            }
        }

        public async Task<Result<DirectThread>> GetThreadByParticipantsAsync(IEnumerable<long> userIds)
        {
            ValidateLoggedIn();
            try
            {
                var threadUri = UriCreator.GetThreadByRecipientsUri(userIds);
                var response = await _httpClient.GetAsync(threadUri);
                var json = await response.Content.ReadAsStringAsync();
                _logger.LogResponse(response);

                if (response.StatusCode != HttpStatusCode.Ok)
                    return Result<DirectThread>.Fail(json, response.ReasonPhrase);

                if (string.IsNullOrEmpty(json)) return Result<DirectThread>.Success(null, json);

                var threadResponse = JsonConvert.DeserializeObject<DirectThread>(json);

                threadResponse.Items?.Reverse();

                return Result<DirectThread>.Success(threadResponse, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<DirectThread>.Except(exception);
            }
        }

        /// <summary>
        ///     Get ranked recipients (threads and users) asynchronously
        ///     <para>Note: Some recipient has User, some recipient has Thread</para>
        /// </summary>
        /// <param name="username">Username to search</param>
        /// <returns>
        ///     <see cref="RankedRecipientsResponse" />
        /// </returns>
        public async Task<Result<RankedRecipientsResponse>> GetRankedRecipientsByUsernameAsync(string username)
        {
            ValidateLoggedIn();
            try
            {
                Uri instaUri;
                if (string.IsNullOrEmpty(username))
                    instaUri = UriCreator.GetRankedRecipientsUri();
                else
                    instaUri = UriCreator.GetRankRecipientsByUserUri(username);

                var response = await _httpClient.GetAsync(instaUri);
                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogResponse(response);

                if (response.StatusCode != HttpStatusCode.Ok)
                    return Result<RankedRecipientsResponse>.Fail(json, response.ReasonPhrase);
                var responseRecipients = JsonConvert.DeserializeObject<RankedRecipientsResponse>(json);
                return Result<RankedRecipientsResponse>.Success(responseRecipients, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<RankedRecipientsResponse>.Except(exception);
            }
        }

        /// <summary>
        ///     Send a like to the conversation
        /// </summary>
        /// <param name="threadId">Thread id</param>
        public async Task<Result<ItemAckPayloadResponse>> SendLikeAsync(string threadId)
        {
            ValidateLoggedIn();
            try
            {
                var uri = UriCreator.GetDirectThreadBroadcastLikeUri();

                var data = new Dictionary<string, string>
                {
                    {"action", "send_item"},
                    {"_csrftoken", Session.CsrfToken},
                    {"_uuid", Device.Uuid.ToString()},
                    {"thread_id", $"{threadId}"},
                    {"client_context", Guid.NewGuid().ToString()}
                };
                var response = await _httpClient.PostAsync(uri, new HttpFormUrlEncodedContent(data));
                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogResponse(response);
                if (response.StatusCode != HttpStatusCode.Ok)
                    return Result<ItemAckPayloadResponse>.Fail(json, response.ReasonPhrase);
                var obj = JsonConvert.DeserializeObject<ItemAckResponse>(json);
                return obj.IsOk()
                    ? Result<ItemAckPayloadResponse>.Success(obj.Payload)
                    : Result<ItemAckPayloadResponse>.Fail(json, response.ReasonPhrase);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<ItemAckPayloadResponse>.Except(exception);
            }
        }

        /// <summary>
        ///     Send direct text message to provided users OR thread. 
        /// You have to provide either a list of recipients or a thread id. One of them can be null.
        /// </summary>
        /// <param name="recipients">users PKs</param>
        /// <param name="threadId"></param>
        /// <param name="text">Message text</param>
        /// <returns>List of threads</returns>
        public async Task<Result<List<DirectThread>>> SendTextAsync(IEnumerable<long> recipients,
            string threadId,
            string text)
        {
            ValidateLoggedIn();
            var threads = new List<DirectThread>();
            try
            {
                if (string.IsNullOrEmpty(text)) throw new ArgumentException("Message text is empty", nameof(text));
                var recipientsString = recipients != null ? string.Join(",", recipients) : string.Empty;
                var directSendMessageUri = UriCreator.GetDirectSendMessageUri();
                var fields = new Dictionary<string, string> { { "text", text } };
                if (!string.IsNullOrEmpty(threadId))
                    fields.Add("thread_ids", "[" + threadId + "]");
                else if (!string.IsNullOrEmpty(recipientsString))
                    fields.Add("recipient_users", "[[" + recipientsString + "]]");
                else throw new ArgumentException("You have to provide either a thread id or a list of users' PKs");

                var response = await _httpClient.PostAsync(directSendMessageUri, new HttpFormUrlEncodedContent(fields));
                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogResponse(response);

                if (response.StatusCode != HttpStatusCode.Ok)
                    return Result<List<DirectThread>>.Fail(json);
                var result = JsonConvert.DeserializeObject<TextSentResponse>(json);
                return result.IsOk()
                    ? Result<List<DirectThread>>.Success(threads, json)
                    : Result<List<DirectThread>>.Fail(json, response.ReasonPhrase);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<List<DirectThread>>.Except(exception);
            }
        }

        /// <summary>
        ///     Send link address to direct thread
        /// </summary>
        /// <param name="text">Text to send</param>
        /// <param name="link">Link to send</param>
        /// <param name="threadIds">Thread ids</param>
        public Task<Result<ItemAckPayloadResponse>> SendLinkAsync(string text, IEnumerable<string> link,
            params string[] threadIds)
        {
            return SendDirectLink(text, link, threadIds, null);
        }

        /// <summary>
        ///     Send link address to direct thread
        /// </summary>
        /// <param name="text">Text to send</param>
        /// <param name="link">Link to send</param>
        /// <param name="recipients">Recipients ids</param>
        public Task<Result<ItemAckPayloadResponse>> SendLinkToRecipientsAsync(string text,
            IEnumerable<string> link,
            params long[] recipients)
        {
            return SendDirectLink(text, link, null, recipients);
        }

        /// <summary>
        ///     Send link address to direct thread
        /// </summary>
        /// <param name="text">Text to send</param>
        /// <param name="link">Link to send</param>
        /// <param name="threadIds">Thread ids</param>
        /// <param name="recipients">Recipients ids</param>
        private async Task<Result<ItemAckPayloadResponse>> SendDirectLink(string text, IEnumerable<string> link,
            string[] threadIds,
            long[] recipients)
        {
            ValidateLoggedIn();
            try
            {
                var instaUri = UriCreator.GetSendDirectLinkUri();
                var clientContext = Guid.NewGuid().ToString();
                var data = new Dictionary<string, string>
                {
                    {"link_text", text ?? string.Empty},
                    {"link_urls", JsonConvert.SerializeObject(link, Formatting.None)},
                    {"action", "send_item"},
                    {"client_context", clientContext},
                    {"_csrftoken", Session.CsrfToken},
                    {"_uuid", Device.Uuid.ToString()}
                };
                if (threadIds?.Length > 0)
                {
                    data.Add("thread_ids", JsonConvert.SerializeObject(threadIds, Formatting.None));
                }
                if (recipients?.Length > 0)
                {
                    var recipientString = string.Join(",", recipients);
                    data.Add("recipient_users", $"[[{recipientString}]]");
                }

                var response = await _httpClient.PostAsync(instaUri, new HttpFormUrlEncodedContent(data));
                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogResponse(response);

                if (response.StatusCode != HttpStatusCode.Ok)
                    return Result<ItemAckPayloadResponse>.Fail(json, response.ReasonPhrase);
                var obj = JsonConvert.DeserializeObject<ItemAckResponse>(json);
                return obj.IsOk()
                    ? Result<ItemAckPayloadResponse>.Success(obj.Payload, json, obj.Message)
                    : Result<ItemAckPayloadResponse>.Fail(json, obj.Message);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<ItemAckPayloadResponse>.Except(exception);
            }
        }

        /// <summary>
        ///     Mark direct message as seen
        /// </summary>
        /// <param name="threadId">Thread id</param>
        /// <param name="itemId">Message id (item id)</param>
        public async Task<Result<bool>> MarkItemSeenAsync(string threadId, string itemId)
        {
            ValidateLoggedIn();
            try
            {
                var instaUri = UriCreator.GetDirectThreadSeenUri(threadId, itemId);

                var data = new Dictionary<string, string>
                {
                    {"thread_id", threadId},
                    {"action", "mark_seen"},
                    {"_csrftoken", Session.CsrfToken},
                    {"_uuid", Device.Uuid.ToString()},
                    {"item_id", itemId},
                    {"use_unified_inbox", "true"},
                };
                var response = await _httpClient.PostAsync(instaUri, new HttpFormUrlEncodedContent(data));
                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogResponse(response);

                if (response.StatusCode != HttpStatusCode.Ok)
                    return Result<bool>.Fail(json, response.ReasonPhrase);
                var obj = JsonConvert.DeserializeObject<DefaultResponse>(json);
                return obj.IsOk()
                    ? Result<bool>.Success(true, json, obj.Message)
                    : Result<bool>.Fail(json, obj.Message);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<bool>.Except(exception);
            }
        }

        /// <summary>
        ///     Like direct message in a thread
        /// </summary>
        /// <param name="threadId">Thread id</param>
        /// <param name="itemId">Item id (message id)</param>
        public async Task<Result<ItemAckResponse>> LikeItemAsync(string threadId, string itemId)
        {
            ValidateLoggedIn();
            try
            {
                var instaUri = UriCreator.GetLikeUnlikeDirectMessageUri();

                var data = new Dictionary<string, string>
                {
                    {"item_type", "reaction"},
                    {"reaction_type", "like"},
                    {"action", "send_item"},
                    {"_csrftoken", Session.CsrfToken},
                    {"_uuid", Device.Uuid.ToString()},
                    {"thread_ids", $"[{threadId}]"},
                    {"client_context", Guid.NewGuid().ToString()},
                    {"node_type", "item"},
                    {"reaction_status", "created"},
                    {"item_id", itemId}
                };
                var response = await _httpClient.PostAsync(instaUri, new HttpFormUrlEncodedContent(data));
                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogResponse(response);

                if (response.StatusCode != HttpStatusCode.Ok)
                    return Result<ItemAckResponse>.Fail(json, response.ReasonPhrase);
                var obj = JsonConvert.DeserializeObject<ItemAckResponse>(json);
                return obj.IsOk() ? Result<ItemAckResponse>.Success(obj, json, obj.Message) : Result<ItemAckResponse>.Fail(json, obj.Message);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<ItemAckResponse>.Except(exception);
            }
        }

        /// <summary>
        ///     UnLike direct message in a thread
        /// </summary>
        /// <param name="threadId">Thread id</param>
        /// <param name="itemId">Item id (message id)</param>
        public async Task<Result<ItemAckResponse>> UnlikeItemAsync(string threadId, string itemId)
        {
            ValidateLoggedIn();
            try
            {
                var instaUri = UriCreator.GetLikeUnlikeDirectMessageUri();

                var data = new Dictionary<string, string>
                {
                    {"item_type", "reaction"},
                    {"reaction_type", "like"},
                    {"action", "send_item"},
                    {"_csrftoken", Session.CsrfToken},
                    {"_uuid", Device.Uuid.ToString()},
                    {"thread_ids", $"[{threadId}]"},
                    {"client_context", Guid.NewGuid().ToString()},
                    {"node_type", "item"},
                    {"reaction_status", "deleted"},
                    {"item_id", itemId}
                };
                var response = await _httpClient.PostAsync(instaUri, new HttpFormUrlEncodedContent(data));
                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogResponse(response);

                if (response.StatusCode != HttpStatusCode.Ok)
                    return Result<ItemAckResponse>.Fail(json, response.ReasonPhrase);
                var obj = JsonConvert.DeserializeObject<ItemAckResponse>(json);
                return obj.IsOk() ? Result<ItemAckResponse>.Success(obj, json, obj.Message) : Result<ItemAckResponse>.Fail(json, obj.Message);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<ItemAckResponse>.Except(exception);
            }
        }
    }
}
