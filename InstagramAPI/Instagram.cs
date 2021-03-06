﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Storage;
using Windows.Web.Http;
using InstagramAPI.Classes;
using InstagramAPI.Classes.Android;
using InstagramAPI.Classes.Responses;
using InstagramAPI.Classes.User;
using InstagramAPI.Push;
using InstagramAPI.Sync;
using InstagramAPI.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HttpClient = Windows.Web.Http.HttpClient;
using HttpMethod = Windows.Web.Http.HttpMethod;
using HttpRequestMessage = Windows.Web.Http.HttpRequestMessage;

namespace InstagramAPI
{
    public partial class Instagram
    {
        private static Instagram _instance;
        public static Instagram Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = new Instagram();
                return _instance;
            }
        }

        public static bool IsUserAuthenticatedPersistent
        {
            get => (bool?)LocalSettings.Values["_isUserAuthenticated"] ?? false;
            private set => LocalSettings.Values["_isUserAuthenticated"] = value;
        }

        public bool IsUserAuthenticated { get; private set; }
        public UserSessionData Session { get; } = new UserSessionData();
        public AndroidDevice Device { get; } = AndroidDevice.GetRandomAndroidDevice();
        public PushClient PushClient { get; }
        public SyncClient SyncClient { get; }
        public Dictionary<long, InstaUser> CentralUserRegistry { get; } = new Dictionary<long, InstaUser>();

        // For push notification to look up thread title from thread id. Instagram doesn't pass thread title for push notifications.
        public const string THREAD_TITLE_PERSISTENT_DICTIONARY_KEY = "ThreadTitlePersistentDictionary";
        public PersistentDictionary<string> ThreadTitlePersistentDictionary { get; } = new PersistentDictionary<string>(THREAD_TITLE_PERSISTENT_DICTIONARY_KEY);

        private readonly HttpClient _httpClient = new HttpClient();
        private readonly ApiRequestMessage _apiRequestMessage;
        private readonly DebugLogger _logger;
        private string _fbAccessToken;
        public TwoFactorLoginInfo TwoFactorInfo { get; private set; }  // Only used when login returns two factor
        public ChallengeLoginInfo ChallengeInfo { get; private set; }  // Only used when login returns challenge
        private static readonly ApplicationDataContainer LocalSettings = ApplicationData.Current.LocalSettings;

        private Instagram()
        {
#if DEBUG
            _logger = new DebugLogger(LogLevel.All);
#endif
            SetDefaultRequestHeaders();
            _apiRequestMessage = new ApiRequestMessage(this);

            IsUserAuthenticated = IsUserAuthenticatedPersistent;
            PushClient = new PushClient(this, IsUserAuthenticated);
            SyncClient = new SyncClient(this);

            if (!IsUserAuthenticated) return;
            ThreadTitlePersistentDictionary.LoadFromAppSettings();
            Session.LoadFromAppSettings();
            Device = AndroidDevice.CreateFromAppSettings() ?? Device;
            SetDefaultRequestHeaders();
        }

        /// <summary>
        ///     Login using given credentials asynchronously
        /// </summary>
        /// <param name="isNewLogin"></param>
        /// <returns>
        ///     Success --> is succeed
        ///     TwoFactorRequired --> requires 2FA login.
        ///     BadPassword --> Password is wrong
        ///     InvalidUser --> User/phone number is wrong
        ///     Exception --> Something wrong happened
        ///     ChallengeRequired --> You need to pass Instagram challenge
        /// </returns>
        public async Task<Result<LoginResult>> LoginAsync(string username, string password, bool isNewLogin = true)
        {
            Session.Username = username;
            Session.Password = password;
            ValidateRequestMessage();
            try
            {
                if (isNewLogin)
                {
                    var firstResponse = await _httpClient.GetAsync(UriCreator.BaseInstagramUri);
                    _logger?.LogResponse(firstResponse);
                }

                var csrftoken = GetCsrfToken();
                Session.CsrfToken = csrftoken;
                var loginUri = UriCreator.GetLoginUri();
                var apiVersion = ApiVersion.CurrentApiVersion;
                var signature =
                    $"{_apiRequestMessage.GenerateChallengeSignature(apiVersion, apiVersion.SignatureKey, csrftoken, out var devid)}.{_apiRequestMessage.GetChallengeMessageString(csrftoken)}";
                Device.DeviceId = devid;
                var fields = new Dictionary<string, string>
                {
                    {"signed_body", signature},
                    {"ig_sig_key_version", "4"}
                };
                var request = new HttpRequestMessage(HttpMethod.Post, loginUri);
                request.Headers.Host = new HostName("i.instagram.com");
                request.Content = new HttpFormUrlEncodedContent(fields);
                var response = await _httpClient.SendRequestAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.Ok)
                {
                    var loginFailReason = JsonConvert.DeserializeObject<LoginBaseResponse>(json);

                    if (loginFailReason.InvalidCredentials)
                        return Result<LoginResult>.Fail(loginFailReason.ErrorType == "bad_password"
                                ? LoginResult.BadPassword
                                : LoginResult.InvalidUser,
                            "Invalid Credentials", json
                        );
                    if (loginFailReason.TwoFactorRequired)
                    {
                        if (loginFailReason.TwoFactorLoginInfo != null)
                            Session.Username = loginFailReason.TwoFactorLoginInfo.Username;
                        TwoFactorInfo = loginFailReason.TwoFactorLoginInfo;
                        //2FA is required!
                        return Result<LoginResult>.Fail(LoginResult.TwoFactorRequired, "Two Factor Authentication is required", json);
                    }
                    if (loginFailReason.ErrorType == "checkpoint_challenge_required"
                       /* || !string.IsNullOrEmpty(loginFailReason.Message) && loginFailReason.Message == "challenge_required"*/)
                    {
                        ChallengeInfo = loginFailReason.Challenge;
                        
                        return Result<LoginResult>.Fail(LoginResult.ChallengeRequired, "Challenge is required", json);
                    }
                    if (loginFailReason.ErrorType == "rate_limit_error")
                    {
                        return Result<LoginResult>.Fail(LoginResult.LimitError, "Please wait a few minutes before you try again.", json);
                    }
                    if (loginFailReason.ErrorType == "inactive user" || loginFailReason.ErrorType == "inactive_user")
                    {
                        return Result<LoginResult>.Fail(LoginResult.InactiveUser, $"{loginFailReason.Message}\r\nHelp url: {loginFailReason.HelpUrl}");
                    }
                    if (loginFailReason.ErrorType == "checkpoint_logged_out")
                    {
                        return Result<LoginResult>.Fail(LoginResult.CheckpointLoggedOut, $"{loginFailReason.ErrorType} {loginFailReason.CheckpointUrl}");
                    }
                    return Result<LoginResult>.Fail(LoginResult.Exception, json: json);
                }
                var loginInfo = JsonConvert.DeserializeObject<LoginResponse>(json);
                if (loginInfo.User == null)
                {
                    return Result<LoginResult>.Fail(LoginResult.Exception, "User is null!", json);
                }
                IsUserAuthenticated = loginInfo.User != null;
                Session.Username = loginInfo.User.Username;
                Session.LoggedInUser = loginInfo.User;
                Session.RankToken = $"{loginInfo.User.Pk}_{_apiRequestMessage.PhoneId}";
                if (string.IsNullOrEmpty(Session.CsrfToken))
                {
                    Session.CsrfToken = GetCsrfToken();
                }
                SaveToAppSettings();
                return Result<LoginResult>.Success(LoginResult.Success, json: json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<LoginResult>.Except(exception, LoginResult.Exception);
            }
        }

        /// <summary>
        ///     Login with Facebook access token
        /// </summary>
        /// <param name="fbAccessToken">Facebook access token</param>
        /// <param name="cookiesContainer">Cookies</param>
        /// <returns>
        ///     Success --> is succeed
        ///     TwoFactorRequired --> requires 2FA login.
        ///     BadPassword --> Password is wrong
        ///     InvalidUser --> User/phone number is wrong
        ///     Exception --> Something wrong happened
        ///     ChallengeRequired --> You need to pass Instagram challenge
        /// </returns>
        public async Task<Result<LoginResult>> LoginWithFacebookAsync(string fbAccessToken)
        {
            try
            {
                if (string.IsNullOrEmpty(fbAccessToken)) throw new ArgumentNullException(nameof(fbAccessToken));
                if (GetCsrfToken() == string.Empty)
                {
                    var firstResponse = await _httpClient.GetAsync(UriCreator.BaseInstagramUri);
                    _logger?.LogResponse(firstResponse);
                }

                var instaUri = UriCreator.GetFacebookSignUpUri();

                var data = new JObject
                {
                    {"dryrun", "true"},
                    {"phone_id", Device.PhoneId.ToString()},
                    {"_csrftoken", GetCsrfToken()},
                    {"adid", Guid.NewGuid().ToString()},
                    {"guid",  Device.Uuid.ToString()},
                    {"_uuid",  Device.Uuid.ToString()},
                    {"device_id", Device.DeviceId},
                    {"waterfall_id", Guid.NewGuid().ToString()},
                    {"fb_access_token", fbAccessToken},
                };

                _fbAccessToken = fbAccessToken;
                var request = GetSignedRequest(instaUri, data);
                var response = await _httpClient.SendRequestAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogResponse(response);

                if (response.StatusCode != HttpStatusCode.Ok)
                {
                    var loginFailReason = JsonConvert.DeserializeObject<LoginBaseResponse>(json);

                    if (loginFailReason.InvalidCredentials)
                        return Result<LoginResult>.Fail(loginFailReason.ErrorType == "bad_password"
                            ? LoginResult.BadPassword
                            : LoginResult.InvalidUser, "Invalid Credentials", json);
                    if (loginFailReason.TwoFactorRequired)
                    {
                        if (loginFailReason.TwoFactorLoginInfo != null)
                            Session.Username = loginFailReason.TwoFactorLoginInfo.Username;
                        TwoFactorInfo = loginFailReason.TwoFactorLoginInfo;
                        return Result<LoginResult>.Fail(LoginResult.TwoFactorRequired, "Two Factor Authentication is required", json);
                    }
                    if (loginFailReason.ErrorType == "checkpoint_challenge_required")
                    {
                        ChallengeInfo = loginFailReason.Challenge;

                        return Result<LoginResult>.Fail(LoginResult.ChallengeRequired, "Challenge is required", json);
                    }
                    if (loginFailReason.ErrorType == "rate_limit_error")
                    {
                        return Result<LoginResult>.Fail(LoginResult.LimitError, "Please wait a few minutes before you try again.", json);
                    }
                    if (loginFailReason.ErrorType == "inactive user" || loginFailReason.ErrorType == "inactive_user")
                    {
                        return Result<LoginResult>.Fail(LoginResult.InactiveUser, $"{loginFailReason.Message}\r\nHelp url: {loginFailReason.HelpUrl}");
                    }
                    if (loginFailReason.ErrorType == "checkpoint_logged_out")
                    {
                        return Result<LoginResult>.Fail(LoginResult.CheckpointLoggedOut, $"{loginFailReason.ErrorType} {loginFailReason.CheckpointUrl}");
                    }
                    return Result<LoginResult>.Fail(LoginResult.Exception, json: json);
                }

                var fbUserId = string.Empty;
                InstaUser loginInfoUser = null;
                if (json.Contains("\"account_created\""))
                {
                    var rmt = JsonConvert.DeserializeObject<FacebookRegistrationResponse>(json);
                    if (rmt?.AccountCreated != null)
                    {
                        fbUserId = rmt?.FbUserId;
                        if (rmt.AccountCreated.Value)
                        {
                            loginInfoUser = JsonConvert.DeserializeObject<FacebookLoginResponse>(json)?.CreatedUser;
                        }
                        else
                        {
                            return Result<LoginResult>.Fail(LoginResult.Exception, "Facebook account is not linked", json);
                        }
                    }
                }

                if (loginInfoUser == null)
                {
                    var obj = JsonConvert.DeserializeObject<FacebookLoginResponse>(json);
                    fbUserId = obj?.FbUserId;
                    loginInfoUser = obj?.LoggedInUser;
                }

                if (loginInfoUser == null) return Result<LoginResult>.Fail(LoginResult.Exception, json: json);

                IsUserAuthenticated = true;
                Session.LoggedInUser = loginInfoUser;
                Session.RankToken = $"{Session.LoggedInUser.Pk}_{Device.PhoneId}";
                Session.CsrfToken = GetCsrfToken();
                Session.FacebookUserId = fbUserId;
                Session.Username = loginInfoUser.Username;
                Session.FacebookAccessToken = fbAccessToken;
                Session.Password = "LOGGED_IN_THROUGH_FB";
                return Result<LoginResult>.Success(LoginResult.Success, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<LoginResult>.Except(exception, LoginResult.Exception);
            }
        }

        /// <summary>
        /// No need to clear data. If IsUserAuthenticated is false, next time when constructor is called,
        /// data will not be loaded.
        /// </summary>
        public async void Logout()
        {
            IsUserAuthenticated = false;
            SyncClient.Shutdown();
            PushClient.UnregisterTasks();
            SaveToAppSettings();
            await PushClient.Shutdown().ConfigureAwait(false);    // long task
            PushClient.ConnectionData.Clear();
        }

        public async Task<Result<CurrentUser>> GetCurrentUserAsync()
        {
            ValidateLoggedIn();
            try
            {
                var instaUri = UriCreator.GetCurrentUserUri();
                var fields = new Dictionary<string, string>
                {
                    {"_uuid", Device.Uuid.ToString()},
                    {"_uid", Session.LoggedInUser.Pk.ToString()},
                    {"_csrftoken", Session.CsrfToken}
                };
                var response = await _httpClient.PostAsync(instaUri, new HttpFormUrlEncodedContent(fields));
                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogResponse(response);

                if (response.StatusCode != HttpStatusCode.Ok)
                    return Result<CurrentUser>.Fail(json, response.ReasonPhrase);
                var statusResponse = JObject.Parse(json);
                if (statusResponse["status"].ToObject<string>() != "ok")
                    Result<CurrentUser>.Fail(json);

                var user = statusResponse["user"].ToObject<CurrentUser>();
                if (user.Pk < 1)
                    Result<CurrentUser>.Fail(json, "Pk is incorrect");
                CentralUserRegistry.Add(user.Pk, user);
                return Result<CurrentUser>.Success(user, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result<CurrentUser>.Except(exception);
            }
        }

        public void SaveToAppSettings()
        {
            IsUserAuthenticatedPersistent = IsUserAuthenticated;
            Device.SaveToAppSettings();
            if (IsUserAuthenticated)
            {
                Session.SaveToAppSettings();
                PushClient.ConnectionData.SaveToAppSettings();
                ThreadTitlePersistentDictionary.SaveToAppSettings();
            }
            else
            {
                Session.RemoveFromAppSettings();
                PushClient.ConnectionData.RemoveFromAppSettings();
                ThreadTitlePersistentDictionary.RemoveFromAppSettings();
            }
        }
    }
}
