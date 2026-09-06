const localLinkUrl = 'http://127.0.0.1:5123';
const gridColumns = 9;
const gridRows = 5;
const clockColumnSpan = 3;
const clockPositionKey = 'memoClockGridPosition';
const minBreatheInset = 0.7;
const maxBreatheInset = 1.4;
const placementClickWindow = 650;

const clockPanel = document.getElementById('clock-panel');
const snapPreview = document.getElementById('snap-preview');
const digitElements = [
    document.getElementById('hours-tens'),
    document.getElementById('hours-ones'),
    document.getElementById('minutes-tens'),
    document.getElementById('minutes-ones'),
    document.getElementById('seconds-tens'),
    document.getElementById('seconds-ones')
];

let localLinkEventSource = null;
let localLinkReconnectTimer = null;
let effectiveThemeName = 'light';
let currentMonth = new Date().getMonth();
let usesRemoteMonth = false;
let clockPosition = loadClockPosition();
let isPlacing = false;
let placementFollowTimer = null;
let pendingSnapPosition = null;
let placementResetTimer = null;
let placementAnimationFrame = null;
let placementStartedAt = 0;
let breatheExtra = 0;
let placementClickCount = 0;
let placementClickStartedAt = 0;

const monthColors = [
    'rgb(218, 78, 78)', 'rgb(218, 124, 78)', 'rgb(218, 160, 78)', 'rgb(218, 203, 78)',
    'rgb(183, 218, 78)', 'rgb(78, 218, 122)', 'rgb(78, 218, 157)', 'rgb(62, 218, 200)',
    'rgb(78, 174, 218)', 'rgb(78, 131, 218)', 'rgb(78, 98, 218)', 'rgb(98, 78, 218)'
];
const darkMonthColors = [
    'rgb(128, 72, 72)', 'rgb(128, 92, 72)', 'rgb(128, 108, 72)', 'rgb(128, 124, 72)',
    'rgb(112, 128, 72)', 'rgb(72, 128, 92)', 'rgb(72, 128, 108)', 'rgb(66, 128, 120)',
    'rgb(72, 108, 128)', 'rgb(72, 90, 128)', 'rgb(72, 78, 128)', 'rgb(78, 72, 128)'
];

const themeStyles = {
    light: {
        pageBg: 'rgb(200, 200, 200)',
        panelShadow: () => '0vh 0vh 0.8vh 0.4vh rgb(0, 0, 0, 0.2)',
        mutedText: 'rgba(255, 255, 255, 0.72)',
        ghostText: 'rgba(255, 255, 255, 0.1)'
    },
    dark: {
        pageBg: 'rgb(25, 25, 25)',
        panelShadow: (month) => `0vh 0vh 0.8vh 0.4vh ${colorWithOpacity(monthColors[month], 0.2)}`,
        mutedText: 'rgba(245, 247, 250, 0.68)',
        ghostText: 'rgba(255, 255, 255, 0.045)'
    }
};

function updateClock() {
    const now = new Date();
    const month = now.getMonth();

    const digits = `${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
    digitElements.forEach((element, index) => {
        element.textContent = digits[index];
    });

    if (!usesRemoteMonth && currentMonth != month) {
        currentMonth = month;
        applyMonthColor();
    }
}

function pad(value) {
    return value.toString().padStart(2, '0');
}

function loadClockPosition() {
    try {
        const position = JSON.parse(localStorage.getItem(clockPositionKey));
        if (position && Number.isInteger(position.column) && Number.isInteger(position.row)) {
            return {
                column: clamp(position.column, 0, gridColumns - clockColumnSpan),
                row: clamp(position.row, 0, gridRows - 1)
            };
        }
    } catch (e) {
    }

    return { column: 3, row: 2 };
}

function saveClockPosition() {
    try {
        localStorage.setItem(clockPositionKey, JSON.stringify(clockPosition));
    } catch (e) {
    }
}

function applyClockPosition(position) {
    document.documentElement.style.setProperty('--clock-col', position.column);
    document.documentElement.style.setProperty('--clock-row', position.row);
}

function updateSnapPreview(position) {
    document.documentElement.style.setProperty('--snap-col', position.column);
    document.documentElement.style.setProperty('--snap-row', position.row);
}

function moveClockToSnapPosition(position) {
    clockPanel.style.left = `calc(${position.column} * var(--grid-cell-w) + var(--panel-inset))`;
    clockPanel.style.top = `calc(${position.row} * var(--grid-cell-h) + var(--panel-inset))`;
}

function scheduleClockFollow(position) {
    pendingSnapPosition = position;
    if (placementFollowTimer) {
        return;
    }

    placementFollowTimer = setTimeout(() => {
        if (pendingSnapPosition) {
            moveClockToSnapPosition(pendingSnapPosition);
        }
        placementFollowTimer = null;
    }, 90);
}

function getSnapPosition(clientX, clientY) {
    const cellWidth = window.innerWidth / gridColumns;
    const cellHeight = window.innerHeight / gridRows;
    const targetColumn = Math.round(clientX / cellWidth - clockColumnSpan / 2);
    const targetRow = Math.round(clientY / cellHeight - 0.5);

    return {
        column: clamp(targetColumn, 0, gridColumns - clockColumnSpan),
        row: clamp(targetRow, 0, gridRows - 1)
    };
}

function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
}

function getPanelInset() {
    return window.innerHeight * 0.012 + 20;
}

function startPlacementBreathing() {
    placementStartedAt = performance.now();
    breatheExtra = 0;
    document.documentElement.style.setProperty('--breathe-extra', '0vh');

    function updateExtra(now) {
        if (!isPlacing) {
            return;
        }

        const elapsed = now - placementStartedAt;
        const settle = Math.min(1, elapsed / 520);
        const wave = (1 - Math.cos(elapsed / 1250 * Math.PI * 2)) / 2;
        const targetInset = minBreatheInset + wave * (maxBreatheInset - minBreatheInset);
        breatheExtra = targetInset * settle;
        document.documentElement.style.setProperty('--breathe-extra', breatheExtra.toFixed(3) + 'vh');
        placementAnimationFrame = requestAnimationFrame(updateExtra);
    }

    placementAnimationFrame = requestAnimationFrame(updateExtra);
}

function stopPlacementBreathing() {
    if (placementAnimationFrame) {
        cancelAnimationFrame(placementAnimationFrame);
        placementAnimationFrame = null;
    }

    document.documentElement.style.setProperty('--breathe-extra', breatheExtra.toFixed(3) + 'vh');
    requestAnimationFrame(() => {
        document.documentElement.style.setProperty('--breathe-extra', '0vh');
    });
}

function startPlacementAt(clientX, clientY) {
    if (isPlacing) {
        return;
    }

    if (placementResetTimer) {
        clearTimeout(placementResetTimer);
        placementResetTimer = null;
    }

    isPlacing = true;

    clockPanel.classList.add('placing');
    snapPreview.classList.add('visible');
    startPlacementBreathing();
    updatePlacementPreviewAt(clientX, clientY);
}

function countPlacementClick(event) {
    if (isPlacing) {
        return;
    }

    const now = performance.now();
    if (now - placementClickStartedAt > placementClickWindow) {
        placementClickStartedAt = now;
        placementClickCount = 0;
    }

    placementClickCount++;
    if (placementClickCount >= 3) {
        event.preventDefault();
        event.stopPropagation();
        placementClickCount = 0;
        placementClickStartedAt = 0;
        startPlacementAt(event.clientX, event.clientY);
    }
}

function updatePlacementPreview(event) {
    if (!isPlacing) {
        return;
    }

    event.preventDefault();
    updatePlacementPreviewAt(event.clientX, event.clientY);
}

function updatePlacementPreviewAt(clientX, clientY) {
    const snapPosition = getSnapPosition(clientX, clientY);
    updateSnapPreview(snapPosition);
    scheduleClockFollow(snapPosition);
}

function finishPlacement(event) {
    if (!isPlacing) {
        return;
    }

    event.preventDefault();
    isPlacing = false;
    if (placementFollowTimer) {
        clearTimeout(placementFollowTimer);
        placementFollowTimer = null;
    }
    pendingSnapPosition = null;

    clockPosition = getSnapPosition(event.clientX, event.clientY);
    saveClockPosition();
    applyClockPosition(clockPosition);
    updateSnapPreview(clockPosition);

    clockPanel.classList.remove('placing');
    stopPlacementBreathing();
    placementResetTimer = setTimeout(() => {
        clockPanel.style.left = '';
        clockPanel.style.top = '';
        placementResetTimer = null;
    }, 180);
    snapPreview.classList.remove('visible');
}

function applyTheme(themeName) {
    effectiveThemeName = themeName == 'dark' ? 'dark' : 'light';
    const root = document.documentElement;
    const month = currentMonth >= 0 ? currentMonth : new Date().getMonth();
    const theme = themeStyles[effectiveThemeName];

    root.style.setProperty('--page-bg', theme.pageBg);
    root.style.setProperty('--panel-shadow', theme.panelShadow(month));
    root.style.setProperty('--muted-text', theme.mutedText);
    root.style.setProperty('--ghost-text', theme.ghostText);

    applyMonthColor();
}

function applyMonthColor() {
    const month = currentMonth >= 0 ? currentMonth : new Date().getMonth();
    const colors = effectiveThemeName == 'dark' ? darkMonthColors : monthColors;
    const color = colors[month];

    document.documentElement.style.setProperty('--panel-bg', color);
    document.documentElement.style.setProperty('--panel-shadow', themeStyles[effectiveThemeName].panelShadow(month));
    document.documentElement.style.setProperty('--snap-color', colorWithOpacity(color, 0.55));
    document.documentElement.style.setProperty('--snap-bg', colorWithOpacity(color, 0.12));
}

function colorWithOpacity(color, opacity) {
    return color.replace('rgb', 'rgba').replace(')', `, ${opacity})`);
}

function applyLocalLinkStatus(status, changes) {
    if (!status) {
        return;
    }

    if (hasChange(changes, 'theme')) {
        applyTheme(status.theme?.effective);
    }

    if (hasChange(changes, 'accentMonth') && typeof status.appearance?.accentMonth == 'number') {
        usesRemoteMonth = true;
        currentMonth = Math.max(0, Math.min(11, status.appearance.accentMonth));
        applyMonthColor();
    }
}

function hasChange(changes, name) {
    return !changes || changes.includes('all') || changes.includes(name);
}

async function updateFromLocalLink() {
    try {
        const response = await fetch(`${localLinkUrl}/api/status`);
        if (response.ok) {
            applyLocalLinkStatus(await response.json(), ['all']);
        }
    } catch (e) {
    }
}

function connectLocalLinkEvents() {
    if (localLinkReconnectTimer) {
        clearTimeout(localLinkReconnectTimer);
        localLinkReconnectTimer = null;
    }

    if (localLinkEventSource) {
        localLinkEventSource.close();
        localLinkEventSource = null;
    }

    try {
        localLinkEventSource = new EventSource(`${localLinkUrl}/api/events`);
        localLinkEventSource.onerror = function () {
            if (localLinkEventSource) {
                localLinkEventSource.close();
                localLinkEventSource = null;
            }

            localLinkReconnectTimer = setTimeout(() => {
                connectLocalLinkEvents();
                updateFromLocalLink();
            }, 3000);
        };
        localLinkEventSource.addEventListener('status', function (event) {
            try {
                const statusEvent = JSON.parse(event.data);
                applyLocalLinkStatus(statusEvent.status, statusEvent.changes || ['all']);
            } catch (e) {
            }
        });
    } catch (e) {
    }
}

updateClock();
setInterval(updateClock, 1000);
applyClockPosition(clockPosition);
updateSnapPreview(clockPosition);
clockPanel.addEventListener('pointerdown', countPlacementClick);
document.addEventListener('pointermove', updatePlacementPreview);
document.addEventListener('pointerdown', finishPlacement);
document.addEventListener('selectstart', event => event.preventDefault());
document.addEventListener('dragstart', event => event.preventDefault());
document.addEventListener('contextmenu', event => {
    if (isPlacing) {
        event.preventDefault();
    }
});
applyTheme(effectiveThemeName);
connectLocalLinkEvents();
updateFromLocalLink();
