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
            toggle.setAttribute('aria-label', toggle.dataset.openLabel || 'Open navigation menu');
            if (returnFocus) toggle.focus();
        }

        function onToggle() {
            const opening = toggle.getAttribute('aria-expanded') !== 'true';
            panel.classList.toggle('is-open', opening);
            toggle.setAttribute('aria-expanded', String(opening));
            toggle.setAttribute('aria-label', opening ? (toggle.dataset.closeLabel || 'Close navigation menu') : (toggle.dataset.openLabel || 'Open navigation menu'));
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

        let navigationFrame = null;
        function updateNavigation() {
            navigationFrame = null;
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
                const target = new URL(link.href, window.location.origin);
                const active = sections.length ? target.pathname === window.location.pathname && target.hash === '#' + current : !target.hash && (window.location.pathname === target.pathname || window.location.pathname.startsWith(target.pathname + '/'));
                link.classList.toggle('active', active);
                if (active) link.setAttribute('aria-current', 'location');
                else link.removeAttribute('aria-current');
            });
        }

        function onScroll() {
            if (navigationFrame === null) {
                navigationFrame = window.requestAnimationFrame(updateNavigation);
            }
        }

        function onLinkClick() { closeMenu(); }

        toggle.addEventListener('click', onToggle);
        document.addEventListener('click', onDocumentClick);
        document.addEventListener('keydown', onKeydown);
        window.addEventListener('resize', onResize);
        window.addEventListener('scroll', onScroll, { passive: true });
        links.forEach(link => link.addEventListener('click', onLinkClick));
        updateNavigation();

        let revealObserver = null;
        let flowObserver = null;
        const motionPreference = window.matchMedia('(prefers-reduced-motion: reduce)');
        const reveals = [...document.querySelectorAll('.reveal')];
        const flows = [...document.querySelectorAll('.request-flow')];

        function configureMotion() {
            revealObserver?.disconnect();
            flowObserver?.disconnect();
            document.documentElement.classList.remove('js-enabled');
            document.documentElement.classList.toggle('motion-enabled', !motionPreference.matches);

            if (motionPreference.matches || !('IntersectionObserver' in window)) {
                reveals.forEach(el => el.classList.add('is-visible'));
                flows.forEach(el => el.classList.remove('is-flowing', 'is-in-view'));
                return;
            }

            revealObserver = new IntersectionObserver(entries => {
                // Stagger only siblings arriving together, so isolated cards never wait.
                const groupOffsets = new Map();
                const arriving = new Set(entries.filter(entry => entry.isIntersecting).map(entry => entry.target));
                reveals.forEach(el => {
                    if (!arriving.has(el)) return;
                    const group = el.parentElement;
                    let delay = 0;
                    if (group.matches('.projects-grid, .skills-grid')) {
                        const index = groupOffsets.get(group) || 0;
                        delay = Math.min(index, 3) * 75;
                        groupOffsets.set(group, index + 1);
                    }
                    el.style.setProperty('--reveal-delay', delay + 'ms');
                    el.classList.add('is-visible');
                    revealObserver.unobserve(el);
                });
            }, { rootMargin: '0px 0px -45px 0px', threshold: .05 });

            flowObserver = new IntersectionObserver(entries => {
                entries.forEach(entry => {
                    // One finite sequence; pause it while the diagram is off screen.
                    entry.target.classList.toggle('is-in-view', entry.isIntersecting);
                    if (entry.isIntersecting) entry.target.classList.add('is-flowing');
                });
            }, { threshold: .35 });

            document.documentElement.classList.add('js-enabled');
            reveals.filter(el => !el.classList.contains('is-visible')).forEach(el => revealObserver.observe(el));
            flows.forEach(el => flowObserver.observe(el));
        }

        configureMotion();
        motionPreference.addEventListener('change', configureMotion);

        cleanup = () => {
            toggle.removeEventListener('click', onToggle);
            document.removeEventListener('click', onDocumentClick);
            document.removeEventListener('keydown', onKeydown);
            window.removeEventListener('resize', onResize);
            window.removeEventListener('scroll', onScroll);
            links.forEach(link => link.removeEventListener('click', onLinkClick));
            if (navigationFrame !== null) window.cancelAnimationFrame(navigationFrame);
            revealObserver?.disconnect();
            flowObserver?.disconnect();
            motionPreference.removeEventListener('change', configureMotion);
        };
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initializePortfolio, { once: true });
    else initializePortfolio();
    document.addEventListener('enhancedload', initializePortfolio);
})();
