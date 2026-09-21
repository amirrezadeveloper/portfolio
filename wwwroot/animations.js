(function () {
    let cleanup = null;

    function initializePortfolio() {
        if (cleanup) cleanup();
        const header = document.getElementById('site-header');
        const toggle = document.querySelector('.menu-toggle');
        const panel = document.getElementById('site-menu');
        const links = [...document.querySelectorAll('.nav-link, .nav-contact')];
        const sections = [...document.querySelectorAll('main section[id]')];
        if (!header || !toggle || !panel) return;

        function closeMenu(returnFocus = false) {
            panel.classList.remove('is-open');
            toggle.setAttribute('aria-expanded', 'false');
            toggle.setAttribute('aria-label', 'Open navigation menu');
            if (returnFocus) toggle.focus();
        }

        function onToggle() {
            const opening = toggle.getAttribute('aria-expanded') !== 'true';
            panel.classList.toggle('is-open', opening);
            toggle.setAttribute('aria-expanded', String(opening));
            toggle.setAttribute('aria-label', opening ? 'Close navigation menu' : 'Open navigation menu');
        }

        function onDocumentClick(event) {
            if (!header.contains(event.target)) closeMenu();
        }

        function onKeydown(event) {
            if (event.key === 'Escape' && panel.classList.contains('is-open')) closeMenu(true);
        }

        function onResize() {
            if (window.innerWidth > 850) closeMenu();
        }

        let ticking = false;
        function updateNavigation() {
            ticking = false;
            header.classList.toggle('is-scrolled', window.scrollY > 12);
            const marker = window.scrollY + header.offsetHeight + Math.min(window.innerHeight * .28, 190);
            let current = sections[0]?.id || 'home';
            for (const section of sections) {
                if (section.offsetTop <= marker) current = section.id;
            }
            if (window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 8) {
                current = sections.at(-1)?.id || current;
            }
            links.forEach(link => {
                const active = link.getAttribute('href') === '#' + current;
                link.classList.toggle('active', active);
                if (active) link.setAttribute('aria-current', 'location');
                else link.removeAttribute('aria-current');
            });
        }

        function onScroll() {
            if (!ticking) {
                ticking = true;
                window.requestAnimationFrame(updateNavigation);
            }
        }

        toggle.addEventListener('click', onToggle);
        document.addEventListener('click', onDocumentClick);
        document.addEventListener('keydown', onKeydown);
        window.addEventListener('resize', onResize);
        window.addEventListener('scroll', onScroll, { passive: true });
        links.forEach(link => link.addEventListener('click', () => closeMenu()));
        updateNavigation();

        let revealObserver = null;
        const reveals = document.querySelectorAll('.reveal');
        if ('IntersectionObserver' in window && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
            document.documentElement.classList.add('js-enabled');
            revealObserver = new IntersectionObserver(entries => {
                entries.forEach(entry => {
                    if (entry.isIntersecting) {
                        entry.target.classList.add('is-visible');
                        revealObserver.unobserve(entry.target);
                    }
                });
            }, { rootMargin: '0px 0px -45px 0px', threshold: .05 });
            reveals.forEach(el => revealObserver.observe(el));
        } else {
            document.documentElement.classList.remove('js-enabled');
        }

        cleanup = () => {
            toggle.removeEventListener('click', onToggle);
            document.removeEventListener('click', onDocumentClick);
            document.removeEventListener('keydown', onKeydown);
            window.removeEventListener('resize', onResize);
            window.removeEventListener('scroll', onScroll);
            revealObserver?.disconnect();
        };
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initializePortfolio, { once: true });
    else initializePortfolio();
    document.addEventListener('enhancedload', initializePortfolio);
})();
