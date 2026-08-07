using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Industrial.Gateway.Host.Controllers;

/// <summary>
/// Browser bootstrap for the Android/iOS PDA Authorization Code + PKCE flow.
/// The verifier never leaves the PDA; this page receives only the PKCE challenge and state.
/// IAM credentials are posted directly to the Gateway's /account/login reverse-proxy route.
/// </summary>
[ApiController]
[Route("pda-auth")]
public sealed class PdaSecurityController : ControllerBase
{
    private const string ClientId = "industrial-pda";
    private const string RedirectUri = "industrial-platform://pda/callback";

    [AllowAnonymous]
    [HttpGet("signin")]
    public IActionResult SignIn([FromQuery] string? state, [FromQuery] string? codeChallenge)
    {
        if (!IsBase64Url(state, 24, 160) || !IsBase64Url(codeChallenge, 32, 160))
            return BadRequest(new { error = "Invalid PDA PKCE state or code challenge." });

        var authorizeUrl = "/connect/authorize"
            + "?client_id=" + Uri.EscapeDataString(ClientId)
            + "&response_type=code"
            + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
            // Native PDA sessions need offline_access so the app can renew its short-lived
            // access token with the refresh-token flow instead of asking operators to sign
            // in again every access-token lifetime. The PKCE verifier still remains local.
            + "&scope=" + Uri.EscapeDataString("openid profile offline_access industrial-platform")
            + "&state=" + Uri.EscapeDataString(state!)
            + "&code_challenge=" + Uri.EscapeDataString(codeChallenge!)
            + "&code_challenge_method=S256";
        var authorizeJson = JsonSerializer.Serialize(authorizeUrl);

        var html = $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover" />
  <title>工业平台 PDA 统一登录</title>
  <style>
    *{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px;font-family:system-ui,-apple-system,"Segoe UI",sans-serif;background:#f3f7fb;color:#102a43}
    .card{width:min(420px,100%);padding:28px;border-radius:18px;background:#fff;box-shadow:0 18px 50px rgba(15,23,42,.12)}
    .eyebrow{font-size:12px;font-weight:700;letter-spacing:.12em;color:#2563eb;text-transform:uppercase}h1{margin:10px 0 8px;font-size:24px}.hint{margin:0 0 22px;color:#64748b;font-size:14px;line-height:1.7}
    label{display:block;margin:14px 0 7px;font-size:13px;font-weight:600}input{width:100%;padding:12px;border:1px solid #cbd5e1;border-radius:10px;font-size:16px;outline:none}input:focus{border-color:#2563eb;box-shadow:0 0 0 3px rgba(37,99,235,.12)}
    button,.continue{display:block;width:100%;margin-top:16px;padding:12px;border:0;border-radius:10px;text-align:center;text-decoration:none;font-weight:700;cursor:pointer}.continue{background:#eff6ff;color:#1d4ed8}button{background:#2563eb;color:#fff}.divider{text-align:center;color:#94a3b8;font-size:12px;margin-top:16px}.error{min-height:22px;margin-top:12px;color:#dc2626;font-size:13px}
  </style>
</head>
<body>
  <main class="card">
    <div class="eyebrow">Industrial IAM</div>
    <h1>PDA 统一身份登录</h1>
    <p class="hint">使用平台 IAM 账号认证。PKCE verifier 仅保存在 PDA 本机，不会发送到此页面。</p>
    <a id="continue" class="continue" href="#">已有 IAM 会话，直接继续</a>
    <div class="divider">或重新验证账号</div>
    <form id="loginForm">
      <label for="userName">IAM 账号</label>
      <input id="userName" autocomplete="username" required />
      <label for="password">IAM 密码</label>
      <input id="password" type="password" autocomplete="current-password" required />
      <label for="tenant">租户（可选）</label>
      <input id="tenant" autocomplete="organization" />
      <button type="submit">登录并返回 PDA</button>
      <div id="error" class="error"></div>
    </form>
  </main>
<script>
const authorizeUrl={{authorizeJson}};
document.getElementById('continue').addEventListener('click',(event)=>{event.preventDefault();window.location.replace(authorizeUrl);});
document.getElementById('loginForm').addEventListener('submit',async(event)=>{
  event.preventDefault();
  const error=document.getElementById('error');error.textContent='';
  try{
    const response=await fetch('/account/login',{
      method:'POST',credentials:'include',headers:{'Content-Type':'application/json'},
      body:JSON.stringify({
        userName:document.getElementById('userName').value.trim(),
        password:document.getElementById('password').value,
        tenant:document.getElementById('tenant').value.trim()||null
      })
    });
    if(!response.ok){error.textContent='IAM 账号或密码错误，或账号已被禁用。';return;}
    window.location.replace(authorizeUrl);
  }catch(e){error.textContent='无法连接统一身份服务，请检查网络后重试。';}
});
</script>
</body>
</html>
""";
        return Content(html, "text/html; charset=utf-8");
    }

    private static bool IsBase64Url(string? value, int minLength, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < minLength || value.Length > maxLength)
            return false;
        foreach (var ch in value)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '-' or '_'))
                return false;
        }
        return true;
    }
}
