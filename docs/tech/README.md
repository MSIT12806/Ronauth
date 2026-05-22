# RonAuth 身分驗證

這篇文章想記錄的，不是抽象的 authentication 理論，而是 RonAuth 在實作過程中，我們到底遇到哪些難題、看過哪些做法，最後又怎麼收斂成現在這個版本。

如果只用一句話描述 RonAuth 的身分驗證，那會是：

> 我們不是只想「登入成功」，而是想讓 consuming app 可以驗證使用者、讓前端可以還原登入狀態、讓 logout 真的有效，還要為 second-factor flow 留出清楚的中間態。

所以 RonAuth 身分驗證要攻克的難題有：

1. A. 要不要追求純 stateless？
2. B. 登出、停用帳號、session 過期後，如何讓既有登入狀態真的失效？
3. C. 前端重整後，如何還原登入狀態？
4. D. 第二階段驗證完成前，如何避免過早發出正式 token？

---

## A. 要不要追求純 stateless？

為什麼會有 A？

因為只要談到 auth，幾乎一定會有人說：`JWT 是 stateless，所以比較現代。`

這句話本身沒有錯，但它說中的只有一半。

RonAuth 不是某個 app 裡面單純的登入 helper，而是 RonFlow 的 supporting domain。它要處理的不只是「這個 request 合不合法」，還包括：

1. RonFlow backend 要怎麼驗證 caller。
2. logout 之後登入狀態要怎麼真的失效。
3. 前端重整之後要怎麼 restore session。
4. second-factor 中間態要怎麼表達。

如果只追求純 stateless，很多需求會變得不好處理。

有哪些解決方案？

1. 純 server-side session
2. 純 JWT access token
3. opaque token + introspection
4. BFF / cookie-only 模型
5. JWT + cookie session + server-side session 驗證的混合模型

### RonAuth 如何解決

RonAuth 最後沒有走純 JWT，也沒有走純 session，而是選擇：

> `JWT access token + HttpOnly session cookie + server-side session 驗證`

採用的原因是：

1. RonFlow backend 需要 bearer token，方便在 consuming service 驗證使用者身份。
2. RonAuth 自己又不能放棄 revocation 與 session restore。
3. 我們不想把 logout 與 restore 都變成前端自己硬兜的行為。

優點是：

1. JWT 讓 consuming app 很好整合。
2. session 讓 RonAuth 保有 server-side control。
3. 不用在「跨服務驗證」與「可撤銷登入狀態」之間硬選一邊。

缺點是：

1. 模型比純 JWT 複雜。
2. 要同時維護 token 與 session。
3. 不是那種一句話就能說完的極簡設計。

相關程式碼段落：

`RonAuth.Api/Program.cs` 不是只驗 JWT，而是 JWT 驗過後還會再驗 session：

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.Zero,
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var sessionOptions = context.HttpContext.RequestServices.GetRequiredService<IOptions<CrossSiteSessionOptions>>().Value;
                var sessionId = context.HttpContext.Request.Cookies[sessionOptions.CookieName];
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    context.Fail("Missing session cookie.");
                    return;
                }

                var sessionService = context.HttpContext.RequestServices.GetRequiredService<IdentitySessionService>();
                var session = await sessionService.GetActiveAsync(sessionId, context.HttpContext.RequestAborted);
                if (session is null)
                {
                    context.Fail("Session is invalid.");
                    return;
                }
            },
        };
    });
```

這段就是 RonAuth 對 A 的答案：它沒有把 stateless 當唯一目標，而是用混合模型去平衡不同需求。

---

## B. 登出或停用帳號後，如何讓既有登入狀態真的失效？

為什麼會有 B？

因為很多系統的 logout 其實只是「前端把 token 刪掉」。

這樣做表面上看起來完成了登出，但如果 token 還沒過期，它在某些地方可能仍然有效。對 RonAuth 這種 supporting domain 來說，這是不夠的。

RonAuth 必須面對的其實是這些場景：

1. 使用者主動 logout
2. 管理者停用帳號
3. session timeout
4. 未來可能加入的密碼變更或強制失效政策

有哪些解決方案？

1. 只依賴短效 JWT，等它自然過期
2. token 黑名單
3. 每個 request 都 introspection
4. server-side session revoke
5. refresh token rotation + revoke chain

### RonAuth 如何解決

RonAuth 目前比較明確地採用了第 4 種：

> 正式登入時建立 server-side session；logout 時撤銷 session；之後受保護 API 在 token 驗證後還要再查 session 是否有效。

採用的原因是：

1. 比 token 黑名單更直接。
2. 比每次 introspection 更輕量。
3. 跟 bootstrap / restore flow 可以共用同一個 session 模型。

優點是：

1. logout 有真正的撤銷語意。
2. 日後要支援停用帳號、idle timeout，比較自然。
3. session 變成清楚的 application concept，不是藏在 middleware 裡的 incidental detail。

缺點是：

1. 需要 session persistence。
2. 不能把 token 想成完全自給自足的東西。

相關程式碼段落：

`RonAuth.Application/Services/IdentitySessionService.cs` 把 session 明確建模成 `AuthSession`：

```csharp
public async Task<AuthSession> CreateAsync(
    Guid userId,
    string identityProvider,
    string subject,
    CancellationToken cancellationToken)
{
    var currentTime = timeProvider.GetUtcNow();
    var session = new AuthSession
    {
        SessionId = $"sid_{Guid.NewGuid():N}",
        UserId = userId,
        IdentityProvider = identityProvider,
        Subject = subject,
        IssuedAtUtc = currentTime,
        ExpiresAtUtc = currentTime.AddMinutes(sessionOptions.Value.SessionLifetimeMinutes),
        LastSeenAtUtc = currentTime,
    };

    await sessionRepository.CreateAsync(session, cancellationToken);
    return session;
}
```

而 `RonAuth.Api/Controllers/AuthController.cs` 的 logout 也真的會去 revoke session：

```csharp
[Authorize]
[HttpPost("logout")]
public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
{
    if (Request.Cookies.TryGetValue(sessionOptions.Value.CookieName, out var sessionId) && !string.IsNullOrWhiteSpace(sessionId))
    {
        await sessionService.RevokeAsync(sessionId, cancellationToken);
    }

    ClearSessionCookie();
    return NoContent();
}
```

這一段就是 RonAuth 對 B 的答案：logout 不只是改前端狀態，而是真的使 session 失效。

---

## C. 前端重整後，如何還原登入狀態？

為什麼會有 C？

因為 Web app 一定會碰到這個場景：

1. 使用者剛登入
2. 前端重整
3. 記憶體裡的 state 全沒了
4. 但使用者其實不該被迫重新輸入帳密

如果只有純 JWT，而且前端又只把 token 放在記憶體裡，那一重整就沒了。

如果把 token 永久存在 localStorage，雖然方便，但安全邊界又變差。

有哪些解決方案？

1. localStorage / sessionStorage 持久化 token
2. refresh token 保存在 cookie，前端啟動時先 refresh
3. server-side session + bootstrap endpoint
4. 每次 app 啟動都強制重新登入
5. BFF 代理還原 session

### RonAuth 如何解決

RonAuth 目前採用第 3 種：

> 用 HttpOnly session cookie 保存可還原的登入狀態，前端啟動時呼叫 `bootstrap`，由 RonAuth 用 cookie 查 session，再回新的 access token 與目前使用者資訊。

採用的原因是：

1. 前端不需要把長效憑證暴露在 localStorage。
2. restore 與 logout / revocation 可以共用同一個 session 模型。
3. 後來抽出 RonAuth SDK 時，這條流程也比較容易包裝起來。

優點是：

1. 使用者體驗自然。
2. security posture 比直接持久化 token 更穩。
3. consuming app 只要呼叫 bootstrap，不用自己設計 restore 協定。

缺點是：

1. 對 cookie 設定有要求。
2. 跨站部署時，CORS、SameSite、Secure 都會變成真正的整合議題。

相關程式碼段落：

`RonAuth.Api/Controllers/AuthController.cs` 的 `bootstrap` 現在就是 restore 的核心：

```csharp
[HttpGet("bootstrap")]
[HttpGet("session-restore")]
public async Task<ActionResult<AuthenticationResponse>> BootstrapAsync(CancellationToken cancellationToken)
{
    var session = await GetActiveSessionAsync(cancellationToken);
    if (session is null)
    {
        ClearSessionCookie();
        return Unauthorized();
    }

    var result = await authenticationService.RefreshAccessTokenAsync(session.UserId, cancellationToken);
    if (result.Status != LoginStatus.Success)
    {
        await sessionService.RevokeAsync(session.SessionId, cancellationToken);
        ClearSessionCookie();
        return Unauthorized();
    }

    return Ok(ToResponse(result));
}
```

這段的意思很簡單：RonAuth 把 restore 當成正式 use case，而不是讓前端自己偷偷延命 token。

---

## D. 第二階段驗證完成前，如何避免過早發出正式 token？

為什麼會有 D？

因為 second-factor flow 在語意上其實有三個狀態：

1. 還沒通過第一階段驗證
2. 已通過第一階段，但 second-factor 還沒完成
3. 已完成正式登入

如果在第 2 階段就直接給正式 access token，後面很多事情都會開始模糊：

1. 使用者到底算不算已登入？
2. consuming app 到底可不可以信任這個 token？
3. second-factor 失敗後，先前發出去的 token 怎麼辦？

有哪些解決方案？

1. 第一階段一過就直接發正式 access token
2. 把 second-factor 綁在 server-side temporary state
3. 發短效 temporary token，第二階段成功後再換正式 token
4. 完全交給外部 identity provider 處理 MFA
5. 先建立 session，但標記為 partially authenticated

### RonAuth 如何解決

RonAuth 目前採用第 3 種：

> 第一階段通過後，如果需要 second-factor，不直接簽正式 access token，而是先發短效 `temporary token`。只有 second-factor verify 成功後，才建立正式登入狀態與 session cookie。

採用的原因是：

1. 語意最清楚。
2. 前後端都比較容易理解這條 flow。
3. 正式登入與中間態不會混在一起。

優點是：

1. 2FA state machine 很乾淨。
2. `RequiresSecondFactor` 與 `Success` 的差別非常明確。
3. 日後擴充不同 provider，比較不會互相纏在一起。

缺點是：

1. 需要多一種 token 類型。
2. SDK 或 consuming app 必須理解這個中間態。

相關程式碼段落：

`RonAuth.Infrastructure/Security/JwtTokenService.cs` 現在同時有 access token 與 temporary token 兩種簽發邏輯：

```csharp
public string GenerateAccessToken(User user, IReadOnlyList<UserAccess> accesses)
{
    var descriptor = new SecurityTokenDescriptor
    {
        Issuer = jwtSettings.Value.Issuer,
        Audience = jwtSettings.Value.Audience,
        Subject = new ClaimsIdentity(claims),
        Expires = currentTime.AddMinutes(jwtSettings.Value.AccessTokenLifetimeMinutes),
        SigningCredentials = CreateSigningCredentials(),
    };

    var token = tokenHandler.CreateToken(descriptor);
    return tokenHandler.WriteToken(token);
}

public string GenerateTemporaryToken(Guid userId, string providerName, TimeSpan lifetime)
{
    var descriptor = new SecurityTokenDescriptor
    {
        Issuer = jwtSettings.Value.Issuer,
        Audience = jwtSettings.Value.Audience,
        Subject = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("provider", providerName),
            new Claim("token_type", "temporary"),
        ]),
        Expires = currentTime.Add(lifetime),
        SigningCredentials = CreateSigningCredentials(),
    };

    var token = tokenHandler.CreateToken(descriptor);
    return tokenHandler.WriteToken(token);
}
```

而 `RonAuth.Application/Services/AuthenticationService.cs` 在需要 second-factor 時，也不是直接 `BuildSuccessAsync(...)`，而是改走 `IssueSecondFactorChallengeAsync(...)`。

這就是 RonAuth 對 D 的答案：第二階段還沒完成，就還不能算正式登入。

---

## 最後怎麼收斂成現在這個 RonAuth

如果把上面四題放在一起看，RonAuth 最後的做法其實很一致：

1. 用 JWT 解決 consuming service 的 caller 驗證。
2. 用 server-side session 解決 revocation。
3. 用 HttpOnly cookie 解決 restore。
4. 用 temporary token 解決 2FA 中間態。

所以 RonAuth 最終沒有追求某一派最純粹的作法，而是選了一個對 supporting domain 更務實的混合模型。

它的優點很清楚：

1. 好整合。
2. 可撤銷。
3. 可 restore。
4. second-factor 狀態清楚。

它的缺點也很清楚：

1. 比純 JWT 複雜。
2. 需要 session persistence。
3. 對部署與 cookie 策略有要求。

但從 RonAuth 的定位來看，這個代價是合理的。因為 RonAuth 的任務本來就不是只「發一個 token」，而是把 authentication / identity mechanics 這個 supporting domain 真的承接下來。