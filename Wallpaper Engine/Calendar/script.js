// Wallpaper Engine Properties
window.wallpaperPropertyListener = {
    applyUserProperties: async function (properties) {
        if (properties.firstdayofweek) {
            if (weekOffset != properties.firstdayofweek.value) {
                weekOffset = parseInt(properties.firstdayofweek.value);
                await RefreshCalendarForWeekStart();
            }
        }
    },
};

// consts and variables
var localLinkUrl = 'http://127.0.0.1:5123';
var locationCache = null;
var locationRequestPromise = null;
var weatherUpdateInProgress = false;
var weatherInitialized = false;
var weatherRenderInProgress = false;
var pendingLocalLinkUpdate = null;
var localLinkEventSource = null;
var localLinkReconnectTimer = null;
var localLinkEventsConnected = false;
const locationCacheDuration = 24 * 60 * 60 * 1000;
const locationCacheKey = 'memoCalendarLocationCache';

var weekOffset = 0;
var effectiveThemeName = 'light';

const weatherRefreshButton = document.getElementById('weather-refresh-button');
var timer = 0;

const mainContainer = document.getElementById('main-container');
const leftContainer = document.getElementById('left-container');
const rightContainer = document.getElementById('right-container');
const calendarContainer = document.getElementById('calendar-container');
const calendarBodyContainer = document.getElementById('calendar-body-container');
const weekContainer = document.getElementById('week-container');

const yearText = document.getElementById('year-text');
const weekText = document.getElementById('week-text');
const dayText = document.getElementById('day-text');

const memoContainer = document.getElementById('memo-container');

const weatherIcon = document.getElementById('weather-icon');
const weatherState = document.getElementById('weather-state');
const temperatureText = document.getElementById('temperature-text');
const locationText = document.getElementById('location-text');
var latitude;
var longitude;

const monthTexts = document.querySelectorAll('.month-text');
const monthButtons = document.querySelectorAll('.month-button');
const monthClickState = new Array(monthTexts.length).fill(false);

const numbersContainer = document.getElementById('numbers-container');
const clickArea = document.querySelectorAll('.click-area');
const dayTexts = document.querySelectorAll('.numbers');
const eventIndicator = document.querySelectorAll('.event-indicator'); // List
const holidayIndicator = document.querySelectorAll('.holiday-indicator'); // List
const gameIndicator = document.querySelectorAll('.game-logo');
const interactable = new Array(numbersContainer.length);
const dayClickState = new Array(numbersContainer.length);
const isHover = new Array(numbersContainer.length).fill(false);
const cellMonths = new Array(dayTexts.length);

const weeks = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
const weeksAbbr = ['Sun', 'Mon', 'Tue', 'Wed', 'Thr', 'Fri', 'Sat', 'Sun'];

const colors = ['rgb(218, 78, 78)', 'rgb(218, 124, 78)', 'rgb(218, 160, 78)', 'rgb(218, 203, 78)', 'rgb(183, 218, 78)', 'rgb(78, 218, 122)',
    'rgb(78, 218, 157)', 'rgb(62, 218, 200)', 'rgb(78, 174, 218)', 'rgb(78, 131, 218)', 'rgb(78, 98, 218)', 'rgb(98, 78, 218)'];

const colors2 = ['rgb(217, 107, 107)', 'rgb(217, 143, 107)', 'rgb(217, 171, 107)', 'rgb(217, 205, 107)', 'rgb(189, 217, 107)', 'rgb(107, 217, 141)',
    'rgb(107, 217, 169)', 'rgb(107, 217, 202)', 'rgb(107, 181, 217)', 'rgb(107, 148, 217)', 'rgb(107, 122, 217)', 'rgb(122, 107, 217)'];

const colors3 = ['rgb(242, 195, 195)', 'rgb(242, 210, 195)', 'rgb(242, 223, 195)', 'rgb(242, 237, 195)', 'rgb(230, 242, 195)', 'rgb(195, 242, 210)',
    'rgb(195, 242, 222)', 'rgb(195, 242, 236)', 'rgb(195, 227, 242)', 'rgb(195, 213, 242)', 'rgb(195, 201, 242)', 'rgb(201, 195, 242)'];

const darkColors = [
    'rgb(178, 92, 92)', 'rgb(178, 120, 92)', 'rgb(178, 142, 92)', 'rgb(178, 168, 92)', 'rgb(156, 178, 92)', 'rgb(92, 178, 120)',
    'rgb(92, 178, 142)', 'rgb(82, 178, 166)', 'rgb(92, 150, 178)', 'rgb(92, 124, 178)', 'rgb(92, 104, 178)', 'rgb(104, 92, 178)'
];

const darkColors2 = [
    'rgb(194, 116, 116)', 'rgb(194, 142, 116)', 'rgb(194, 162, 116)', 'rgb(194, 186, 116)', 'rgb(174, 194, 116)', 'rgb(116, 194, 142)',
    'rgb(116, 194, 162)', 'rgb(106, 194, 184)', 'rgb(116, 170, 194)', 'rgb(116, 146, 194)', 'rgb(116, 128, 194)', 'rgb(128, 116, 194)'
];

const darkColors3 = [
    'rgb(128, 72, 72)', 'rgb(128, 92, 72)', 'rgb(128, 108, 72)', 'rgb(128, 124, 72)', 'rgb(112, 128, 72)', 'rgb(72, 128, 92)',
    'rgb(72, 128, 108)', 'rgb(66, 128, 120)', 'rgb(72, 108, 128)', 'rgb(72, 90, 128)', 'rgb(72, 78, 128)', 'rgb(78, 72, 128)'
];

const lightTheme = {
    colors: colors,
    colors2: colors2,
    colors3: colors3,
    pageBg: 'rgb(200, 200, 200)',
    mainBg: 'white',
    mainBorder: 'transparent',
    mainShadow: () => '0vh 0vh 0.8vh 0.4vh rgb(0, 0, 0, 0.2)',
    leftShadow: '0vh 0vh 0.3vh 0.15vh rgb(0, 0, 0, 0.1)',
    leftBorder: 'transparent',
    primaryText: 'lightgray',
    mutedText: 'dimgray',
    outsideText: 'lightgray',
    inactiveMarker: 'silver',
    activeText: 'white'
};

const darkTheme = {
    colors: darkColors3,
    colors2: darkColors2,
    colors3: darkColors3,
    pageBg: '#191919',
    mainBg: '#333333',
    mainBorder: 'transparent',
    mainShadow: (month) => '0vh 0vh 0.8vh 0.4vh ' + ColorWithOpacity(colors[month], 0.2),
    leftShadow: 'none',
    leftBorder: 'rgba(170, 178, 235, 0.18)',
    primaryText: 'rgb(218, 222, 246)',
    mutedText: 'rgb(154, 160, 196)',
    outsideText: 'rgb(82, 86, 122)',
    inactiveMarker: 'rgb(58, 61, 86)',
    activeText: 'rgb(245, 247, 250)'
};

function ColorWithOpacity(color, opacity) {
    return color.replace('rgb', 'rgba').replace(')', ', ' + opacity + ')');
}

function Theme() {
    return effectiveThemeName == 'dark' ? darkTheme : lightTheme;
}

var currentYear;
var currentMonth;
var currentWeek;
var currentDay;
var currentDayIndex;
var clickedDayIndex;

var memoData = null;
var memoRequestPromise = null;

var selectedMonth;

// style initialization
mainContainer.style.width = mainContainer.clientHeight / 3 * 4 + 'px';

const margin = mainContainer.clientHeight * 0.01;
const radius = parseFloat(window.getComputedStyle(mainContainer).getPropertyValue('border-radius'));
leftContainer.style.left = margin + 'px';
leftContainer.style.top = margin + 'px';
leftContainer.style.height = mainContainer.clientHeight - margin * 2 + 'px';
leftContainer.style.borderRadius = radius - margin + 'px';

rightContainer.style.width = mainContainer.clientWidth - margin - leftContainer.clientWidth + 'px';
calendarContainer.style.left = margin + 'px';
calendarContainer.style.right = margin + 'px';
calendarContainer.style.bottom = margin + 'px';
weatherRefreshButton.style.left = radius - weatherRefreshButton.clientWidth / 2 + 'px';
weatherRefreshButton.style.bottom = radius - weatherRefreshButton.clientWidth / 2 + 'px';

eventIndicator.forEach(indicator => {
    indicator.style.height = indicator.clientWidth + 'px';
});

const holidayIndicatorHeight = numbersContainer.clientHeight * 0.3;
holidayIndicator.forEach(indicator => {
    indicator.style.height = holidayIndicatorHeight + 'px';
    indicator.style.width = holidayIndicatorHeight + 'px';
});

const eventIndicatorTop = numbersContainer.clientHeight * 0.75;
eventIndicator.forEach(indicator => {
    indicator.style.top = eventIndicatorTop + 'px';
});

/////////////////////////////
InitDate();
ConnectLocalLinkEvents();
UpdateWeather();

setInterval(() => {
    UpdateWeather();
}, 10 * 60 * 1000);
///////////////////////////////////
// event listener initialization //
///////////////////////////////////

// refresh
weatherRefreshButton.addEventListener('click', function () {
    UpdateWeather();
});

// month buttons
monthButtons.forEach((button, index) => {
    button.addEventListener('click', () => {
        SwitchMonth(index);
    });
});

// year text
yearText.addEventListener('click', () => {
    if (selectedMonth != currentMonth) {
        monthClickState.fill(false);
        SwitchMonth(currentMonth);
    }
});

// day buttons
clickArea.forEach((indicator, index) => {
    indicator.addEventListener('click', function () {
        clickedDayIndex = index;
        ReadMemo(selectedMonth, index);
    });

    indicator.addEventListener('mouseover', function () {
        if (interactable[index] && index != currentDayIndex && index != clickedDayIndex && !isHover[index]) {
            holidayIndicator[index].style.outline = '0.2vh solid ' + Theme().primaryText;
            isHover[index] = true;
        }
    });

    indicator.addEventListener('mouseleave', function () {
        if (isHover[index]) {
            setTimeout(() => {
                if (index != clickedDayIndex) {
                    holidayIndicator[index].style.outline = '0.2vh solid transparent';
                }
                isHover[index] = false;
            }, 400);
        }
    });
});

setInterval(() => {
    timer++;
    if (timer == 60) {
        timer = 0;
        const newDate = new Date().getDate();
        if (currentDay != newDate) {
            InitDate();
            currentDay = newDate;
            monthClickState.fill(false);
        }
        UpdateThemeFromLocalLink();
        SwitchMonth(currentMonth);
        clickArea[currentDayIndex].click();
    }
}, 1000);

///////////////
// functions //
///////////////
function InitDate() {
    const currentDate = new Date();
    currentYear = currentDate.getFullYear();
    currentMonth = currentDate.getMonth();
    currentWeek = currentDate.getDay();
    currentDay = currentDate.getDate();

    memoData = null;
    memoRequestPromise = null;

    yearText.textContent = currentYear;
    dayText.textContent = currentDay;
    weekText.textContent = weeks[currentWeek];

}

function ApplyTheme() {
    ApplyThemeStyle();
}

async function UpdateThemeFromLocalLink() {
    try {
        const response = await fetch(`${localLinkUrl}/api/theme`);
        const themeData = await response.json();
        const nextTheme = themeData.effective == 'dark' ? 'dark' : 'light';

        if (effectiveThemeName != nextTheme) {
            effectiveThemeName = nextTheme;
            ApplyTheme();
        }
    } catch (e) {
    }
}

function ApplyThemeStyle() {
    const theme = Theme();
    const month = selectedMonth ?? currentMonth ?? new Date().getMonth();

    document.documentElement.style.setProperty('--page-bg', theme.pageBg);
    document.documentElement.style.setProperty('--main-bg', theme.mainBg);
    document.documentElement.style.setProperty('--main-shadow', theme.mainShadow(month));
    document.documentElement.style.setProperty('--main-border', theme.mainBorder);
    document.documentElement.style.setProperty('--left-shadow', theme.leftShadow);
    document.documentElement.style.setProperty('--left-border', theme.leftBorder);
    document.documentElement.style.setProperty('--primary-text', theme.primaryText);
    document.documentElement.style.setProperty('--muted-text', theme.mutedText);
    document.documentElement.style.setProperty('--outside-text', theme.outsideText);

    if (!currentYear) {
        return;
    }

    monthTexts.forEach(text => {
        text.style.color = theme.primaryText;
        text.style.fontWeight = 'normal';
    });

    yearText.style.color = month == currentMonth ? theme.primaryText : theme.colors3[month];
    monthTexts[month].style.color = theme.colors[month];
    monthTexts[month].style.fontWeight = 'bold';
    leftContainer.style.backgroundColor = theme.colors[month];

    RefreshCalendarTheme(month);
}

function HasVisibleColor(color) {
    return color && color != 'transparent' && color != 'rgba(0, 0, 0, 0)';
}

function RefreshCalendarTheme(month) {
    if (selectedMonth == null) {
        return;
    }

    const theme = Theme();

    dayTexts.forEach((text, index) => {
        text.style.color = interactable[index] ? theme.mutedText : theme.outsideText;
        if (index == currentDayIndex && selectedMonth == currentMonth) {
            text.style.color = theme.colors[currentMonth];
        }
    });

    holidayIndicator.forEach((indicator, index) => {
        indicator.style.outline = '0.2vh solid transparent';
        if (HasVisibleColor(indicator.style.backgroundColor)) {
            indicator.style.backgroundColor = interactable[index] ? theme.colors[month] : theme.inactiveMarker;
            dayTexts[index].style.color = theme.activeText;
        }
    });

    eventIndicator.forEach((indicator, index) => {
        if (HasVisibleColor(indicator.style.backgroundColor)) {
            indicator.style.backgroundColor = interactable[index] ? theme.colors[month] : theme.inactiveMarker;
        }
    });

    if (clickedDayIndex != null && dayTexts[clickedDayIndex].style.fontSize != '3vh') {
        holidayIndicator[clickedDayIndex].style.outline = '0.2vh solid ' + theme.colors2[month];
    }
}

function CreateCalendar(year, month) {
    const theme = Theme();
    // consts and variables
    var daysInCurrentMonth = new Date(year, month + 1, 0).getDate();
    var daysInLastMonth = new Date(year, month, 0).getDate();
    var firstDayInCurrentMonth = new Date(year, month, 1).getDay() - 1 + weekOffset; // it returns week of first day
    if (firstDayInCurrentMonth == -1) {
        firstDayInCurrentMonth = 6;
    }

    var residual = daysInLastMonth - firstDayInCurrentMonth + 1;
    var dayOut = 0;
    var monthOut = 0;

    // reset style
    dayTexts.forEach(text => {
        text.style.color = theme.mutedText;
        text.style.fontSize = '1vh';
    });

    eventIndicator.forEach(indicator => {
        indicator.style.top = eventIndicatorTop + 'px';
        indicator.style.backgroundColor = 'transparent';
    });

    holidayIndicator.forEach(indicator => {
        indicator.style.height = holidayIndicatorHeight + 'px';
        indicator.style.width = holidayIndicatorHeight + 'px';
        indicator.style.borderRadius = '0.5vh';
        indicator.style.backgroundColor = 'transparent';
    });

    gameIndicator.forEach(indicator => {
        indicator.style.opacity = 0;
    });

    // create numbers
    for (let i = 0; i < dayTexts.length; i++) {
        if (i < firstDayInCurrentMonth) {
            dayOut = residual;
            monthOut = month - 1;
            dayTexts[i].textContent = dayOut;
            dayTexts[i].style.color = theme.outsideText;
            interactable[i] = false;
            dayClickState[i] = true;
            residual++;
        }
        else if (i >= firstDayInCurrentMonth && i < daysInCurrentMonth + firstDayInCurrentMonth) {
            dayOut = i - firstDayInCurrentMonth + 1;
            monthOut = month;
            interactable[i] = true;
            dayClickState[i] = false;
            dayTexts[i].textContent = dayOut;
            if (dayTexts[i].textContent == currentDay && selectedMonth == currentMonth) {
                dayTexts[i].style.fontSize = '3vh';
                dayTexts[i].style.color = theme.colors[currentMonth];
                currentDayIndex = i;
                holidayIndicator[i].style.width = holidayIndicatorHeight * 2.5 + 'px';
                holidayIndicator[i].style.height = holidayIndicatorHeight * 2.5 + 'px';
                holidayIndicator[i].style.borderRadius = '1.2vh';
                eventIndicator[i].style.top = eventIndicatorTop * 1.1 + 'px';
            }
        }
        else {
            dayOut = i - daysInCurrentMonth - firstDayInCurrentMonth + 1;
            monthOut = month + 1;
            dayTexts[i].textContent = dayOut;
            dayTexts[i].style.color = theme.outsideText;
            interactable[i] = false;
            dayClickState[i] = true;
        }
        cellMonths[i] = monthOut;
        CheckMemo(monthOut, i);
    }
}

async function RefreshCalendarForWeekStart() {
    const weekTexts = weekContainer.querySelectorAll('p');
    const numberRows = calendarBodyContainer.querySelectorAll('tr');
    const fadeElements = [weekContainer, ...numberRows];
    const selectedDay = clickedDayIndex != null && interactable[clickedDayIndex]
        ? dayTexts[clickedDayIndex].textContent
        : null;

    await FadeController(fadeElements, 0, 40);

    weekTexts.forEach((text, index) => {
        text.textContent = weeksAbbr[index - weekOffset + 1];
    });

    currentDayIndex = null;
    clickedDayIndex = null;
    CreateCalendar(currentYear, selectedMonth);

    if (selectedDay != null) {
        dayTexts.forEach((text, index) => {
            if (interactable[index] && text.textContent == selectedDay) {
                clickedDayIndex = index;
            }
        });
    }

    RefreshCalendarTheme(selectedMonth);
    await FadeController(fadeElements, 1, 40);
}

SwitchMonth(currentMonth);
async function SwitchMonth(month) {
    if (!monthClickState[month]) {
        const theme = Theme();
        // reset style
        monthTexts.forEach(text => {
            text.style.color = theme.primaryText;
            text.style.fontWeight = 'normal';
        });

        holidayIndicator.forEach(indicator => {
            indicator.style.outline = '0.2vh solid transparent';
        });

        // update variables
        selectedMonth = month;
        timer = 0;

        // change style for selected button
        yearText.style.color = selectedMonth == currentMonth ? theme.primaryText : theme.colors3[month];
        monthTexts[month].style.color = theme.colors[month];
        monthTexts[month].style.fontWeight = 'bold';
        leftContainer.style.backgroundColor = theme.colors[month];
        document.documentElement.style.setProperty('--main-shadow', theme.mainShadow(month));
        SyncAccentMonth(month);

        // reset and update click state
        monthClickState.fill(false);
        monthClickState[month] = true;
        clickedDayIndex = null;
        currentDayIndex = null;

        CreateCalendar(currentYear, selectedMonth);
        if (dayText.style.opacity == 1) {
            let events = memoContainer.querySelectorAll('p');
            let items = memoContainer.querySelectorAll('li');
            let fadeElements = [dayText, weekText, ...events, ...items];
            await FadeController(fadeElements, 0, 40);
        }
        if (month == currentMonth) {
            yearText.style.color = theme.primaryText;
            clickArea[currentDayIndex].click();
        }

    }
}

function SyncAccentMonth(month) {
    fetch(`${localLinkUrl}/api/accent-month`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ month: month })
    }).catch(() => {
    });
}

async function GetMemoData() {
    if (memoData !== null) {
        return memoData;
    }
    if (memoRequestPromise) {
        return memoRequestPromise;
    }

    memoRequestPromise = (async () => {
        try {
            const response = await fetch(`${localLinkUrl}/api/memos?year=${currentYear}`);
            memoData = response.ok ? await response.json() : [];
        } catch (e) {
            memoData = [];
        }
        memoRequestPromise = null;
        return memoData;
    })();
    return memoRequestPromise;
}

async function GetMemos(month, day) {
    const memos = await GetMemoData();
    return memos.filter(memo => memo.month == month && memo.day == day);
}

async function CheckMemo(month, index) {
    month++;
    const memos = await GetMemos(month, dayTexts[index].textContent);
    const theme = Theme();

    memos.forEach(memo => {
        if (memo.type == 'holiday') {
            holidayIndicator[index].style.backgroundColor = interactable[index] ? theme.colors[month - 1] : theme.inactiveMarker;
            dayTexts[index].style.color = theme.activeText;
        }
        if (memo.type == 'event' || memo.type == 'item') {
            eventIndicator[index].style.backgroundColor = interactable[index] ? theme.colors[month - 1] : theme.inactiveMarker;
        }
        if (currentMonth + 1 == memo.month && memo.day == currentDay && interactable[index]) {
            holidayIndicator[index].style.backgroundColor = 'transparent';
            eventIndicator[index].style.backgroundColor = 'transparent';
            dayTexts[index].style.color = theme.colors[currentMonth];
            gameIndicator[index].src = '';
        }
    });
}

function ClearMemoMarkers() {
    const theme = Theme();

    dayTexts.forEach((text, index) => {
        text.style.color = interactable[index] ? theme.mutedText : theme.outsideText;
        if (index == currentDayIndex && selectedMonth == currentMonth) {
            text.style.color = theme.colors[currentMonth];
        }
    });

    holidayIndicator.forEach(indicator => {
        indicator.style.backgroundColor = 'transparent';
    });

    eventIndicator.forEach(indicator => {
        indicator.style.backgroundColor = 'transparent';
    });

    gameIndicator.forEach(indicator => {
        indicator.style.opacity = 0;
    });
}

async function RefreshMemoMarkers() {
    ClearMemoMarkers();
    await Promise.all(Array.from(dayTexts, (_, index) => {
        if (cellMonths[index] == null) {
            return Promise.resolve();
        }

        return CheckMemo(cellMonths[index], index);
    }));
    RefreshCalendarTheme(selectedMonth);
}

async function RenderMemoItems(month, day) {
    const memos = await GetMemos(month, day);
    memoContainer.innerHTML = '';
    let newEvents = [];
    let newItems = [];

    memos.forEach(memo => {
        if (memo.type == 'holiday' || memo.type == 'event') {
            newEvents.push(memo.text);
        }
        if (memo.type == 'item') {
            newItems.push(memo.text);
        }
    });

    newEvents.forEach(newEvent => {
        const eventElement = document.createElement('p');
        eventElement.className = 'memo-event';
        eventElement.textContent = newEvent;
        memoContainer.appendChild(eventElement);
    });
    const itemList = document.createElement('ul');
    itemList.className = 'memo-list';
    memoContainer.appendChild(itemList);
    newItems.forEach(newItem => {
        const itemElement = document.createElement('li');
        itemElement.className = 'memo-item';
        itemElement.textContent = newItem;
        itemList.appendChild(itemElement);
    });
}

async function RefreshSelectedMemoItems() {
    if (clickedDayIndex == null || !interactable[clickedDayIndex]) {
        return;
    }

    const events = memoContainer.querySelectorAll('p');
    const items = memoContainer.querySelectorAll('li');
    const fadeElements = [...events, ...items];
    if (fadeElements.length > 0) {
        await FadeController(fadeElements, 0, 40);
    }

    await RenderMemoItems(selectedMonth + 1, dayTexts[clickedDayIndex].textContent);

    const newEvents = memoContainer.querySelectorAll('p');
    const newItems = memoContainer.querySelectorAll('li');
    await FadeController([...newEvents, ...newItems], 1, 40);
}

async function ReadMemo(month, index) {
    if (!dayClickState[index]) {
        const theme = Theme();
        holidayIndicator.forEach(indicator => {
            indicator.style.outline = '0.2vh solid transparent';
        });
        if (dayTexts[index].style.fontSize != '3vh') {
            holidayIndicator[index].style.outline = '0.2vh solid ' + theme.colors2[month];
        }
        timer = 0;
        let events = memoContainer.querySelectorAll('p');
        let items = memoContainer.querySelectorAll('li');
        let fadeElements = [dayText, weekText, ...events, ...items];
        if (dayText.style.opacity == 1) {
            await FadeController(fadeElements, 0, 40);
        }

        // reset and update click state
        for (let i = 0; i < dayTexts.length; i++) {
            dayClickState[i] = !interactable[i];
        }
        dayClickState[index] = true;
        month++;

        dayText.textContent = dayTexts[index].textContent;
        weekText.textContent = weeks[index % 7 + 1 - weekOffset];
        await RenderMemoItems(month, dayTexts[index].textContent);
        events = memoContainer.querySelectorAll('p');
        items = memoContainer.querySelectorAll('li');
        fadeElements = [dayText, weekText, ...events, ...items];
        await FadeController(fadeElements, 1, 40);
    }
}

async function RefreshMemosFromLocalLink() {
    memoData = null;
    memoRequestPromise = null;
    await RefreshMemoMarkers();
    await RefreshSelectedMemoItems();
}

var weatherUpdates = [];
var currentWeatherStats = '';

function ConnectLocalLinkEvents() {
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
        localLinkEventSource.onopen = function () {
            localLinkEventsConnected = true;
        };
        localLinkEventSource.onerror = function () {
            localLinkEventsConnected = false;
            if (localLinkEventSource) {
                localLinkEventSource.close();
                localLinkEventSource = null;
            }

            localLinkReconnectTimer = setTimeout(() => {
                ConnectLocalLinkEvents();
                UpdateWeather();
            }, 3000);
        };
        localLinkEventSource.addEventListener('status', async (event) => {
            try {
                const statusEvent = JSON.parse(event.data);
                if (HasLocalLinkChange(statusEvent.changes, 'memos')) {
                    await RefreshMemosFromLocalLink();
                }
                QueueLocalLinkStatus(statusEvent.status, statusEvent.changes || ['all']);
            } catch (e) {
            }
        });
    } catch (e) {
    }
}

function GetTemperatureUnitLabel(units) {
    const normalizedUnits = (units || '').toLowerCase();

    if (normalizedUnits == 'imperial') {
        return ' F';
    }

    if (normalizedUnits == 'standard') {
        return ' K';
    }

    return ' C';
}

function GetLocationCacheKey() {
    return locationCacheKey + '-locallink-' + localLinkUrl;
}

function LoadLocationCache() {
    if (locationCache) {
        return locationCache;
    }

    try {
        const cache = JSON.parse(localStorage.getItem(GetLocationCacheKey()));
        if (cache && Date.now() - cache.time < locationCacheDuration) {
            locationCache = cache;
            return locationCache;
        }
    } catch (e) {
        return null;
    }

    return null;
}

function SaveLocationCache(cache) {
    locationCache = cache;
    try {
        localStorage.setItem(GetLocationCacheKey(), JSON.stringify(cache));
    } catch (e) {
    }
}

function ClearLocationCache() {
    locationCache = null;
    latitude = null;
    longitude = null;
    try {
        localStorage.removeItem(GetLocationCacheKey());
    } catch (e) {
    }
}

async function getLocalLinkStatus() {
    const response = await fetch(`${localLinkUrl}/api/status`);
    if (!response.ok) {
        return null;
    }

    return await response.json();
}

function HasLocalLinkChange(changes, name) {
    return !changes || changes.includes('all') || changes.includes(name);
}

function ApplyLocalLinkStatus(status, changes) {
    if (!status) {
        return;
    }

    weatherUpdates = [];

    if (HasLocalLinkChange(changes, 'theme')) {
        const nextTheme = status.theme?.effective == 'dark' ? 'dark' : 'light';
        if (effectiveThemeName != nextTheme) {
            effectiveThemeName = nextTheme;
            ApplyTheme();
        }
    }

    if (HasLocalLinkChange(changes, 'location') && status.location) {
        latitude = status.location.latitude;
        longitude = status.location.longitude;

        const locationLabel = status.location.city + ", " + status.location.state;
        if (locationText.textContent != locationLabel) {
            weatherUpdates.push({ element: locationText, value: locationLabel, order: 4 });
        }
    }

    if (!HasLocalLinkChange(changes, 'weather') || !status.weather) {
        return;
    }

    const temperature = Number(status.weather.temperature).toFixed(1);
    const unitLabel = GetTemperatureUnitLabel(status.weather.units);
    const newWeatherStats = temperature + '|' + status.weather.humidity + '|' + unitLabel;
    const newState = `<span><img src="icons/Thermometer.png" style="width: 1vh; vertical-align: -0.1vh";> ${temperature}${unitLabel}</span>
                        <span style="margin: 0.5vh;"></span>
                        <span><img src="icons/Humidity.png" style="width: 1vh; vertical-align: -0.1vh";> ${status.weather.humidity}<b>%</b></span>`;

    if (weatherState.textContent != CapitalizeFirstLetters(status.weather.description)) {
        weatherUpdates.push(
            { element: weatherIcon, value: "icons/" + status.weather.icon + ".png", order: 1 },
            { element: weatherState, value: CapitalizeFirstLetters(status.weather.description), order: 2 }
        );
    }

    if (currentWeatherStats != newWeatherStats) {
        currentWeatherStats = newWeatherStats;
        weatherUpdates.push({ element: temperatureText, value: newState, order: 3 });
    }
}

async function UpdateWeather() {
    if (weatherUpdateInProgress) {
        return;
    }

    weatherUpdateInProgress = true;
    try {
        const status = await getLocalLinkStatus();
        QueueLocalLinkStatus(status, ['all']);
    } finally {
        weatherUpdateInProgress = false;
    }
}

function QueueLocalLinkStatus(status, changes) {
    if (!status) {
        return;
    }

    const normalizedChanges = changes || ['all'];
    if (pendingLocalLinkUpdate) {
        pendingLocalLinkUpdate = {
            status: status,
            changes: Array.from(new Set([...pendingLocalLinkUpdate.changes, ...normalizedChanges]))
        };
    } else {
        pendingLocalLinkUpdate = {
            status: status,
            changes: normalizedChanges
        };
    }

    if (!weatherRenderInProgress) {
        ProcessLocalLinkStatusQueue();
    }
}

async function ProcessLocalLinkStatusQueue() {
    weatherRenderInProgress = true;

    try {
        while (pendingLocalLinkUpdate) {
            const update = pendingLocalLinkUpdate;
            pendingLocalLinkUpdate = null;
            await RenderLocalLinkStatus(update.status, update.changes);
        }
    } finally {
        weatherRenderInProgress = false;
    }
}

async function RenderLocalLinkStatus(status, changes) {
    ApplyLocalLinkStatus(status, changes || ['all']);
    weatherUpdates.sort((a, b) => a.order - b.order);
    const weatherElements = weatherUpdates.map(update => update.element);
    if (weatherUpdates.length == 0) {
        return;
    }

    if (weatherInitialized) {
        await FadeController(weatherElements, 0, 40);
    }

    weatherUpdates.forEach(update => {
        const element = update.element;

        if (element.tagName === 'IMG') {
            element.src = update.value;
        } else {
            try {
                element.innerHTML = update.value;
            } catch (e) {
                console.error("Error setting innerHTML on element:", element, e);
            }
        }
    });

    if (!weatherInitialized) {
        await FadeController(weatherElements, 1, 40);
        weatherInitialized = true;
        return;
    }

    await FadeController(weatherElements, 1, 40);
}

function FadeController(elements, fadeIn, delay) {
    const targetOpacity = fadeIn ? 1 : 0;
    const initialOpacity = fadeIn ? 0 : 1;

    elements.forEach(item => {
        item.style.opacity = initialOpacity;
    });

    requestAnimationFrame(() => {
        elements.forEach((item, index) => {
            setTimeout(() => {
                item.style.opacity = targetOpacity;
            }, delay * index);
        });
    });

    return new Promise((complete) => {
        setTimeout(() => {
            complete();
        }, delay * elements.length + 200);
    });
}

function CapitalizeFirstLetters(sentence) {
    return sentence
        .split(' ')
        .map(word => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase())
        .join(' ');
}
