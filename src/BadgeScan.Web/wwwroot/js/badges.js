// The Badges page: assign, change, clear and add, each one call to /api/badges.
(() => {
  const section = document.querySelector('.badges');
  const defaultTeam = Number(section.dataset.defaultTeam);
  const table = document.getElementById('people');
  const message = document.getElementById('message');

  const say = (ok, text) => {
    message.className = `notice ${ok ? 'notice--ok' : 'notice--warn'}`;
    message.textContent = text;
    message.hidden = false;
  };

  const api = async (method, url, body) => {
    const res = await fetch(url, {
      method,
      headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const data = res.status === 204 ? {} : await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || `The server answered ${res.status}`);
    return data;
  };

  // Redraw one row after a change.
  const render = (row, badge) => {
    row.dataset.badge = badge.badgeNumber || '';
    const cell = row.querySelector('.badge');
    cell.replaceChildren();
    if (badge.badgeNumber) {
      const code = document.createElement('code');
      code.textContent = badge.badgeNumber;
      cell.append(code);
    } else {
      const span = document.createElement('span');
      span.className = 'muted';
      span.textContent = 'no badge';
      cell.append(span);
    }
    const actions = row.querySelector('.actions');
    actions.replaceChildren();
    const assign = document.createElement('button');
    assign.type = 'button';
    assign.className = 'btn btn--ghost btn--sm assign';
    assign.textContent = badge.badgeNumber ? 'Change' : 'Assign';
    actions.append(assign);
    if (badge.badgeNumber) {
      const clear = document.createElement('button');
      clear.type = 'button';
      clear.className = 'btn-link clear';
      clear.textContent = 'Clear';
      actions.append(clear);
    }
    count();
  };

  const count = () => {
    const rows = [...table.tBodies[0].rows];
    const without = rows.filter(r => !r.dataset.badge).length;
    document.getElementById('count').textContent = `${without} of ${rows.length} without a badge.`;
  };

  // Assign: an input appears in the row; scan or type, Enter saves, Escape cancels.
  const openAssign = (row) => {
    document.querySelectorAll('.assign-box').forEach(b => b.remove());
    const box = document.createElement('div');
    box.className = 'assign-box';
    const input = document.createElement('input');
    input.placeholder = 'Scan badge now';
    input.autocomplete = 'off';
    input.spellcheck = false;
    box.append(input);
    row.querySelector('.badge').append(box);
    input.focus();
    input.addEventListener('keydown', async (e) => {
      if (e.key === 'Escape') { box.remove(); return; }
      if (e.key !== 'Enter') return;
      e.preventDefault();
      const value = input.value.trim();
      if (!value) return;
      try {
        const saved = await api('PUT', `/api/badges/${row.dataset.id}`, { badge: value });
        render(row, saved);
        say(true, `${saved.name} → badge ${saved.badgeNumber}`);
      } catch (err) {
        say(false, err.message);
      }
    });
  };

  table.addEventListener('click', async (e) => {
    const row = e.target.closest('tr');
    if (!row) return;
    if (e.target.classList.contains('assign')) openAssign(row);
    if (e.target.classList.contains('clear')) {
      try {
        const saved = await api('PUT', `/api/badges/${row.dataset.id}`, { badge: null });
        render(row, saved);
        say(true, `Cleared ${saved.name}'s badge`);
      } catch (err) {
        say(false, err.message);
      }
    }
  });

  // The team box takes a team number, or Mentor, Coach or Advisor.
  const group = (text) => {
    const t = String(text || '').trim().replace(/^team\s+/i, '');
    if (!t) return { team: defaultTeam };
    return /^\d+$/.test(t) ? { team: Number(t) } : { role: t };
  };

  // Add a person. The page reloads so the new row lands in alphabetical order.
  document.getElementById('add').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = new FormData(e.target);
    try {
      const saved = await api('POST', '/api/badges', {
        name: form.get('name'),
        ...group(form.get('team')),
        badge: form.get('badge') || null,
      });
      sessionStorage.setItem('badges-message', `Added ${saved.name}${saved.badgeNumber ? ` with badge ${saved.badgeNumber}` : ''}`);
      location.reload();
    } catch (err) {
      say(false, err.message);
    }
  });
  const pending = sessionStorage.getItem('badges-message');
  if (pending) { sessionStorage.removeItem('badges-message'); say(true, pending); }
})();
