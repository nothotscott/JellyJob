// Refreshes the stats and job table in place, and wires up the copy buttons.

(() => {
    const jobs = document.getElementById("jobs");
    const refreshMs = 3000;

    // Timestamps are rendered in the container's time zone (UTC unless TZ is set); show them in the browser's.
    function localizeTimes(root) {
        const format = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });
        for (const time of root.querySelectorAll("time[datetime]")) {
            const date = new Date(time.dateTime);
            if (!isNaN(date)) time.textContent = format.format(date);
        }
    }

    async function refresh() {
        if (document.hidden) return;
        try {
            const response = await fetch(jobs.dataset.refresh, { headers: { Accept: "text/html" } });
            if (!response.ok) return;
            // Keep expanded error details open across the swap.
            const open = new Set([...jobs.querySelectorAll("details[open][data-job]")].map(d => d.dataset.job));
            jobs.innerHTML = await response.text();
            for (const details of jobs.querySelectorAll("details[data-job]")) {
                if (open.has(details.dataset.job)) details.open = true;
            }
            localizeTimes(jobs);
        } catch {
            // The server is restarting or unreachable; try again on the next tick.
        }
    }

    localizeTimes(document);
    if (jobs) {
        setInterval(refresh, refreshMs);
        document.addEventListener("visibilitychange", refresh);
    }

    // The Recordings page's filter: hides sections whose data-search doesn't contain every typed word.
    for (const input of document.querySelectorAll("input[data-filter]")) {
        const sections = [...document.querySelectorAll(input.dataset.filter)];
        const none = document.querySelector(".no-matches");
        input.addEventListener("input", () => {
            const words = input.value.toLowerCase().split(/\s+/).filter(Boolean);
            let shown = 0;
            for (const section of sections) {
                const match = words.every(w => section.dataset.search.includes(w));
                section.hidden = !match;
                if (match) shown++;
            }
            if (none) none.hidden = shown > 0;
        });
    }

    // navigator.clipboard only exists on secure origins, and this is usually served over plain http on a LAN.
    async function copy(text) {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
            return;
        }
        const area = document.createElement("textarea");
        area.value = text;
        area.style.position = "fixed";
        area.style.opacity = "0";
        document.body.appendChild(area);
        area.select();
        document.execCommand("copy");
        area.remove();
    }

    document.addEventListener("click", async (e) => {
        const button = e.target.closest("button[data-copy]");
        if (!button) return;
        try {
            await copy(button.dataset.copy);
            button.textContent = "Copied";
            button.classList.add("copied");
        } catch {
            button.textContent = "Failed";
        }
        setTimeout(() => {
            button.textContent = "Copy";
            button.classList.remove("copied");
        }, 1500);
    });
})();
