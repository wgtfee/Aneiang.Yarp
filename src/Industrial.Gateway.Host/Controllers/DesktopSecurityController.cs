using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Industrial.Gateway.Host.Controllers;

/// <summary>
/// Browser bootstrap for the WCS native Desktop Authorization Code + PKCE flow.
/// The verifier stays in the desktop process; the browser only receives its challenge.
/// The fixed loopback callback is registered as an OpenIddict public-client redirect URI.
/// </summary>
[ApiController]
[Route("desktop-auth")]
public sealed class DesktopSecurityController : ControllerBase
{
    private const string ClientId = "industrial-desktop";
    private const string RedirectUri = "http://127.0.0.1:5210/callback";

    [AllowAnonymous]
    [HttpGet("signin")]
    public IActionResult SignIn([FromQuery] string? state, [FromQuery] string? codeChallenge)
    {
        if (!IsBase64Url(state, 24, 160) || !IsBase64Url(codeChallenge, 32, 160))
            return BadRequest(new { error = "Invalid Desktop PKCE state or code challenge." });

        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";

        var authorizeUrl = "/connect/authorize"
            + "?client_id=" + Uri.EscapeDataString(ClientId)
            + "&response_type=code"
            + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
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
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <title>WCS 统一身份登录</title>
  <style>
    *{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px;font-family:system-ui,-apple-system,"Segoe UI",sans-serif;background:#f3f7fb;color:#102a43}
    .card{width:min(430px,100%);padding:30px;border-radius:18px;background:#fff;box-shadow:0 18px 50px rgba(15,23,42,.12)}
    .eyebrow{font-size:12px;font-weight:700;letter-spacing:.12em;color:#2563eb}h1{margin:10px 0 8px;font-size:24px}.hint{margin:0 0 22px;color:#64748b;font-size:14px;line-height:1.7}
    label{display:block;margin:14px 0 7px;font-size:13px;font-weight:600}input{width:100%;padding:12px;border:1px solid #cbd5e1;border-radius:10px;font-size:16px;outline:none}input:focus{border-color:#2563eb;box-shadow:0 0 0 3px rgba(37,99,235,.12)}
    button{display:block;width:100%;margin-top:18px;padding:12px;border:0;border-radius:10px;font-weight:700;cursor:pointer;background:#2563eb;color:#fff}.error{min-height:22px;margin-top:12px;color:#dc2626;font-size:13px}
  </style>
</head>
<body>
  <main class="card">
    <div class="eyebrow">INDUSTRIAL IAM</div>
    <h1>WCS 统一身份登录</h1>
    <p class="hint">请使用 IAM 账号登录。授权完成后浏览器会安全地返回 WCS Desktop。</p>
    <form id="loginForm">
      <label for="userName">IAM 账号</label><input id="userName" autocomplete="username" required />
      <label for="password">IAM 密码</label><input id="password" type="password" autocomplete="current-password" required />
      <label for="tenant">租户（可选）</label><input id="tenant" autocomplete="organization" />
      <button type="submit">登录并打开 WCS</button><div id="error" class="error"></div>
    </form>
  </main>
<script>
const authorizeUrl={{authorizeJson}};
document.getElementById('loginForm').addEventListener('submit',async(event)=>{
  event.preventDefault();const error=document.getElementById('error');error.textContent='';
  try{
    try{await fetch('/account/logout',{method:'POST',credentials:'include'});}catch(e){}
    const response=await fetch('/account/login',{method:'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:JSON.stringify({
      userName:document.getElementById('userName').value.trim(),password:document.getElementById('password').value,tenant:document.getElementById('tenant').value.trim()||null
    })});
    if(!response.ok){error.textContent='IAM 账号或密码错误，或账号已被禁用。';return;}
    window.location.replace(authorizeUrl);
  }catch(e){error.textContent='无法连接统一身份服务，请检查 Gateway 与 IAM。';}
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
        return value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
    }
}
