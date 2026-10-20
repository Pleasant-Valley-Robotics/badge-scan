// The kiosk: a clock, a box the badge scanner types into, and the last few scans.
//
// The scanner is a keyboard as far as the browser knows: it types the badge number and
// presses Enter. So the box is a plain text input, visible on purpose: whoever is watching
// sees the number arrive and the answer underneath, which is how they know the scan worked.
(() => {
  const section = document.querySelector('.kiosk');
  const team = section.dataset.team ? Number(section.dataset.team) : null;
  const input = document.getElementById('badge');
  const banner = document.getElementById('banner');
  const recent = document.getElementById('recent');
  const live = document.getElementById('live');
  const seen = new Set();
  let busy = false;
  let hideTimer = null;

  // ---- the clock, on the team's wall clock (Central) whatever this laptop is set to ----
  const clock = document.getElementById('clock');
  const date = document.getElementById('date');
  const tick = () => {
    const now = new Date();
    clock.textContent = now.toLocaleTimeString('en-US', { timeZone: 'America/Chicago', hour: 'numeric', minute: '2-digit' });
    date.textContent = now.toLocaleDateString('en-US', { timeZone: 'America/Chicago', weekday: 'long', month: 'long', day: 'numeric' });
  };
  tick();
  setInterval(tick, 1000);

  // ---- focus never wanders: nobody is using this screen for anything else ----
  const focus = () => input.focus({ preventScroll: true });
  focus();
  input.addEventListener('blur', () => setTimeout(focus, 0));
  setInterval(() => { if (document.activeElement !== input) focus(); }, 2000);

  // ---- one scan ----
  const show = (tone, text) => {
    banner.className = `kiosk-banner is-${tone}`;
    banner.textContent = text;
    banner.hidden = false;
    clearTimeout(hideTimer);
    hideTimer = setTimeout(() => { banner.hidden = true; }, 4000);
  };

  const submit = async () => {
    const badge = input.value.trim();
    input.value = '';
    if (!badge || busy) return;
    busy = true;
    input.placeholder = 'Saving…';
    try {
      const res = await fetch('/api/scans', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ badge, team }),
      });
      const body = await res.json().catch(() => ({}));
      if (!res.ok) {
        show('error', body.error || `Could not save the scan (${res.status})`);
      } else if (body.already) {
        show('again', `${body.name} already scanned in at ${body.firstStamp || body.stamp}`);
        add(body);
      } else {
        show('ok', `✓ ${body.name} — ${body.stamp}`);
        add(body);
      }
    } catch (err) {
      show('error', 'Could not reach the server: ' + err.message);
    } finally {
      busy = false;
      input.placeholder = 'Scan a badge, or type its number and press Enter';
      focus();
    }
  };
  input.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); submit(); } });

  // ---- the recent list: the last ten, newest at the top ----
  const add = (scan, append = false) => {
    if (seen.has(scan.id)) return;
    seen.add(scan.id);
    const li = document.createElement('li');
    const name = document.createElement('span');
    name.className = 'who';
    name.textContent = scan.recognised ? scan.name : `Badge ${scan.badge} not recognised`;
    const when = document.createElement('span');
    when.className = 'when' + (scan.already ? ' is-again' : '') + (scan.recognised ? '' : ' is-error');
    when.textContent = (scan.already ? 'again · ' : '') + scan.stamp;
    li.append(name, when);
    if (append) recent.append(li); else recent.prepend(li);
    while (recent.children.length > 10) recent.lastChild.remove();
  };

  const load = async () => {
    try {
      const url = '/api/scans?limit=10' + (team ? `&team=${team}` : '');
      const res = await fetch(url);
      if (!res.ok) return;
      const body = await res.json();
      recent.replaceChildren();
      seen.clear();
      for (const scan of body.scans) add(scan, true);
    } catch { /* the stream will fill it in */ }
  };

  // ---- live updates: the server pushes every new scan ----
  const stream = new EventSource('/api/scans/stream');
  stream.addEventListener('scan', (e) => {
    const scan = JSON.parse(e.data);
    if (team && scan.team && scan.team !== team) return;
    add(scan);
  });
  stream.addEventListener('open', () => { live.hidden = true; load(); });
  stream.addEventListener('error', () => { live.hidden = false; });
})();
