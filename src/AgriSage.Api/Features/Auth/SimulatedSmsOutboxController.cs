using AgriSage.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Auth;

// Test environment only: 404 unless the API runs with MessageDelivery:SmsProvider=Simulated (which Program allows only in
// Development). Anonymous on purpose: a person who is resetting a password cannot sign in to read the code. It shows the
// messages the API would have sent as real SMS, so a demo costs nothing.[ApiController]
[Route("api/dev/sms-outbox")]
public sealed class SimulatedSmsOutboxController(ISimulatedSmsOutbox? outbox = null) : ControllerBase
{
    // [AllowAnonymous] sits on each action: the permission map recognises it there as an explicit exception.
    [HttpGet, AllowAnonymous]
    public IActionResult List() => outbox is null ? NotFound() : Ok(outbox.Recent());

    // A small self-refreshing page that looks like a phone's inbox, to put on a second screen during a demo.
    [HttpGet("view"), AllowAnonymous]
    public IActionResult View() => outbox is null ? NotFound() : Content(Page, "text/html; charset=utf-8");

    private const string Page = """
        <!doctype html>
        <html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Hộp thư SMS giả lập - AgriSage</title>
        <style>
          body{font-family:system-ui,"Segoe UI",sans-serif;background:#f3f4f1;margin:0;padding:16px;color:#1c2a1f}
          main{max-width:560px;margin:0 auto}
          h1{font-size:18px;margin:0 0 4px}
          .note{color:#5b6b5f;font-size:13px;margin:0 0 16px}
          .msg{background:#fff;border:1px solid #d9dfd6;border-radius:12px;padding:12px 14px;margin-bottom:10px}
          .meta{display:flex;justify-content:space-between;gap:8px;font-size:12px;color:#5b6b5f;margin-bottom:6px}
          .code{font-size:28px;letter-spacing:6px;font-weight:700;color:#1f6f3d}
          .text{font-size:13px;color:#3b4a3f;margin-top:4px;word-break:break-word}
          .empty{color:#5b6b5f;text-align:center;padding:32px 0}
        </style></head>
        <body><main>
          <h1>Hộp thư SMS giả lập</h1>
          <p class="note">Chế độ giả lập: không có tin nhắn thật nào được gửi và không tốn phí. Trang tự làm mới mỗi 2 giây.</p>
          <div id="list"></div>
        </main>
        <script>
          const purposes = {PasswordReset: 'Đặt lại mật khẩu', PhoneVerification: 'Xác minh số điện thoại', EmailVerification: 'Xác minh email'};
          function el(tag, cls, text) { const e = document.createElement(tag); if (cls) e.className = cls; if (text !== undefined) e.textContent = text; return e; }
          async function load() {
            try {
              const items = await (await fetch('/api/dev/sms-outbox', {cache: 'no-store'})).json();
              const list = document.getElementById('list');
              list.replaceChildren();
              if (!items.length) { list.append(el('div', 'empty', 'Chưa có tin nhắn nào.')); return; }
              for (const m of items) {
                const box = el('div', 'msg');
                const meta = el('div', 'meta');
                meta.append(el('span', '', 'Tới ' + m.destination + ' · ' + (purposes[m.purpose] || m.purpose)),
                            el('span', '', new Date(m.at).toLocaleTimeString('vi-VN')));
                box.append(meta, el('div', 'code', m.code), el('div', 'text', m.text));
                list.append(box);
              }
            } catch (e) { /* the API is restarting: try again on the next tick */ }
          }
          load(); setInterval(load, 2000);
        </script></body></html>
        """;
}
