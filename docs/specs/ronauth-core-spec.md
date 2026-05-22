# RonAuth Core Spec

## 1. 文件定位

本文件是 RonAuth 的 living spec，用來描述 RonAuth 作為一個獨立 supporting domain 時，應該提供的後端成品行為。

這份文件的目標是：

1. 定義 RonAuth 的 bounded context 與對外責任。
2. 把目前從 cityinfo-kernel Identity 與 GSP Authorization / Auth 流程萃取出的能力整理成可驗證的產品規格。
3. 作為 RonAuth 後續 API、application service、資料庫 schema、整合測試與驗收測試的上游依據。
4. 明確區分「RonAuth 核心能力」與「消費系統自己的授權語意」。

本文件描述的是 RonAuth 現在應有的產品意圖，而不是 cityinfo-kernel 或 GSP 目前程式碼的逐行翻譯。

---

## 2. Bounded Context 定位

RonAuth 是 RonFlow 的 supporting domain，負責提供通用的 authentication / identity / authorization mechanics。

RonAuth 應承擔的責任如下：

```text
1. 使用者帳號與登入憑證管理
2. 密碼政策、登入失敗次數與鎖定規則
3. OTP / 2FA 驗證流程
4. JWT access token 與短效 two-factor token 發行
5. 跨請求登入 session 管理
6. 角色在某個邊界內的指派與查詢
7. 權限定義、權限同步與權限指派管理
8. 提供通用的權限解析 primitives
9. 提供 SSO provider 擴充點
10. 提供供外部系統整合的 API 與初始化流程
```

RonAuth 不應承擔的責任如下：

```text
1. RonFlow 自己的產品角色語意，例如「專案管理員」「工作區成員」
2. 消費系統自己的 effective permission merge 規則
3. 消費系統自己的 permission code 命名與 catalog 分類內容
4. RonFlow 畫面、前端流程或 UI state
5. RonFlow 業務資料，例如 Project、Task、Workflow、Board
```

一句話定義：

> RonAuth 提供的是 identity 與 authorization 的通用機制，不是 RonFlow 自己的業務授權語意。

---

## 3. 文件使用原則

閱讀與維護本文件時，採以下原則：

1. 若 RonAuth 的 API、規則、狀態模型、設定需求或整合方式改變，應直接更新本文件。
2. 若某能力屬於消費系統的 application layer，而不是 RonAuth core，應寫在「整合邊界」或「非目標」區，而不是寫成 RonAuth 核心責任。
3. 本文件描述可驗證的系統行為，但不限定必須由 unit test、integration test 或 contract test 哪一層承接。
4. 若某能力目前只存在於 cityinfo-kernel 或 GSP 的 site layer，移植到 RonAuth 時應先判斷是否仍屬於 core；不是 core 的能力不應直接照搬。

---

## 4. Use Case 導向原則

本文件後續以 use case 為主，而不是先列資料模型或欄位清單。

採用以下原則：

1. 先描述「誰要完成什麼事」與「系統應如何回應」。
2. 只有當某個資料概念是完成 use case 的必要條件時，才在規格中提到它。
3. `IsActive`、`Metadata`、`Scope`、`TargetPermission` 這類概念，不應先當成前提；它們只能在對應 use case 真正需要時被推導出來。
4. 設計模型、欄位、資料表與 class 結構，應在 use case 穩定後再往下推導。

---

## 5. Use Case Index

RonAuth v0.1 先聚焦在以下 use cases：

```text
UC-01 使用帳號密碼登入
UC-02 完成第二階段驗證
UC-03 使用一次性驗證碼登入
UC-04 還原既有登入狀態
UC-05 刷新 access token
UC-06 登出
UC-07 查詢目前登入身分資訊
UC-08 變更自己的密碼
UC-09 啟用或停用自己的第二因子設定
UC-10 建立帳號
UC-11 停用或恢復帳號
UC-12 指派使用者在某個範圍內的角色
UC-13 維護角色與角色權限
UC-14 維護權限目錄
UC-15 維護目標權限
UC-16 解析多目標權限集合
UC-17 整合外部 SSO 提供者
```

---

## 6. Domain Boundary

### 6.1 RonAuth Core 內部責任

RonAuth core 應直接負責：

```text
1. 驗證帳號是否存在且啟用
2. 驗證密碼與密碼政策
3. 記錄登入失敗次數與鎖定期限
4. 發送與驗證 OTP / 2FA code
5. 建立 token 與 session
6. 保存與查詢身份、角色指派與權限指派所需資料
7. 寫入角色權限與直接權限指派
8. 同步 permission catalog
9. 提供 generic permission resolve API
```

### 6.2 留在消費系統的責任

消費系統仍需自己決定：

```text
1. 哪些 role name 代表 super admin、workspace owner、project member
2. 哪些 target category 存在，以及它們的業務意義
3. user direct permission、team permission、workspace permission、project permission 如何合併
4. 某些權限是否可委派、是否應隱藏、是否能被低權限管理者看見
5. 站台前端的登入畫面、captcha 呈現、帳號管理 UI
6. 與 RonFlow 自己 domain event 或 workflow 的整合行為
```

### 6.3 Extension Points

RonAuth 應保留以下 extension points：

```text
1. OTP Provider 擴充
2. SSO Provider 擴充
3. Permission Catalog 由 consuming app 定義與同步
4. Effective Permission Policy 由 consuming app 實作
5. Delegation / assignment policy 由 consuming app 實作
```

---

## 7. Use Case 規格

### UC-01 使用帳號密碼登入

**Actor**

未登入使用者。

**Goal**

使用帳號密碼取得有效登入狀態。

**Main Success Scenario**

```text
1. 使用者提交帳號與密碼。
2. 系統驗證這組憑證可否被接受。
3. 若此帳號不需要第二階段驗證，系統建立登入狀態。
4. 系統回傳 access token 與目前登入 payload。
```

**Alternative / Failure Scenarios**

```text
1. 若 anti-abuse challenge 驗證失敗，系統拒絕登入。
2. 若帳號不存在，系統拒絕登入。
3. 若帳號目前不可登入，系統拒絕登入。
4. 若帳號已被暫時鎖定，系統回傳 locked out 狀態。
5. 若密碼已過有效使用期限，系統拒絕登入並要求先變更密碼。
6. 若帳號需要第二階段驗證，系統不直接完成登入，而是回傳 requires-two-factor 狀態與短效 temp token。
```

**Rules**

```text
1. 系統必須追蹤連續登入失敗次數。
2. 連續失敗達到門檻時，系統必須在一段時間內拒絕該帳號的登入嘗試。
3. 一旦成功登入，系統必須清除既有的登入失敗狀態。
```

### UC-02 完成第二階段驗證

**Actor**

已通過第一階段驗證、但尚未完成登入的使用者。

**Goal**

使用第二因子完成最終登入。

**Main Success Scenario**

```text
1. 使用者提交 temp token、provider 與驗證碼。
2. 系統驗證 temp token 是否有效。
3. 系統驗證 provider 與驗證碼是否正確且仍可使用。
4. 系統建立正式登入狀態。
5. 系統回傳 access token 與目前登入 payload。
```

**Alternative / Failure Scenarios**

```text
1. 若 temp token 已過期或不合法，系統拒絕驗證。
2. 若 provider 不存在、未啟用或不適用於該帳號，系統拒絕驗證。
3. 若驗證碼錯誤、已過期或已使用，系統拒絕驗證。
```

### UC-03 使用一次性驗證碼登入

**Actor**

未登入使用者。

**Goal**

不輸入密碼，改以一次性驗證碼登入。

**Main Success Scenario**

```text
1. 使用者提交帳號與請求 OTP 的資訊。
2. 系統確認此帳號目前可發送一次性驗證碼。
3. 系統發送驗證碼，並回傳短效 temp token。
4. 使用者提交 temp token 與驗證碼。
5. 系統驗證成功後建立正式登入狀態。
6. 系統回傳 access token 與目前登入 payload。
```

**Alternative / Failure Scenarios**

```text
1. 若帳號目前不可登入，系統拒絕發送驗證碼。
2. 若 temp token 或驗證碼無效，系統拒絕登入。
3. 若 OTP provider 暫時不可用，系統回傳可辨識的失敗結果。
```

### UC-04 還原既有登入狀態

**Actor**

已曾成功登入、目前帶有 session cookie 的使用者端。

**Goal**

在重新整理頁面或重新開啟前端後，恢復目前登入狀態。

**Main Success Scenario**

```text
1. 前端提交 bootstrap 請求。
2. 系統從 session cookie 讀取既有登入狀態。
3. 若登入狀態仍有效，系統回傳目前登入 payload。
```

**Alternative / Failure Scenarios**

```text
1. 若 session 不存在、已撤銷、已過期或閒置逾時，系統回傳 unauthorized。
2. 若 session 對應帳號目前不可登入，系統撤銷該 session 並回傳 unauthorized。
```

### UC-05 刷新 access token

**Actor**

已登入使用者。

**Goal**

在不重新登入的情況下，換發新的 access token。

**Main Success Scenario**

```text
1. 使用者端提交 refresh 請求。
2. 系統驗證目前 session 仍有效。
3. 系統重新發行 access token。
4. 系統回傳新的 token。
```

**Rules**

```text
1. refresh 成功不應改變使用者原本的身份與授權來源。
2. refresh 不應繞過 session 驗證。
```

### UC-06 登出

**Actor**

已登入使用者。

**Goal**

主動結束目前登入狀態。

**Main Success Scenario**

```text
1. 使用者提交 logout 請求。
2. 系統撤銷目前 session。
3. 系統清除對應 cookie。
4. 系統回傳 logout 成功結果。
```

### UC-07 查詢目前登入身分資訊

**Actor**

已登入使用者或 consuming app。

**Goal**

取得目前登入主體的身份摘要，供前端或其他後端流程使用。

**Main Success Scenario**

```text
1. 使用者提交 me 或等價查詢。
2. 系統回傳目前登入主體的身份摘要。
3. 摘要至少包含辨識使用者身份所需的資訊，以及 consuming app 後續授權判斷所需的基礎資料。
```

**Rules**

```text
1. 若回傳 permissions，必須清楚說明這些 permissions 的來源是 RonAuth 的 generic resolve，還是 consuming app 的自定 policy。
2. RonAuth 不應在此 use case 中偷渡 consuming app 專屬的授權語意。
```

### UC-08 變更自己的密碼

**Actor**

已登入使用者。

**Goal**

把目前密碼變更為新密碼。

**Main Success Scenario**

```text
1. 使用者提交舊密碼與新密碼。
2. 系統驗證舊密碼正確。
3. 系統驗證新密碼符合密碼政策。
4. 系統更新密碼。
```

**Rules**

```text
1. 新密碼不得違反複雜度要求。
2. 新密碼不得與最近 N 次已使用過的密碼相同。
3. 若未達最短使用天數，不得再次變更密碼。
```

### UC-09 啟用或停用自己的第二因子設定

**Actor**

已登入使用者。

**Goal**

管理自己的第二因子登入方式。

**Main Success Scenario**

```text
1. 使用者選擇要啟用或停用某個第二因子 provider。
2. 系統驗證該 provider 是否可用。
3. 系統保存新的第二因子設定。
```

---

### UC-10 建立帳號

**Actor**

系統管理者、被授權的上游系統，或在 consuming app 啟用 self-service registration 時的未登入使用者。

**Goal**

為一個新的身份主體建立可登入帳號。

**Main Success Scenario**

```text
1. Actor 提交建立帳號請求。
2. 系統驗證必要欄位完整且帳號識別值未被使用。
3. 若建立流程包含初始密碼，系統驗證其符合密碼政策。
4. 系統建立新帳號。
5. 系統視需要附加初始角色或其他登入所需資訊。
```

**Rules**

```text
1. 帳號識別值必須唯一。
2. 是否允許建立時一併附加延伸屬性，屬於此 use case 的設計延伸，但不是前提。
```

### UC-11 停用或恢復帳號

**Actor**

系統管理者。

**Goal**

控制某個帳號是否允許登入。

**Main Success Scenario**

```text
1. 管理者選定一個帳號。
2. 管理者將其切換為可登入或不可登入狀態。
3. 系統保存新的登入可用性。
4. 若帳號被停用，該帳號之後不得再透過既有 session 繼續使用系統。
```

**Design Implication**

```text
1. RonAuth 很可能需要一種可表達「此帳號目前能否登入」的狀態。
2. 這個狀態如何命名、是否獨立成欄位，留待設計階段決定。
```

### UC-12 指派使用者在某個範圍內的角色

**Actor**

系統管理者，或 consuming app 中被授權的管理流程。

**Goal**

讓某個使用者在某個範圍內具備某個角色。

**Main Success Scenario**

```text
1. Actor 選定使用者。
2. Actor 選定角色。
3. Actor 選定該角色生效的範圍。
4. 系統保存這個 access assignment。
5. 後續 token 或身份摘要可反映這個 assignment。
```

**Rules**

```text
1. RonAuth 需要支援「角色在某個邊界內生效」的能力。
2. 這個邊界可以叫 scope、tenant、workspace 或其他名稱，但規格先只要求其語意存在，不先綁定資料欄位設計。
```

### UC-13 維護角色與角色權限

**Actor**

系統管理者。

**Goal**

建立角色、調整角色內容，並決定該角色擁有哪些權限。

**Main Success Scenario**

```text
1. 管理者建立或編輯角色。
2. 管理者選擇該角色要擁有的權限。
3. 系統保存角色定義與角色權限對應。
4. 若角色仍被使用中，系統需保護刪除行為，避免破壞既有指派。
```

### UC-14 維護權限目錄

**Actor**

RonAuth 啟動流程，或 consuming app 的初始化流程。

**Goal**

把 code-first 的權限定義同步為系統可查詢、可指派的權限目錄。

**Main Success Scenario**

```text
1. consuming app 提供一份 permission definitions。
2. RonAuth 比對目前資料庫中的權限定義。
3. 若缺漏，系統建立之。
4. 若名稱或描述已改變，系統更新之。
5. 系統提供 catalog 查詢能力，讓上層可用於管理 UI 或管理 API。
```

### UC-15 維護目標權限

**Actor**

系統管理者，或 consuming app 中的授權管理流程。

**Goal**

把權限直接授予某個特定目標。

**Main Success Scenario**

```text
1. Actor 指定一個目標類型與目標識別值。
2. Actor 選擇要直接授予該目標的權限集合。
3. 系統覆蓋或更新該目標的直接權限。
4. consuming app 後續可把這些直接權限納入自己的授權判斷。
```

**Rules**

```text
1. 目標不只限於使用者；也可能是 team、workspace、unit、project 等。
2. RonAuth 只提供 generic 機制，不定義這些目標在各產品中的業務語意。
```

### UC-16 解析多目標權限集合

**Actor**

consuming app 的 application service 或管理 API。

**Goal**

把多個目標上的權限集合做統一解析。

**Main Success Scenario**

```text
1. consuming app 提交多個 target。
2. 系統讀取各 target 的直接權限。
3. 系統依指定模式做 union 或 intersection。
4. 系統回傳排序後、去重複的 permission codes。
```

**Rules**

```text
1. RonAuth 只提供 union / intersection 等 generic primitive。
2. 哪些 target 要參與解析，以及何時使用哪種模式，屬於 consuming app 決策。
```

### UC-17 整合外部 SSO 提供者

**Actor**

consuming app 的整合流程，或外部身份提供者。

**Goal**

讓外部身份可透過 RonAuth 轉換為內部登入狀態。

**Main Success Scenario**

```text
1. 外部系統提交 SSO callback 或 token。
2. RonAuth 驗證外部身份資訊。
3. RonAuth 將外部身份映射到內部帳號。
4. 若映射成功，RonAuth 建立自己的登入狀態並回傳登入結果。
```

**Alternative / Failure Scenarios**

```text
1. 若外部 token 驗證失敗，系統拒絕登入。
2. 若內部帳號不存在，系統依設定決定是否允許 auto-provision 或直接拒絕。
```

---

## 8. Use Case 導出的核心規則

從上述 use cases，可以先推導出 RonAuth 必須支援的規則，而不是先推導資料表。

```text
1. 系統必須有能力判斷某個帳號目前是否可登入。
2. 系統必須有能力追蹤登入失敗狀態與暫時鎖定。
3. 系統必須有能力表達第二因子設定。
4. 系統必須有能力建立短效登入前置憑證與正式登入狀態。
5. 系統必須有能力表達「某個角色在某個邊界內生效」。
6. 系統必須有能力保存 code-first 權限定義。
7. 系統必須有能力把權限直接授予任意 target。
```

這些規則可能導向某些資料概念，例如：

```text
1. 帳號登入狀態欄位
2. 帳號延伸屬性承載方式
3. 角色與範圍的關聯模型
4. server-side session store
5. target permission schema
```

但這一層目前仍是設計推論，不是規格主體。

---

## 9. Use Case 對應 API 邊界

RonAuth 是純後端專案，因此每個 use case 最後都會落到 API 或等價的 application boundary。

建議對應如下：

```text
UC-01 -> POST /api/auth/login
UC-02 -> POST /api/auth/second-factor/verify
UC-03 -> POST /api/auth/otp/send, POST /api/auth/second-factor/verify
UC-04 -> GET /api/auth/bootstrap 或 GET /api/auth/session-restore
UC-05 -> POST /api/auth/refresh
UC-06 -> POST /api/auth/logout
UC-07 -> GET /api/auth/me
UC-08 -> POST /api/auth/password/change
UC-09 -> PUT /api/auth/second-factor/{providerName}
UC-10 -> POST /api/auth/register 或 POST /users
UC-11 -> PUT /users/{id}/availability 或等價 API
UC-12 -> PUT /users/{id}/accesses 或等價 API
UC-13 -> POST /roles, PUT /roles/{id}, PUT /roles/{id}/permissions
UC-14 -> POST /permissions/catalog-sync, GET /permissions/catalog
UC-15 -> PUT /permissions/targets/{targetCategory}/{targetId}
UC-16 -> POST /permissions/resolve
UC-17 -> POST /auth/sso/{provider}
```

實際 route 可調整，但 API 設計應回頭對齊 use case，而不是先列資源再反推需求。

---

## 10. 設定需求

RonAuth 至少需要以下設定：

```text
1. JwtSettings.SecretKey
2. JwtSettings.Issuer
3. JwtSettings.Audience
4. JwtSettings.ExpiryMinutes
5. PasswordPolicy.*
6. SessionTtl / IdleTimeout / CookieName / CookieDomain / SecureCookie
7. Mail / OTP provider settings
8. Connection string for identity store
```

**Rules**

```text
1. 缺少必要 JWT 設定時，服務不得啟動。
2. 缺少 PasswordPolicy 必要設定時，服務不得啟動。
3. Session 設定必須可支撐 server-side revocation 與 idle timeout。
```

---

## 11. 非目標

RonAuth v0.1 不以以下內容作為核心內建責任：

```text
1. RonFlow 專用的 role name 常數
2. RonFlow 專用的 permission codes
3. workspace、project、task 的業務授權規則
4. 任何站台前端頁面與 UI 組件
5. 各消費系統自己的 effective permission merge policy
6. 各消費系統自己的 delegation policy
7. 消費系統自己的 audit log domain
```

---

## 12. 驗收重點

RonAuth 至少應以驗收測試或整合測試驗證以下行為：

```text
1. UC-01 成功時會建立 session 與 access token。
2. UC-01 連續失敗達門檻後會進入鎖定狀態。
3. UC-01 在需要第二因子時會回傳 requires-two-factor，而不是直接登入成功。
4. UC-02 驗證正確第二因子後可完成登入。
5. UC-03 可以在不輸入密碼的情況下完成 OTP login flow。
6. UC-04 與 UC-05 在 session 過期或被撤銷後必須失敗。
7. UC-08 在新密碼不符合政策時必須失敗。
8. UC-08 不允許重複使用最近 N 次已使用過的密碼。
9. UC-13 不允許刪除仍被使用中的角色。
10. UC-14 會建立與更新缺漏的權限目錄項目。
11. UC-16 對 union / intersection 會回傳正確結果。
12. RonAuth 不會在任何 use case 中內建 consuming app 專屬的 effective permission business rule。
```

---

## 13. 後續設計說明

當 use cases 固定後，才建議開始往下切 domain model、application service、schema 與 API contract。

到那個階段，再回答像這些問題：

```text
1. 帳號登入可用性要不要叫 IsActive？
2. 延伸屬性要不要叫 Metadata？
3. role 生效邊界是否統一叫 Scope？
4. 直接授權的主體模型要怎麼落表？
5. session store 與 token claim 結構要怎麼設計？
```

換句話說：

> 這份 spec 先回答 RonAuth 要完成哪些事；資料模型名稱與 class 形狀，留到下一輪設計再決定。

---

## 14. 後續切分建議

當 RonAuth 開始實作時，建議至少切成以下 backend 專案：

```text
1. RonAuth.Domain
2. RonAuth.Application
3. RonAuth.Infrastructure
4. RonAuth.Api
5. RonAuth.Api.Tests
```

其中責任預期如下：

```text
1. Domain：User、Role、Scope、Permission、Session、PasswordPolicy 與相關 domain rule
2. Application：Login、VerifyOtp、RefreshSession、RegisterUser、ReplaceTargetPermissions 等 use case
3. Infrastructure：JWT、Dapper / EF、Email OTP provider、SSO provider adapters、session store
4. Api：HTTP contracts、authentication middleware、authorization policy wiring
```
