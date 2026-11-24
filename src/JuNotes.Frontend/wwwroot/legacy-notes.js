// The filter box of the notes list (moved out of the page: the content policy allows no inline script).
const filter = document.getElementById('filter');
filter.addEventListener('input', () => {
    const q = filter.value.trim().toLowerCase();
    document.querySelectorAll('.book').forEach(b => b.style.display = !q || b.dataset.title.includes(q) ? '' : 'none');
    document.querySelectorAll('.subject').forEach(s => s.style.display = [...s.querySelectorAll('.book')].some(b => b.style.display !== 'none') ? '' : 'none');
});
