(function () {
    const prefix = 'portfolio-blog-draft:';
    const query = new URLSearchParams(location.search);
    try {
        if (query.get('saved') === '1' && query.has('draft')) localStorage.removeItem(prefix + query.get('draft'));
    } catch (_) { }
    const form = document.querySelector('[data-editor-id]');
    if (!form) return;
    const key = prefix + form.dataset.editorId;
    const fields = [...form.querySelectorAll('[name^="Input."]')];
    const notice = form.querySelector('.local-draft-notice');
    const status = form.querySelector('[data-draft-status]');
    let saved = null;
    try { saved = JSON.parse(localStorage.getItem(key)); } catch (_) { }
    const hasDifference = saved && fields.some(field => typeof saved[field.name] === 'string' && saved[field.name] !== field.value);
    if (hasDifference) notice.hidden = false;
    form.querySelector('[data-restore-draft]').addEventListener('click', () => {
        fields.forEach(field => { if (typeof saved?.[field.name] === 'string') field.value = saved[field.name]; });
        notice.hidden = true;
        updateDirection();
    });
    form.querySelector('[data-discard-draft]').addEventListener('click', () => {
        try { localStorage.removeItem(key); } catch (_) { }
        notice.hidden = true;
    });
    function persist() {
        try {
            localStorage.setItem(key, JSON.stringify(Object.fromEntries(fields.map(field => [field.name, field.value]))));
            status.textContent = 'نسخهٔ موقت در مرورگر ذخیره شد. برای ثبت مقاله در بلاگ، «ذخیرهٔ پیش‌نویس» یا «انتشار مقاله» را بزن.';
        } catch (_) { status.textContent = 'مرورگر نمی‌تواند نسخهٔ موقت را ذخیره کند. از «ذخیرهٔ پیش‌نویس» استفاده کن.'; }
    }
    let timer;
    form.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(persist, 500); });
    form.addEventListener('submit', persist);
    function updateDirection() {
        form.querySelector('[name="Input.ContentMarkdown"]').dir = form.querySelector('[name="Input.Language"]').value === 'en' ? 'ltr' : 'rtl';
    }
    form.querySelector('[name="Input.Language"]').addEventListener('change', updateDirection);
})();
