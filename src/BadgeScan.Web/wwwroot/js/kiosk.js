// The kiosk: a clock, a box the badge scanner types into, what the last scan was, and the
// last few scans. Every team scans in at the same kiosk.
//
// The scanner is a keyboard as far as the browser knows: it types the badge number and
// presses Enter. So the box is a plain text input, visible on purpose: whoever is watching
// sees the number arrive and the answer underneath, which is how they know the scan worked.
(() => {
  const section = document.querySelector('.kiosk');
  const endsAt = Number(section.dataset.ends);
  const input = document.getElementById('badge');
  const banner = document.getElementById('banner');
  const result = document.getElementById('result');
  const who = document.getElementById('who');
  const facts = document.getElementById('facts');
  const when = document.getElementById('when');
  const assign = document.getElementById('assign');
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

  // ---- the meeting ends: reloading shows the sign-in page ----
  // (The server decides; this just doesn't leave a dead kiosk on screen.)
  const ended = () => window.location.reload();
  setTimeout(ended, Math.max(0, endsAt - Date.now()) + 2000);

  // ---- focus never wanders: nobody is using this screen for anything else ----
  const focus = () => input.focus({ preventScroll: true });
  focus();
  input.addEventListener('blur', () => setTimeout(focus, 0));
  setInterval(() => { if (document.activeElement !== input) focus(); }, 2000);

  // ---- one scan: what happened, who, their team and badge, and when ----
  // unknownBadge: offer "Assign this badge" (a lead signs it off) and leave the message up longer.
  const show = (tone, heading, name = '', detail = '', time = '', unknownBadge = null) => {
    banner.className = `kiosk-banner is-${tone}`;
    result.textContent = heading;
    who.textContent = name;
    facts.textContent = detail;
    when.textContent = time;
    assign.hidden = !unknownBadge;
    if (unknownBadge) assign.href = `/kiosk/assign?badge=${encodeURIComponent(unknownBadge)}`;
    banner.hidden = false;
    clearTimeout(hideTimer);
    hideTimer = setTimeout(() => { banner.hidden = true; }, unknownBadge ? 20000 : 6000);
  };
  // "17:05" from a stamp like Th10/08/2026|17:05, as "5:05 PM".
  const twelveHour = (stamp) => {
    const [h, m] = stamp.slice(-5).split(':').map(Number);
    return `${h % 12 || 12}:${String(m).padStart(2, '0')} ${h < 12 ? 'AM' : 'PM'}`;
  };
  const showScan = (scan) => {
    const detail = `${scan.group} · Badge ${scan.badge}`;
    const time = `${scan.dateText} · ${scan.timeText}`;
    if (scan.already) show('again', scan.firstStamp ? `Already checked in at ${twelveHour(scan.firstStamp)}` : 'Already checked in today', scan.name, detail, time);
    else show('ok', '✓ Checked in', scan.name, detail, time);
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
        body: JSON.stringify({ badge }),
      });
      if (res.status === 401) { ended(); return; }
      const body = await res.json().catch(() => ({}));
      if (!res.ok) {
        show('error', body.error || `Could not save the scan (${res.status})`, '', body.badge ? `Badge ${body.badge}` : '', '',
          res.status === 404 && body.badge ? body.badge : null);
      } else {
        showScan(body);
        add(body);
      }
    } catch (err) {
      show('error', 'Could not reach the server', '', err.message);
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
    const badge = document.createElement('span');
    badge.className = 'badge-no';
    badge.textContent = scan.recognised ? `${scan.team ?? scan.group} · #${scan.badge}` : '';
    const at = document.createElement('span');
    at.className = 'when' + (scan.already ? ' is-again' : '') + (scan.recognised ? '' : ' is-error');
    at.textContent = (scan.already ? 'again · ' : '') + scan.stamp;
    li.append(name, badge, at);
    if (append) recent.append(li); else recent.prepend(li);
    while (recent.children.length > 10) recent.lastChild.remove();
  };

  const load = async () => {
    try {
      const res = await fetch('/api/scans?limit=10');
      if (res.status === 401) { ended(); return; }
      if (!res.ok) return;
      const body = await res.json();
      recent.replaceChildren();
      seen.clear();
      for (const scan of body.scans) add(scan, true);
    } catch { /* the stream will fill it in */ }
  };

  // ---- live updates: the list now, then the server pushes every new scan ----
  load();
  const stream = new EventSource('/api/scans/stream');
  stream.addEventListener('scan', (e) => {
    add(JSON.parse(e.data));
  });
  stream.addEventListener('open', () => { live.hidden = true; load(); });
  // A stream that drops may mean the meeting ended; load() finds out and reloads if so.
  stream.addEventListener('error', () => { live.hidden = false; load(); });
})();
