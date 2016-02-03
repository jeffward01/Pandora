﻿using System;
using System.Collections.Generic;
using System.IdentityModel;
using System.IdentityModel.Services;
using System.IdentityModel.Tokens;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using Elders.Pandora;
using Thinktecture.IdentityModel.Web;
using Thinktecture.IdentityModel.SystemWeb;

namespace Thinktecture.IdentityModel.Oidc
{
    public static class OidcClientConfiguration
    {
        public static string AppRelativeCallbackUrl = ApplicationConfiguration.Get("app_relative_callback_url");
        public static string AuthorizeErrorRedirectUrl = ApplicationConfiguration.Get("authorize_error_redirect_url");
        public static string ClientId = ApplicationConfiguration.Get("client_id");
        public static string ClientSecret = ApplicationConfiguration.Get("client_secret");
        public static string IssuerName = ApplicationConfiguration.Get("issuer");
        public static string Audience = ApplicationConfiguration.Get("audience");
        public static string Scope = ApplicationConfiguration.Get("scopes");
        public static bool CallUserInfoEndpoint = Boolean.Parse(ApplicationConfiguration.Get("call_user_info_endpoint"));

        public static class Endpoints
        {
            public static string Authorize = ApplicationConfiguration.Get("authorize_endpoint");
            public static string Token = ApplicationConfiguration.Get("token_endpoint");
            public static string UserInfo = ApplicationConfiguration.Get("user_info_endpoint");
        }
    }

    public class AuthorizeResponseEventArgs : EventArgs
    {
        public OidcAuthorizeResponse Response { get; set; }
        public bool Cancel { get; set; }
        public string RedirectUrl { get; set; }
    }
    public class TokenResponseEventArgs : EventArgs
    {
        public OidcTokenResponse Response { get; set; }
        public bool Cancel { get; set; }
        public string RedirectUrl { get; set; }
    }
    public class IdentityTokenValidatedEventArgs : EventArgs
    {
        public IEnumerable<Claim> Claims { get; set; }
        public bool Cancel { get; set; }
        public string RedirectUrl { get; set; }
    }
    public class UserInfoClaimsReceivedEventArgs : EventArgs
    {
        public IEnumerable<Claim> Claims { get; set; }
        public bool Cancel { get; set; }
        public string RedirectUrl { get; set; }
    }
    public class SessionTokenCreatedEventArgs : EventArgs
    {
        public SessionSecurityToken Token { get; set; }
    }
    public class ErrorEventArgs : EventArgs
    {
        public Exception Exception { get; set; }
        public bool ExceptionHandled { get; set; }
    }

    public class OpenIdConnectAuthenticationModule : IHttpModule
    {

        public void Init(HttpApplication app)
        {
            app.AddOnAuthenticateRequestAsync(BeginAuthenticateRequest, EndAuthenticateRequest);
            app.EndRequest += OnEndRequest;
        }

        public IAsyncResult BeginAuthenticateRequest(
            object sender, EventArgs e, AsyncCallback cb, object extraData)
        {
            var tcs = new TaskCompletionSource<object>(extraData);
            AuthenticateAsync(HttpContext.Current).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    tcs.SetException(t.Exception.InnerExceptions);
                }
                else
                {
                    tcs.SetResult(null);
                }
                if (cb != null) cb(tcs.Task);
            });
            return tcs.Task;
        }

        public void EndAuthenticateRequest(IAsyncResult result)
        {
            Task task = (Task)result;
            task.Wait();
        }

        public event EventHandler<AuthorizeResponseEventArgs> AuthorizeResponse;
        protected virtual void OnAuthorizeResponse(AuthorizeResponseEventArgs args)
        {
            if (AuthorizeResponse != null)
            {
                AuthorizeResponse(this, args);
            }
        }
        public event EventHandler<TokenResponseEventArgs> TokenResponse;
        protected virtual void OnTokenResponse(TokenResponseEventArgs args)
        {
            if (TokenResponse != null)
            {
                TokenResponse(this, args);
            }
        }
        public event EventHandler<IdentityTokenValidatedEventArgs> IdentityTokenValidated;
        protected virtual void OnIdentityTokenValidated(IdentityTokenValidatedEventArgs args)
        {
            if (IdentityTokenValidated != null)
            {
                IdentityTokenValidated(this, args);
            }
        }
        public event EventHandler<UserInfoClaimsReceivedEventArgs> UserInfoClaimsReceived;
        protected virtual void OnUserInfoClaimsReceived(UserInfoClaimsReceivedEventArgs args)
        {
            if (UserInfoClaimsReceived != null)
            {
                UserInfoClaimsReceived(this, args);
            }
        }

        public static event EventHandler<ClaimsIdentity> ClaimsTransformed;
        protected virtual void OnClaimsTransformed(ClaimsIdentity args)
        {
            if (ClaimsTransformed != null)
            {
                ClaimsTransformed(this, args);
            }
        }

        public event EventHandler<SessionTokenCreatedEventArgs> SessionSecurityTokenCreated;
        protected virtual void OnSessionSecurityTokenCreated(SessionTokenCreatedEventArgs args)
        {
            if (SessionSecurityTokenCreated != null)
            {
                SessionSecurityTokenCreated(this, args);
            }
        }
        public event EventHandler SignedIn;
        protected virtual void OnSignedIn()
        {
            if (SignedIn != null)
            {
                SignedIn(this, EventArgs.Empty);
            }
        }
        public event EventHandler<ErrorEventArgs> Error;
        protected virtual void OnError(ErrorEventArgs args)
        {
            if (Error != null)
            {
                Error(this, args);
            }
        }

        async Task AuthenticateAsync(HttpContext context)
        {
            Init();

            var appRelativeCallbackUrl = OidcClientConfiguration.AppRelativeCallbackUrl;
            if (context.Request.AppRelativeCurrentExecutionFilePath.Equals(appRelativeCallbackUrl, StringComparison.OrdinalIgnoreCase))
            {
                var authorizeErrorUrl = OidcClientConfiguration.AuthorizeErrorRedirectUrl;
                var tokenUrl = OidcClientConfiguration.Endpoints.Token;
                var userInfoUrl = OidcClientConfiguration.Endpoints.UserInfo;
                var clientId = OidcClientConfiguration.ClientId;
                var clientSecret = OidcClientConfiguration.ClientSecret;
                var issuerName = OidcClientConfiguration.IssuerName;
                var callUserInfoEndpoint = OidcClientConfiguration.CallUserInfoEndpoint;

                // parse OIDC authorize response
                var response = OidcClient.HandleAuthorizeResponse(context.Request.QueryString);

                // event? callback recieved, pass response (allow cancel and need new url?)
                var authorizeResponseEventArgs = new AuthorizeResponseEventArgs { Response = response };
                OnAuthorizeResponse(authorizeResponseEventArgs);
                if (authorizeResponseEventArgs.Cancel)
                {
                    if (String.IsNullOrWhiteSpace(authorizeResponseEventArgs.RedirectUrl))
                    {
                        throw new ArgumentNullException("RedirectUrl");
                    }
                    context.Response.Redirect(authorizeResponseEventArgs.RedirectUrl);
                    return;
                }

                if (response.IsError)
                {
                    if (!String.IsNullOrWhiteSpace(authorizeErrorUrl))
                    {
                        if (!authorizeErrorUrl.Contains("?"))
                        {
                            authorizeErrorUrl += "?";
                        }
                        if (!authorizeErrorUrl.EndsWith("?"))
                        {
                            authorizeErrorUrl += "&";
                        }
                        authorizeErrorUrl += "error=" + response.Error;
                        context.Response.Redirect(authorizeErrorUrl);
                        return;
                    }

                    throw new InvalidOperationException("OpenID Connect Callback Error: " + response.Error + ". Handle the AuthorizeResponse event to handle authorization errors.");
                }

                try
                {
                    // read and parse state cookie
                    var cookie = new SelfProtectedCookie(ProtectionMode.MachineKey, HttpContext.Current.Request.IsSecureConnection);
                    var storedState = cookie.Read("oidcstate");
                    SelfProtectedCookie.TryDelete("oidcstate");

                    var separator = storedState.IndexOf('_');
                    if (separator == -1)
                    {
                        throw new InvalidOperationException("state invalid.");
                    }

                    var state = storedState.Substring(0, separator);
                    var returnUrl = storedState.Substring(separator + 1);

                    if (appRelativeCallbackUrl.StartsWith("~/"))
                    {
                        appRelativeCallbackUrl = appRelativeCallbackUrl.Substring(2);
                    }
                    var redirectUri = context.Request.GetApplicationUrl() + appRelativeCallbackUrl;

                    // validate state
                    if (response.State != state)
                    {
                        throw new InvalidOperationException("state invalid.");
                    }

                    // call token endpoint and retrieve id and access token (and maybe a refresh token)
                    var tokenResponse = await OidcClient.CallTokenEndpointAsync(
                        new Uri(tokenUrl),
                        new Uri(redirectUri),
                        response.Code,
                        clientId,
                        clientSecret);

                    // event -- token obtained event
                    var tokenResponseEventArgs = new TokenResponseEventArgs { Response = tokenResponse };
                    OnTokenResponse(tokenResponseEventArgs);
                    if (tokenResponseEventArgs.Cancel)
                    {
                        if (String.IsNullOrWhiteSpace(tokenResponseEventArgs.RedirectUrl))
                        {
                            throw new ArgumentNullException("RedirectUrl");
                        }

                        context.Response.Redirect(tokenResponseEventArgs.RedirectUrl);
                        return;
                    }

                    // validate identity token
                    var identityClaims = OidcClient.ValidateIdentityToken(tokenResponse.IdentityToken);

                    // event -- identity token validated w/ claims
                    var identityTokenValidatedEventArgs = new IdentityTokenValidatedEventArgs { Claims = identityClaims };
                    OnIdentityTokenValidated(identityTokenValidatedEventArgs);
                    if (identityTokenValidatedEventArgs.Cancel)
                    {
                        if (String.IsNullOrWhiteSpace(identityTokenValidatedEventArgs.RedirectUrl))
                        {
                            throw new ArgumentNullException("RedirectUrl");
                        }

                        context.Response.Redirect(identityTokenValidatedEventArgs.RedirectUrl);
                        return;
                    }

                    var claims = identityTokenValidatedEventArgs.Claims.ToList();

                    if (callUserInfoEndpoint)
                    {
                        // retrieve user info data
                        var userInfoClaims = await OidcClient.GetUserInfoClaimsAsync(
                            new Uri(userInfoUrl),
                            tokenResponse.AccessToken);

                        // event -- profile loaded w/ claims
                        var userInfoClaimsReceivedEventArgs = new UserInfoClaimsReceivedEventArgs { Claims = userInfoClaims };
                        OnUserInfoClaimsReceived(userInfoClaimsReceivedEventArgs);
                        if (userInfoClaimsReceivedEventArgs.Cancel)
                        {
                            if (String.IsNullOrWhiteSpace(userInfoClaimsReceivedEventArgs.RedirectUrl))
                            {
                                throw new ArgumentNullException("RedirectUrl");
                            }

                            context.Response.Redirect(userInfoClaimsReceivedEventArgs.RedirectUrl);
                            return;
                        }

                        claims = userInfoClaimsReceivedEventArgs.Claims.ToList();
                    }

                    // create identity
                    claims.Add(new Claim("at", tokenResponse.AccessToken));
                    var id = new ClaimsIdentity(claims, "oidc");

                    if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
                    {
                        id.AddClaim(new Claim("refresh_token", tokenResponse.RefreshToken));
                    }
                    if (!string.IsNullOrWhiteSpace(tokenResponse.IdentityToken))
                    {
                        id.AddClaim(new Claim("id_token", tokenResponse.IdentityToken));
                    }

                    OnClaimsTransformed(id);
                    // create principal
                    var principal = new ClaimsPrincipal(id);
                    var transformedPrincipal = FederatedAuthentication.FederationConfiguration.IdentityConfiguration.ClaimsAuthenticationManager.Authenticate(string.Empty, principal);

                    // establish session
                    var sessionToken = new SessionSecurityToken(transformedPrincipal, new TimeSpan(0, 0, 0, tokenResponse.ExpiresIn, 0));

                    var exp = new SelfProtectedCookie(ProtectionMode.MachineKey, HttpContext.Current.Request.IsSecureConnection);
                    exp.Write("tryGoOp", DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn).ToFileTimeUtc().ToString(), DateTime.UtcNow.AddHours(1));
                    // event? raise session security token created
                    var sessionTokenCreatedEventArgs = new SessionTokenCreatedEventArgs { Token = sessionToken };
                    OnSessionSecurityTokenCreated(sessionTokenCreatedEventArgs);

                    FederatedAuthentication.SessionAuthenticationModule.WriteSessionTokenToCookie(sessionToken);

                    // event? signed in -- pass in return URL and allow them to change
                    OnSignedIn();

                    // redirect local to return url
                    if (!string.IsNullOrWhiteSpace(returnUrl))
                    {
                        context.Response.Redirect(returnUrl, false);
                    }
                    else
                    {
                        context.Response.Redirect("~/");
                    }
                }
                catch (Exception ex)
                {
                    // event? general error/fail
                    var errorEventArgs = new ErrorEventArgs() { Exception = ex };
                    OnError(errorEventArgs);
                    if (!errorEventArgs.ExceptionHandled)
                    {
                        throw;
                    }
                }
            }
        }
        private static Dictionary<string, string> Cookies = new Dictionary<string, string>();

        List<CookieTransform> _NonSecureTransforms = new List<CookieTransform>
            {
                new DeflateCookieTransform(),
                new MachineKeyTransform(),
            };


        static object locker = new Object();
        public static void Init()
        {
            if (locker != null)
            {
                lock (locker)
                {
                    if (locker != null)
                    {
                        if (HttpContext.Current != null && HttpContext.Current.Request != null)
                        {
                            locker = null;
                            FederatedAuthentication.SessionAuthenticationModule.CookieHandler.RequireSsl = HttpContext.Current.Request.IsSecureConnection;
                        }
                    }
                }
            }
        }

        void OnEndRequest(object sender, EventArgs e)
        {

            //  Login();

            var context = HttpContext.Current;
            if ((context.Response.StatusCode == 401 &&
              !context.User.Identity.IsAuthenticated &&
              !context.Response.SuppressFormsAuthenticationRedirect) && !context.Request.RawUrl.Contains("/api"))
            {
                //context.Response.Redirect(LoginUrl());
                context.Response.Redirect("/Login");
            }
        }

        public static string LoginUrl()
        {
            var context = HttpContext.Current;

            //  SelfProtectedCookie.Delete("tryGoOp");
            var authorizeUrl = OidcClientConfiguration.Endpoints.Authorize;
            var clientId = OidcClientConfiguration.ClientId;
            var scopes = "openid " + OidcClientConfiguration.Scope;
            var state = Guid.NewGuid().ToString("N");
            var returnUrl = context.Request.RawUrl;

            var appRelativeCallbackUrl = OidcClientConfiguration.AppRelativeCallbackUrl;
            if (appRelativeCallbackUrl.StartsWith("~/"))
            {
                appRelativeCallbackUrl = appRelativeCallbackUrl.Substring(2);
            }
            var redirectUri = context.Request.GetApplicationUrl() + appRelativeCallbackUrl;
            bool newUser = false;
            bool reset = false;
            if (returnUrl.ToLower().Contains("newuser=true"))
            {
                newUser = true;
            }
            if (returnUrl.ToLower().Contains("reset=true"))
            {
                reset = true;
            }
            var authorizeUri = OidcClient.GetAuthorizeUrl(
                new Uri(authorizeUrl),
                new Uri(redirectUri),
                clientId,
                scopes,
                state);

            var cookie = new SelfProtectedCookie(ProtectionMode.MachineKey, HttpContext.Current.Request.IsSecureConnection);
            cookie.Write("oidcstate", state + "_" + returnUrl, DateTime.UtcNow.AddHours(24));
            context.ClearError();
            return (authorizeUri.AbsoluteUri + (newUser ? "&newUser=true" : "") + (reset ? "&reset=true" : ""));
        }
        public void Dispose()
        { }
    }
}