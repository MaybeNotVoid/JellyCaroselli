(function () {
    'use strict';

    const ROOT_ATTR = 'data-seerr-home-carousel';
    const TARGET_DAILY_COUNT = 20;
    const STATUS_LABELS = {
        2: { text: 'In attesa', cls: 'shc-badge-pending' },
        3: { text: 'Richiesto', cls: 'shc-badge-processing' },
        4: { text: 'Parziale', cls: 'shc-badge-partial' },
        5: { text: 'Disponibile', cls: 'shc-badge-available' }
    };

    let isRendering = false;
    let renderQueued = false;
    let observer = null;

    function isHomeRoute() {
        const hash = (window.location.hash || '').toLowerCase();
        if (hash.indexOf('seerrfintab=') !== -1) {
            return false;
        }
        return hash === '' || hash === '#' || hash === '#/' || hash.indexOf('#/home') === 0;
    }

    function findHomeSectionsContainer() {
        const activeHomeTab = document.querySelector('#homeTab:not(.hide) .homeSectionsContainer');
        if (activeHomeTab) {
            return activeHomeTab;
        }
        const activePage = document.querySelector('.page:not(.hide) .homeSectionsContainer');
        if (activePage) {
            return activePage;
        }
        return document.querySelector('#homeTab .homeSectionsContainer');
    }

    function getTodayDateString() {
        const now = new Date();
        const yyyy = now.getFullYear();
        const mm = String(now.getMonth() + 1).padStart(2, '0');
        const dd = String(now.getDate()).padStart(2, '0');
        return yyyy + '-' + mm + '-' + dd;
    }

    function getDaySeed(mediaType) {
        const key = getTodayDateString() + ':' + mediaType;
        let h = 2166136261 >>> 0;
        for (let i = 0; i < key.length; i++) {
            h ^= key.charCodeAt(i);
            h = Math.imul(h, 16777619);
        }
        return h >>> 0;
    }

    function createSeededRandom(seed) {
        let a = seed >>> 0;
        return function () {
            a |= 0;
            a = (a + 0x6D2B79F5) | 0;
            let t = Math.imul(a ^ (a >>> 15), 1 | a);
            t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
            return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
        };
    }

    function deterministicShuffle(items, seed) {
        const arr = items.slice();
        const rand = createSeededRandom(seed);
        for (let i = arr.length - 1; i > 0; i--) {
            const j = Math.floor(rand() * (i + 1));
            const tmp = arr[i];
            arr[i] = arr[j];
            arr[j] = tmp;
        }
        return arr;
    }

    function getProviderId(item, key) {
        if (window.seerrFinPlugin && typeof window.seerrFinPlugin.getProviderId === 'function') {
            return window.seerrFinPlugin.getProviderId(item, key);
        }
        const ids = item && (item.ProviderIds || item.providerIds);
        return ids ? (ids[key] || ids[key.toLowerCase()] || null) : null;
    }

    function extractReleaseDate(item) {
        if (!item) {
            return null;
        }
        const rawDate = item.PremiereDate || item.premiereDate || item.ReleaseDate || item.releaseDate || item.FirstAirDate || item.firstAirDate;
        if (rawDate) {
            const str = String(rawDate);
            if (str.indexOf('1970-01-01') === 0 && !item.ProductionYear) {
                return null;
            }
            const parsed = new Date(str);
            if (!isNaN(parsed.getTime())) {
                return parsed;
            }
        }
        const yr = item.ProductionYear || item.productionYear;
        if (yr && Number(yr) > 1900) {
            return new Date(Number(yr), 0, 1);
        }
        return null;
    }

    function extractYear(item) {
        if (item && (item.ProductionYear || item.productionYear)) {
            return String(item.ProductionYear || item.productionYear);
        }
        const dt = extractReleaseDate(item);
        if (dt) {
            return String(dt.getFullYear());
        }
        return '';
    }

    function isAlreadyReleased(item) {
        const now = new Date();
        const currentYear = now.getFullYear();
        const dt = extractReleaseDate(item);
        if (!dt) {
            return false;
        }
        if (dt.getFullYear() > currentYear) {
            return false;
        }
        if (dt.getTime() > now.getTime()) {
            return false;
        }
        return true;
    }

    function getPosterCandidates(item, mediaType, tmdbId, hasTmdbKey) {
        const urls = [];
        function addUrl(u) {
            if (u && typeof u === 'string' && urls.indexOf(u) === -1) {
                urls.push(u);
            }
        }

        if (hasTmdbKey && tmdbId && /^\d+$/.test(String(tmdbId))) {
            addUrl(ApiClient.getUrl('SeerrHomeCarousel/poster/' + mediaType + '/' + tmdbId));
        }

        const tmdbPath = getProviderId(item, 'TmdbPosterPath');
        if (tmdbPath) {
            const norm = tmdbPath.charAt(0) === '/' ? tmdbPath : '/' + tmdbPath;
            addUrl('https://image.tmdb.org/t/p/w600_and_h900_bestv2' + norm);
        }

        if (window.seerrFinPlugin && typeof window.seerrFinPlugin.buildDiscoverPosterUrl === 'function') {
            addUrl(window.seerrFinPlugin.buildDiscoverPosterUrl(item));
        }

        let seerrPoster = getProviderId(item, 'JellyseerrPoster');
        if (seerrPoster) {
            if (seerrPoster.indexOf('http') !== 0) {
                seerrPoster = ApiClient.getUrl(seerrPoster.replace(/^\//, ''));
            }
            addUrl(seerrPoster);
        }

        const backdropPath = getProviderId(item, 'TmdbBackdropPath');
        if (backdropPath) {
            const normB = backdropPath.charAt(0) === '/' ? backdropPath : '/' + backdropPath;
            addUrl('https://image.tmdb.org/t/p/w780' + normB);
        }

        if (!hasTmdbKey && tmdbId && /^\d+$/.test(String(tmdbId))) {
            addUrl(ApiClient.getUrl('SeerrHomeCarousel/poster/' + mediaType + '/' + tmdbId));
        }

        return urls;
    }

    function getStatusInfo(item) {
        const raw = getProviderId(item, 'JellyseerrMediaStatus') || getProviderId(item, 'SeerrStatus');
        const code = raw ? parseInt(raw, 10) : 0;
        return {
            code: code,
            badge: STATUS_LABELS[code] || null
        };
    }

    function fetchJson(path) {
        return ApiClient.ajax({
            url: ApiClient.getUrl(path),
            type: 'GET',
            dataType: 'json'
        });
    }

    function extractItemsList(res) {
        if (!res) {
            return [];
        }
        if (Array.isArray(res.Items)) {
            return res.Items;
        }
        if (Array.isArray(res.items)) {
            return res.items;
        }
        if (Array.isArray(res)) {
            return res;
        }
        return [];
    }

    function fetchReleasedPool(mediaType, source, targetCount, hideAvailable) {
        const today = getTodayDateString();
        const seed = getDaySeed(mediaType);
        const page1 = (seed % 5) + 1;
        const page2 = ((seed + 2) % 5) + 1;
        const dateParam = mediaType === 'tv' ? 'firstAirDateLte=' + today : 'primaryReleaseDateLte=' + today;
        const dateSort = mediaType === 'tv' ? 'first_air_date.desc' : 'primary_release_date.desc';
        const endpointPrefix = mediaType === 'tv' ? 'SeerrFin/discover/tv/' : 'SeerrFin/discover/movies/';

        const requests = [];

        if (source === 'daily' || source === 'upcoming' || !source) {
            requests.push(
                fetchJson('SeerrFin/discover/filtered/' + mediaType + '?sortBy=popularity.desc&' + dateParam + '&voteCountGte=10&page=' + page1).catch(function () { return null; }),
                fetchJson('SeerrFin/discover/filtered/' + mediaType + '?sortBy=' + dateSort + '&' + dateParam + '&voteCountGte=15&page=' + page2).catch(function () { return null; }),
                fetchJson(endpointPrefix + 'trending?startIndex=0&limit=40').catch(function () { return null; }),
                fetchJson(endpointPrefix + 'popular?startIndex=' + ((page1 - 1) * 20) + '&limit=40').catch(function () { return null; })
            );
        } else {
            const offset = ((page1 - 1) * 10);
            requests.push(
                fetchJson(endpointPrefix + source + '?startIndex=' + offset + '&limit=50').catch(function () { return null; }),
                fetchJson(endpointPrefix + source + '?startIndex=0&limit=50').catch(function () { return null; }),
                fetchJson('SeerrFin/discover/filtered/' + mediaType + '?sortBy=popularity.desc&' + dateParam + '&voteCountGte=10&page=' + page1).catch(function () { return null; })
            );
        }

        return Promise.all(requests).then(function (responses) {
            const seen = {};
            const pool = [];

            responses.forEach(function (res) {
                extractItemsList(res).forEach(function (item) {
                    const tmdbId = getProviderId(item, 'Tmdb') || item.Id;
                    if (!tmdbId || seen[tmdbId]) {
                        return;
                    }
                    if (!isAlreadyReleased(item)) {
                        return;
                    }
                    if (hideAvailable && getStatusInfo(item).code === 5) {
                        return;
                    }
                    seen[tmdbId] = true;
                    pool.push(item);
                });
            });

            const shuffled = deterministicShuffle(pool, seed);
            return shuffled.slice(0, targetCount);
        });
    }

    function openSeerrFinModal(tmdbId, mediaType) {
        if (window.seerrFinModal && typeof window.seerrFinModal.open === 'function') {
            window.seerrFinModal.open(String(tmdbId), mediaType);
            return;
        }
        console.warn('SeerrHomeCarousel • window.seerrFinModal non disponibile. Assicurati che SeerrFin sia attivo.');
    }

    function createCard(item, mediaType, hasTmdbKey, getDragState) {
        const tmdbId = getProviderId(item, 'Tmdb') || item.Id;
        const title = item.Name || item.OriginalTitle || 'Senza titolo';
        const year = extractYear(item);
        const posterCandidates = getPosterCandidates(item, mediaType, tmdbId, hasTmdbKey);
        const statusInfo = getStatusInfo(item);

        const card = document.createElement('div');
        card.className = 'card portraitCard card-hoverable card-withuserdata shc-card';
        card.setAttribute('data-tmdb-id', String(tmdbId));
        card.setAttribute('data-media-type', mediaType);

        const cardBox = document.createElement('div');
        cardBox.className = 'cardBox cardBox-bottompadded';

        const scalable = document.createElement('div');
        scalable.className = 'cardScalable';

        const padder = document.createElement('div');
        padder.className = 'cardPadder cardPadder-portrait';

        const imageContainer = document.createElement('div');
        imageContainer.className = 'cardImageContainer coveredImage cardContent itemAction shc-poster-wrap';

        const fallback = document.createElement('div');
        fallback.className = 'shc-poster-fallback';
        fallback.textContent = title;
        imageContainer.appendChild(fallback);

        if (posterCandidates.length > 0) {
            let candidateIdx = 0;
            const img = document.createElement('img');
            img.className = 'shc-poster-img';
            img.loading = 'lazy';
            img.alt = title;
            img.draggable = false;
            img.src = posterCandidates[0];
            img.addEventListener('error', function () {
                candidateIdx++;
                if (candidateIdx < posterCandidates.length) {
                    img.src = posterCandidates[candidateIdx];
                } else {
                    img.remove();
                }
            });
            imageContainer.appendChild(img);
        }

        if (statusInfo.badge) {
            const badge = document.createElement('span');
            badge.className = 'shc-badge ' + statusInfo.badge.cls;
            badge.textContent = statusInfo.badge.text;
            imageContainer.appendChild(badge);
        }

        const overlay = document.createElement('div');
        overlay.className = 'cardOverlayContainer itemAction';
        const reqBtn = document.createElement('button');
        reqBtn.type = 'button';
        reqBtn.className = 'cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light shc-overlay-btn';
        reqBtn.title = statusInfo.code === 5 ? 'Apri in SeerrFin' : 'Richiedi con SeerrFin';
        reqBtn.innerHTML = '<span class="material-icons cardOverlayButtonIcon">' + (statusInfo.code === 5 ? 'play_arrow' : 'add') + '</span>';
        overlay.appendChild(reqBtn);

        scalable.appendChild(padder);
        scalable.appendChild(imageContainer);
        scalable.appendChild(overlay);

        const footer = document.createElement('div');
        footer.className = 'cardText cardTextCentered cardText-first';
        const titleLink = document.createElement('button');
        titleLink.type = 'button';
        titleLink.className = 'itemAction textActionButton shc-title-btn';
        titleLink.textContent = title;
        titleLink.title = title;
        footer.appendChild(titleLink);

        cardBox.appendChild(scalable);
        cardBox.appendChild(footer);

        if (year) {
            const yearEl = document.createElement('div');
            yearEl.className = 'cardText cardTextCentered cardText-secondary';
            yearEl.textContent = year;
            cardBox.appendChild(yearEl);
        }

        card.appendChild(cardBox);

        card.addEventListener('click', function (e) {
            if (getDragState && getDragState()) {
                e.preventDefault();
                e.stopPropagation();
                return;
            }
            e.preventDefault();
            e.stopPropagation();
            e.stopImmediatePropagation();
            if (tmdbId) {
                openSeerrFinModal(tmdbId, mediaType);
            }
        });

        return card;
    }

    function attachInteractiveScroll(scroller) {
        let isDown = false;
        let wasDragged = false;
        let startX = 0;
        let scrollLeft = 0;

        scroller.addEventListener('mousedown', function (e) {
            if (e.button !== 0) {
                return;
            }
            isDown = true;
            wasDragged = false;
            startX = e.pageX - scroller.offsetLeft;
            scrollLeft = scroller.scrollLeft;
        });

        scroller.addEventListener('mousemove', function (e) {
            if (!isDown) {
                return;
            }
            const x = e.pageX - scroller.offsetLeft;
            const walk = x - startX;
            if (Math.abs(walk) > 6) {
                wasDragged = true;
                scroller.classList.add('shc-dragging');
                e.preventDefault();
                scroller.scrollLeft = scrollLeft - walk;
            }
        });

        function stopDrag() {
            if (!isDown) {
                return;
            }
            isDown = false;
            scroller.classList.remove('shc-dragging');
            setTimeout(function () {
                wasDragged = false;
            }, 50);
        }

        scroller.addEventListener('mouseup', stopDrag);
        scroller.addEventListener('mouseleave', stopDrag);

        return function () {
            return wasDragged;
        };
    }

    function buildSection(title, items, mediaType, hasTmdbKey) {
        if (!items || items.length === 0) {
            return null;
        }

        const section = document.createElement('div');
        section.className = 'verticalSection shc-section';
        section.setAttribute(ROOT_ATTR, mediaType);

        const header = document.createElement('div');
        header.className = 'sectionTitleContainer sectionTitleContainer-cards padded-left shc-header';

        const h2 = document.createElement('h2');
        h2.className = 'sectionTitle sectionTitle-cards';
        h2.textContent = title;

        const controls = document.createElement('div');
        controls.className = 'shc-scroll-controls';

        const prevBtn = document.createElement('button');
        prevBtn.type = 'button';
        prevBtn.className = 'paper-icon-button-light shc-scroll-btn';
        prevBtn.setAttribute('aria-label', 'Scorri a sinistra');
        prevBtn.title = 'Scorri a sinistra';
        prevBtn.innerHTML = '<span class="material-icons">chevron_left</span>';

        const nextBtn = document.createElement('button');
        nextBtn.type = 'button';
        nextBtn.className = 'paper-icon-button-light shc-scroll-btn';
        nextBtn.setAttribute('aria-label', 'Scorri a destra');
        nextBtn.title = 'Scorri a destra';
        nextBtn.innerHTML = '<span class="material-icons">chevron_right</span>';

        controls.appendChild(prevBtn);
        controls.appendChild(nextBtn);
        header.appendChild(h2);
        header.appendChild(controls);

        const scrollerWrap = document.createElement('div');
        scrollerWrap.className = 'shc-scroller-wrap';

        const scroller = document.createElement('div');
        scroller.className = 'itemsContainer focuscontainer-x padded-left padded-right shc-scroller';

        const getDragState = attachInteractiveScroll(scroller);

        items.forEach(function (item) {
            scroller.appendChild(createCard(item, mediaType, hasTmdbKey, getDragState));
        });

        prevBtn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            const step = Math.max(320, Math.floor(scroller.clientWidth * 0.75));
            scroller.scrollBy({ left: -step, behavior: 'smooth' });
        });

        nextBtn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            const step = Math.max(320, Math.floor(scroller.clientWidth * 0.75));
            scroller.scrollBy({ left: step, behavior: 'smooth' });
        });

        scrollerWrap.appendChild(scroller);
        section.appendChild(header);
        section.appendChild(scrollerWrap);
        return section;
    }

    function insertSections(container, sections, position) {
        container.querySelectorAll('[' + ROOT_ATTR + ']').forEach(function (el) {
            el.remove();
        });

        if (sections.length === 0) {
            return;
        }

        if (position === 'top') {
            for (let i = sections.length - 1; i >= 0; i--) {
                container.prepend(sections[i]);
            }
            return;
        }

        if (position === 'afterMyMedia' && container.children.length > 0) {
            const firstSection = container.children[0];
            let anchor = firstSection;
            sections.forEach(function (sec) {
                anchor.insertAdjacentElement('afterend', sec);
                anchor = sec;
            });
            return;
        }

        sections.forEach(function (sec) {
            container.appendChild(sec);
        });
    }

    function renderCarousels(forceRefresh) {
        if (!isHomeRoute() || typeof ApiClient === 'undefined') {
            return;
        }
        const container = findHomeSectionsContainer();
        if (!container) {
            return;
        }
        if (!forceRefresh && container.querySelector('[' + ROOT_ATTR + ']')) {
            return;
        }
        if (isRendering) {
            renderQueued = true;
            return;
        }

        isRendering = true;

        fetchJson('SeerrHomeCarousel/client-config').then(function (cfg) {
            const targetCount = cfg.maxItems || TARGET_DAILY_COUNT;
            const tasks = [];

            if (cfg.enableMoviesCarousel) {
                tasks.push(
                    fetchReleasedPool('movie', cfg.moviesSource, targetCount, cfg.hideAvailable)
                        .then(function (items) {
                            return buildSection(cfg.moviesTitle, items, 'movie', cfg.hasTmdbKey);
                        })
                        .catch(function () { return null; })
                );
            }
            if (cfg.enableTvCarousel) {
                tasks.push(
                    fetchReleasedPool('tv', cfg.tvSource, targetCount, cfg.hideAvailable)
                        .then(function (items) {
                            return buildSection(cfg.tvTitle, items, 'tv', cfg.hasTmdbKey);
                        })
                        .catch(function () { return null; })
                );
            }

            return Promise.all(tasks).then(function (sections) {
                const validSections = sections.filter(Boolean);
                const latestContainer = findHomeSectionsContainer();
                if (latestContainer && isHomeRoute()) {
                    insertSections(latestContainer, validSections, cfg.homePosition);
                }
            });
        }).catch(function (err) {
            console.warn('SeerrHomeCarousel • Errore caricamento caroselli:', err);
        }).finally(function () {
            isRendering = false;
            if (renderQueued) {
                renderQueued = false;
                renderCarousels(false);
            }
        });
    }

    function init() {
        document.addEventListener('viewshow', function () {
            setTimeout(function () { renderCarousels(false); }, 150);
        });

        window.addEventListener('hashchange', function () {
            setTimeout(function () { renderCarousels(false); }, 150);
        });

        document.addEventListener('seerrfin-request-changed', function () {
            renderCarousels(true);
        });

        observer = new MutationObserver(function () {
            if (!isHomeRoute()) {
                return;
            }
            const container = findHomeSectionsContainer();
            if (container && container.children.length > 0 && !container.querySelector('[' + ROOT_ATTR + ']')) {
                renderCarousels(false);
            }
        });

        observer.observe(document.body, { childList: true, subtree: true });
        setTimeout(function () { renderCarousels(false); }, 400);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
