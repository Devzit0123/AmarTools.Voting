// wwwroot/js/countdown.js

/**
 * Voting Countdown Timer
 * 
 * Usage in Razor view (Vote.cshtml):
 *   <div id="countdown" data-endtime="@Model.EndTime.ToString("o")"></div>
 * 
 * Or pass via ViewBag:
 *   <div id="countdown" data-endtime="@ViewBag.EndTimeIso"></div>
 */

document.addEventListener('DOMContentLoaded', function () {
    const countdownElement = document.getElementById('countdown');
    if (!countdownElement) {
        console.warn('Countdown element not found.');
        return;
    }

    // Get end time from data attribute (ISO format: 2025-12-31T23:59:59.000Z)
    const endTimeStr = countdownElement.getAttribute('data-endtime');
    if (!endTimeStr) {
        countdownElement.innerHTML = '<span class="text-danger">Timer not configured</span>';
        return;
    }

    const endTime = new Date(endTimeStr).getTime();
    if (isNaN(endTime)) {
        countdownElement.innerHTML = '<span class="text-danger">Invalid end time</span>';
        return;
    }

    // Container for styling changes
    const container = document.getElementById('countdown-container') || countdownElement;

    function updateTimer() {
        const now = new Date().getTime();
        const distance = endTime - now;

        if (distance <= 0) {
            countdownElement.innerHTML = 'Voting has ended!';
            if (container) {
                container.classList.remove('alert-info');
                container.classList.add('alert-danger');
            }
            clearInterval(timerInterval);
            return;
        }

        const days = Math.floor(distance / (1000 * 60 * 60 * 24));
        const hours = Math.floor((distance % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
        const minutes = Math.floor((distance % (1000 * 60 * 60)) / (1000 * 60));
        const seconds = Math.floor((distance % (1000 * 60)) / 1000);

        // Build display string
        let display = '';
        if (days > 0) {
            display += `${days}d `;
        }
        display += `${hours.toString().padStart(2, '0')}h `;
        display += `${minutes.toString().padStart(2, '0')}m `;
        display += `${seconds.toString().padStart(2, '0')}s`;

        countdownElement.innerHTML = display;

        // Visual warning when less than 1 hour remains
        if (distance < 60 * 60 * 1000) {
            countdownElement.classList.add('text-danger');
        } else {
            countdownElement.classList.remove('text-danger');
        }
    }

    // Initial call
    updateTimer();

    // Update every second
    const timerInterval = setInterval(updateTimer, 1000);

    // Cleanup when page unloads (optional but good practice)
    window.addEventListener('beforeunload', function () {
        clearInterval(timerInterval);
    });
});