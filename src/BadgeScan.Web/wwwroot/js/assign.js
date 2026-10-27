// Assigning a badge at the kiosk: ask for a new person's name and team (or role) only when
// "Someone new" is picked, and don't let the scanner's Enter submit the form early.
(() => {
  const person = document.getElementById('person');
  const fresh = document.getElementById('new-person');
  const sync = () => { fresh.hidden = person.value !== ''; };
  person.addEventListener('change', sync);
  sync();

  // "Another team…" asks for its number.
  const group = document.getElementById('group');
  const other = document.getElementById('other-team');
  const syncOther = () => { other.hidden = group.value !== 'other'; };
  group.addEventListener('change', syncOther);
  syncOther();

  // A badge scanner types the number and presses Enter: move on to "Who it's for" instead.
  document.getElementById('badge').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); person.focus(); }
  });
})();
