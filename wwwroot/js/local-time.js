document.addEventListener('DOMContentLoaded', () => {
    for (const element of document.querySelectorAll('time[data-utc]')) {
        const instant = new Date(element.dataset.utc);
        if (Number.isNaN(instant.getTime()))
            continue;

        element.textContent = new Intl.DateTimeFormat(undefined, {
            dateStyle: 'medium',
            timeStyle: 'short'
        }).format(instant);
    }
});
