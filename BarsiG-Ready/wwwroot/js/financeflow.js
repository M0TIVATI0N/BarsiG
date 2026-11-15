/*
 * FinanceFlow — client side helpers.
 * The file is intentionally small and dependency free, so the application also works offline.
 */
window.financeFlow = (function () {
    const themeKey = 'financeflow-theme';
    let connection = null;

    function applyTheme(theme) {
        const isDark = theme === 'dark';

        try {
            window.localStorage.setItem(themeKey, isDark ? 'dark' : 'light');
        } catch {
            // The browser storage can be disabled, the theme still applies for this session.
        }

        document.documentElement.classList.toggle('theme-dark', isDark);
        document.documentElement.style.colorScheme = isDark ? 'dark' : 'light';

        const themeColor = document.querySelector('meta[name="theme-color"]');
        if (themeColor) {
            themeColor.setAttribute('content', isDark ? '#0F1330' : '#F4EFE6');
        }

        notifyHost(isDark ? 'dark' : 'light');
    }

    /*
     * Tells the host of the window which colour scheme is active. The web application
     * is the only place where the colour scheme is chosen, so the title bar of the
     * window of BarsiG always shows the same colour scheme as the interface.
     * The message carries nothing but the name of the colour scheme.
     */
    function notifyHost(theme) {
        if (!window.chrome || !window.chrome.webview) {
            return;
        }

        try {
            window.chrome.webview.postMessage({ source: 'barsig', type: 'theme', theme: theme });
        } catch {
            // The page is opened in an ordinary browser, there is no host to inform.
        }
    }

    /*
     * Returns the colour scheme that is active right now. The window of the desktop
     * application reads it after the page was loaded.
     */
    function getCurrentTheme() {
        return document.documentElement.classList.contains('theme-dark') ? 'dark' : 'light';
    }

    function getStoredTheme() {
        try {
            return window.localStorage.getItem(themeKey) || 'light';
        } catch {
            return 'light';
        }
    }

    async function connectRealtime(dotNetReference) {
        if (typeof signalR === 'undefined' || connection) {
            return;
        }

        connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/notifications')
            .withAutomaticReconnect()
            .build();

        connection.on('dataChanged', function (payload) {
            dotNetReference.invokeMethodAsync('OnDataChanged', payload.entity, payload.action);
        });

        try {
            await connection.start();
        } catch {
            connection = null;
        }
    }

    async function disconnectRealtime() {
        if (!connection) {
            return;
        }

        try {
            await connection.stop();
        } catch {
            // The connection was already closed.
        }

        connection = null;
    }

    function registerServiceWorker() {
        if (!('serviceWorker' in navigator)) {
            return;
        }

        window.addEventListener('load', function () {
            navigator.serviceWorker.register('/service-worker.js').catch(function () {
                // The application works without the offline support.
            });
        });
    }

    /*
     * Marks the page as interactive. The automated tests wait for this flag,
     * because Blazor replaces the parts of the page that were rendered on the server.
     */
    function markReady() {
        window.__financeFlowReady = true;
    }

    /*
     * Signs the user in from inside the browser. The request has to be made by the browser
     * itself, because only the browser can store the cookie of the answer.
     */
    async function signIn(identifier, password) {
        const response = await fetch('/account/sign-in', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify({ email: identifier, password: password })
        });

        return { ok: response.ok, body: await response.text() };
    }

    /*
     * Creates a new account from inside the browser.
     */
    async function register(email, displayName, password, confirmPassword, phoneNumber) {
        const response = await fetch('/account/register', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify({
                email: email,
                displayName: displayName,
                password: password,
                confirmPassword: confirmPassword,
                phoneNumber: phoneNumber
            })
        });

        return { ok: response.ok, body: await response.text() };
    }

    async function verifyPhone(challengeId, code) {
        const response = await fetch('/account/phone/demo-verify', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify({ challengeId: challengeId, code: code })
        });

        return { ok: response.ok, body: await response.text() };
    }

    async function resumePhoneChallenge() {
        const response = await fetch('/account/phone/demo-resume', {
            method: 'POST',
            credentials: 'same-origin'
        });

        return { ok: response.ok, body: await response.text() };
    }

    async function resendPhoneCode() {
        const response = await fetch('/account/phone/demo-resend', {
            method: 'POST',
            credentials: 'same-origin'
        });

        return { ok: response.ok, body: await response.text() };
    }

    async function recoverPhoneRegistration(phoneNumber, password) {
        const response = await fetch('/account/phone/recovery/request', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify({ phoneNumber: phoneNumber, password: password })
        });

        return { ok: response.ok, body: await response.text() };
    }

    async function requestPhoneChange(phoneNumber, password) {
        const response = await fetch('/account/phone/change/request', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify({ phoneNumber: phoneNumber, password: password })
        });

        return { ok: response.ok, body: await response.text() };
    }

    return {
        applyTheme: applyTheme,
        getCurrentTheme: getCurrentTheme,
        getStoredTheme: getStoredTheme,
        connectRealtime: connectRealtime,
        disconnectRealtime: disconnectRealtime,
        registerServiceWorker: registerServiceWorker,
        markReady: markReady,
        signIn: signIn,
        register: register,
        verifyPhone: verifyPhone,
        resumePhoneChallenge: resumePhoneChallenge,
        resendPhoneCode: resendPhoneCode,
        recoverPhoneRegistration: recoverPhoneRegistration,
        requestPhoneChange: requestPhoneChange
    };
})();

document.addEventListener('DOMContentLoaded', function () {
    window.financeFlow.applyTheme(window.financeFlow.getStoredTheme());
    window.financeFlow.registerServiceWorker();
});
